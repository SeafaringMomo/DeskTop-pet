using DesktopGent.Services.Core;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace DesktopGent.Services.MCP;

/// <summary>
/// stdio 传输的 MCP 服务器客户端：启动一个子进程，通过 stdin/stdout 交换 JSON-RPC 2.0 消息。
///
/// 协议要点（MCP 规范）：
/// - 消息以换行分隔的 NDJSON 格式传输
/// - 启动时先发 initialize 握手
/// - 然后发 tools/list 列举工具，tools/call 调用工具
///
/// 配置对应 secrets.yaml 中 mcp_servers 条目 type=stdio：
///   - name: fs-server
///     type: stdio
///     command: npx
///     args: ["-y", "@modelcontextprotocol/server-filesystem", "/path"]
/// </summary>
public sealed class StdioMcpServer : IMcpServer
{
    private readonly string _command;
    private readonly List<string> _args;
    private Process? _process;
    private StreamWriter? _stdin;
    private StreamReader? _stdout;
    private int _nextId = 1;
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    public StdioMcpServer(string name, string command, List<string>? args = null)
    {
        Name = name;
        _command = command;
        _args = args ?? new List<string>();
    }

    public string Name { get; set; }
    public string Version => "1.0.0";

    /// <summary>启动子进程并完成 initialize 握手。</summary>
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo
        {
            FileName = _command,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var a in _args) psi.ArgumentList.Add(a);

        _process = Process.Start(psi)
            ?? throw new InvalidOperationException($"启动 MCP 进程失败：{_command}");
        _stdin = _process.StandardInput;
        _stdout = _process.StandardOutput;
        _stdin.AutoFlush = true;

        // 把 stderr 单独转发到日志，便于排查子进程错误
        _ = Task.Run(() => PipeStdErrToLogger(_process.StandardError), ct);

        // initialize 握手
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
        var resp = await SendRequestAsync("initialize", initParams, ct).ConfigureAwait(false);
        // 发送 initialized 通知（无 id 字段表示通知）
        await SendNotificationAsync("notifications/initialized", new JsonObject(), ct).ConfigureAwait(false);

        Logger.Info("MCP", $"stdio MCP 服务器 {Name} 握手完成");
    }

    /// <summary>把子进程 stderr 转发到日志，便于排查问题。</summary>
    private async Task PipeStdErrToLogger(StreamReader reader)
    {
        try
        {
            while (!_process!.HasExited)
            {
                var line = await reader.ReadLineAsync().ConfigureAwait(false);
                if (line == null) break;
                if (!string.IsNullOrWhiteSpace(line))
                    Logger.Warn("MCP", $"[{Name}] stderr: {line}");
            }
        }
        catch { /* 进程退出时关闭 */ }
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
        // MCP 工具结果 content 是数组，提取文本内容
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

    /// <summary>发送 JSON-RPC 请求并等待对应 id 的响应。</summary>
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
            await WriteLineAsync(request.ToJsonString()).ConfigureAwait(false);

            // 读取响应：跳过通知（无 id 字段）和 progress 通知，找到匹配 id 的响应
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                var line = await ReadLineAsync().ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(line)) continue;

                JsonNode? msg;
                try { msg = JsonNode.Parse(line); }
                catch { continue; }

                if (msg?["id"]?.GetValue<int>() == id)
                {
                    // 检查是否为错误响应
                    if (msg["error"] != null)
                    {
                        var err = msg["error"];
                        throw new InvalidOperationException(
                            $"MCP 错误：{err?["code"]} {err?["message"]}");
                    }
                    return msg["result"];
                }
                // 其他消息（通知、其他 id 的响应）忽略
            }
        }
        finally
        {
            _sendLock.Release();
        }
    }

    /// <summary>发送 JSON-RPC 通知（无 id，不需要响应）。</summary>
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
            await WriteLineAsync(notification.ToJsonString()).ConfigureAwait(false);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    private async Task WriteLineAsync(string line)
    {
        if (_stdin == null) throw new InvalidOperationException("MCP 进程未启动");
        await _stdin.WriteLineAsync(line).ConfigureAwait(false);
    }

    private async Task<string?> ReadLineAsync()
    {
        if (_stdout == null) throw new InvalidOperationException("MCP 进程未启动");
        return await _stdout.ReadLineAsync().ConfigureAwait(false);
    }

    public void Dispose()
    {
        try
        {
            if (_stdin != null)
            {
                try { _stdin.Close(); } catch { }
                _stdin = null;
            }
            if (_process != null && !_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
                _process.WaitForExit(2000);
            }
            _process?.Dispose();
            _process = null;
            _sendLock.Dispose();
        }
        catch { }
    }
}
