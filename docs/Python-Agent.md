# Python Agent 侧车（sidecar）

C# 主进程与 Python Agent 微服务的混合架构。本文档描述运行时行为、故障排查与扩展方式；
设计依据见 [Python-Agent融合方案.md](Python-Agent融合方案.md)，Python 侧细节见 [agent/README.md](../agent/README.md)。

## 涉及文件

| 文件 | 作用 |
|---|---|
| `Services/AI/AgentSidecarService.cs` | Python 子进程生命周期（启动/健康检查/日志转发/退出清理） |
| `Services/AI/AgentToolBridge.cs` | C# 工具反向桥（`/tools`、`/tools/invoke`），复用 `McpManager` |
| `Services/AI/Providers/PythonAgentProvider.cs` | `IAiProvider` + `IStreamingAiProvider` 实现（SSE 客户端） |
| `Services/AI/SseReader.cs` | 极简 SSE 解析（`ResponseHeadersRead` + 逐行） |
| `Services/AI/AgentEvent.cs` | 事件 DTO + 流式接口 |
| `Services/AI/ChatOrchestrator.cs` | 新增 `TalkStreamAsync`、`LoadHistory` |
| `Models/SecretsConfig.cs` | `AgentConfig`（secrets.yaml 的 `agent` 块） |
| `Windows/Talk/TalkWindow.xaml(.cs)` | 流式渲染 + 工具状态行 |
| `agent/` | Python 微服务（domain/ports/core/adapters/tools/services/api） |

## 启动时序

```
OnStartup
 ├─ Logger.Init
 ├─ RegisterAiProviders（注册 local-klee / openai-compatible / python-agent）
 │    └─ 若默认被选为 python-agent → 暂时切回其他提供方（sidecar 未就绪）
 ├─ Launcher（用户登录）
 ├─ Todos / ChatHistory 初始化
 ├─ RegisterMcpServers（local-todo 等）
 ├─ GentWindow.Show
 └─ StartAgentSidecarAsync
      ├─ 工具桥 Start()（TcpListener 127.0.0.1:0）
      ├─ 选择 sidecar 模型（第一个启用的 openai-compatible）
      ├─ sidecar StartAsync（分配端口 + 随机令牌 + 环境变量注入）
      ├─ 轮询 /health（≤30s）
      └─ 就绪 → TrySetDefault("python-agent")
```

退出：`App.OnExit` → `AgentSidecarService.Stop()`（`Kill(entireProcessTree:true)`）→ `AgentToolBridge.Dispose()`。

## 端口与鉴权

| 通道 | 地址 | 鉴权 |
|---|---|---|
| C# → Python | `http://127.0.0.1:<随机>` | `X-Agent-Token`（32 字节随机，每次启动更换） |
| Python → C# 工具桥 | `http://127.0.0.1:<随机>` | 同上，独立令牌 |

两者都只监听环回地址，随机端口，令牌不写日志、不落配置。

> 工具桥使用 `TcpListener` 自实现 HTTP/1.1 而非 `HttpListener`：后者在 Windows 上对非管理员
> 应用通常需要 URL ACL 授权，会导致普通用户启动直接 Access Denied。

## 配置（`config/secrets.yaml`）

```yaml
ai_providers:
  - name: python-agent
    type: python-agent
    enabled: true

agent:
  enabled: true
  auto_start: true
  python_path: ""              # 空=自动探测 <项目根>/.venv
  workspace_root: ""           # 空=<用户目录>/agent_workspace
  max_tool_iterations: 8
  request_timeout_seconds: 120
  enable_exec: false
  log_level: INFO
  brave_api_key: ""
```

**模型密钥不重复配置**：sidecar 复用 `ai_providers` 中第一个启用的 `openai-compatible`，
由 C# 通过进程环境变量 `AGENT_LLM_*` 注入。

## 数据流

```
用户输入
  → TalkWindow.ProcessAsync
  → ChatOrchestrator.TalkStreamAsync
  → PythonAgentProvider.ChatStreamAsync（POST /v1/chat/stream）
  → Python ChatService → AgentLoop
       ├─ LLM 流式（chunk → SSE → C# 逐字渲染）
       └─ 工具调用
            ├─ 本地工具（记忆/命令/网页）在 Python 进程内执行
            └─ todo_* → POST 工具桥 → McpManager → LocalTodoMcpServer → SQLite
  → done 事件 → 落 chat_history.db + TTS 朗读
```

**会话唯一事实来源是 C# 的 `chat_history.db`**：Python 不落会话文件，每轮随请求接收历史。
历史在 Python 侧会归一化（丢弃非法 role、缺 `tool_call_id` 的 tool 消息、非法 `tool_calls`），
再修复工具调用配对：孤立 tool 消息丢弃，结果不全的 `tool_calls` 整组降级为纯文本（或丢弃空消息），
避免严格 OpenAI 兼容服务返回 400。历史按条数截断后不会以 tool 消息开头（`trim_history`）。

## 故障回退

| 触发 | 行为 |
|---|---|
| 启动时 `/health` 超时或进程提前退出 | 记录错误，不切换默认提供方（保持原提供方） |
| 运行中进程崩溃 | 子进程 `Exited` 事件 → `IsReady=false` → `UnexpectedExit` → 默认提供方回退 |
| 请求时连接失败（端口已关） | `PythonAgentProvider` 调 `MarkUnavailable()` → 同上回退，不等超时 |
| 手动在模型设置里选中 `python-agent` 但未就绪 | 弹窗提示，不静默失败 |

回退目标优先取注册表中除 `python-agent` 外的第一个提供方，最后兜底 `local-klee`。
**不自动重启 sidecar**（避免崩溃循环），需退出应用后重启。

用户档案的 `default_provider` 会被记住：登录后恢复该值（跳过 `python-agent`），
sidecar 就绪时若用户明确选过其他在线提供方，则不再自动抢占默认。

## 工具归属

| 工具 | 执行位置 | 说明 |
|---|---|---|
| `memory_*` | Python | 写 `<user>/agent_workspace/` 下的 Markdown |
| `exec_run` | Python | 需 `agent.enable_exec=true`；白名单 + 危险模式拦截 + 限定工作目录 |
| `web_fetch` / `web_search` | Python | 后者需 `brave_api_key` |
| `todo_*` | C# | 经工具桥，保证单点写库与缓存一致 |

同名工具冲突时本地优先（`CompositeToolbox` 记警告）。

## 故障排查

| 现象 | 检查 |
|---|---|
| 启动日志无「sidecar 就绪」 | `.venv` 是否存在；`agent/pyproject.toml` 是否存在；`secrets.yaml` 是否有启用的 openai-compatible 且填了 key |
| 「没有已启用的 openai-compatible 提供方」 | 在「⚙ 模型」窗口启用一个并填 API Key |
| 「sidecar 进程提前退出」 | 查日志中 `[py/stderr]` 行；常见为缺依赖（`uv pip install -e agent`） |
| 聊天可发送但回答为空 | 查 `[py/stdout]` 中的 LLM 错误；确认 `base_url` 与 `model` 正确 |
| 待办工具不可用 | 查日志「工具桥」启动行；确认 `local-todo` 已在 `mcp_servers` 注册或走了兜底 |
| sidecar 反复重启 | 当前版本不自动重启（避免崩溃循环），退出应用后重启即可；崩溃后默认提供方会自动回退，无需手动切换 |

## 关闭方式（回滚）

`secrets.yaml` 中把 `agent.enabled` 设为 `false` → 下次启动完全回到纯 C# 链路
（`ChatOrchestrator` + `OpenAiCompatibleProvider` + `LocalKleeProvider`），无需改代码。

## 扩展

### 给 Python Agent 增加 C# 能力

在 `McpManager` 注册新的 `IMcpServer` 即可 —— 工具桥每次 `GET /tools` 都会聚合全部工具，
Python 侧无需改动。（注意：sidecar 会在启动时拉取一次，新增工具需重启 sidecar。）

### 给 C# 增加 Python 能力

在 `agent/agent_service/tools/local.py` 的 `_handlers` 与 `_SPECS` 中同时登记即可；
`LocalToolbox.list_specs()` 会自动暴露。

## 已知限制

- sidecar 崩溃后不自动重启（设计取舍：优先避免崩溃循环与重复拉起）；默认提供方会自动回退，但需重启应用才能恢复 `python-agent`。
- 上下文压缩/会话摘要尚未实现（Python 侧 `history_limit` 仅按条数截断，且截断点不会切散工具调用配对）。
- 记忆为关键词检索，无向量语义检索。
- 事件契约需两侧同步修改，无 schema 校验。
