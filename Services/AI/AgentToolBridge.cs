using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using DesktopGent.Services.Core;
using DesktopGent.Services.MCP;

namespace DesktopGent.Services.AI;

/// <summary>
/// 工具桥：把 C# 侧能力（复用 <see cref="McpManager"/>，如待办）通过本地 HTTP
/// 反向暴露给 Python sidecar。
///
/// 端点（仅监听环回地址 + 令牌校验）：
///   GET  /tools         → {"tools":[{"name","description","parameters"}]}
///   POST /tools/invoke  → {"content","is_error"}
///
/// 实现说明：
/// - 使用 <see cref="TcpListener"/> 自实现极简 HTTP/1.1，而不用 HttpListener：
///   Windows 上 HttpListener 对非管理员的 http:// 前缀通常需要 URL ACL 授权，
///   普通用户启动会直接 Access Denied。
/// - 每个连接处理完即关闭（Connection: close），不实现 keep-alive，
///   客户端（Python httpx）会自动重连，开销可忽略。
///
/// 为什么不直接让 Python 读写 SQLite：待办/金币/好感度属于 C# 领域模型，
/// 必须由 C# 单点写入（AGENTS.md §4.2），避免双写与缓存不一致。
/// </summary>
public sealed class AgentToolBridge : IDisposable
{
    public static AgentToolBridge Instance { get; } = new();

    /// <summary>单个请求的最大字节数（防御异常请求）。</summary>
    private const int MaxRequestBytes = 1024 * 1024;

    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _loopTask;
    private bool _disposed;

    /// <summary>桥地址，如 http://127.0.0.1:12345。</summary>
    public string BaseUrl { get; private set; } = "";

    /// <summary>桥令牌。</summary>
    public string Token { get; private set; } = "";

    /// <summary>是否正在监听。</summary>
    public bool IsRunning { get; private set; }

    private AgentToolBridge() { }

    /// <summary>启动监听（幂等）。</summary>
    public bool Start()
    {
        if (IsRunning) return true;

        try
        {
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            var port = ((IPEndPoint)_listener.LocalEndpoint).Port;

            Token = RandomNumberGenerator.GetHexString(32);
            BaseUrl = $"http://127.0.0.1:{port}";

            _cts = new CancellationTokenSource();
            _loopTask = Task.Run(() => AcceptLoopAsync(_listener, _cts.Token));
            IsRunning = true;
            Logger.Info("Agent", $"工具桥已启动：{BaseUrl}");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("Agent", "启动工具桥失败", ex);
            IsRunning = false;
            return false;
        }
    }

    private async Task AcceptLoopAsync(TcpListener listener, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await listener.AcceptTcpClientAsync(ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            catch (SocketException) { break; }

            _ = Task.Run(() => HandleClientAsync(client), ct);
        }
    }

    private async Task HandleClientAsync(TcpClient client)
    {
        using (client)
        {
            try
            {
                client.ReceiveTimeout = 10_000;
                client.SendTimeout = 30_000;

                await using var stream = client.GetStream();

                // 1) 读取请求头（直到 \r\n\r\n）
                var (headerText, bodyBytes) = await ReadRequestAsync(stream).ConfigureAwait(false);

                // 2) 解析请求行与头
                var lines = headerText.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
                if (lines.Length == 0) return;

                var requestLine = lines[0].Split(' ');
                if (requestLine.Length < 2)
                {
                    await WriteResponseAsync(stream, 400, ErrorJson("bad request")).ConfigureAwait(false);
                    return;
                }

                var method = requestLine[0];
                var path = requestLine[1].Split('?')[0];

                var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                for (var i = 1; i < lines.Length; i++)
                {
                    var idx = lines[i].IndexOf(':');
                    if (idx <= 0) continue;
                    headers[lines[i][..idx].Trim()] = lines[i][(idx + 1)..].Trim();
                }

                // 3) 令牌校验
                headers.TryGetValue("X-Agent-Token", out var token);
                if (token != Token)
                {
                    Logger.Warn("Agent", $"工具桥拒绝未授权请求：{method} {path}");
                    await WriteResponseAsync(stream, 401, ErrorJson("unauthorized")).ConfigureAwait(false);
                    return;
                }

                // 4) 路由
                if (method == "GET" && path == "/tools")
                {
                    await WriteResponseAsync(stream, 200, await BuildToolListAsync().ConfigureAwait(false))
                        .ConfigureAwait(false);
                    return;
                }

                if (method == "POST" && path == "/tools/invoke")
                {
                    var body = Encoding.UTF8.GetString(bodyBytes);
                    JsonNode? node = null;
                    try { node = string.IsNullOrWhiteSpace(body) ? null : JsonNode.Parse(body); }
                    catch (Exception ex)
                    {
                        Logger.Warn("Agent", $"工具桥请求体解析失败：{ex.Message}");
                    }

                    var name = node?["name"]?.GetValue<string>() ?? "";
                    var args = node?["args"];
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        await WriteResponseAsync(stream, 400, ErrorJson("missing tool name")).ConfigureAwait(false);
                        return;
                    }

                    Logger.Info("Agent", $"工具桥调用：{name}");
                    var result = await McpManager.Instance.InvokeToolAsync(name, args).ConfigureAwait(false);

                    var payload = new JsonObject
                    {
                        ["content"] = result.Content,
                        ["is_error"] = result.IsError
                    };
                    await WriteResponseAsync(stream, 200, payload).ConfigureAwait(false);
                    return;
                }

                await WriteResponseAsync(stream, 404, ErrorJson("not found")).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Logger.Error("Agent", "工具桥处理请求失败", ex);
            }
        }
    }

    /// <summary>把 McpManager 聚合的所有工具转成桥协议格式。</summary>
    private static async Task<JsonObject> BuildToolListAsync()
    {
        var tools = await McpManager.Instance.ListAllToolsAsync().ConfigureAwait(false);
        var arr = new JsonArray();
        foreach (var t in tools)
        {
            arr.Add(new JsonObject
            {
                ["name"] = t.Name,
                ["description"] = t.Description,
                ["parameters"] = t.ParametersSchema?.DeepClone()
                    ?? new JsonObject { ["type"] = "object", ["properties"] = new JsonObject() }
            });
        }
        return new JsonObject { ["tools"] = arr };
    }

    /// <summary>读取请求头与请求体。</summary>
    private static async Task<(string Header, byte[] Body)> ReadRequestAsync(NetworkStream stream)
    {
        var buffer = new List<byte>(1024);
        var oneByte = new byte[1];
        var headerEnd = -1;
        var bodyStart = 0;

        while (buffer.Count < MaxRequestBytes)
        {
            var read = await stream.ReadAsync(oneByte).ConfigureAwait(false);
            if (read == 0) break;
            buffer.Add(oneByte[0]);

            // 检测 \r\n\r\n
            var n = buffer.Count;
            if (n >= 4 &&
                buffer[n - 4] == (byte)'\r' && buffer[n - 3] == (byte)'\n' &&
                buffer[n - 2] == (byte)'\r' && buffer[n - 1] == (byte)'\n')
            {
                headerEnd = n - 4;
                bodyStart = n;
                break;
            }
        }

        if (headerEnd < 0)
            return ("", Array.Empty<byte>());

        var header = Encoding.UTF8.GetString(buffer.GetRange(0, headerEnd).ToArray());

        // 解析 Content-Length
        var contentLength = 0;
        foreach (var line in header.Split("\r\n"))
        {
            if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
            {
                int.TryParse(line[15..].Trim(), out contentLength);
                break;
            }
        }
        contentLength = Math.Clamp(contentLength, 0, MaxRequestBytes);

        var body = new byte[contentLength];
        var offset = 0;
        // 已读入 buffer 的部分先拷走
        var already = buffer.Count - bodyStart;
        if (already > 0)
        {
            var copy = Math.Min(already, contentLength);
            buffer.CopyTo(bodyStart, body, 0, copy);
            offset = copy;
        }
        while (offset < contentLength)
        {
            var read = await stream.ReadAsync(body.AsMemory(offset, contentLength - offset)).ConfigureAwait(false);
            if (read == 0) break;
            offset += read;
        }
        return (header, body);
    }

    private static JsonObject ErrorJson(string message) => new() { ["error"] = message };

    /// <summary>写一个完整的 HTTP/1.1 响应并关闭连接。</summary>
    private static async Task WriteResponseAsync(NetworkStream stream, int status, JsonNode body)
    {
        var payload = Encoding.UTF8.GetBytes(body.ToJsonString());
        var reason = status switch
        {
            200 => "OK",
            400 => "Bad Request",
            401 => "Unauthorized",
            404 => "Not Found",
            _ => "Error"
        };

        var head = new StringBuilder()
            .Append("HTTP/1.1 ").Append(status).Append(' ').Append(reason).Append("\r\n")
            .Append("Content-Type: application/json; charset=utf-8\r\n")
            .Append("Content-Length: ").Append(payload.Length).Append("\r\n")
            .Append("Connection: close\r\n\r\n")
            .ToString();

        var headBytes = Encoding.UTF8.GetBytes(head);
        await stream.WriteAsync(headBytes).ConfigureAwait(false);
        await stream.WriteAsync(payload).ConfigureAwait(false);
        await stream.FlushAsync().ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try { _cts?.Cancel(); } catch { }
        try { _listener?.Stop(); } catch { }
        _listener = null;
        _cts?.Dispose();
        _cts = null;
        IsRunning = false;
        Logger.Info("Agent", "工具桥已停止");
    }
}
