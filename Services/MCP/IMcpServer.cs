using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace DesktopPetCSharp.Services.MCP;

/// <summary>
/// MCP（Model Context Protocol）服务器接口。预留扩展，便于后续接入真实的 MCP 服务器
/// （stdio / SSE / WebSocket 等传输），或本进程内的"虚拟"MCP 服务器（如 <see cref="LocalTodoMcpServer"/>）。
///
/// 设计原则：
/// - 与官方 MCP 抽象对齐：Tools（可调用函数）/ Resources（可读数据源）/ Prompts（可复用提示模板）
/// - 本接口只声明能力，不绑定具体传输；接入真实 MCP 服务时只需实现一个 StdioMcpClient。
/// - 调用方（<c>ChatService</c>）通过 <see cref="McpManager"/> 聚合多个服务器，
///   并把工具列表下发给 <see cref="IAiProvider"/>，由模型决定调用哪个。
/// </summary>
public interface IMcpServer : IDisposable
{
    /// <summary>服务器名（唯一标识）</summary>
    string Name { get; set; }

    /// <summary>服务器版本</summary>
    string Version { get; }

    /// <summary>列出该服务器提供的工具</summary>
    Task<IReadOnlyList<McpTool>> ListToolsAsync(CancellationToken ct = default);

    /// <summary>调用工具</summary>
    Task<McpToolResult> InvokeToolAsync(string toolName, JsonNode? arguments, CancellationToken ct = default);

    /// <summary>列出该服务器提供的资源（可选能力，不支持的返回空）</summary>
    Task<IReadOnlyList<McpResource>> ListResourcesAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<McpResource>>(Array.Empty<McpResource>());

    /// <summary>读取资源内容</summary>
    Task<string> ReadResourceAsync(string uri, CancellationToken ct = default)
        => Task.FromResult(string.Empty);
}

/// <summary>MCP 工具定义（与 <see cref="ToolDefinition"/> 等价）。</summary>
public sealed class McpTool
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public JsonNode? InputSchema { get; set; }
}

/// <summary>MCP 工具调用结果。</summary>
public sealed class McpToolResult
{
    public string Content { get; set; } = string.Empty;
    public bool IsError { get; set; }
}

/// <summary>MCP 资源描述。</summary>
public sealed class McpResource
{
    public string Uri { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? MimeType { get; set; }
}
