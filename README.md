# DesktopGent 桌面助手

一个基于 WPF (.NET 8) 的桌面助手（Agent）应用。项目主方向是**制作并运行 Agent**，外观与养成作为辅助：每个 Agent 以「角色」形式存在，外观来自 `static/<角色目录>/` 下的像素图，角色可被用户导入、切换与养成。

当前版本保留了**原地呼吸循环**与**随机扣鼻孔**两种帧动画；喂食、抚摸、击掌、聊天、待办等操作只更新状态（饱食度、好感度、金币），不播放响应动画。

## 特性

- **角色目录驱动**：角色不再由配置文件声明，而是扫描 `static/` 下含 `theme.yaml` 的目录自动发现。
- **角色外观**：`theme.yaml` 定义显示名与主题配色；`01_idle_breath/` 与 `02_idle_nose_pick/` 子文件夹提供动作帧。
- **角色导入**：托盘菜单 → 设置 → 「导入助手角色」，选择含 `theme.yaml` 的文件夹复制到 `static/`，无需重启即可切换。
- **助手管理**：商店（金币充值 + 食物购买）、互动（好感度）、喂食（饱食度）、状态（总览）。
- **养成系统**：好感度等级影响 AI 对话风格；饱食度、金币、好感度、背包存于用户档案。
- **AI 对话**：通用 Provider 接口，支持本地 `local-klee`、`openai-compatible` 与 **`python-agent`（流式）**；聊天历史持久化。
- **Python Agent（混合开发）**：C# 负责 UI、养成与持久化，Python sidecar 负责 Agent 推理——流式逐字输出、工具调用闭环、长期/每日记忆、人格文件。
- **MCP 能力**：`local` / `stdio` / `sse` 三种传输类型，内置 `local-todo` 待办工具；待办能力同时经**工具桥**反向暴露给 Python Agent。
- **多用户隔离**：`user_data/users/<name>/` 下独立保存档案、聊天记录、待办数据库与 Agent 工作空间。
- **运维能力**：单实例检测、全局异常兜底、开机自启、用户档案 ZIP 导入导出、日志按天滚动。

## 目录结构

```
DesktopGent/
├── App.xaml(.cs)                # 应用入口、异常兜底、AI/MCP 注册、启动流程
├── app.manifest                 # 高 DPI 清单
├── config/
│   ├── app.yaml                 # 应用常量（动画间隔、窗口边距）
│   ├── gent_management.yaml     # 养成常量（奖励/冷却/衰减/等级）
│   ├── foods.yaml               # 食物定义
│   ├── secrets.yaml.example     # 敏感配置模板（复制为 secrets.yaml）
│   └── secrets.yaml             # API_KEY / MCP 配置（不入库）
├── Models/                      # 配置、档案、食物、待办等模型
├── Services/
│   ├── Core/                    # Config/User/Affinity/Feed/Interaction/Mood/Logger 等
│   ├── AI/                      # AI Provider、聊天编排、Agent sidecar 守护、工具桥、SSE
│   ├── Data/                    # SQLite 仓库（待办、聊天历史）
│   └── MCP/                     # MCP 服务器
├── agent/                       # Python Agent 微服务（独立可运行，见 agent/README.md）
│   ├── agent_service/           # domain / ports / core / adapters / tools / services / api
│   └── tests/                   # pytest（70 个用例，不联网）
├── Windows/
│   ├── Gent/                    # 桌面助手主窗口
│   ├── GentManager/             # 助手管理窗口（商店/互动/喂食/状态）
│   ├── Launcher/                # 启动页（选择/新建用户）
│   ├── Settings/                # 设置（开机自启、档案导入导出、角色导入）
│   ├── Talk/                    # 聊天窗口
│   ├── Todo/                    # 待办窗口
│   └── Chat/                    # 聊天历史
├── static/
│   └── default_role/            # 默认角色
│       ├── theme.yaml           # 显示名 + 主题配色
│       ├── icon.png             # 托盘图标
│       ├── 01_idle_breath/      # 原地呼吸循环帧
│       └── 02_idle_nose_pick/   # 随机扣鼻孔帧
└── user_data/                   # 运行时生成（日志、用户档案、数据库）
```

## 角色与动作

### 角色目录

每个角色是 `static/` 下的一个目录，必须包含固定文件 `theme.yaml`：

```yaml
name: 默认角色   # 角色显示名（在菜单中显示）
primary: "#F5D547"
accent: "#E74C3C"
background: "#3D2817"
surface: "#5C3A1E"
text: "#FFF3D9"
border: "#F5D547"
```

目录下的子文件夹名带 `NN_` 数字前缀，表示动作：

| 目录 | 动作 | 播放方式 |
|---|---|---|
| `01_idle_breath/` | 原地呼吸 | 空闲时循环播放全部帧 |
| `02_idle_nose_pick/` | 随机扣鼻孔 | 随机间隔触发一次后回到呼吸 |

帧文件为顺序命名的 `*.png`（如 `01.png`、`02.png`）。新增角色只需复制 `default_role` 结构并改 `theme.yaml`。

### 导入角色

1. 托盘菜单 → 设置 → 「导入助手角色」。
2. 选择包含 `theme.yaml` 的文件夹。
3. 程序复制到 `static/<文件夹名>/` 并刷新角色菜单。

## 助手管理

右键助手 → 「助手管理」，共四个页签：

| 页签 | 功能 |
|---|---|
| 商店 | 金币充值（+100/+500/+1000）与食物购买（金币购买后进入背包） |
| 互动 | 抚摸 / 击掌增加好感度（带冷却），只更新状态 |
| 喂食 | 展示背包食物与当前饱食度，喂食扣背包、加饱食度、按偏好加好感度 |
| 状态 | 展示金币、好感度（等级）、饱食度与等级对 AI 对话的影响 |

## 配置

- `config/app.yaml`：`frame_interval_ms`（帧间隔）与 `random_action_interval_ms`（随机动作间隔），默认 120ms / 9000ms。
- `config/gent_management.yaml`：奖励、冷却、衰减、好感度等级、初始金币/好感度/饱食度。
- `config/foods.yaml`：食物定义（价格、饱食度增益、角色偏好）。
- `config/secrets.yaml`：AI Provider 与 MCP 配置（复制 `secrets.yaml.example` 创建）。

配置服务读取规则见 [docs/config配置.md](docs/config配置.md)。

## 用户数据与多用户

`user_data/users.yaml` 保存用户注册表；每个用户目录 `user_data/users/<name>/` 包含：

- `profile.yaml`：角色偏好、金币、好感度、饱食度、背包
- `todo.db`：待办（SQLite）
- `chat_history.db`：聊天历史（SQLite）

详见 [docs/启动与用户管理.md](docs/启动与用户管理.md) 与 [docs/助手管理数据库.md](docs/助手管理数据库.md)。

## 开发

```bash
# C# 主程序（Windows）
dotnet build DesktopGent.csproj
dotnet run --project DesktopGent.csproj

# Python Agent（任意平台，首次需创建虚拟环境）
uv venv --python 3.13 .venv
UV_PROJECT_ENVIRONMENT=.venv uv sync --project agent --locked
cd agent && ../.venv/bin/python -m pytest -q     # 70 个用例
cd agent && ../.venv/bin/python -m ruff check agent_service tests
```

### Python Agent 运行前提

1. 项目根存在 `.venv`（`uv venv` + `uv pip install -e agent`）；
2. `secrets.yaml` 中至少有一个**已启用**的 `openai-compatible` 提供方且填了 API Key；
3. `secrets.yaml` 的 `agent.enabled` 为 true（默认）。

满足后应用启动会自动拉起 sidecar 并切换默认提供方；任一前提不满足则**自动回退**到
`local-klee` 或直连的 `openai-compatible`，不影响使用。详见 [agent/README.md](agent/README.md)。

开发规则与易错点见 [AGENTS.md](AGENTS.md)。主要文档：

| 文档 | 内容 |
|---|---|
| [docs/启动与用户管理.md](docs/启动与用户管理.md) | 启动流程、用户创建/登录、角色偏好 |
| [docs/助手主窗口.md](docs/助手主窗口.md) | 助手窗口、文件夹驱动动画、菜单、交互 |
| [docs/助手管理.md](docs/助手管理.md) | 商店/互动/喂食/状态面板 |
| [docs/助手管理数据库.md](docs/助手管理数据库.md) | profile.yaml 字段、数据库与好感度衰减 |
| [docs/config配置.md](docs/config配置.md) | 配置模型与 role 扫描 |
| [docs/日志.md](docs/日志.md) | 日志分类与规范 |
| [docs/AI与MCP.md](docs/AI与MCP.md) | AI Provider 与 MCP |
| [docs/Python-Agent.md](docs/Python-Agent.md) | Python Agent 侧车架构与运维 |
| [docs/Python-Agent融合方案.md](docs/Python-Agent融合方案.md) | 融合方案（设计依据） |
| [docs/待办.md](docs/待办.md) | 待办功能与 MCP 工具 |
| [docs/聊天.md](docs/聊天.md) | 聊天窗口与历史 |

## 后续规划

- 更多动作目录约定（交互动作、状态动作），仍以 `static/` 帧文件夹驱动。
- 角色「外观定制」：把角色配置做成可视化编辑器，生成 `theme.yaml` 与帧目录。
- 商店扩充：食物种类、折扣活动、金币获得途径。
- AI 增强：会话摘要、记忆可视化编辑、更多 MCP 工具。
