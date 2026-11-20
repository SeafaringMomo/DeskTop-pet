# DeskTopGent Python Agent 微服务

DeskTopGent 的 Agent 推理层：独立可运行的 Python 服务，由 C# 主进程以 **sidecar 子进程**方式拉起，
通过本地 HTTP + SSE 通信。

## 架构

```
api        HTTP/SSE 协议转换（FastAPI）
  ↓
services   用例编排（ChatService）
  ↓
core       纯逻辑（AgentLoop / PromptBuilder / Settings）
  ↓
ports      抽象接口（LLMPort / ToolPort / MemoryPort / WorkspacePort）
  ↑
adapters   具体实现（OpenAI / Markdown 文件 / 工具集）
```

依赖方向严格单向：**内层不感知外层**。`core` 不导入 `openai` / `fastapi`，
因此核心循环可以用假实现完整单元测试。

### 为什么不直接用 hello-agents 的 Agent 类

1. `hello-agents==1.0.1` 的 `SimpleAgent` 流式工具链与本项目需求有 2 处不兼容（详见 `docs/Python-Agent融合方案.md` §6.4）：
   - 非流式回退路径在运行中的事件循环里调用同步入口会抛 `RuntimeError`
   - 基类 `astream_invoke_with_tools` 返回的是 `str`/`StreamStats`，不是工具事件，直接在基类上跑会让工具循环静默退出
2. 本项目只需要「流式 + 工具调用 + 记忆」，自建 200 行循环比跨版本适配私有 API 更稳
3. `hello-agents` 仍作为依赖保留，未来若需要其 RAG/多智能体能力可独立接入

## 快速开始

```bash
# 项目根目录执行（依赖锁定在 agent/uv.lock，可复现）
uv venv --python 3.13 .venv
UV_PROJECT_ENVIRONMENT=.venv uv sync --project agent --locked

# 启动（独立调试）
cd agent
AGENT_LLM_MODEL=glm-4-flash \
AGENT_LLM_API_KEY=sk-xxx \
AGENT_LLM_BASE_URL=https://open.bigmodel.cn/api/paas/v4 \
../.venv/bin/python -m agent_service.main \
  --port 18000 --token dev \
  --workspace /tmp/gent_ws --user tester --role 小助手

# 健康检查
curl -H 'X-Agent-Token: dev' http://127.0.0.1:18000/health
```

> `agent/uv.lock` 必须入库：它固定了 45 个包的精确版本，避免不同机器解析出不同依赖。

## 测试

```bash
cd agent
../.venv/bin/python -m pytest -q      # 70 个用例
../.venv/bin/python -m ruff check agent_service tests
```

测试不联网、不依赖 C#：

| 用例文件 | 覆盖内容 |
|---|---|
| `test_agent_loop.py` | 纯文本、工具调用闭环、参数非法、轮数耗尽、LLM 故障、空回复 |
| `test_messages.py` | 历史归一化（脏 role、缺 tool_call_id、tool_calls JSON 字符串、截断、工具调用配对修复） |
| `test_workspace_memory.py` | 工作空间初始化幂等、记忆捕获/去重/检索、提示组装优先级 |
| `test_tools.py` | 命令白名单与危险模式拦截、工具组合与冲突、远程工具降级 |
| `test_bridge.py` | C# 工具桥协议（清单/调用/鉴权/延迟加载 rebuild） |
| `test_api.py` | 鉴权、SSE 事件契约、错误路径仍收尾、记忆检索端点 |

## 对外接口

| 方法 | 路径 | 说明 |
|---|---|---|
| GET | `/health` | 返回 status / user / role / tools / model |
| POST | `/v1/chat/stream` | SSE 流式对话 |
| POST | `/v1/session/reset` | 会话重置（无服务端状态，仅日志） |
| POST | `/v1/memory/search` | 记忆检索 |

所有端点需请求头 `X-Agent-Token`。

### SSE 事件契约

| event | data |
|---|---|
| `session` | `{"session_id"}` |
| `step_start` | `{"step","max_steps"}` |
| `chunk` | `{"content","step"}` |
| `tool_start` | `{"tool","args"}` |
| `tool_finish` | `{"tool","result","is_error"}` |
| `step_finish` | `{"step"}` |
| `done` | `{"content","session_id"}` |
| `error` | `{"error","type"}` |

> 事件名与 C# 侧 `Services/AI/AgentEvent.cs` 一一对应，改任一侧必须同步另一侧。

## 工具

| 工具 | 来源 | 说明 |
|---|---|---|
| `memory_search` / `memory_add` / `memory_update_longterm` / `memory_list` | 本地 | 记忆读写 |
| `exec_run` | 本地 | 白名单命令（默认关闭，需 `--enable-exec`） |
| `web_fetch` / `web_search` | 本地 | 网页抓取/搜索（后者需 `BRAVE_API_KEY`） |
| `todo_*` | 远程 C# | 经工具桥调用 `McpManager`，由 C# 写入 SQLite |

命名冲突时**本地优先**（`CompositeToolbox` 先到先得并记警告）。

## 数据目录

```
<用户目录>/agent_workspace/
├── IDENTITY.md / SOUL.md / USER.md / MEMORY.md / AGENTS.md   # 人格与记忆
└── memory/YYYY-MM-DD.md                                       # 每日记忆
```

会话历史**不落 Python 侧**：由 C# 每轮随请求传入（SQLite 是唯一事实来源）。

## 日志

统一写 stdout，由 C# 侧 `AgentSidecarService` 捕获并转发到 `user_data/logs/app_YYYYMMDD.log`
（category=`Agent`）。日志包含：启动参数（不含密钥）、工具调用、记忆条数、错误堆栈。
