# AI 与 MCP 模块

通用 AI 接口、提供方注册表、多轮对话编排、MCP 服务器扩展。

## 涉及文件

| 文件 | 作用 |
|---|---|
| `Services/AI/IAiProvider.cs` | 通用 AI 接口 + 请求/响应/工具类型 |
| `Services/AI/AiProviderRegistry.cs` | 提供方注册表（单例） |
| `Services/AI/ChatOrchestrator.cs` | 多轮对话 + 工具调用编排器 |
| `Services/AI/Providers/LocalKleeProvider.cs` | 本地占位实现（离线兜底） |
| `Services/AI/Providers/OpenAiCompatibleProvider.cs` | OpenAI 兼容 HTTP 适配器 |
| `Services/MCP/IMcpServer.cs` | MCP 服务器接口 + 工具/资源类型 |
| `Services/MCP/McpManager.cs` | 多服务器聚合管理器（单例） |
| `Services/MCP/LocalTodoMcpServer.cs` | 把 TodoRepository 暴露为 MCP 工具 |

## AI 通用接口（IAiProvider）

```csharp
public interface IAiProvider
{
    string Name { get; }
    bool SupportsToolCalling { get; }
    Task<AiResponse> ChatAsync(AiRequest request, CancellationToken ct = default);
}
```

### AiRequest 字段

| 字段 | 类型 | 说明 |
|---|---|---|
| `SystemPrompt` | string | 系统提示（角色设定） |
| `History` | List<ChatMessage> | 历史对话（不含当前消息） |
| `UserMessage` | string | 当前用户消息 |
| `Tools` | ToolDefinition[] | 可用工具列表（来自 MCP） |

### AiResponse 字段

| 字段 | 类型 | 说明 |
|---|---|---|
| `Content` | string | 模型回复文本 |
| `HasToolCalls` | bool | 是否请求工具调用 |
| `ToolCalls` | List<ToolCall> | 工具调用列表 |

### ToolDefinition 字段

| 字段 | 类型 | 说明 |
|---|---|---|
| `Name` | string | 工具名 |
| `Description` | string | 工具描述 |
| `ParametersJson` | string | 参数 schema（JSON） |

### ToolCall 字段

| 字段 | 类型 | 说明 |
|---|---|---|
| `Id` | string | 工具调用 ID（OpenAI 协议） |
| `Name` | string | 工具名 |
| `ArgumentsJson` | string | 参数（JSON 字符串） |

## 提供方注册表（AiProviderRegistry）

单例（`AiProviderRegistry.Instance`），运行时管理提供方：

| 方法 | 用途 |
|---|---|
| `Register(provider)` | 注册提供方 |
| `Unregister(name)` | 注销提供方 |
| `TrySetDefault(name)` | 切换默认提供方 |
| `Names` | 所有已注册提供方名 |
| `Default` | 当前默认提供方 |

被模型设置窗口调用，运行时切换默认提供方、增删提供方。

## 内置实现

### LocalKleeProvider

- 本地占位，关键词匹配回复，不联网
- 不支持工具调用（`SupportsToolCalling = false`）
- 默认启用，不可删除（兜底）
- 名称固定为 `local-klee`

### OpenAiCompatibleProvider

- OpenAI 兼容 HTTP 适配器，一个类对接 OpenAI / DeepSeek / GLM / Qwen / vLLM 等
- 支持工具调用（`SupportsToolCalling = true`）
- 字段：`Name` / `BaseUrl` / `ApiKey` / `Model`
- 通过 `config/secrets.yaml` 配置，可在运行时通过模型设置窗口增删改

### PythonAgentProvider

- 指向本地 Python Agent sidecar（`Services/AI/AgentSidecarService.cs` 拉起），走 HTTP + SSE
- 同时实现 `IAiProvider` 与 `IStreamingAiProvider`：聊天窗口逐字渲染、显示工具调用状态
- `SupportsToolCalling = false`：**工具循环在 Python 侧完成**，C# 不再注入 MCP 工具（避免工具被执行两次）
- 未就绪时抛异常，由上层回退到 `openai-compatible` / `local-klee`
- **运行期回退**：sidecar 进程崩溃（`Exited` 事件）或请求连接失败时，`AgentSidecarService` 置为未就绪并触发
  `UnexpectedExit`，`App` 把默认提供方回退到其他提供方；不自动重启 sidecar
- 详见 [Python-Agent.md](Python-Agent.md)

### 流式接口（IStreamingAiProvider）

```csharp
IAsyncEnumerable<AgentEvent> ChatStreamAsync(
    AiRequest request, string sessionId, string user, string role,
    string? levelHint, CancellationToken ct = default);
```

事件类型（与 Python SSE 事件名一一对应，改一侧必须改另一侧）：

| Kind | 含义 |
|---|---|
| `session` | 会话 ID 确认 |
| `step_start` / `step_finish` | 工具循环轮次起止 |
| `chunk` | 增量文本（逐字渲染） |
| `tool_start` / `tool_finish` | 工具调用开始/结束 |
| `done` | 最终完整文本（落库 + TTS 的触发点） |
| `error` | 错误，`ErrorMessage` 带原因 |

## 聊天编排器（ChatOrchestrator）

在 `IAiProvider` 与 `McpManager` 之间执行多轮对话 + 工具调用循环：

### 工作流

1. 从注册表取默认提供方；若不支持工具调用或没有 MCP 工具，直接单轮对话返回
2. 否则把 MCP 工具列表传给提供方；若模型请求工具，通过 `McpManager` 执行并把结果回传
3. 最多循环 `MaxToolRounds` 次（默认 6）以防死循环
4. **系统提示动态生成**：根据 `ConfigService.Load().Role` 构造系统提示（角色由 `theme.yaml` 扫描而来），切换角色后在线 AI 也会切换角色身份
5. **好感度等级注入**：`AffinityService.GetSystemPromptHint(role)` 把当前好感度等级的对话风格提示注入系统提示

### 关键属性

| 属性 | 说明 |
|---|---|
| `MaxToolRounds` | 单次会话允许的最大工具调用轮数（默认 6） |
| `SystemPrompt` | 系统提示（角色设定 + 好感度等级提示） |
| `History` | 历史对话快照（只读） |

### 关键方法

| 方法 | 用途 |
|---|---|
| `TalkAsync(userMessage, ct)` | 发送一条用户消息并返回最终回复 |
| `ResetHistory()` | 清空对话历史 |

### 兜底回复

达到 `MaxToolRounds` 仍未结束时返回：「（处理了好多步还是没搞明白…能换个说法再问一次吗？）」

## MCP 服务器接口（IMcpServer）

```csharp
public interface IMcpServer
{
    string Name { get; }
    Task<List<ToolDefinition>> ListToolsAsync(CancellationToken ct = default);
    Task<ToolInvocationResult> InvokeToolAsync(string name, JsonNode? args, CancellationToken ct = default);
}
```

### ToolInvocationResult 字段

| 字段 | 类型 | 说明 |
|---|---|---|
| `Content` | string | 工具返回内容 |
| `IsError` | bool | 是否出错 |

## McpManager

单例聚合器（`McpManager.Instance`），把多个服务器的工具统一暴露给 AI 层：

| 方法 | 用途 |
|---|---|
| `RegisterAsync(server)` | 注册一个 MCP 服务器 |
| `ListAllToolsAsync(ct)` | 列出所有已注册服务器的工具 |
| `InvokeToolAsync(name, args, ct)` | 按工具名调用（自动路由到对应服务器） |

## LocalTodoMcpServer

把 `TodoRepository` 暴露为 4 个 MCP 工具：

| 工具名 | 参数 | 返回 | 说明 |
|---|---|---|---|
| `todo_list` | 无 | 待办列表（Id/Content/IsDone/CreatedAt） | 列出所有待办 |
| `todo_add` | `content: string` | 新建待办的 Id | 添加待办 |
| `todo_complete` | `id: int` | 成功/失败 | 标记完成 |
| `todo_delete` | `id: int` | 成功/失败 | 删除待办 |

AI 调用示例：用户在聊天窗说"帮我加个待办：明天买牛奶"→ ChatOrchestrator 调用 `todo_add` → 待办入库 → TodoWindow 下次打开时可见。

## 配置化注册

`App.RegisterMcpServers` 从 `config/secrets.yaml` 加载：

| type | name | 行为 |
|---|---|---|
| `local` | `local-todo` | 注册 `LocalTodoMcpServer` |
| `local` | 其他 | 记录警告（未实现） |
| `stdio` | 任意 | 预留接口，记录警告跳过 |
| `sse` | 任意 | 预留接口，记录警告跳过 |
| 无配置 | — | 兜底注册 `local-todo` |

## 多轮工具调用示例

```
用户："帮我加个待办：明天买牛奶，然后列出所有待办"
  ↓
ChatOrchestrator.TalkAsync
  ↓
轮 1：模型请求 todo_add(content="明天买牛奶")
  ↓
McpManager.InvokeToolAsync("todo_add", {content: "明天买牛奶"})
  ↓
LocalTodoMcpServer → App.Todos.Add("明天买牛奶") → 返回新 Id
  ↓
工具结果回传模型
  ↓
轮 2：模型请求 todo_list()
  ↓
McpManager.InvokeToolAsync("todo_list", null)
  ↓
LocalTodoMcpServer → App.Todos.ListAll() → 返回列表
  ↓
工具结果回传模型
  ↓
轮 3：模型生成最终回复："已添加待办'明天买牛奶'。当前待办列表：..."
  ↓
返回给用户 + TTS 朗读
```

## 已知限制

- **OpenAI 工具调用多轮对话**：`ChatOrchestrator` 在历史消息中只保存了文本内容，未携带 OpenAI 协议要求的 `tool_calls` 结构化字段和 `tool_call_id`。单轮工具调用可正常工作，连续多轮工具调用的第二次请求可能被部分严格的 OpenAI 兼容服务拒绝。
  > 走 `python-agent` 时不受此限制：Python 侧 `normalize_history` 会丢弃不完整的工具消息并重新组织，且当轮的工具往返在 Python 内部完成。
- **stdio/sse MCP**：当前仅实现 `local` 类型 MCP 服务器；`stdio`/`sse` 类型在 `secrets.yaml` 中可配置但会记录警告跳过。

## 扩展指引

### 接入新 AI 后端

三种方式：
1. **配置化**（推荐）：在 `config/secrets.yaml` 的 `ai_providers` 列表新增条目（`type: openai-compatible`），或通过「⚙ 模型」窗口的「➕ 新增」按钮添加——变更即时写回 `secrets.yaml` 并注册到 `AiProviderRegistry`
2. **代码化**：实现 `IAiProvider`（需要逐字输出则同时实现 `IStreamingAiProvider`），在 `App.RegisterAiProviders` 中调用 `AiProviderRegistry.Instance.Register(...)`
3. **增强 Python Agent**：见 [Python-Agent.md](Python-Agent.md) 的「扩展」一节

### 工具桥（Python → C#）

`AgentToolBridge` 把 `McpManager` 聚合的全部工具以本地 HTTP 暴露给 Python sidecar：

| 端点 | 说明 |
|---|---|
| `GET /tools` | 返回 `{"tools":[{"name","description","parameters"}]}` |
| `POST /tools/invoke` | 请求 `{"name","args"}`，返回 `{"content","is_error"}` |

只监听 `127.0.0.1`，随机端口 + 随机令牌（`X-Agent-Token`）。
因此**新增 MCP 工具会自动出现在 Python Agent 的工具列表里**，无需改 Python 代码。


### 接入真实 MCP 服务器

两种方式：
1. **配置化**：在 `config/secrets.yaml` 的 `mcp_servers` 列表新增条目（`type: local`/`stdio`/`sse`）。当前仅 `local` 类型且 `name=local-todo` 已实现；`stdio`/`sse` 为预留接口
2. **代码化**：实现 `IMcpServer`（例如基于 stdio 的 `StdioMcpClient`），在 `App.RegisterMcpServers` 中 `await McpManager.Instance.RegisterAsync(...)`。`LocalTodoMcpServer` 可作为实现模板
