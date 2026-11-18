# DeskTopGent × DeskTop-helper 融合技术方案

> 目标：把 DeskTop-helper 的 Python Agent 能力（hello-agents 框架、记忆系统、工作空间、工具集）
> 融合进 DeskTopGent（C#/WPF 桌面助手），形成 **C# 负责 UI/养成，Python 负责 Agent 推理** 的混合开发架构。
>
> 本文档为实施方案，**尚未改动任何 C# 源码**；Python 侧仅在项目根创建了 `.venv` 并完成兼容性验证（见 §12）。

---

## 0. 结论摘要

| 项 | 结论 |
|---|---|
| 融合方式 | **本地 sidecar 进程**：C# 主进程启动/守护一个 Python FastAPI 服务（仅监听 `127.0.0.1`），通过 **HTTP + SSE** 通信 |
| Python 环境 | 项目根 `.venv`，由 **uv** 管理，Python 3.13.11，`hello-agents==1.0.1` ✅ 已验证可装可用 |
| C# 改动面 | 新增 3 个文件（Provider / Sidecar 守护 / SSE 客户端）+ 改 3 处（`App.xaml.cs` 注册、`ModelSettingsWindow` 展示、`TalkWindow` 流式输出） |
| 工具归属 | **Agent 工具在 Python 内部执行**（记忆、文件、命令、搜索）；C# 专属能力（待办）通过 **HTTP 工具回调** 反向暴露给 Python |
| 会话持久化 | **C# SQLite 为唯一事实来源**；Python 侧不落会话文件，历史每轮由 C# 传入 |
| 长期记忆 | Python 侧 Markdown（`MEMORY.md` + 每日记忆），目录改为 **按用户隔离** `user_data/users/<name>/agent_workspace/` |
| 命名约束 | 移植时把 helper 的 `helloclaw` / `HelloClaw` 改为 `gent` / `助手` 术语，遵守 AGENTS.md §9 |
| 主要风险 | hello-agents 1.0.1 内部 API 与 helper 编写时的版本存在差异（已定位 3 处，见 §6.4），回退路径必须改写 |

**已确认不可行/不推荐**：Python.NET（pythonnet）内嵌解释器 —— WPF 单线程 STA + Python GIL + .NET 8 跨版本 ABI + 打包体积，均不适合长期维护；且与 `uv` 管理虚拟环境的诉求冲突。

---

## 1. 现状识别

### 1.1 DeskTopGent（C# / WPF）

| 维度 | 现状 |
|---|---|
| 框架 | .NET 8 `net8.0-windows`，WPF，`WinExe`，单项目 `DesktopGent.csproj` |
| 依赖 | `Hardcodet.NotifyIcon.Wpf`、`Microsoft.Data.Sqlite`、`NAudio`、`System.Speech`、`YamlDotNet` |
| 架构 | 单例服务 + 静态配置（`ConfigService` 带缓存与失效）+ 仓储（SQLite）+ 窗口层 |
| AI 抽象 | `IAiProvider`（`Name` / `SupportsToolCalling` / `ChatAsync`）→ `AiProviderRegistry` → `ChatOrchestrator`（C# 侧多轮工具循环，`MaxToolRounds=6`） |
| 工具抽象 | `IMcpServer`（`ListToolsAsync` / `InvokeToolAsync`）→ `McpManager` 聚合；现有实现 `LocalTodoMcpServer`（`todo_list/add/complete/delete`）、`StdioMcpServer`、`SseMcpServer` |
| Provider | `LocalKleeProvider`（离线兜底，不支持工具）、`OpenAiCompatibleProvider`（OpenAI 兼容 HTTP，支持工具） |
| 持久化 | `user_data/users/<name>/chat_history.db`（`ChatSession` / `ChatMessage`，已含 `ToolCalls/ToolCallId/ToolName`）、`todo.db`、`profile.yaml` |
| 语音 | `TTSService`（System.Speech）、`SpeechRecognitionService`（麦克风听写） |
| 界面 | `GentWindow`（像素助手）、`TalkWindow`（聊天）、`ChatHistoryWindow`、`TodoWindow`、`GentManagerWindow`、`SettingsWindow`、`LauncherWindow` |
| 配置 | `config/app.yaml`、`gent_management.yaml`、`foods.yaml`、`secrets.yaml`（不入库） |
| 当前工具循环 | **同步一次性**：`TalkAsync` 返回最终字符串，UI 无增量输出、无工具状态展示 |

**关键缺口**（相对于 helper）：

1. 无流式输出 —— `TalkWindow` 只能等完整回复后 `AppendText`。
2. 无工具执行过程可见性 —— 仅写日志。
3. 无长期/每日记忆 —— 配置里的角色提示是静态拼接。
4. 无工作空间/人格文件体系 —— 角色信息只在 `theme.yaml` 的显示名。
5. 无上下文压缩 / 会话摘要 —— 长对话会无限增长历史。
6. 工具集窄 —— 只有待办，无文件读写、命令执行、网页搜索。

### 1.2 DeskTop-helper（Python / Vue）

| 维度 | 现状 |
|---|---|
| 框架 | Python 3.12–3.13，`hello-agents>=1.0.0`（**实际解析到 1.0.1**） |
| Agent | `HelloClawAgent` → `EnhancedSimpleAgent`（继承 `SimpleAgent`）→ `EnhancedHelloAgentsLLM`（继承 `HelloAgentsLLM`，覆盖 `astream_invoke_with_tools`） |
| 服务 | FastAPI + uvicorn，SSE（`sse-starlette`） |
| 前端 | Vue3 + TS + Ant Design Vue + Pinia（**WPF 方案中不需要**） |
| 工作空间 | `~/.helloclaw/workspace/`：`AGENTS.md` / `IDENTITY.md` / `SOUL.md` / `USER.md` / `MEMORY.md` / `BOOTSTRAP.md` / `HEARTBEAT.md` / `memory/` / `sessions/` |
| 记忆 | `MemoryCaptureManager`（正则触发 → 分类 `preference/decision/entity/fact` → 去重写入每日记忆）、`MemoryFlushManager`（上下文将满时静默回合提醒保存）、`SessionSummarizer`（会话摘要 Markdown） |
| 工具 | `Read/Write/Edit/Calculator`（框架内置）+ `MemoryTool`（展开 6 个）+ `ExecuteCommandTool`（白名单 + 危险模式拦截 + 目录限制）+ `WebSearchTool`（Brave API）+ `WebFetchTool` |
| 事件 | `StreamEventType`：`AGENT_START/STEP_START/LLM_CHUNK/TOOL_CALL_START/TOOL_CALL_FINISH/STEP_FINISH/AGENT_FINISH/ERROR` |
| 会话 | 每会话 JSON 文件（`sessions/<id>.json`），含 OpenAI 格式 `tool_calls` / `tool_call_id` |

### 1.3 能力差距对照

| 能力 | DeskTopGent | DeskTop-helper | 融合后归属 |
|---|---|---|---|
| 角色人格 | theme.yaml 显示名 | IDENTITY/SOUL/AGENTS.md | Python（C# 注入运行时角色名） |
| 流式输出 | ❌ | ✅ SSE | Python 产生 → C# 渲染 |
| 多轮工具循环 | C# `ChatOrchestrator` | Python `EnhancedSimpleAgent` | **Python**（C# 不再循环） |
| 记忆 | ❌ | ✅ Markdown | Python（按用户目录） |
| 待办 | ✅ SQLite | ❌ | C#（反向暴露为工具） |
| 语音 | ✅ | ❌ | C# |
| 好感度/养成 | ✅ | ❌ | C# |
| 会话记录 | ✅ SQLite | ✅ JSON | **C#**（唯一事实来源） |

---

## 2. 融合目标与边界

**目标**

- `TalkWindow` 获得流式逐字输出 + 工具调用状态行。
- AI 具备长期记忆、人格文件、上下文压缩，且**按用户隔离**。
- 保留现有 `IAiProvider` / MCP 抽象与 **AI Provider 兜底逻辑**（AGENTS.md §7.1 禁止移除）。
- Python 侧完全可独立运行、可测试（`uv run pytest`），不依赖 WPF。

**明确不做**

- ❌ 不引入 Vue 前端（WPF 已是 UI）。
- ❌ 不在 C# 内嵌 Python 解释器。
- ❌ 不恢复 `characters.yaml` / `skins.yaml` / `animations.yaml`。
- ❌ 不改 `logs/` 目录结构（AGENTS.md §2.3）。
- ❌ 不让 Python 直接读写 `chat_history.db`（避免双写）。
- ❌ 不使用 `pet` / `宠物` 术语。

---

## 3. 架构选型

### 3.1 候选方案对比

| 方案 | 优点 | 缺点 | 结论 |
|---|---|---|---|
| **A. HTTP + SSE sidecar** | 语言边界清晰；Python 可独立开发/测试；SSE 天然支持流式；复用 helper 现有 FastAPI 代码 | 需管理子进程生命周期；多一个端口 | ✅ **采用** |
| B. stdio JSON-RPC（MCP 风格） | 无端口、无网络栈 | 需自建多路复用与流式分帧；调试困难；helper 的 FastAPI 代码几乎废弃 | 备用（仅用于工具桥接，见 §7） |
| C. pythonnet 内嵌 | 无 IPC | STA/GIL/ABI/打包/崩溃隔离全面劣化 | ❌ 否决 |
| D. 完全 C# 重写 Agent | 单进程 | 放弃 helper 全部成果，违反融合诉求 | ❌ 否决 |

### 3.2 进程拓扑

```
┌─────────────────────────────── Windows 桌面 ───────────────────────────────┐
│                                                                             │
│  DeskTopGent.exe (WPF, net8.0-windows)                                      │
│  ├─ UI: GentWindow / TalkWindow / TodoWindow …                              │
│  ├─ ChatOrchestrator ──► AiProviderRegistry                                 │
│  │                        ├─ LocalKleeProvider        （离线兜底，保留）      │
│  │                        ├─ OpenAiCompatibleProvider （直连，保留）          │
│  │                        └─ PythonAgentProvider      （新增，默认）          │
│  ├─ McpManager（保留；本地待办工具仍注册）                                     │
│  ├─ AgentSidecarService（新增）                                              │
│  │     ├─ 启动: python.exe -m agent_service.main --port <空闲端口> --token <随机> │
│  │     ├─ 守护: 健康检查 / 自动重启（最多 3 次） / 退出时 kill 进程树             │
│  │     └─ 日志: stdout/stderr → Logger("Agent")                             │
│  └─ AgentToolBridge（新增，HttpListener/Kestrel 127.0.0.1:<另一端口>）         │
│        └─ 把 todo_* 等 C# 能力反向暴露给 Python                                 │
│                                                                             │
│  agent_service (uv .venv, Python 3.13)                                      │
│  └─ FastAPI 127.0.0.1:<port>                                                │
│       ├─ POST /v1/chat/stream   → SSE 事件流                                 │
│       ├─ POST /v1/session/reset → 清空内存历史                                │
│       ├─ GET  /health                                                        │
│       └─ 调用 C# 工具: POST http://127.0.0.1:<toolPort>/tools/invoke          │
└─────────────────────────────────────────────────────────────────────────────┘
```

**为什么工具回调反向走 HTTP 而不是让 Python 直连 SQLite**：待办、金币、好感度等属于 C# 领域模型，且有 `SaveCurrentProfile()` / 缓存失效等副作用；必须由 C# 单点写入（AGENTS.md §4.2）。

---

## 4. 接口契约

### 4.1 HTTP 端点（Python 侧，需 `X-Agent-Token` 校验）

| 方法 | 路径 | 请求体 | 响应 |
|---|---|---|---|
| GET | `/health` | — | `{"status":"ok","version":"...","user":"<name>"}` |
| POST | `/v1/chat/stream` | 见下 | `text/event-stream` |
| POST | `/v1/session/reset` | `{"session_id":"..."}` | `{"ok":true}` |
| POST | `/v1/memory/search` | `{"keyword":"...","limit":5}` | `{"items":[...]}`（供「记忆」设置页用，P4） |

**`/v1/chat/stream` 请求体**

```jsonc
{
  "message": "帮我把明天的会议加进待办",
  "session_id": "csharp-<ChatSession.Id>",     // C# SQLite 会话 ID，用于 Python 侧内存历史键
  "user": "parakeet",
  "role": "默认角色",                            // 当前角色显示名
  "level_hint": "亲密：表达亲近",                 // AffinityService.GetSystemPromptHint 结果
  "system_prompt": "你是桌面助手『默认角色』…",     // C# 运行时身份（最高优先级）
  "history": [                                  // 由 C# 从 SQLite 取出，OpenAI 格式
    {"role":"user","content":"..."},
    {"role":"assistant","content":"...","tool_calls":[...]},
    {"role":"tool","content":"...","tool_call_id":"call_1"}
  ],
  "options": {"temperature": 0.7}
}
```

> **历史以 C# 为准**：Python 侧每轮都接收完整历史（或按 `session_id` 做增量同步），保证进程重启后上下文不丢。
> 为控制带宽，history 只传最近 N 条（建议 N=40）+ 记忆摘要，由 `Config.compression_threshold` 决定。

### 4.2 SSE 事件（与 helper 对齐，C# 侧映射为 `AgentEvent`）

| event | data | C# 处理 |
|---|---|---|
| `session` | `{"session_id":"..."}` | 记录 |
| `step_start` | `{"step":1,"max_steps":6}` | 状态行「思考中…」 |
| `chunk` | `{"content":"增量文本"}` | `OutputBox.AppendText` |
| `tool_start` | `{"tool":"todo_add","args":{...}}` | 状态行「🔧 调用 todo_add」+ Logger |
| `tool_finish` | `{"tool":"todo_add","result":"...","is_error":false}` | 状态行「✅ 完成」+ Logger |
| `step_finish` | `{"step":1}` | 清除状态行 |
| `done` | `{"content":"完整回复","session_id":"..."}` | 收尾 + TTS + 落 SQLite |
| `error` | `{"error":"...","type":"..."}` | `Logger.Error("Agent", ...)` + UI 提示 |

> 事件名沿用 helper `src/api/chat.py`，减少移植改动。

### 4.3 配置扩展（`config/secrets.yaml`，不入库）

```yaml
agent:
  enabled: true                  # 关闭则回退到现有 C# Provider
  auto_start: true               # App 启动时拉起 sidecar
  python_path: ""                # 空=自动探测 <BaseDir>/.venv/Scripts/python.exe
  workspace_root: ""             # 空=<UserDataDir>/users/<name>/agent_workspace
  max_tool_iterations: 8
  request_timeout_seconds: 120
  # 以下密钥透传给 Python 环境变量（禁止写日志）
  brave_api_key: ""
```

`ai_providers` 新增 `type: python-agent`：

```yaml
ai_providers:
  - name: python-agent
    type: python-agent
    description: Python Agent（hello-agents，支持记忆与流式工具调用）
    enabled: true
    model: glm-4-flash           # 实际由 sidecar 内的 LLM 配置决定，此处仅展示
```

> 注意：sidecar 内 LLM 的 `api_key/base_url/model` **不再单独配置**，而是复用 `secrets.yaml` 中已启用的第一个 `openai-compatible` 条目，由 C# 通过启动环境变量注入（`AGENT_LLM_*`），避免密钥存在两份。

### 4.4 数据目录（用户隔离）

```
user_data/users/<name>/
├── profile.yaml
├── chat_history.db              # 会话唯一事实来源（C#）
├── todo.db
└── agent_workspace/             # 新增（Python 专属，按用户）
    ├── IDENTITY.md              # 首次生成时注入当前角色名
    ├── SOUL.md
    ├── USER.md
    ├── MEMORY.md
    ├── AGENTS.md
    ├── memory/YYYY-MM-DD.md
    └── .gent_meta.json          # 记录 schema 版本，便于升级迁移
```

**不再使用 `~/.helloclaw/`**（helper 的全局路径）——违反 AGENTS.md §4.1 的用户隔离。

---

## 5. C# 侧改造

### 5.1 新增文件

| 文件 | 职责 |
|---|---|
| `Services/AI/Providers/PythonAgentProvider.cs` | 实现 `IAiProvider` + 新增 `IStreamingAiProvider`；POST SSE 并解析 |
| `Services/AI/AgentSidecarService.cs` | 子进程生命周期、端口/令牌生成、健康检查、重启、日志转发 |
| `Services/AI/AgentSseClient.cs` | 轻量 SSE 解析（.NET 8 无内置客户端） |
| `Services/AI/AgentToolBridge.cs` | 反向 HTTP 端点，供 Python 调用 C# 工具 |
| `Services/AI/AgentEvent.cs` | 事件 DTO |

### 5.2 修改点（最小）

| 文件 | 改动 |
|---|---|
| `Services/AI/IAiProvider.cs` | 追加 `IStreamingAiProvider` 接口（不改现有接口，避免破坏兼容） |
| `Services/AI/ChatOrchestrator.cs` | 追加 `TalkStreamAsync(...)`；`SystemPrompt` 增加记忆摘要占位（P4） |
| `App.xaml.cs` | `RegisterAiProviders()` 增加 `python-agent` 分支（**保留兜底顺序**）；登录后启动 sidecar |
| `Windows/Talk/TalkWindow.xaml.cs` | `ProcessAsync` 改为流式消费；新增状态行控件 |
| `Windows/Settings/ModelSettingsWindow.xaml.cs` | `Describe()` 增加 Python 分支 |
| `config/secrets.yaml.example` | 增加 §4.3 示例 |

### 5.3 代码骨架

**新接口（追加，不修改现有）**

```csharp
namespace DesktopGent.Services.AI;

/// <summary>支持流式事件 + 内部工具循环的 AI 提供方（Python sidecar 专用）。</summary>
public interface IStreamingAiProvider
{
    IAsyncEnumerable<AgentEvent> ChatStreamAsync(AiRequest request, CancellationToken ct = default);
}
```

**Provider 骨架**

```csharp
public sealed class PythonAgentProvider : IAiProvider, IStreamingAiProvider
{
    public string Name { get; set; } = "python-agent";

    /// <summary>工具循环在 Python 内部完成，C# 侧不再注入 MCP 工具。</summary>
    public bool SupportsToolCalling => false;

    /// <summary>sidecar 内部模型名（仅用于 UI 展示）。</summary>
    public string DisplayModel { get; set; } = "";

    private readonly HttpClient _http;

    /// <summary>非流式入口：消费完整事件流后返回最终文本（供 ChatOrchestrator 兼容路径）。</summary>
    public async Task<AiResponse> ChatAsync(AiRequest request, CancellationToken ct = default)
    {
        var sb = new StringBuilder();
        await foreach (var ev in ChatStreamAsync(request, ct).ConfigureAwait(false))
            if (ev.Kind == AgentEventKind.Chunk) sb.Append(ev.Text);
        return new AiResponse { Content = sb.ToString() };
    }

    public async IAsyncEnumerable<AgentEvent> ChatStreamAsync(
        AiRequest request, [EnumeratorCancellation] CancellationToken ct = default)
    {
        // 1) 组装 JSON（message/session_id/user/role/system_prompt/history/options）
        // 2) HttpCompletionOption.ResponseHeadersRead 发 POST
        // 3) AgentSseClient 逐事件 yield
        // 4) 非 2xx / 连接失败 → Logger.Error("Agent", ...) 并抛出，由调用方回退
    }
}
```

**SSE 客户端要点**（`.NET 8` 无 `EventSource`，需手写）

```csharp
// 逐行读取；空行作为事件分隔；解析 "event: xxx" 与 "data: json"
// 关键：使用 ResponseHeadersRead + StreamReader，避免整包缓冲导致"伪流式"
using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
resp.EnsureSuccessStatusCode();
await using var stream = await resp.Content.ReadAsStreamAsync(ct);
using var reader = new StreamReader(stream, Encoding.UTF8);
```

**Sidecar 守护要点**

```csharp
// 端口：TcpListener(IPAddress.Loopback, 0) 获取空闲端口后立即释放，再传给 uvicorn
// 令牌：RandomNumberGenerator.GetHexString(32)
// 启动参数：-m agent_service.main --host 127.0.0.1 --port {port} --token {token}
//           --workspace {userWorkspace} --user {name} --role {role}
// 环境变量：AGENT_LLM_API_KEY / AGENT_LLM_BASE_URL / AGENT_LLM_MODEL / BRAVE_API_KEY
// 健康检查：最多 30s 轮询 /health（间隔 300ms），失败则 Logger.Error 并禁用 agent
// 退出：Kill(entireProcessTree: true)；Dispose 时确保调用（AGENTS.md §6.6）
```

**`App.RegisterAiProviders` 插入（保留兜底）**

```csharp
else if (p.Type == "python-agent")
{
    if (!secrets.Agent.Enabled) { Logger.Warn("App", "agent.enabled=false，跳过 python-agent"); continue; }
    registry.Register(new PythonAgentProvider { /* 依赖 AgentSidecarService 的地址与令牌 */ });
    anyRegistered = true;
    Logger.Info("App", $"注册 AI 提供商（python-agent）：{p.Name}");
}
// 原有「无任何 provider → 环境变量 → LocalKleeProvider」兜底顺序保持不变
```

---

## 6. Python 侧改造

### 6.1 目录（新建 `agent/`，不改动 `DeskTop-helper/`）

```
agent/
├── pyproject.toml               # uv 项目；依赖固定版本
├── agent_service/
│   ├── __init__.py
│   ├── main.py                  # FastAPI 入口 + 参数解析 + 令牌校验
│   ├── config.py                # 环境变量/命令行 → Settings
│   ├── events.py                # SSE 事件模型（与 §4.2 一一对应）
│   ├── runtime.py               # AgentRuntime（每个用户一个实例，懒加载）
│   ├── history.py               # 由 C# 传入的 history → hello-agents Message
│   ├── tools/
│   │   ├── __init__.py
│   │   ├── memory.py            # 移植自 helper tools/builtin/memory.py
│   │   ├── exec.py              # 移植自 execute_command.py（保留白名单/危险模式）
│   │   ├── web.py               # 移植自 web_search.py / web_fetch.py
│   │   └── remote.py            # 新增：C# 工具代理（RemoteTool）
│   ├── memory/
│   │   ├── capture.py           # 移植
│   │   ├── flush.py             # 移植
│   │   └── summarizer.py        # 移植（P4）
│   └── workspace/
│       ├── manager.py           # 移植并改为 per-user
│       └── templates/*.md       # 移植，角色名占位 {role}
└── tests/
    ├── test_events.py
    ├── test_workspace.py
    └── test_agent_loop.py       # 用 FakeLLM，不联网
```

> `DeskTop-helper/` **保留为只读参考**，不参与构建，不被 `agent/` import（两侧都有 `src` 包名会冲突）。
> 建议在 `DeskTop-helper/README.md` 顶部加一行「已迁移至 ../agent，本目录仅作参考」。

### 6.2 移植映射表

| helper 文件 | 目标 | 必改点 |
|---|---|---|
| `src/agent/enhanced_llm.py` | `agent_service/llm.py` | 见 §6.4-1 |
| `src/agent/enhanced_simple_agent.py` | `agent_service/agent.py` | 见 §6.4-2；去掉 `print` 改 `logging` |
| `src/agent/helloclaw_agent.py` | `agent_service/runtime.py` | 改名 `GentAgent`；去掉 session 文件；注入 C# 历史 |
| `src/workspace/manager.py` | `agent_service/workspace/manager.py` | 去掉 `~/.helloclaw` 硬编码；`workspace_path` 必传 |
| `src/memory/*.py` | `agent_service/memory/*.py` | `print` → `logging`；异常不吞关键信息 |
| `src/tools/builtin/*.py` | `agent_service/tools/*.py` | 保持白名单；`ExecuteCommandTool` 允许目录收敛到 workspace |
| `src/api/chat.py` | `agent_service/main.py` 的 SSE 路由 | 事件名保持；增加 `user`/`role`/`system_prompt` |
| `src/api/session.py` / `memory.py` | P4 最小子集 | 会话由 C# 负责，仅保留记忆检索 |

### 6.3 运行链路骨架

```python
# runtime.py（核心）
class GentAgent:
    def __init__(self, settings: Settings, csharp_tools: RemoteToolProvider):
        self.workspace = WorkspaceManager(settings.workspace_path)   # per-user
        self.workspace.ensure_workspace_exists(role=settings.role)
        self.tool_registry = self._setup_tools(csharp_tools)
        self.agent = EnhancedSimpleAgent(
            name=settings.role,
            llm=EnhancedHelloAgentsLLM(model=..., api_key=..., base_url=...),
            tool_registry=self.tool_registry,
            system_prompt=self._build_system_prompt(settings.system_prompt),
            config=Config(session_enabled=False, context_window=128_000,
                          compression_threshold=0.8, min_retain_rounds=10),
            max_tool_iterations=settings.max_tool_iterations,
        )

    async def chat_stream(self, message: str, history: list[dict], **kw):
        self.agent.history = build_messages(history)      # 以 C# 历史为准
        async for ev in self.agent.arun_stream_with_tools(message, **kw):
            yield to_sse_event(ev)
```

**系统提示拼装优先级**（写入 `_build_system_prompt`）：

```
1. C# 传入 system_prompt（运行时身份 + 好感度等级提示）  ← 最高
2. workspace/IDENTITY.md（长期人格）
3. workspace/USER.md（用户画像）
4. workspace/SOUL.md（语气模板）
5. workspace/MEMORY.md（长期记忆）
6. 跨会话摘要（memory/*.md 最近摘要，P4）
```

### 6.4 已定位的兼容性问题（**必须在移植时修复**）

> 以下为本次在 `hello-agents==1.0.1` 上实际运行得到，不是推测。

**1）流式工具调用的类型判定**

`EnhancedSimpleAgent` 用 `isinstance(llm, EnhancedHelloAgentsLLM)` 决定是否走流式工具路径。
1.0.1 的基类 `HelloAgentsLLM` 已自带 `astream_invoke_with_tools`，但产出的是 `str` / `StreamStats`，
**不是** helper 定义的 `StreamToolEvent`。因此仍必须使用 `EnhancedHelloAgentsLLM` 覆盖实现，
且**不能**替换为「直接调用基类」——否则 `get_last_stream_tool_result()` 返回 `None`，循环会静默退出。

**2）非流式回退路径在 async 中会抛异常**

`enhanced_simple_agent.py` 的回退分支调用 `self.run(input_text)`：
1.0.1 中 `SimpleAgent.run` 内部走 `sync_events(...)`，在已运行的事件循环里会抛
`RuntimeError: 同步入口不能在运行中的事件循环内调用，请使用 await arun 或 arun_stream`。
→ **修复**：回退改为 `await self.arun(...)`，或直接删除回退分支（本方案只使用增强 LLM，回退无意义）。

**3）`_build_tool_schemas` / `_history` 等私有成员已变动**

1.0.1 的 `SimpleAgent` 提供 `get_history()` / `add_message()` / `clear_history()`，
`_build_tool_schemas` 仍存在但签名与语义可能随版本变化。
→ **修复**：移植时优先用公开方法；对私有成员（`_history`）的访问收敛到 `agent_service/agent.py` 一处适配层，便于后续升级。

**已验证正常的部分**：`Tool(name, description, expandable)`、`ToolResponse.success/error`、
`Message(content, role, metadata=)`、`Config(...)` 字段、`ToolRegistry.register_tool`、
`ReadTool/WriteTool/EditTool(project_root=)`、`hello_agents.tools.tool_action` —— 全部与 helper 用法一致。

---

## 7. 工具桥接

### 7.1 方向一：Python → C#（主方案，P3）

C# 启动 `AgentToolBridge`（`HttpListener`，`127.0.0.1` 随机端口，令牌校验），暴露：

| 路径 | 请求 | 说明 |
|---|---|---|
| `GET /tools` | — | 返回工具清单（name/description/JSON Schema） |
| `POST /tools/invoke` | `{"name":"todo_add","args":{...}}` | 内部走 `McpManager.InvokeToolAsync` |

Python 侧 `RemoteTool` 启动时拉取清单并注册到 `ToolRegistry`，调用即 POST。

**好处**：C# 现有 `McpManager` / `LocalTodoMcpServer` 零改动即可复用；
日后新增 C# 侧 MCP 工具自动对 Python 可见。

### 7.2 方向二：Python → C#（备选，P6 可选）

若希望完全走 MCP 标准：新增 `DesktopGent.AgentHost --mcp-stdio` 入口，把 `McpManager` 以 stdio JSON-RPC 暴露，
Python 用 `hello_agents.tools.MCPConnection` 消费。**但需额外安装 `mcp` 包**（`hello-agents` 默认未装，已实测 `No module named 'mcp'`），
且需要 C# 侧实现 JSON-RPC 服务端（现有的 `StdioMcpServer` 是客户端）。**不作为首版范围。**

### 7.3 方向三：C# 工具在 Python 内的执行边界

- `exec` 工具：`allowed_directories` **仅允许** `agent_workspace`，禁止 `BaseDir`、`user_data` 根、系统目录。
- 文件读写（Read/Write/Edit）：`project_root=agent_workspace`。
- `web_search`：无 `BRAVE_API_KEY` 时返回明确错误，不静默失败。
- 所有工具调用结果按 `tool_output_max_bytes` 截断（hello-agents Config 已有该字段）。

---

## 8. 记忆与用户隔离

| 关注点 | 方案 |
|---|---|
| 目录 | `user_data/users/<name>/agent_workspace/`（§4.4） |
| 切换用户 | sidecar 重启（传入新 `--workspace` 与 `--user`），避免内存串号 |
| 角色切换 | 不重启；`system_prompt` 与 `role` 每轮由 C# 传入，Python 重建提示 |
| 记忆写入时机 | 每轮对话结束后 `capture_and_store`；上下文达阈值触发 flush（沿用 helper） |
| 去重 | 沿用关键词重合度 0.7 阈值 |
| 清理 | 保留 30 天，P4 提供设置页入口（可选） |
| 隐私 | 记忆仅落本地 Markdown；**禁止**写入日志全文（只记条数与类别） |

---

## 9. 分阶段实施计划

> 每阶段结束必须验收。**注意：本机为 Linux 且未安装 .NET SDK，C# 编译验收需在 Windows 上执行 `dotnet build`。**

### P0 — Python 工程骨架（0.5 天）

1. 根目录 `pyproject.toml`（uv）：

```toml
[project]
name = "desktop-gent-agent"
version = "0.1.0"
requires-python = ">=3.12,<3.14"
dependencies = [
  "hello-agents==1.0.1",
  "fastapi>=0.109",
  "uvicorn[standard]>=0.27",
  "sse-starlette>=2.0",
  "httpx[socks]>=0.28",
  "python-dotenv>=1.0",
]
[dependency-groups]
dev = ["pytest>=8", "pytest-asyncio>=0.24", "ruff>=0.6"]
```

2. `agent_service/main.py` 提供 `/health`；`main.py` 支持 `--host/--port/--token/--workspace/--user/--role`。
3. `.gitignore` 追加 `.venv/`、`agent_service/__pycache__/`、`*.pyc`。
4. 验收：`.venv/bin/uv run uvicorn ...` 后 `curl /health` 返回 `{"status":"ok"}`；`uv run ruff check` 通过。

### P1 — Agent 核心可跑（1–2 天）

1. 移植 `workspace/manager.py`、`memory/capture.py`、`memory/flush.py`、`tools/{memory,exec,web}.py`。
2. 移植 `enhanced_llm.py` / `enhanced_simple_agent.py`，**修复 §6.4 的 1、2 两处**。
3. 验收：
   - `tests/test_agent_loop.py` 用 `FakeLLM` 跑通「请求工具 → 执行 → 最终回答」，事件序列与预期一致。
   - 不联网、不访问 `~/.helloclaw`（断言路径全部落在传入的临时 workspace）。

### P2 — SSE 服务 + C# 接入（2–3 天）

1. `POST /v1/chat/stream` 输出 §4.2 事件；令牌校验；`/v1/session/reset`。
2. C#：`AgentSidecarService` + `AgentSseClient` + `PythonAgentProvider` + `App` 注册 + `secrets.yaml` 扩展。
3. 验收：
   - Windows 下 `dotnet build` **0 错误 0 警告**。
   - 应用启动日志出现「Python Agent sidecar 就绪，端口=…」。
   - `TalkWindow` 能收到流式增量（可先只在输出区追加）。
   - 关闭应用后子进程消失（任务管理器核对）。
   - 故意杀掉 Python 进程 → C# 记录 `Logger.Error("Agent", ...)`，UI 提示并回退 `LocalKleeProvider`，不崩溃。

### P3 — 工具桥接（1–2 天）

1. C# `AgentToolBridge`（`/tools`、`/tools/invoke`）。
2. Python `RemoteTool` + `RemoteToolProvider`，启动时拉取并注册。
3. 验收：聊天里说「帮我加待办：明天买牛奶」→ Python 调用 `todo_add` → `todo.db` 出现记录 → `TodoWindow` 可见；日志含 `[Agent] 调用工具 todo_add`。

### P4 — 记忆与人格（2 天）

1. 首次登录生成 workspace 模板（`IDENTITY.md` 注入角色名）。
2. 提示拼装（§6.3 优先级）；记忆捕获/检索；会话摘要（`summarizer.py`）。
3. 验收：
   - 说「记住我喜欢简洁回复」→ 次日记忆文件出现 `[preference]` 条目。
   - 重启应用后问「我喜欢什么风格」→ 回答体现记忆。
   - 切换用户后不串记忆（两个用户目录互不可见）。

### P5 — 流式 UI 与体验（1 天）

1. `TalkWindow` 增加状态行（`🔧 tool → ✅ done`），增量写入，自动滚动。
2. 取消支持：窗口关闭/再次发送时 `CancellationTokenSource.Cancel()`。
3. TTS 改为**整段完成后朗读**（避免逐字朗读）。
4. 验收：长回答逐字出现；工具状态行出现后自动消失；取消后无残留任务。

### P6 — 打包与运维（1–2 天）

1. 首启动引导：检测 `.venv` 缺失 → 提示执行 `uv sync`（或随包分发 `.venv`，二选一）。
2. 日志：Python `logging` → stdout → C# `Logger("Agent")`；文件仍写 `user_data/logs/app_YYYYMMDD.log`。
3. 文档：新增 `docs/Python-Agent.md`；更新 `docs/AI与MCP.md`、`docs/聊天.md`、`README.md`。
4. 验收：全新机器按文档 3 步内跑起来；`dotnet build` 与 `uv run pytest` 均通过。

**总计约 9–12 人日。**

---

## 10. 构建与运行命令

```bash
# 1) 创建/同步 Python 环境（已在项目根执行过 venv 创建）
uv venv --python 3.13 .venv
UV_PROJECT_ENVIRONMENT=.venv uv sync --project agent --locked

# 2) 独立调试 sidecar
cd agent
../.venv/bin/python -m agent_service.main --host 127.0.0.1 --port 18000 --token dev \
  --workspace ../user_data/users/parakeet/agent_workspace --user parakeet --role 默认角色

# 3) 冒烟
curl -s http://127.0.0.1:18000/health -H 'X-Agent-Token: dev'

# 4) C# 构建（Windows；Linux 用 -p:EnableWindowsTargeting=true 仅做编译校验）
dotnet build DesktopGent.csproj
```

> 说明：`uv sync --project agent` 用 `agent/uv.lock` 保证可复现；
> 用 `UV_PROJECT_ENVIRONMENT` 把依赖装到项目根 `.venv`，让 C# 的 `ResolvePython` 能自动探测到。

**分发形态**（二选一，P6 决策）：

| 方式 | 优点 | 缺点 |
|---|---|---|
| 随包分发已 `uv sync` 的 `.venv` | 用户零操作 | 体积大（~200MB），路径不可重定位需用 venv 的 relocatable 方案 |
| 首启动执行 `uv sync` | 体积小 | 依赖用户网络与 uv 已安装 |

> 建议：开发期用方式二；发行版用方式一并提供「修复环境」按钮。

---

## 11. 风险、回滚与待决问题

| 风险 | 影响 | 缓解 |
|---|---|---|
| hello-agents 小版本 API 变动 | 移植后运行期异常 | 锁定 `==1.0.1`；私有成员访问收敛到适配层；`tests/test_agent_loop.py` 做版本护栏 |
| sidecar 启动慢（导入依赖约 1–3s） | 首次聊天卡顿 | App 启动即拉起；`/health` 就绪后再注册 Provider |
| 端口/令牌泄漏 | 本机其他进程可调用 | 仅 `127.0.0.1`、随机令牌、每次启动更换 |
| Python 崩溃 | 聊天不可用 | 自动重启（≤3 次）；失败回退 `LocalKleeProvider`；UI 明示 |
| 双会话体系冲突 | 历史不一致 | C# SQLite 唯一事实来源；Python `session_enabled=False` |
| 记忆含敏感信息 | 隐私 | 仅本地文件；日志只记条数；提供清空入口 |
| 打包体积 | 发行困难 | 见 §10 两方案 |

**回滚策略**：`secrets.yaml` 中 `agent.enabled: false` → 立即回到纯 C# 链路（`ChatOrchestrator` + `OpenAiCompatibleProvider`），
无需回滚代码。这是选用 sidecar 而非内嵌的核心收益。

**待决问题（需产品决策）**

1. 发行版是否随包分发 `.venv`？（影响体积与首启体验）
2. 是否允许 Agent 使用 `execute_command`？（默认仅 workspace 内白名单命令；可默认关闭）
3. 记忆是否需要「设置页可视化编辑」？（当前方案仅文件级）
4. `python-agent` 是否设为**默认** Provider？（建议：首次配置在线模型后设为默认）

---

## 12. 本次环境验证记录

> 用于说明方案中的兼容性结论来源。

**工具链**：`uv 0.13.0`、`Python 3.13.11`、Node 24（未使用）、**`dotnet` 未安装（本机无法编译 C#）**。

**已执行**

1. 在项目根创建虚拟环境：`uv venv --python 3.13 .venv`（成功）。
2. 安装并解析依赖（成功）：
   `hello-agents 1.0.1`、`fastapi 0.163.1`、`uvicorn 0.54.0`、`sse-starlette 3.7.0`、`openai 1.109.1`。
   `hello-agents` 元数据要求 `>=3.12,<3.14`，与 3.13 兼容。
3. 用该环境**导入 DeskTop-helper 全部模块**：`workspace.manager` / `memory.capture` / `memory.flush` /
   `tools.builtin.memory` / `agent.enhanced_llm` / `agent.enhanced_simple_agent` / `agent.helloclaw_agent` /
   `src.main:app` —— **全部成功**（无需改代码即可 import）。
4. 离线实例化 `HelloClawAgent`（临时 workspace，假 API Key）：成功；工具展开为
   `Read / Write / Edit / python_calculator / memory_* (6) / exec_* (3) / search_web / fetch_url / Task`。
5. 运行 `EnhancedSimpleAgent.arun_stream_with_tools`（注入 FakeLLM）：**暴露 §6.4-1 与 §6.4-2 两个问题**
   （isinstance 判定导致回退、回退中 `self.run()` 在事件循环内抛错）。已给出修复方向。
6. 核对 hello-agents 1.0.1 公开 API：`Tool` / `ToolResponse.success|error` / `Message` / `Config` /
   `ToolRegistry.register_tool` / `ReadTool|WriteTool|EditTool(project_root=)` / `tool_action` —— 与 helper 用法一致。
7. 核对 MCP：`hello_agents.tools.MCPConnection` 存在，但 `mcp` 包**未随 hello-agents 安装**
   （`No module named 'mcp'`）→ §7.2 备选方案需显式加依赖，首版不采用。

**未执行**：C# 编译与运行（本机无 .NET SDK，且 WPF 为 Windows-only）；Windows 侧 Python 路径探测
（`.venv/Scripts/python.exe` vs Linux 的 `.venv/bin/python`）在 P2 实现时需分支处理。

### 12.1 实施后补充验证（2026-11-17）

后续在本机临时安装了 .NET 8 SDK（8.0.428），因此**补齐了 C# 侧验证**：

| 验证项 | 命令 | 结果 |
|---|---|---|
| C# 全项目编译 | `dotnet build DesktopGent.csproj -p:EnableWindowsTargeting=true` | **0 error / 0 warning** |
| Python 单元测试 | `.venv/bin/python -m pytest -q` | **63 passed** |
| Python 静态检查 | `.venv/bin/python -m ruff check agent_service tests` | **All checks passed** |
| Python → C# 工具桥（真实进程） | 真实 `AgentToolBridge` + `LocalTodoMcpServer` + SQLite，由 `RemoteToolbox` 调用 | 列举到 `todo_list/add/complete/delete`；`todo_add` 写入成功并可读回 |
| sidecar 自动加载远程工具 | `python -m agent_service.main --tool-bridge-url ...` | 日志：`从 C# 工具桥加载 4 个工具`，`/health` 返回 `tools: 9` |

`EnableWindowsTargeting=true` 只用于**编译期**校验（引用 WindowsDesktop 引用程序集），运行时仍需 Windows。

实施中由编译器/联调发现并修复的问题：
1. `AgentSidecarService` 缺 `using System.Net.Http`（编译错误，静态审查未发现）。
2. `App.RegisterAiProviders` 早期就把 `python-agent` 选为默认 → sidecar 尚未就绪，首条消息必然失败。
   改为启动阶段显式回退到其他提供方，健康检查通过后再切换。
3. FastAPI `@app.on_event("startup")` 已弃用 → 迁移到 `lifespan`（消除 15 条弃用警告）。
4. 工具桥首版用 `HttpListener` → Windows 非管理员需 URL ACL，改为 `TcpListener` 自实现 HTTP/1.1。


---

## 附录 A：C# ↔ Python 术语对照

| helper | 本方案 |
|---|---|
| `HelloClawAgent` | `GentAgent` |
| `helloclaw` / `~/.helloclaw` | `agent_service` / `user_data/users/<name>/agent_workspace` |
| `workspace_path` | `settings.workspace_path`（必传） |
| `print(...)` | `logging.getLogger("gent.agent")` |
| `session`（JSON 文件） | C# `chat_history.db`（Python 内存态仅缓存） |

## 附录 B：AGENTS.md 合规检查

- [x] 术语统一 `gent` / `助手`（移植时同步改名）
- [x] 用户数据全部写入 `CurrentUserDir`（§4.4）
- [x] 保留 AI Provider / MCP 兜底逻辑（§5.3）
- [x] 日志走 `Logger`，category 新增 `Agent`（PascalCase）
- [x] 不硬编码颜色/数值/路径（端口、令牌、时限均运行时生成或配置）
- [x] 退出时 kill 子进程树（§6.6 对应）
- [x] 不改 `logs/` 目录结构与命名
- [x] `secrets.yaml` 不入库，新增字段同步 `secrets.yaml.example`
- [x] 不恢复已废弃配置与服务
