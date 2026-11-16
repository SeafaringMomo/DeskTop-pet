using YamlDotNet.Serialization;

namespace DesktopGent.Models;

/// <summary>
/// 敏感信息配置模型，对应 config/secrets.yaml。
/// 含 AI 提供方（API_KEY）与 MCP 服务器地址等敏感信息，不入库。
/// 可在运行时通过 ModelSettingsWindow 修改并保存回 secrets.yaml。
/// </summary>
public class SecretsConfig
{
    /// <summary>AI 提供方列表</summary>
    [YamlMember(Alias = "ai_providers")]
    public List<AiProviderConfig> AiProviders { get; set; } = new();

    /// <summary>MCP 服务器列表</summary>
    [YamlMember(Alias = "mcp_servers")]
    public List<McpServerConfig> McpServers { get; set; } = new();

    /// <summary>查找指定名称的 AI 提供方配置。</summary>
    public AiProviderConfig? FindAiProvider(string name)
        => AiProviders.FirstOrDefault(p => p.Name == name);

    /// <summary>查找指定名称的 MCP 服务器配置。</summary>
    public McpServerConfig? FindMcpServer(string name)
        => McpServers.FirstOrDefault(s => s.Name == name);
}

/// <summary>
/// AI 提供方配置。
/// type=local：本地占位实现；type=openai-compatible：OpenAI 兼容 HTTP 服务。
/// </summary>
public class AiProviderConfig
{
    /// <summary>提供方注册名（唯一，如 "deepseek"、"openai"、"local-klee"）</summary>
    [YamlMember(Alias = "name")]
    public string Name { get; set; } = "";

    /// <summary>类型：local / openai-compatible</summary>
    [YamlMember(Alias = "type")]
    public string Type { get; set; } = "local";

    /// <summary>OpenAI 兼容服务的 API 基址（仅 type=openai-compatible 时有效）</summary>
    [YamlMember(Alias = "base_url")]
    public string BaseUrl { get; set; } = "https://api.openai.com/v1";

    /// <summary>API Key（敏感信息）</summary>
    [YamlMember(Alias = "api_key")]
    public string ApiKey { get; set; } = "";

    /// <summary>模型名（如 gpt-4o-mini、deepseek-chat）</summary>
    [YamlMember(Alias = "model")]
    public string Model { get; set; } = "gpt-4o-mini";

    /// <summary>描述（用于 UI 显示）</summary>
    [YamlMember(Alias = "description")]
    public string Description { get; set; } = "";

    /// <summary>是否启用（false 则不注册）</summary>
    [YamlMember(Alias = "enabled")]
    public bool Enabled { get; set; } = true;
}

/// <summary>
/// MCP 服务器配置。
/// type=local：本进程内服务器；type=stdio/sse：远程或子进程服务器（预留接口）。
/// </summary>
public class McpServerConfig
{
    /// <summary>服务器名（唯一）</summary>
    [YamlMember(Alias = "name")]
    public string Name { get; set; } = "";

    /// <summary>类型：local / stdio / sse</summary>
    [YamlMember(Alias = "type")]
    public string Type { get; set; } = "local";

    /// <summary>是否启用</summary>
    [YamlMember(Alias = "enabled")]
    public bool Enabled { get; set; } = true;

    /// <summary>stdio 类型：可执行文件路径</summary>
    [YamlMember(Alias = "command")]
    public string? Command { get; set; }

    /// <summary>stdio 类型：命令行参数</summary>
    [YamlMember(Alias = "args")]
    public List<string>? Args { get; set; }

    /// <summary>sse 类型：服务器 URL</summary>
    [YamlMember(Alias = "url")]
    public string? Url { get; set; }
}
