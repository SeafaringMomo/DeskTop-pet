using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using DesktopGent.Services.Core;
using DesktopGent.Services.MCP;

namespace DesktopGent.Services.AI;

/// <summary>
/// 聊天编排器：在 <see cref="IAiProvider"/> 与 <see cref="McpManager"/> 之间执行多轮对话 + 工具调用循环。
/// 这是替代旧 <c>IChatService</c> 的高层 API：UI 只需调用 <see cref="TalkAsync"/>。
///
/// 工作流：
/// 1. 从注册表取默认 IAiProvider；若不支持工具调用或没有 MCP 工具，直接单轮对话返回。
/// 2. 否则把 MCP 工具列表传给提供方，若模型请求工具，则通过 McpManager 执行并把结果回传，
///    最多循环 <see cref="MaxToolRounds"/> 次以避免死循环。
/// </summary>
public sealed class ChatOrchestrator
{
    /// <summary>单次会话允许的最大工具调用轮数。</summary>
    public int MaxToolRounds { get; set; } = 6;

    /// <summary>
    /// 系统提示（角色设定）。默认根据配置文件中的主角名动态生成，
    /// 这样用户替换像素图、改 role 后，AI 在线模型也会跟着切换角色身份。
    /// </summary>
    public string SystemPrompt { get; set; } = BuildDefaultSystemPrompt();

    private static string BuildDefaultSystemPrompt()
    {
        var role = "默认角色";
        try { role = ConfigService.Load().Role; } catch { /* 配置读取失败用默认 */ }
        var hint = "";
        try { hint = AffinityService.Instance.GetSystemPromptHint(role); } catch { /* 未登录时忽略 */ }
        return
            $"你是桌面助手『{role}』。回答要活泼可爱、保持角色一致性，使用中文。" +
            "如果有需要，可以借助工具帮用户管理待办事项。" +
            hint;
    }

    private readonly List<ChatMessage> _history = new();

    /// <summary>历史对话快照（只读）。</summary>
    public IReadOnlyList<ChatMessage> History => _history;

    /// <summary>清空对话历史。</summary>
    public void ResetHistory() => _history.Clear();

    /// <summary>从对话记录仓库恢复历史（用于「加载历史会话」）。</summary>
    public void LoadHistory(IEnumerable<ChatMessage> messages)
    {
        _history.Clear();
        _history.AddRange(messages);
    }

    /// <summary>
    /// 流式对话：仅当默认提供方实现 <see cref="IStreamingAiProvider"/> 时可用。
    ///
    /// 与 <see cref="TalkAsync"/> 的分工：
    /// - 本方法不执行 C# 侧工具循环（工具循环由 Python sidecar 内部完成）
    /// - 调用方负责把 chunk 增量渲染到 UI，并在结束时落库 assistant 全文
    /// - 历史由本类持有；工具调用过程消息不进入 C# 历史（Python 每轮已接收完整历史）
    /// </summary>
    /// <param name="userMessage">用户输入</param>
    /// <param name="sessionId">C# 会话 ID</param>
    /// <param name="ct">取消令牌</param>
    public async IAsyncEnumerable<AgentEvent> TalkStreamAsync(
        string userMessage,
        string sessionId,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var provider = AiProviderRegistry.Instance.Default
            ?? throw new InvalidOperationException("没有可用的 AI 提供方，请先注册一个。");
        if (provider is not IStreamingAiProvider streaming)
            throw new InvalidOperationException($"提供方 {provider.Name} 不支持流式对话");

        var role = ConfigService.Load().Role;
        var levelHint = "";
        try { levelHint = AffinityService.Instance.GetSystemPromptHint(role); }
        catch { /* 未登录时忽略 */ }

        var request = new AiRequest
        {
            SystemPrompt = SystemPrompt,
            History = _history.ToList(),
            UserMessage = userMessage,
            // 工具由 Python 侧执行，C# 不再下发 MCP 工具，避免双重执行
            Tools = Array.Empty<ToolDefinition>(),
            SessionId = sessionId,
            User = UserService.Instance.CurrentUserName,
            Role = role,
            LevelHint = levelHint
        };

        _history.Add(new ChatMessage { Role = "user", Content = userMessage });

        var finalText = "";
        var errored = false;

        await foreach (var ev in streaming
            .ChatStreamAsync(request, sessionId, request.User ?? "", role, levelHint, ct)
            .ConfigureAwait(false))
        {
            if (ev.Kind == "done") finalText = ev.FinalContent;
            if (ev.Kind == "error") errored = true;
            yield return ev;
        }

        // 只把成功的最终答复写入历史；失败不污染上下文
        if (!errored && !string.IsNullOrEmpty(finalText))
            _history.Add(new ChatMessage { Role = "assistant", Content = finalText });
    }

    /// <summary>发送一条用户消息并返回最终回复。</summary>
    public async Task<string> TalkAsync(string userMessage, CancellationToken ct = default)
    {
        var provider = AiProviderRegistry.Instance.Default
            ?? throw new InvalidOperationException("没有可用的 AI 提供方，请先注册一个。");

        // 加入历史（用户消息）
        _history.Add(new ChatMessage { Role = "user", Content = userMessage });

        // 识别用户消息关键词 → 触发情绪
        try { MoodService.Instance.DetectFromMessage(userMessage); }
        catch { /* MoodService 异常不影响主流程 */ }

        // 取所有 MCP 工具
        var tools = provider.SupportsToolCalling
            ? await McpManager.Instance.ListAllToolsAsync(ct).ConfigureAwait(false)
            : Array.Empty<ToolDefinition>();

        var request = new AiRequest
        {
            SystemPrompt = SystemPrompt,
            History = _history.Take(_history.Count - 1).ToList(),
            UserMessage = userMessage,
            Tools = tools
        };

        for (int round = 0; round <= MaxToolRounds; round++)
        {
            var resp = await provider.ChatAsync(request, ct).ConfigureAwait(false);

            // 模型没有请求工具调用 → 直接返回文本
            if (!resp.HasToolCalls || !provider.SupportsToolCalling)
            {
                _history.Add(new ChatMessage { Role = "assistant", Content = resp.Content });
                // 识别 AI 回复关键词 → 同步情绪（让助手表情跟着 AI 语气走）
                try { MoodService.Instance.DetectFromMessage(resp.Content); }
                catch { /* MoodService 异常不影响主流程 */ }
                return resp.Content;
            }

            // 1) 把助手的工具调用消息追加进历史
            // 按 OpenAI 协议把 tool_calls 序列化到 assistant 消息上，否则下一轮请求被严格服务拒绝
            _history.Add(new ChatMessage
            {
                Role = "assistant",
                Content = resp.Content,
                ToolCalls = SerializeToolCallsForHistory(resp.ToolCalls)
            });
            request.History = _history.ToList();

            // 2) 逐个执行工具，把结果作为 tool 角色消息回传
            foreach (var call in resp.ToolCalls)
            {
                JsonNode? argsNode = null;
                try { argsNode = string.IsNullOrEmpty(call.ArgumentsJson) ? null : JsonNode.Parse(call.ArgumentsJson); }
                catch { /* 参数解析失败按空对象处理 */ }

                Logger.Info("Chat", $"调用工具 {call.Name}，参数={call.ArgumentsJson}");
                var result = await McpManager.Instance.InvokeToolAsync(call.Name, argsNode, ct).ConfigureAwait(false);
                Logger.Info("Chat", $"工具 {call.Name} 返回：{(result.IsError ? "[错误] " : "")}{result.Content}");
                _history.Add(new ChatMessage
                {
                    Role = "tool",
                    Content = result.Content,
                    ToolCallId = call.Id,
                    ToolName = call.Name
                });

                request.History = _history.ToList();
            }

            // 3) 进入下一轮：让模型基于工具结果继续生成
            // 此时 userMessage 仍为最初输入，但模型会读取历史里的 tool 结果
            request.UserMessage = "请根据工具返回结果继续回答。";
        }

        // 达到最大轮数仍未结束
        const string fallback = "（处理了好多步还是没搞明白…能换个说法再问一次吗？）";
        _history.Add(new ChatMessage { Role = "assistant", Content = fallback });
        Logger.Warn("Chat", $"达到最大工具调用轮数 {MaxToolRounds}，返回兜底回复");
        return fallback;
    }

    /// <summary>
    /// 把 ToolCall 列表序列化为 OpenAI 协议的 tool_calls JSON 数组字符串。
    /// 用于把 assistant 消息的 tool_calls 字段持久化到历史，下一轮请求重放时使用。
    /// </summary>
    private static string? SerializeToolCallsForHistory(IReadOnlyList<ToolCall> calls)
    {
        if (calls == null || calls.Count == 0) return null;
        var arr = new JsonArray();
        foreach (var c in calls)
        {
            var fn = new JsonObject
            {
                ["name"] = c.Name,
                ["arguments"] = c.ArgumentsJson
            };
            arr.Add(new JsonObject
            {
                ["id"] = c.Id,
                ["type"] = "function",
                ["function"] = fn
            });
        }
        return arr.ToJsonString();
    }
}
