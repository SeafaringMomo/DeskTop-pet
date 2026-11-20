# AGENTS.md

面向 AI Agent 的**开发规则手册**。本文件规定整个项目开发生命周期必须遵守的规则、易错点与边界条件，不重复 [README.md](README.md) 的功能介绍。读代码前先读此文件。

项目定位：**桌面助手（Agent）制作**，外观与养成作为辅助功能。代码与文档中的 `gent` / `助手` 是统一术语，禁止再使用 `pet` / `宠物`。

## 1. 总则

- **中文优先**：所有指令、代码注释、XML 文档注释、日志消息、文档均用中文；技术术语、类名、字段名保留英文。
- **最小改动**：优先编辑现有文件，不主动创建新文件（尤其文档）。不增加未被请求的抽象、配置项、错误处理。
- **分阶段实施**：复杂任务按 P1→Pn 推进，**每阶段完成必须 `dotnet build` 验证 0 错误**，再进入下一阶段。
- **不主动 commit**：除非用户明确要求。

## 2. 日志规则

`Logger` 是静态类，位于 [Services/Core/Logger.cs](Services/Core/Logger.cs)。

### 2.1 必须记录的操作
- 所有用户操作：拖拽、菜单点击、快捷键、聊天发消息、待办增删改、喂食、互动、角色切换
- 关键状态切换：登录/登出、应用启动/退出、AI Provider 注册、MCP 服务器注册
- 好感度变化：每次 `AddAffinity` / `SubtractAffinity` 内部已记录，不要再重复记
- 异常：catch 块内 `Logger.Error(category, message, ex)`

### 2.2 API 使用规范
```csharp
Logger.Info("Category", "消息");           // 常规事件
Logger.Warn("Category", "消息");           // 可恢复异常
Logger.Error("Category", "消息");          // 错误
Logger.Error("Category", "消息", ex);      // 带异常对象（推荐）
```

### 2.3 硬规则
- **日志失败不得影响主流程**：Logger 内部已 `try/catch` 静默吞异常，调用方不要再包一层 try/catch 影响可读性。
- **不得用 `Console.WriteLine` / `Debug.WriteLine` 替代日志**：`Debug.WriteLine` 仅用于 ConfigService 解析失败的内部诊断，不对外可见。
- **category 用 PascalCase 短词**：如 `App` / `User` / `Affinity` / `Feed` / `Interaction` / `Gent` / `Shop` / `Chat` / `Todo` / `MCP` / `AI`。
- **日志消息含上下文**：如 `$"用户登录：{name}，角色={profile.Role}"`，不要只写「登录成功」。
- **文件路径**：`<UserDataDir>/logs/app_YYYYMMDD.log`，按天滚动，线程安全（内部加锁）。**禁止改路径结构**。
- **初始化时机**：必须在 `App.OnStartup` 第一步调用 `Logger.Init(UserDataDir)`，后续步骤才能被记录。

## 3. 配置与角色规则

### 3.1 配置文件分工（禁止混用）
| 文件 | 内容 | 是否入库 | 修改方式 |
|---|---|---|---|
| `config/app.yaml` | 应用常量（音频开关、动画间隔、窗口边距） | ✅ 入库 | 手编 |
| `config/gent_management.yaml` | 养成常量（奖励、冷却、衰减、等级、初始金币/好感度/饱食度） | ✅ 入库 | 手编 |
| `config/foods.yaml` | 食物定义 | ✅ 入库 | 手编 |
| `config/secrets.yaml` | API_KEY、MCP 地址 | ❌ 不入库 | UI 修改 |

角色配置不再放入 `config/`，而是放在静态资源目录下：
| 文件 | 内容 | 是否入库 | 说明 |
|---|---|---|---|
| `static/<角色目录>/theme.yaml` | 角色显示名 + 主题配色 | ✅ | 固定文件名，禁止改名 |

已废弃并删除的文件：`characters.yaml`、`skins.yaml`、`animations.yaml`，相关模型与服务不得恢复。

### 3.2 序列化硬规则
- **必须用 `NullNamingConvention.Instance`**：保持 YAML 字段名与 C# 属性名原样对应，**禁止用 CamelCaseConvention / UnderscoredNamingConvention**。
- **字段名用 snake_case**：YAML 内字段名用 `snake_case`（如 `satiety_loss_per_6h`），C# 属性用 PascalCase，通过 `[YamlMember(Alias = "...")]` 显式映射。
- **解析失败回退默认值**：ConfigService 已实现「文件缺失/解析失败 → 返回 `new T()`」，沿用 `LoadYamlFile<T>` 模式。

### 3.3 缓存与失效规则
- **所有 Load 方法都缓存**：`Load()` / `LoadSecrets()` / `LoadGentManagement()` / `LoadFoods()` 返回缓存实例。
- **修改配置后必须 Invalidate**：
  - `InvalidateCache()` — 失效 GentConfig（角色扫描结果）
  - `InvalidateSecretsCache()` — 失效 secrets
  - `InvalidateGentManagementCache()` — 同时失效 gent_management/foods
- **运行时导入角色后必须 `ConfigService.InvalidateCache()`**：否则 `Load()` 仍返回旧角色列表。设置窗口的「导入助手角色」流程已有示范。
- **UI 修改 secrets 必须用 `SaveSecrets`**：不要手写文件，否则缓存与文件不一致。

### 3.4 新增配置字段流程
1. 在对应 Model 加属性 + `[YamlMember(Alias = "snake_case")]`
2. 在 `config/xxx.yaml` 加默认值（带注释）
3. 若有向后兼容需求，在 `LoadXxx` 中处理旧字段
4. `dotnet build` 验证

### 3.5 敏感信息规则
- **API_KEY 与 MCP 地址只存 `config/secrets.yaml`**：禁止硬编码、禁止写入其他配置、禁止写入日志。
- **`secrets.yaml` 必须被 `.gitignore` 排除**，只提交 `secrets.yaml.example` 模板。
- **API_KEY 不入日志**：Logger 调用禁止带 apiKey 字段。

## 4. 用户数据隔离规则

[UserService](Services/Core/UserService.cs) 是单例（`UserService.Instance`），管理 `user_data/users/<name>/`。

### 4.1 目录结构（禁止跨界）
```
user_data/
├── users.yaml                    # 用户注册表（全局）
└── users/<name>/
    ├── profile.yaml              # 用户档案（偏好、好感度、饱食度、金币）
    ├── chat_history.db           # 该用户对话记录（SQLite）
    ├── todo.db                   # 该用户待办（SQLite）
    └── agent_workspace/          # Python Agent 工作空间（人格/记忆 Markdown）
```

### 4.2 硬规则
- **所有用户数据写入 `CurrentUserDir`**，禁止写到全局目录。
- **数据库路径必须基于 `CurrentUserDir`**：`Path.Combine(userDir, "todo.db")`。
- **当前用户为 null 时不得操作用户数据**。
- **修改 profile 后必须保存**：调 `UserService.Instance.SaveCurrentProfile()`。
- **单例字段访问**：`UserService.Instance.CurrentUser` 每次访问都要判空。
- **并发限制**：用户注册表与档案无并发锁，假定单实例运行。

### 4.3 角色偏好规则
- **角色偏好存 `profile.yaml`**：`UserService.SaveCurrentRole(role)` 写入当前用户档案。
- **未登录时角色偏好暂存内存 `_pendingRole`**，登录后 `ApplyPendingRole()` 应用。
- **启动恢复角色优先级**：`CurrentUser.Role` > `_pendingRole` > 扫描到的第一个角色。
- **登录后必须 `InvalidateCache`**：让 ConfigService 重新 Load 时用 CurrentUser.Role。

## 5. 好感度规则

[AffinityService](Services/Core/AffinityService.cs) 是单例，数值常量在 `config/gent_management.yaml`。

### 5.1 数值规则（不得硬编码）
| 场景 | 值 | 来源 |
|---|---|---|
| 喂食喜欢 / 普通 / 讨厌 | +8 / +5 / +1 | `rewards.feed_loved/normal/disliked` |
| 抚摸 / 击掌 | +1 / +2 | `rewards.pat/highfive` |
| 聊天 / 待办完成 | +1 / +2 | `rewards.chat_per_message/todo_complete` |
| 饱食度>80 加成 | ×1.2 | `rewards.satiety_high_bonus` |
| 每日不互动 | -2 | `decay.affinity_loss_per_day` |
| 饱食度<20 持续 2h | -1 | `decay.satiety_low_affinity_loss_minutes` |
| 静音每小时 | -1 | `decay.muted_affinity_loss_per_hour` |
| 好感度范围 | 0-100 | `Math.Clamp` 强制 |

### 5.2 等级匹配规则
- 等级从高到低匹配，**第一个满足 `min` 的等级生效**（`OrderByDescending(l => l.Min)`）。
- 等级影响：AI 对话风格（`GetSystemPromptHint` 注入系统提示）。皮肤折扣已随皮肤系统移除，不要恢复。
- 修改等级表 → 编辑 `config/gent_management.yaml` 的 `levels`。

### 5.3 衰减计时器规则
- `DispatcherTimer` 1 分钟 / 次，只能在 `App.OnStartup` 用户登录后启动。
- 衰减扣款后必须更新 `LastInteraction[role]` 或 `SatietyLowSince[role]`，避免重复扣。
- 饱食度恢复时必须清空 `SatietyLowSince[role] = null`。

### 5.4 新增好感度来源流程
1. 在 `gent_management.yaml` 的 `rewards` 加字段 + 默认值
2. 在 `RewardsConfig` Model 加属性
3. 在触发位置调 `AffinityService.Instance.AddAffinity(role, delta, applySatietyBonus: ...)`
4. `dotnet build` 验证

## 6. 动画与窗口规则

### 6.1 动作来源（文件夹驱动）
- 动作不再由 `animations.yaml` 定义，帧图直接放在角色目录的子文件夹中。
- **约定目录名**：
  - `01_idle_breath/`：原地呼吸循环（默认 idle，循环播放所有帧）
  - `02_idle_nose_pick/`：随机扣鼻孔（偶尔触发一次后回到呼吸）
- 帧文件为 `*.png`，按文件名自然序播放；窗口尺寸按呼吸帧长宽比固定，`Uniform` 居中缩放。
- 新增动作 = 新增子文件夹，代码约定 `01_`/`02_` 前缀决定播放类型。

### 6.2 强制约束
- **只有**原地呼吸循环与随机扣鼻孔两种动作，禁止恢复程序化呼吸/漂浮、粒子、拖拽姿态等响应动画。
- 喂食 / 抚摸 / 击掌等操作**只更新状态**，不播放动画。
- 像素渲染：NearestNeighbor 插值 + Aliased 文本。

### 6.3 双击 vs 拖拽冲突
```csharp
if (e.ClickCount >= 2)
{
    e.Handled = true;
    // 触发抚摸（仅状态变更）
    return;
}
// 拖拽流程...
```

### 6.4 窗口定位规则
- 必须用逻辑 DIP 坐标，禁止用物理屏幕坐标（高 DPI 会越界）。
- 聊天/待办窗口贴近助手，且不超出屏幕边界：用 `SystemParameters.WorkArea` + DPI 缩放转换。
- 窗口拖拽必须用 `PointToScreen(e.GetPosition(this))`，**禁止用 `e.GetPosition(null)`**。

### 6.5 透明置顶窗口规则
- 右键菜单**必须用 WPF 原生 `ContextMenu` 属性**（自绘 Popup 不可见）。
- 右键菜单项固定为：聊天 / 待办 / 助手管理 / 隐藏 / 退出。**静音只在托盘菜单**。
- 右键菜单主题（背景/边框/文字色/悬停高亮）从角色主题 `theme.yaml` 读取，禁止硬编码颜色。

### 6.6 应用退出规则
- `Close` 事件中仅隐藏窗口会阻塞退出，必须实现 `ForceClose()` 路径做实际清理（待办窗口场景）。
- 退出时必须停止 `AffinityService` 计时器、释放托盘资源、保存当前用户档案。

## 7. AI / MCP 规则

### 7.1 Provider 注册规则
- 从 `config/secrets.yaml` 的 `ai_providers` 加载，**支持 `enabled: false` 跳过**。
- **兜底逻辑**：若无任何 provider 注册，回退到环境变量 `OPENAI_API_KEY`；仍无则注册 `LocalKleeProvider`。**禁止移除兜底**。
- `type=local` 注册 `LocalKleeProvider`；`type=openai-compatible` 注册 `OpenAiCompatibleProvider`；`type=python-agent` 注册 `PythonAgentProvider`。新增 type 必须在 `RegisterAiProviders` 加分支。

### 7.2 MCP 注册规则
- 当前**仅实现 `local` 类型**，`stdio`/`sse` 为预留接口（记 Warn 跳过）。
- `local` 类型目前**仅支持 `local-todo`**，其他 name 记 Warn 跳过。
- 兜底：若无任何 MCP 注册，注册默认 `local-todo`。

### 7.3 OpenAI 兼容接口已知限制
- 历史消息只保存文本，未携带 `tool_calls` 结构化字段；连续多轮工具调用的第二次请求可能被严格服务拒绝。修改此限制需同时改 `ChatHistoryRepository` 表结构与 `ChatOrchestrator` 消息构造。
- 走 `python-agent` 时不受此限制（Python 侧 `normalize_history` 会过滤不完整工具消息）。

### 7.4 Python Agent（混合开发）规则
架构与运维见 [docs/Python-Agent.md](docs/Python-Agent.md)，Python 侧规范见 [agent/README.md](agent/README.md)。

**边界**
- C# 是**领域数据的唯一写入方**（用户档案、待办、聊天历史）。Python 只写自己工作空间下的 Markdown。
- 会话历史**不落 Python 侧**：由 C# 每轮随请求传入 `chat_history.db` 的内容。
- Python 工作空间必须在 `CurrentUserDir` 下（默认 `user_data/users/<name>/agent_workspace`），禁止跨用户或写全局目录。
- `SupportsToolCalling = false` 是有意为之：工具循环在 Python 内完成，C# **不得**再注入 MCP 工具（会重复执行）。
- 工具桥只监听 `127.0.0.1`，随机端口 + 随机令牌，**令牌禁止写日志**。

**启动与回退**
- `agent.enabled=false` 时必须回到纯 C# 链路，这是唯一回滚开关，禁止删除。
- sidecar 未就绪时**不得**让 `python-agent` 成为默认提供方（会在首条消息失败）。启动阶段显式回退，健康检查通过后再切换。
- 退出时必须 `AgentSidecarService.Stop()` + `AgentToolBridge.Dispose()`（AGENTS.md §6.6），否则子进程变孤儿。

**分层（禁止跨层依赖）**
```
api → services → core → ports ← adapters
```
- `core` **不得**导入 `openai` / `fastapi` / `httpx`，保持可用假实现单测。
- 新增能力优先实现 port + adapter，不要在 core 里直接调第三方 SDK。

**事件契约**
- SSE 事件名与 `Services/AI/AgentEvent.cs` 一一对应，**改任一侧必须同步另一侧**。
- 新增事件需同时更新：`domain/events.py`、`SseReader.Parse`、`ChatStreamAsync` 消费方、文档。

**测试与检查**
- 改 Python 必须 `pytest` 全绿 + `ruff check` 无错。
- 测试**禁止联网**、禁止依赖 C# 进程（用 `tests/` 里的假实现或本地 HTTP 桩）。
- 依赖变更后必须 `uv lock --project agent` 更新 `agent/uv.lock`（该文件入库）。

**日志**
- Python 统一写 stdout，由 `AgentSidecarService.PumpLogsAsync` 转发到应用日志（category=`Agent`）。
- 禁止打印 API Key、令牌、完整记忆内容；长文本截断。


## 8. 角色与资源规则

### 8.1 角色目录结构
```
static/<角色英文目录>/
├── theme.yaml                   # 角色显示名 + 主题配色（固定文件名）
├── icon.png                     # 托盘图标（可选）
├── 01_idle_breath/              # 原地呼吸循环帧（png，按文件名排序）
└── 02_idle_nose_pick/           # 随机扣鼻孔帧（png）
```

### 8.2 角色发现
- `ConfigService.ScanRoles` 扫描 `static/` 下含 `theme.yaml` 的目录，读取 `name` 作为角色显示名。
- 无 `theme.yaml` 的目录不注册为角色。
- 用户可参考 `static/default_role/` 新建角色，复制到 `static/` 同级目录即可被识别。

### 8.3 角色导入
- 入口：托盘菜单 → 设置 → 「导入助手角色」。
- 流程：选择包含 `theme.yaml` 的文件夹 → 复制到 `static/<文件夹名>/` → `InvalidateCache()` → 刷新托盘角色菜单。
- **禁止强制覆盖**用户已放置的角色资源；目标目录已存在时必须提示。

## 9. 命名与代码风格

- **C# 命名**：PascalCase（类、属性、方法）、camelCase（局部变量、私有字段前缀 `_`）。
- **统一术语**：英文标识符一律用 `gent`（如 `GentWindow` / `GentConfig` / `GentManagerWindow`），中文用 `助手`（如 `桌面助手` / `助手管理`）。**禁止出现 `Pet` / `pet` / `宠物`**。
- **YAML 字段名**：snake_case，C# 用 `[YamlMember(Alias = "snake_case")]` 映射。
- **namespace**：`DesktopGent.<子模块>`（如 `DesktopGent.Services`、`DesktopGent.Windows`）。
- **文件组织**：一个 Model / Service / Window 一个文件，文件名 = 类名。
- **单例模式**：`public static XxxService Instance { get; } = new();` + 私有构造。

## 10. 易错点清单

| # | 现象 | 原因 | 解决 |
|---|---|---|---|
| 1 | 导入角色后菜单没新角色 | 缓存未失效 | 调 `ConfigService.InvalidateCache()` 并重建托盘菜单 |
| 2 | 角色主题颜色不对 | theme.yaml 缺失/解析失败 | 确认 `static/<角色>/theme.yaml` 存在且字段正确 |
| 3 | 呼吸动画不循环 | `01_idle_breath/` 帧不足 | 目录内至少放 2 张 png |
| 4 | 随机扣鼻孔不触发 | 目录名不是 `02_idle_nose_pick` | 按 `02_` 前缀命名动作目录 |
| 5 | 双击触发拖拽 | 未先检测 ClickCount | 先判 `e.ClickCount >= 2`，`e.Handled = true` 后 return |
| 6 | 右键菜单不可见 | 透明窗口用自绘 Popup | 改用 WPF 原生 `ContextMenu` 属性 |
| 7 | 拖拽位置跳跃 | 用 `e.GetPosition(null)` | 改用 `PointToScreen(e.GetPosition(this))` |
| 8 | 高 DPI 下窗口越界 | 用物理屏幕坐标 | 用逻辑 DIP + DPI 缩放转换 |
| 9 | 登录后角色不对 | 未失效缓存或未 ApplyPendingRole | 登录后 `InvalidateCache()` + `ApplyPendingRole()` |
| 10 | 好感度重复扣 | 衰减后未更新 LastInteraction | 扣款后 `user.LastInteraction[role] = now` |
| 11 | 饱食度恢复后仍扣好感 | SatietyLowSince 未清空 | 恢复时 `SatietyLowSince[role] = null` |
| 12 | ChatHistoryWindow 绑定报错 | DisplayTime 只读属性 TwoWay 绑定 | 改用 OneWay 绑定 |
| 13 | OpenAI 第二次工具调用被拒 | 历史消息缺 tool_calls 字段 | 已知限制，需改表结构；或改用 `python-agent` |
| 14 | 首条消息报「sidecar 未就绪」 | `python-agent` 在健康检查前就被设为默认 | 启动阶段回退其他提供方，就绪后 `TrySetDefault` |
| 15 | 应用退出后 Python 进程残留 | 未调 `Stop()` 或未杀进程树 | `OnExit` 调 `Kill(entireProcessTree:true)` |
| 16 | 工具执行两次（重复加待办） | C# 又注入 MCP 工具 | `PythonAgentProvider.SupportsToolCalling=false`，工具循环只在 Python 侧 |
| 17 | Python 读不到待办 | 工具桥未启动或令牌不一致 | 先 `AgentToolBridge.Start()` 再传地址+令牌给 sidecar |
| 18 | 普通用户启动工具桥 Access Denied | 用了 `HttpListener` | 用 `TcpListener` 自实现 HTTP/1.1 |
| 19 | sidecar 拉起的端口冲突 | 端口写死 | 用 `TcpListener(IPAddress.Loopback, 0)` 取空闲端口 |
| 20 | 流式回答重复朗读 | chunk 阶段就调 TTS | 只在流结束（`done` 之后）调 `TTSService.Speak` 一次 |


## 11. 开发流程检查清单

每个任务完成前确认：
- [ ] `dotnet build` 0 错误 0 警告
- [ ] 改 Python 时 `pytest` 全绿 + `ruff check` 无错
- [ ] 新增用户操作有 Logger 记录
- [ ] 新增配置字段走 §3.4 流程
- [ ] 修改好感度数值走 §5.4 流程，不硬编码
- [ ] 修改用户数据后调 `SaveCurrentProfile()`
- [ ] 修改配置后调对应 `InvalidateXxxCache()`
- [ ] 动作只使用 `01_idle_breath` / `02_idle_nose_pick` 文件夹驱动
- [ ] 不恢复皮肤 / 动画配置 / 粒子 / 响应动画
- [ ] 不引入 `pet` / `宠物` 字样（统一 `gent` / `助手`）
- [ ] 不引入硬编码颜色 / 数值 / 路径
- [ ] 异常路径有 `Logger.Error(..., ex)`
- [ ] 中文注释 + 中文日志消息
- [ ] 改 SSE 事件名时两侧同步（Python `events.py` ↔ C# `AgentEvent.cs`）
- [ ] Python `core` 层未引入第三方库
- [ ] 依赖变更后更新 `agent/uv.lock`

## 12. 不应做的事

- ❌ 用 `Console.WriteLine` / `Debug.WriteLine` 替代日志
- ❌ 硬编码好感度数值、金币价格、路径
- ❌ 把 API_KEY 写入非 secrets.yaml 的地方
- ❌ 恢复 `characters.yaml` / `skins.yaml` / `animations.yaml` 或相关服务
- ❌ 恢复程序化呼吸 / 漂浮 / 粒子 / 皮肤系统
- ❌ 使用 `pet` / `宠物` 术语（统一 `gent` / `助手`）
- ❌ 用 `e.GetPosition(null)` 拖拽
- ❌ 直接物理屏幕坐标定位窗口
- ❌ 自绘右键菜单（透明窗口下不可见）
- ❌ 未登录时写用户档案
- ❌ 跨用户目录读写数据
- ❌ 强制覆盖用户手动放入的 `static/` 资源
- ❌ 移除 AI Provider / MCP 的兜底注册逻辑
- ❌ 改 `logs/` 目录结构或日志文件命名格式
- ❌ 用非 `NullNamingConvention` 的 YAML 命名约定
- ❌ 让 Python 直接读写 C# 的 SQLite（必须经工具桥）
- ❌ 在 sidecar 就绪前把 `python-agent` 设为默认提供方
- ❌ 在 `SupportsToolCalling=false` 的 Python Agent 路径上注入 MCP 工具
- ❌ 把 sidecar / 工具桥令牌或 API Key 写进日志
- ❌ 让 `core` 层直接依赖 `openai` / `fastapi` / `httpx`

