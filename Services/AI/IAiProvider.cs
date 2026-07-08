using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace DesktopPetCSharp.Services.AI;

/// <summary>
/// 通用 AI 提供方接口。设计为可同时支持"纯对话"和"工具调用"两种模式，
/// 便于后续接入 OpenAI / Anthropic / 讯飞星火 / Qwen / 本地模型等不同后端，
/// 也便于通过 <see cref="IMcpServer"/>（见 MCP 命名空间）把外部工具/资源暴露给模型。
/// </summary>
public interface IAiProvider
{
    /// <summary>提供方唯一标识，例如 "local-klee" / "openai" / "spark"。</summary>
    string Name { get; }

    /// <summary>是否支持工具调用（function calling）。本地占位实现返回 false。</summary>
    bool SupportsToolCalling { get; }

    /// <summary>
    /// 发起一次对话。
    /// </summary>
    /// <param name="request">请求体：包含系统提示、历史、用户新消息、可选工具列表</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>提供方响应（最终文本 + 可能的工具调用请求）</returns>
    Task<AiResponse> ChatAsync(AiRequest request, CancellationToken ct = default);
}

/// <summary>对话请求。</summary>
public sealed class AiRequest
{
    /// <summary>系统提示（角色设定、行为约束）</summary>
    public string? SystemPrompt { get; set; }

    /// <summary>历史对话（不包含本次新消息）。已按时间顺序排列。</summary>
    public IReadOnlyList<ChatMessage> History { get; set; } = Array.Empty<ChatMessage>();

    /// <summary>本次用户输入。</summary>
    public string UserMessage { get; set; } = string.Empty;

    /// <summary>
    /// 可用工具列表（来自 MCP 服务器等）。若 <see cref="IAiProvider.SupportsToolCalling"/> 为 false，提供方可忽略。
    /// </summary>
    public IReadOnlyList<ToolDefinition> Tools { get; set; } = Array.Empty<ToolDefinition>();

    /// <summary>调用参数：温度、max_tokens 等，由各提供方自行解析。</summary>
    public IReadOnlyDictionary<string, object?>? Options { get; set; }
}

/// <summary>对话响应。</summary>
public sealed class AiResponse
{
    /// <summary>模型最终回复文本（若发生工具调用，此处通常为对工具结果的总结）。</summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>模型请求调用的工具（function calls）。由调用方执行后再发起下一轮对话。</summary>
    public IReadOnlyList<ToolCall> ToolCalls { get; set; } = Array.Empty<ToolCall>();

    /// <summary>原始响应（供调试或高级用途），格式由提供方决定。</summary>
    public object? Raw { get; set; }

    /// <summary>本次响应是否以工具调用结束（无最终文本）。</summary>
    public bool HasToolCalls => ToolCalls.Count > 0;
}

/// <summary>聊天消息（系统/用户/助手/工具结果）。</summary>
public sealed class ChatMessage
{
    /// <summary>"system" / "user" / "assistant" / "tool"</summary>
    public string Role { get; set; } = "user";

    public string Content { get; set; } = string.Empty;

    /// <summary>若该消息是工具结果，则填写调用 ID 与名称。</summary>
    public string? ToolCallId { get; set; }

    public string? ToolName { get; set; }
}

/// <summary>工具定义（function calling），与 MCP 工具定义兼容。</summary>
public sealed class ToolDefinition
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    /// <summary>JSON Schema（参数描述）。使用 JsonNode 以保持与各 SDK 无关。</summary>
    public JsonNode? ParametersSchema { get; set; }
}

/// <summary>模型发起的工具调用。</summary>
public sealed class ToolCall
{
    /// <summary>调用 ID（用于回传 tool 结果）。</summary>
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>参数（JSON 字符串）</summary>
    public string ArgumentsJson { get; set; } = "{}";
}

/// <summary>工具执行结果。</summary>
public sealed class ToolResult
{
    public string Content { get; set; } = string.Empty;
    public bool IsError { get; set; }
}
