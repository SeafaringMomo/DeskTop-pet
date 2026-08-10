using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using DesktopPetCSharp.Services.AI;

namespace DesktopPetCSharp.Services.MCP;

/// <summary>
/// MCP 服务器管理器：聚合多个 <see cref="IMcpServer"/>，向 AI 层统一暴露工具列表，
/// 并按工具名路由调用。同名工具以前注册的服务器优先。
/// </summary>
public sealed class McpManager : IDisposable
{
    private readonly ConcurrentDictionary<string, IMcpServer> _servers = new();
    private readonly Dictionary<string, IMcpServer> _toolOwner = new(StringComparer.Ordinal);
    private readonly object _toolLock = new();

    public static McpManager Instance { get; } = new();

    /// <summary>注册一个 MCP 服务器，并索引其所有工具。</summary>
    public async Task RegisterAsync(IMcpServer server, CancellationToken ct = default)
    {
        if (!_servers.TryAdd(server.Name, server))
            return;

        try
        {
            var tools = await server.ListToolsAsync(ct).ConfigureAwait(false);
            lock (_toolLock)
            {
                foreach (var t in tools)
                {
                    // 不覆盖已注册的同名工具（先到先得）
                    _toolOwner.TryAdd(t.Name, server);
                }
            }
        }
        catch
        {
            // 工具列举失败不应阻止注册
        }
    }

    /// <summary>注销一个 MCP 服务器。</summary>
    public bool Unregister(string name)
    {
        if (!_servers.TryRemove(name, out var server)) return false;
        lock (_toolLock)
        {
            var stale = _toolOwner.Where(kv => kv.Value == server).Select(kv => kv.Key).ToList();
            foreach (var k in stale) _toolOwner.Remove(k);
        }
        server.Dispose();
        return true;
    }

    /// <summary>列出所有已注册服务器。</summary>
    public IEnumerable<IMcpServer> Servers => _servers.Values;

    /// <summary>聚合所有工具，转换为 <see cref="ToolDefinition"/> 列表（供 AI 提供方使用）。</summary>
    public async Task<IReadOnlyList<ToolDefinition>> ListAllToolsAsync(CancellationToken ct = default)
    {
        var list = new List<ToolDefinition>();
        foreach (var s in _servers.Values)
        {
            try
            {
                var tools = await s.ListToolsAsync(ct).ConfigureAwait(false);
                foreach (var t in tools)
                {
                    list.Add(new ToolDefinition
                    {
                        Name = t.Name,
                        Description = t.Description,
                        ParametersSchema = t.InputSchema
                    });
                }
            }
            catch { /* 单个服务器失败不影响其他 */ }
        }
        return list;
    }

    /// <summary>调用工具。按工具名查找所属服务器。</summary>
    public async Task<ToolResult> InvokeToolAsync(string toolName, JsonNode? arguments, CancellationToken ct = default)
    {
        IMcpServer? owner;
        lock (_toolLock) _toolOwner.TryGetValue(toolName, out owner);
        if (owner == null)
            return new ToolResult { Content = $"未注册的工具: {toolName}", IsError = true };

        try
        {
            var r = await owner.InvokeToolAsync(toolName, arguments, ct).ConfigureAwait(false);
            return new ToolResult { Content = r.Content, IsError = r.IsError };
        }
        catch (Exception ex)
        {
            return new ToolResult { Content = $"工具执行异常: {ex.Message}", IsError = true };
        }
    }

    public void Dispose()
    {
        foreach (var s in _servers.Values)
        {
            try { s.Dispose(); } catch { }
        }
        _servers.Clear();
        lock (_toolLock) _toolOwner.Clear();
    }
}
