using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DesktopGent.Services.Core;

namespace DesktopGent.Services.AI;

/// <summary>
/// Python Agent 的 <see cref="IAiProvider"/> + <see cref="IStreamingAiProvider"/> 实现。
///
/// 设计要点：
/// - 工具循环在 sidecar 内部完成，因此 <see cref="SupportsToolCalling"/> 为 false
///   （避免 <see cref="ChatOrchestrator"/> 再套一层 C# 工具循环）
/// - <see cref="ChatAsync"/> 消费完整流后返回最终文本，保证与既有调用方兼容
/// - 所有失败都抛出异常，由上层决定回退（不吞异常导致"假成功"）
/// </summary>
public sealed class PythonAgentProvider : IAiProvider, IStreamingAiProvider
{
    private static readonly HttpClient Http = new() { Timeout = Timeout.InfiniteTimeSpan };

    public string Name { get; set; } = "python-agent";

    /// <summary>工具循环由 Python 负责，C# 层不再注入 MCP 工具。</summary>
    public bool SupportsToolCalling => false;

    /// <summary>sidecar 实时状态来源（可注入，便于测试）。</summary>
    private readonly Func<(bool Ready, string BaseUrl, string Token)> _endpoint;

    /// <summary>请求超时（秒）。</summary>
    public int TimeoutSeconds { get; set; } = 120;

    public PythonAgentProvider() : this(DefaultEndpoint) { }

    /// <summary>测试用构造：注入端点提供者。</summary>
    public PythonAgentProvider(Func<(bool, string, string)> endpoint)
    {
        _endpoint = endpoint;
    }

    private static (bool, string, string) DefaultEndpoint()
    {
        var svc = AgentSidecarService.Instance;
        return (svc.IsReady, svc.BaseUrl, svc.Token);
    }

    /// <summary>非流式兼容入口：聚合 chunk 后返回。</summary>
    public async Task<AiResponse> ChatAsync(AiRequest request, CancellationToken ct = default)
    {
        var sb = new StringBuilder();
        await foreach (var ev in ChatStreamAsync(
            request, request.SessionId ?? "", request.User ?? "", request.Role ?? "", request.LevelHint, ct)
            .ConfigureAwait(false))
        {
            if (ev.Kind == "chunk") sb.Append(ev.Text);
            if (ev.Kind == "done" && sb.Length == 0 && !string.IsNullOrEmpty(ev.FinalContent))
                sb.Append(ev.FinalContent);
            if (ev.Kind == "error")
                throw new InvalidOperationException($"Agent 返回错误：{ev.ErrorMessage}");
        }
        return new AiResponse { Content = sb.ToString() };
    }

    public async IAsyncEnumerable<AgentEvent> ChatStreamAsync(
        AiRequest request,
        string sessionId,
        string user,
        string role,
        string? levelHint,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var (ready, baseUrl, token) = _endpoint();
        if (!ready || string.IsNullOrEmpty(baseUrl))
            throw new InvalidOperationException("Python Agent sidecar 未就绪");

        var payload = BuildPayload(request, sessionId, user, role, levelHint);
        var url = baseUrl.TrimEnd('/') + "/v1/chat/stream";

        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Headers.Add("X-Agent-Token", token);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        req.Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json");

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(TimeoutSeconds));

        HttpResponseMessage resp;
        try
        {
            resp = await Http
                .SendAsync(req, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token)
                .ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            // 连接层失败（端口已关/进程已死）：立刻标记 sidecar 不可用，
            // 让上层回退，而不是让用户对每条消息都等一次超时
            AgentSidecarService.Instance.MarkUnavailable($"请求 sidecar 失败：{ex.Message}");
            throw;
        }

        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync(timeoutCts.Token).ConfigureAwait(false);
            resp.Dispose();
            throw new HttpRequestException($"Agent 请求失败 [{(int)resp.StatusCode}]: {body}");
        }

        // SSE 是推送式接口，用 Channel 把回调转成 IAsyncEnumerable
        var channel = System.Threading.Channels.Channel.CreateUnbounded<AgentEvent>(
            new System.Threading.Channels.UnboundedChannelOptions { SingleWriter = true });

        var writerTask = Task.Run(async () =>
        {
            try
            {
                await SseReader.ReadAsync(
                    resp.Content,
                    (name, data) =>
                    {
                        channel.Writer.TryWrite(SseReader.Parse(name, data));
                        return Task.CompletedTask;
                    },
                    timeoutCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { /* 取消属正常 */ }
            catch (Exception ex) { Logger.Error("Agent", "读取 SSE 流失败", ex); }
            finally { channel.Writer.TryComplete(); }
        }, timeoutCts.Token);

        try
        {
            await foreach (var ev in channel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
                yield return ev;
        }
        finally
        {
            // 调用方提前 break/抛异常时也要释放响应与读取任务
            try { await writerTask.ConfigureAwait(false); } catch (OperationCanceledException) { }
            resp.Dispose();
        }
    }

    /// <summary>构造请求体（与 Python ChatRequest 模型对齐）。</summary>
    private static JsonObject BuildPayload(
        AiRequest request, string sessionId, string user, string role, string? levelHint)
    {
        var history = new JsonArray();
        foreach (var m in request.History)
        {
            var item = new JsonObject { ["role"] = m.Role, ["content"] = m.Content };
            if (!string.IsNullOrEmpty(m.ToolCalls)) item["tool_calls"] = m.ToolCalls;
            if (!string.IsNullOrEmpty(m.ToolCallId)) item["tool_call_id"] = m.ToolCallId;
            history.Add(item);
        }

        var payload = new JsonObject
        {
            ["message"] = request.UserMessage,
            ["session_id"] = sessionId,
            ["user"] = user,
            ["role"] = role,
            ["level_hint"] = levelHint ?? "",
            ["system_prompt"] = request.SystemPrompt ?? "",
            ["history"] = history
        };

        if (request.Options != null && request.Options.Count > 0)
        {
            var opts = new JsonObject();
            foreach (var kv in request.Options)
                opts[kv.Key] = JsonSerializer.SerializeToNode(kv.Value);
            payload["options"] = opts;
        }
        return payload;
    }
}
