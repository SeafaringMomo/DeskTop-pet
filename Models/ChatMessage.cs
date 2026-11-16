using System.Text.Json.Serialization;
using YamlDotNet.Serialization;

namespace DesktopGent.Models;

/// <summary>
/// 对话消息模型，对应 SQLite ChatMessage 表的一条记录。
/// 一条消息属于某个 <see cref="ChatSession"/>，角色为 user/assistant/tool。
/// </summary>
public class ChatMessageRecord
{
    public long Id { get; set; }
    public long SessionId { get; set; }
    /// <summary>消息角色：user / assistant / tool</summary>
    public string Role { get; set; } = "";
    public string Content { get; set; } = "";
    /// <summary>消息时间戳（ISO 8601）</summary>
    public string Timestamp { get; set; } = "";
    /// <summary>assistant 消息携带的 tool_calls JSON 数组（OpenAI 协议），可空</summary>
    public string? ToolCalls { get; set; }
    /// <summary>tool 角色消息对应的调用 ID（OpenAI 协议 tool_call_id），可空</summary>
    public string? ToolCallId { get; set; }
    /// <summary>tool 角色消息对应的工具名，可空</summary>
    public string? ToolName { get; set; }
}

/// <summary>
/// 对话会话模型，对应 SQLite ChatSession 表的一条记录。
/// 一次"打开聊天窗"对应一个会话，包含若干消息。
/// </summary>
public class ChatSession
{
    public long Id { get; set; }
    /// <summary>会话标题（首条消息前 20 字）</summary>
    public string Title { get; set; } = "";
    /// <summary>会话开始时的角色名（默认角色/索隆）</summary>
    public string Role { get; set; } = "";
    /// <summary>会话使用的 AI 提供方名</summary>
    public string Provider { get; set; } = "";
    public string CreatedAt { get; set; } = "";
    public string UpdatedAt { get; set; } = "";

    [JsonIgnore]
    [YamlIgnore]
    public string DisplayTime
    {
        get
        {
            if (string.IsNullOrEmpty(CreatedAt)) return "";
            try { return DateTime.Parse(CreatedAt).ToString("MM-dd HH:mm"); }
            catch { return CreatedAt; }
        }
    }
}
