using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using DesktopPetCSharp.Models;

namespace DesktopPetCSharp.Services.MCP;

/// <summary>
/// 将本地 <see cref="TodoRepository"/> 包装为虚拟 MCP 服务器。
/// 暴露 4 个工具：todo_list / todo_add / todo_complete / todo_delete，
/// 让支持工具调用的 AI（如 OpenAI 兼容提供方）能直接管理用户的待办事项。
/// 同时作为真实 MCP 服务器接入的"参考实现"模板。
/// </summary>
public sealed class LocalTodoMcpServer : IMcpServer
{
    public static readonly string ServerName = "local-todo";

    private readonly TodoRepository _repo;

    public LocalTodoMcpServer(TodoRepository repo)
    {
        _repo = repo;
        Name = ServerName;
    }

    public string Name { get; set; }
    public string Version => "1.0.0";

    public Task<IReadOnlyList<McpTool>> ListToolsAsync(CancellationToken ct = default)
    {
        IReadOnlyList<McpTool> tools = new[]
        {
            new McpTool
            {
                Name = "todo_list",
                Description = "列出当前所有待办事项（包括已完成和未完成）。",
                InputSchema = JsonNode.Parse("""{"type":"object","properties":{}}""")!
            },
            new McpTool
            {
                Name = "todo_add",
                Description = "新增一条待办事项。",
                InputSchema = JsonNode.Parse("""
                    {
                        "type": "object",
                        "properties": {
                            "content": { "type": "string", "description": "待办内容" }
                        },
                        "required": ["content"]
                    }
                    """)!
            },
            new McpTool
            {
                Name = "todo_complete",
                Description = "把指定 ID 的待办事项标记为已完成。",
                InputSchema = JsonNode.Parse("""
                    {
                        "type": "object",
                        "properties": {
                            "id": { "type": "integer", "description": "待办 ID" }
                        },
                        "required": ["id"]
                    }
                    """)!
            },
            new McpTool
            {
                Name = "todo_delete",
                Description = "删除指定 ID 的待办事项。",
                InputSchema = JsonNode.Parse("""
                    {
                        "type": "object",
                        "properties": {
                            "id": { "type": "integer", "description": "待办 ID" }
                        },
                        "required": ["id"]
                    }
                    """)!
            }
        };
        return Task.FromResult(tools);
    }

    public Task<McpToolResult> InvokeToolAsync(string toolName, JsonNode? arguments, CancellationToken ct = default)
    {
        McpToolResult result;
        try
        {
            result = toolName switch
            {
                "todo_list" => TodoList(),
                "todo_add" => TodoAdd(arguments),
                "todo_complete" => TodoComplete(arguments),
                "todo_delete" => TodoDelete(arguments),
                _ => new McpToolResult { Content = $"未知工具: {toolName}", IsError = true }
            };
        }
        catch (Exception ex)
        {
            result = new McpToolResult { Content = $"工具执行失败: {ex.Message}", IsError = true };
        }
        return Task.FromResult(result);
    }

    private McpToolResult TodoList()
    {
        var items = _repo.GetAll().ToList();
        if (items.Count == 0)
            return new McpToolResult { Content = "当前没有待办事项。" };

        var arr = items.Select(i => new
        {
            id = i.Id,
            content = i.Content,
            done = i.IsDone,
            created_at = i.CreatedAt.ToString("o")
        });
        return new McpToolResult { Content = JsonSerializer.Serialize(arr) };
    }

    private McpToolResult TodoAdd(JsonNode? args)
    {
        var content = args?["content"]?.GetValue<string>()?.Trim();
        if (string.IsNullOrEmpty(content))
            return new McpToolResult { Content = "参数 content 不能为空", IsError = true };

        var item = new TodoItem
        {
            Content = content,
            CreatedAt = DateTime.Now,
            UpdatedAt = DateTime.Now,
            IsDone = false
        };
        _repo.Insert(item);
        return new McpToolResult { Content = $"已新增待办 #{item.Id}: {content}" };
    }

    private McpToolResult TodoComplete(JsonNode? args)
    {
        var id = args?["id"]?.GetValue<long>() ?? 0;
        var item = _repo.GetAll().FirstOrDefault(i => i.Id == id);
        if (item == null)
            return new McpToolResult { Content = $"找不到 ID={id} 的待办", IsError = true };

        item.IsDone = true;
        item.UpdatedAt = DateTime.Now;
        _repo.Update(item);
        return new McpToolResult { Content = $"已标记完成 #{id}: {item.Content}" };
    }

    private McpToolResult TodoDelete(JsonNode? args)
    {
        var id = args?["id"]?.GetValue<long>() ?? 0;
        _repo.Delete(id);
        return new McpToolResult { Content = $"已删除待办 #{id}" };
    }

    public void Dispose() { /* 持有外部 repo，不在这里释放 */ }
}
