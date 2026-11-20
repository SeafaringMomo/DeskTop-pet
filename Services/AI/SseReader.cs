using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DesktopGent.Services.AI;

/// <summary>
/// 轻量 SSE 客户端：读取 text/event-stream 并逐事件回调。
///
/// .NET 8 没有内置 EventSource 客户端，这里实现最小可用子集：
/// - 按行读取，空行作为事件边界
/// - 支持 <c>event:</c> 与多行 <c>data:</c>（拼接）
/// - 必须使用 <see cref="HttpCompletionOption.ResponseHeadersRead"/>，
///   否则 HttpClient 会缓冲整个响应体，「流式」退化为一次性返回
///
/// 该类不做业务映射，仅解析协议（便于单独测试）。
/// </summary>
public static class SseReader
{
    /// <summary>读取 SSE 流，逐事件调用 onEvent(eventName, data)。</summary>
    public static async Task ReadAsync(
        HttpContent content,
        Func<string, string, Task> onEvent,
        CancellationToken ct = default)
    {
        await using var stream = await content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        string? eventName = null;
        var dataBuilder = new StringBuilder();

        while (!ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
            if (line == null) break;   // 流结束

            if (line.Length == 0)
            {
                // 空行 = 事件结束
                if (eventName != null)
                {
                    await onEvent(eventName, dataBuilder.ToString()).ConfigureAwait(false);
                }
                eventName = null;
                dataBuilder.Clear();
                continue;
            }

            if (line.StartsWith(':')) continue;          // 注释行（心跳）
            if (line.StartsWith("event:", StringComparison.Ordinal))
            {
                eventName = line[6..].Trim();
            }
            else if (line.StartsWith("data:", StringComparison.Ordinal))
            {
                if (dataBuilder.Length > 0) dataBuilder.Append('\n');
                dataBuilder.Append(line[5..].TrimStart());
            }
            // 其他字段（id:/retry:）忽略
        }

        // 流末尾没有空行时补发
        if (eventName != null && dataBuilder.Length > 0)
        {
            await onEvent(eventName, dataBuilder.ToString()).ConfigureAwait(false);
        }
    }

    /// <summary>把 SSE 事件名 + JSON data 映射为 <see cref="AgentEvent"/>。</summary>
    public static AgentEvent Parse(string eventName, string data)
    {
        JsonNode? node = null;
        try { node = string.IsNullOrWhiteSpace(data) ? null : JsonNode.Parse(data); }
        catch (JsonException) { /* data 非 JSON 时按纯文本处理 */ }

        string Str(string key) => node?[key]?.GetValue<string>() ?? "";
        int Int(string key)
        {
            try { return node?[key]?.GetValue<int>() ?? 0; }
            catch { return 0; }
        }

        return new AgentEvent
        {
            Kind = eventName,
            Text = Str("content"),
            ToolName = Str("tool"),
            ToolArgs = node?["args"],
            ToolResult = Str("result"),
            IsError = node?["is_error"]?.GetValue<bool>() ?? false,
            FinalContent = Str("content"),
            SessionId = Str("session_id"),
            ErrorMessage = Str("error"),
            Step = Int("step"),
        };
    }
}
