using DesktopGent.Services.Core;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace DesktopGent.Services.MCP;

/// <summary>
/// HTTP/SSE 传输的 MCP 服务器客户端：通过 HTTP POST 发送 JSON-RPC 请求。
///
/// 注意：完整的 MCP Streamable HTTP 规范较复杂（SSE 长连接 + 分块响应），
/// 此实现采用简化版：直接对端点 POST JSON-RPC 请求，从响应体读取 result。
/// 适合大多数自托管 MCP HTTP 服务器（如 Python 实现的 streamable_http）。
///
/// 配置对应 secrets.yaml 中 mcp_servers 条目 type=sse：
///   - name: remote-server
///     type: sse
///     url: http://localhost:8000/mcp
/// </summary>
public sealed class SseMcpServer : IMcpServer
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(60) };
    private readonly string _url;
    private int _nextId = 1;
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    public SseMcpServer(string name, string url)
    {
        Name = name;
        _url = url;
    }

    public string Name { get; set; }
    public string Version => "1.0.0";

    /// <summary>HTTP 头（可扩展为携带 Bearer Token 等）。</summary>
    public string? AuthToken { get; set; }

    /// <summary>初始化握手。</summary>
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        var initParams = new JsonObject
        {
            ["protocolVersion"] = "2024-11-05",
            ["capabilities"] = new JsonObject(),
            ["clientInfo"] = new JsonObject
            {
                ["name"] = "DesktopGent",
                ["version"] = "1.0.0"
            }
        };
        await SendRequestAsync("initialize", initParams, ct).ConfigureAwait(false);
        await SendNotificationAsync("notifications/initialized", new JsonObject(), ct).ConfigureAwait(false);
        Logger.Info("MCP", $"sse MCP 服务器 {Name} 握手完成：{_url}");
    }

    public async Task<IReadOnlyList<McpTool>> ListToolsAsync(CancellationToken ct = default)
    {
        var resp = await SendRequestAsync("tools/list", new JsonObject(), ct).ConfigureAwait(false);
        var tools = new List<McpTool>();
        var toolsArray = resp?["tools"] as JsonArray;
        if (toolsArray == null) return tools;

        foreach (var t in toolsArray)
        {
            tools.Add(new McpTool
            {
                Name = t?["name"]?.GetValue<string>() ?? "",
                Description = t?["description"]?.GetValue<string>() ?? "",
                InputSchema = t?["inputSchema"]
            });
        }
        return tools;
    }

    public async Task<McpToolResult> InvokeToolAsync(string toolName, JsonNode? arguments, CancellationToken ct = default)
    {
        var parms = new JsonObject
        {
            ["name"] = toolName,
            ["arguments"] = arguments ?? new JsonObject()
        };
        var resp = await SendRequestAsync("tools/call", parms, ct).ConfigureAwait(false);

        var contentArr = resp?["content"] as JsonArray;
        var sb = new StringBuilder();
        var isError = resp?["isError"]?.GetValue<bool>() ?? false;

        if (contentArr != null)
        {
            foreach (var c in contentArr)
            {
                var text = c?["text"]?.GetValue<string>();
                if (!string.IsNullOrEmpty(text))
                {
                    if (sb.Length > 0) sb.AppendLine();
                    sb.Append(text);
                }
            }
        }
        return new McpToolResult { Content = sb.ToString(), IsError = isError };
    }

    /// <summary>发送 JSON-RPC 请求并解析响应。</summary>
    private async Task<JsonNode?> SendRequestAsync(string method, JsonNode parameters, CancellationToken ct)
    {
        await _sendLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var id = _nextId++;
            var request = new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = id,
                ["method"] = method,
                ["params"] = parameters
            };

            using var req = new HttpRequestMessage(HttpMethod.Post, _url);
            req.Content = new StringContent(request.ToJsonString(), Encoding.UTF8);
            req.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            if (!string.IsNullOrEmpty(AuthToken))
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AuthToken);

            using var resp = await Http.SendAsync(req, ct).ConfigureAwait(false);
            var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                throw new HttpRequestException($"MCP 请求失败 [{(int)resp.StatusCode}]: {body}");

            var msg = JsonNode.Parse(body);
            if (msg?["error"] != null)
            {
                var err = msg["error"];
                throw new InvalidOperationException($"MCP 错误：{err?["code"]} {err?["message"]}");
            }
            return msg?["result"];
        }
        finally
        {
            _sendLock.Release();
        }
    }

    /// <summary>发送 JSON-RPC 通知（无响应）。</summary>
    private async Task SendNotificationAsync(string method, JsonNode parameters, CancellationToken ct)
    {
        await _sendLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var notification = new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["method"] = method,
                ["params"] = parameters
            };
            using var req = new HttpRequestMessage(HttpMethod.Post, _url);
            req.Content = new StringContent(notification.ToJsonString(), Encoding.UTF8);
            req.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            if (!string.IsNullOrEmpty(AuthToken))
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AuthToken);
            await Http.SendAsync(req, ct).ConfigureAwait(false);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    public void Dispose()
    {
        _sendLock.Dispose();
    }
}
