using System.Text.Json.Nodes;

namespace DesktopGent.Services.AI;

/// <summary>
/// 流式 AI 提供方（Python Agent sidecar 专用）。
/// 与 <see cref="IAiProvider"/> 并存的扩展接口：不修改原有契约，避免破坏其他提供方。
///
/// 事件类型与 Python 侧 <c>agent_service/domain/events.py</c> 的 AgentEventKind 一一对应，
/// 修改任一侧必须同步。
/// </summary>
public interface IStreamingAiProvider
{
    /// <summary>
    /// 流式对话。工具循环在 sidecar 内部完成，本方法只负责搬运事件。
    /// </summary>
    /// <param name="request">请求体（历史、系统提示、用户消息）</param>
    /// <param name="sessionId">C# 侧会话 ID（Python 仅用于日志与回传）</param>
    /// <param name="user">当前用户名</param>
    /// <param name="role">当前角色显示名</param>
    /// <param name="levelHint">好感度等级提示（可空）</param>
    /// <param name="ct">取消令牌</param>
    IAsyncEnumerable<AgentEvent> ChatStreamAsync(
        AiRequest request,
        string sessionId,
        string user,
        string role,
        string? levelHint,
        CancellationToken ct = default);
}

/// <summary>Agent 流式事件。字段与 SSE data 负载对齐。</summary>
public sealed class AgentEvent
{
    /// <summary>事件类型：session/step_start/chunk/tool_start/tool_finish/step_finish/done/error</summary>
    public string Kind { get; init; } = "";

    /// <summary>文本增量（Kind=chunk）</summary>
    public string Text { get; init; } = "";

    /// <summary>工具名（Kind=tool_start/tool_finish）</summary>
    public string ToolName { get; init; } = "";

    /// <summary>工具参数 JSON（Kind=tool_start）</summary>
    public JsonNode? ToolArgs { get; init; }

    /// <summary>工具结果（Kind=tool_finish）</summary>
    public string ToolResult { get; init; } = "";

    /// <summary>工具是否出错（Kind=tool_finish）</summary>
    public bool IsError { get; init; }

    /// <summary>最终回复全文（Kind=done）</summary>
    public string FinalContent { get; init; } = "";

    /// <summary>会话 ID（Kind=session/done）</summary>
    public string SessionId { get; init; } = "";

    /// <summary>错误信息（Kind=error）</summary>
    public string ErrorMessage { get; init; } = "";

    /// <summary>当前步数（Kind=step_start/step_finish/chunk）</summary>
    public int Step { get; init; }
}
