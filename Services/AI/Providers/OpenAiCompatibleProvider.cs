using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DesktopGent.Services.AI.Providers;

/// <summary>
/// OpenAI 兼容（Chat Completions /v1/chat/completions）的通用 HTTP 适配器。
/// 同一适配器可对接 OpenAI 官方、Azure OpenAI、DeepSeek、Moonshot、智谱 GLM、
/// 通义千问（DashScope OpenAI 模式）、本地 vLLM/Ollama OpenAI 接口等。
/// 只需修改 <see cref="BaseUrl"/> / <see cref="ApiKey"/> / <see cref="Model"/>。
///
/// 这是模板实现：当前默认未在启动时注册，避免在用户未配置 API Key 时产生异常。
/// 配置好后通过 <code>AiProviderRegistry.Instance.Register(new OpenAiCompatibleProvider {...})</code> 启用。
/// </summary>
public sealed class OpenAiCompatibleProvider : IAiProvider
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(60) };

    public string Name { get; set; } = "openai";

    public bool SupportsToolCalling => true;

    /// <summary>API Base，例如 https://api.openai.com/v1 或 https://api.deepseek.com/v1</summary>
    public string BaseUrl { get; set; } = "https://api.openai.com/v1";

    /// <summary>API Key（Bearer Token）</summary>
    public string? ApiKey { get; set; }

    /// <summary>模型名，例如 gpt-4o-mini / deepseek-chat / glm-4-flash / qwen-plus</summary>
    public string Model { get; set; } = "gpt-4o-mini";

    /// <summary>请求时附加的 Organization 头（可选）</summary>
    public string? Organization { get; set; }

    public async Task<AiResponse> ChatAsync(AiRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
            throw new InvalidOperationException($"AI 提供方 {Name} 缺少 ApiKey 配置。");

        var payload = BuildPayload(request);
        var url = BaseUrl.TrimEnd('/') + "/chat/completions";

        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ApiKey);
        if (!string.IsNullOrEmpty(Organization))
            req.Headers.Add("OpenAI-Organization", Organization);
        req.Content = JsonContent.Create(payload);

        using var resp = await Http.SendAsync(req, ct).ConfigureAwait(false);
        var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
            throw new HttpRequestException($"AI 请求失败 [{(int)resp.StatusCode}]: {json}");

        return ParseResponse(json);
    }

    private JsonObject BuildPayload(AiRequest request)
    {
        var messages = new JsonArray();
        if (!string.IsNullOrEmpty(request.SystemPrompt))
        {
            messages.Add(new JsonObject
            {
                ["role"] = "system",
                ["content"] = request.SystemPrompt
            });
        }
        foreach (var m in request.History)
        {
            var msg = new JsonObject
            {
                ["role"] = m.Role,
                ["content"] = m.Content
            };

            // assistant 消息若带 tool_calls，按 OpenAI 协议附上结构化字段
            // 否则连续多轮工具调用的第二次请求会被严格服务拒绝
            if (m.Role == "assistant" && !string.IsNullOrEmpty(m.ToolCalls))
            {
                try
                {
                    if (JsonNode.Parse(m.ToolCalls) is JsonArray arr)
                        msg["tool_calls"] = arr;
                }
                catch
                {
                    // tool_calls JSON 解析失败时忽略，仅保留 content
                }
            }

            // tool 角色消息必须带 tool_call_id（OpenAI 协议要求）
            if (m.Role == "tool" && !string.IsNullOrEmpty(m.ToolCallId))
            {
                msg["tool_call_id"] = m.ToolCallId;
                if (!string.IsNullOrEmpty(m.ToolName))
                    msg["name"] = m.ToolName;
            }

            messages.Add(msg);
        }
        messages.Add(new JsonObject { ["role"] = "user", ["content"] = request.UserMessage });

        var payload = new JsonObject
        {
            ["model"] = Model,
            ["messages"] = messages
        };

        // 工具调用（function calling）
        if (SupportsToolCalling && request.Tools.Count > 0)
        {
            var tools = new JsonArray();
            foreach (var t in request.Tools)
            {
                tools.Add(new JsonObject
                {
                    ["type"] = "function",
                    ["function"] = new JsonObject
                    {
                        ["name"] = t.Name,
                        ["description"] = t.Description,
                        // 必须 DeepClone：ParametersSchema 由 MCP 服务器持有并在多轮请求间复用，
                        // JsonNode 一旦挂到某个父节点上，第二次组包会抛
                        // InvalidOperationException: The node already has a parent.
                        ["parameters"] = t.ParametersSchema?.DeepClone()
                            ?? new JsonObject { ["type"] = "object" }
                    }
                });
            }
            payload["tools"] = tools;
        }

        if (request.Options != null)
        {
            foreach (var kv in request.Options)
            {
                if (kv.Value == null) continue;
                payload[kv.Key] = JsonSerializer.SerializeToNode(kv.Value);
            }
        }
        return payload;
    }

    private static AiResponse ParseResponse(string json)
    {
        var node = JsonNode.Parse(json);
        var choice = node?["choices"]?[0]?["message"];
        var content = choice?["content"]?.GetValue<string>() ?? "";
        var toolCallsNode = choice?["tool_calls"] as JsonArray;

        var resp = new AiResponse { Content = content, Raw = node };

        if (toolCallsNode != null && toolCallsNode.Count > 0)
        {
            var calls = new List<ToolCall>(toolCallsNode.Count);
            foreach (var tc in toolCallsNode)
            {
                var fn = tc?["function"];
                calls.Add(new ToolCall
                {
                    Id = tc?["id"]?.GetValue<string>() ?? Guid.NewGuid().ToString("N"),
                    Name = fn?["name"]?.GetValue<string>() ?? "",
                    ArgumentsJson = fn?["arguments"]?.GetValue<string>() ?? "{}"
                });
            }
            resp.ToolCalls = calls;
        }
        return resp;
    }
}
