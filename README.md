# DesktopPetCSharp

一个基于 WPF (.NET 8) 的桌面宠物应用。主角为**像素风格的路飞 / 索隆**，由代码程序化生成 32×32 像素画放大而成，用户可手动替换。支持精灵图动画、程序化呼吸/漂浮动画、拖拽反应、系统托盘、背景音乐、聊天对话（含历史持久化）、待办管理、**宠物管理（商店/互动/喂食/状态）**、好感度系统、多用户隔离与日志系统。AI 对话层为通用接口，可接入多种大模型；MCP 服务器接口支持 `local` / `stdio` / `sse` 三种传输类型，可扩展工具能力。应用内置单实例检测、全局异常兜底、用户档案导入导出、开机自启、日志自动清理等运维能力。

> **本文档为总览与导航枢纽。** 各模块的详细说明已拆分到 [docs/](docs/) 目录下，按功能模块独立维护。

## 目录

- [技术栈与整体框架](#技术栈与整体框架)
- [完整项目结构](#完整项目结构)
- [文件夹与文件详解](#文件夹与文件详解)
- [精灵图生成机制](#精灵图生成机制)
- [精灵图如何制作得更精细](#精灵图如何制作得更精细)
- [构建与运行](#构建与运行)
- [核心特性速览](#核心特性速览)
- [扩展指引](#扩展指引)
- [可完善的方向](#可完善的方向)
- [已知限制](#已知限制)
- [文档导航](#文档导航)

---

## 技术栈与整体框架

### 整体架构

应用采用**分层 + 单例服务**架构，没有引入 MVVM 框架（Prism/CommunityToolkit.Mvvm 等），而是使用 WPF 原生 code-behind + 数据绑定混合模式，便于像素级控制与透明窗口处理。

```
┌──────────────────────────────────────────────────────────┐
│  表现层 (Windows/)                                        │
│  PetWindow / TalkWindow / TodoWindow / PetManagerWindow  │
│  SettingsWindow / LauncherWindow / ChatHistoryWindow      │
│  ────────────────────────────────────────────────────    │
│  XAML 渲染 + 透明置顶窗口 + DispatcherTimer 动画驱动     │
└─────────────────────────┬────────────────────────────────┘
                          │ 调用
┌─────────────────────────▼────────────────────────────────┐
│  服务层 (Services/)                                       │
│  Core/   核心：Config / Logger / User / Affinity /        │
│           Animation / Mood / Particle / Skin / Audio /   │
│           Feed / Interaction / AutoStart /                │
│           SingleInstance / UserDataPorter / TTS / SR      │
│  Data/   数据：TodoRepository / ChatHistoryRepository    │
│  AI/     智能：IAiProvider / AiProviderRegistry /         │
│           ChatOrchestrator                                │
│  MCP/    工具：IMcpServer / McpManager /                  │
│           LocalTodoMcpServer / StdioMcpServer /           │
│           SseMcpServer                                    │
└─────────────────────────┬────────────────────────────────┘
                          │ 读写
┌─────────────────────────▼────────────────────────────────┐
│  数据层                                                   │
│  config/*.yaml      配置（YamlDotNet 序列化）             │
│  user_data/         用户数据（SQLite + YAML 档案）         │
│  static/            静态资源（精灵图/图标/BGM）            │
└──────────────────────────────────────────────────────────┘
```

### 技术栈表

| 层级 | 技术 | 用途 |
|---|---|---|
| 运行时 | .NET 8 (LTS) | 目标框架 |
| UI 框架 | WPF (XAML) | 透明置顶窗口、动画、绑定 |
| 配置 | YamlDotNet | 读写 `config/` 下七份 YAML |
| 音频 | NAudio | 背景音乐循环 + 一次性音效（mp3/wav） |
| 语音 | System.Speech.Synthesis | 文本转语音朗读 |
| 语音识别 | System.Speech.Recognition | 麦克风语音输入（优先中文） |
| 数据存储 | Microsoft.Data.Sqlite | 待办 + 对话记录持久化（按用户隔离） |
| 托盘 | Hardcodet.NotifyIcon.Wpf | 系统托盘图标与菜单 |
| AI | 自研 `IAiProvider` 接口 + HTTP | 多模型后端统一接入 |
| 工具协议 | 自研 `IMcpServer` 接口 | MCP 服务器扩展 |
| 像素渲染 | WriteableBitmap + CroppedBitmap | 程序化像素画生成 + 精灵图切帧 |
| 程序化动画 | WPF DoubleAnimation + KeyFrames | 呼吸/漂浮/拖拽姿态/关键帧/动作链/情绪状态机 |
| 粒子效果层 | WPF Canvas + TextBlock 动画 | 爱心/汗滴/✨/💤/❓/❗ 等 emoji 粒子上升淡出 |
| 测试 | xUnit | 单元测试覆盖 Config/User/Affinity/Todo/ChatHistory |

### 设计模式

- **单例服务**：所有 Core 服务（`UserService` / `AffinityService` / `MoodService` / `ParticleService` / `SkinService` 等）通过 `public static XxxService Instance { get; } = new();` + 私有构造实现，全局唯一访问点
- **配置优先 + 硬编码回退**：互动反应、动作播放等先尝试读 YAML 配置，失败回退到代码内默认动画
- **订阅模式**：`MoodService.OnMoodChanged` 事件让 `AnimationService` 订阅情绪变化
- **回调注入**：`AnimationService.OnParticleTrigger` 通过 `Action<>` 回调把粒子触发委托给 `ParticleService`，避免服务间硬依赖
- **缓存 + 失效**：`ConfigService` 全量缓存所有 YAML 实例，修改后必须显式调 `InvalidateXxxCache()`
- **用户数据隔离**：所有用户读写都基于 `UserService.Instance.CurrentUserDir`，跨用户目录访问被禁止

---

## 完整项目结构

```
DeskTop-pet/
├── DesktopPetCSharp.sln                 # 解决方案（主项目 + 测试项目）
├── DesktopPetCSharp.csproj               # 主工程文件（.NET 8 + WPF）
├── App.xaml / App.xaml.cs                # 应用入口：单实例检测、日志初始化、
│                                          #   精灵图生成、AI/MCP 注册、启动页、主窗口
├── app.manifest                          # Windows 应用清单（DPI 感知等）
├── README.md                             # 项目总览（本文档）
├── USAGE.md                              # 使用手册
├── AGENTS.md                             # AI Agent 开发规则手册
│
├── config/                               # ★ 配置文件目录（7 份 YAML）
│   ├── app.yaml                          #   应用常量（音频/精灵图网格/动画/窗口边距）
│   ├── characters.yaml                   #   角色映射、配色主题、动画参数
│   ├── pet_management.yaml               #   价格/衰减/奖励/冷却/等级常量
│   ├── skins.yaml                        #   皮肤定义（精灵图+主题+声音）
│   ├── foods.yaml                        #   食物定义
│   ├── animations.yaml                   #   动作系统定义（独立于皮肤）
│   ├── secrets.yaml.example              #   敏感信息模板（API_KEY、MCP 地址）
│   └── secrets.yaml                       #   敏感信息（不入库，运行时修改）
│
├── Models/                               # 数据模型层
│   ├── AnimationConfig.cs                #   动作系统配置（AnimationDef / KeyframeDef / ParticleDef）
│   ├── ChatMessage.cs                    #   对话消息（含 tool_calls 持久化）
│   ├── FoodConfig.cs                     #   食物配置模型
│   ├── PetConfig.cs                      #   应用配置模型（角色/主题/精灵图）
│   ├── PetManagementConfig.cs            #   宠物管理配置模型（价格/衰减/等级）
│   ├── SecretsConfig.cs                  #   敏感信息模型（AI Provider / MCP）
│   ├── SkinConfig.cs                     #   皮肤配置模型
│   ├── TodoItem.cs                       #   待办项模型
│   └── UserProfile.cs                    #   用户档案模型（偏好/好感度/饱食度/皮肤/金币）
│
├── Services/                             # 服务层
│   ├── AI/                               #   AI 智能子层
│   │   ├── IAiProvider.cs                #     AI 提供方接口（Chat / ToolCalling）
│   │   ├── AiProviderRegistry.cs         #     Provider 注册表（含兜底 LocalKleeProvider）
│   │   ├── ChatOrchestrator.cs          #     对话编排器（消息+工具调用循环+情绪识别）
│   │   └── Providers/
│   │       ├── LocalKleeProvider.cs      #       本地兜底 Provider（无 AI 时返回固定回复）
│   │       └── OpenAiCompatibleProvider.cs #   OpenAI 兼容接口（支持 OpenAI/DeepSeek/Kimi 等）
│   │
│   ├── Core/                             #   核心服务子层
│   │   ├── ConfigService.cs              #     配置服务（缓存 + 失效 + YAML 读写）
│   │   ├── Logger.cs                     #     日志（线程安全 + 按天滚动 + 10MB 归档 + 7 天清理）
│   │   ├── UserService.cs                #     用户服务（多用户隔离 + 角色偏好 + 单例）
│   │   ├── AffinityService.cs            #     好感度服务（加扣/衰减/等级匹配）
│   │   ├── AnimationService.cs           #     动作系统（关键帧/动作链/缓动/mood 集成）
│   │   ├── MoodService.cs                #     情绪状态机（7 种 PetMood + 关键词识别 + 自动重置）
│   │   ├── ParticleService.cs            #     粒子效果（Canvas + TextBlock 上升淡出）
│   │   ├── SkinService.cs                #     皮肤服务（解锁/装备/获取目录）
│   │   ├── AudioService.cs               #     音频服务（BGM 循环 + 一次性音效）
│   │   ├── FeedService.cs                #     喂食服务（饱食度+好感度+偏好触发情绪）
│   │   ├── InteractionService.cs         #     互动服务（抚摸/击掌冷却与奖励）
│   │   ├── TTSService.cs                 #     文本转语音（System.Speech）
│   │   ├── SpeechRecognitionService.cs   #     语音识别（System.Speech，优先中文）
│   │   ├── AutoStartService.cs           #     开机自启（注册表 Run 键）
│   │   ├── SingleInstance.cs             #     单实例检测（Mutex）
│   │   └── UserDataPorter.cs             #     用户档案导入导出（ZIP 打包）
│   │
│   ├── Data/                             #   数据访问子层
│   │   ├── TodoRepository.cs             #     待办仓储（SQLite + PRAGMA 列检查）
│   │   └── ChatHistoryRepository.cs      #     对话历史仓储（SQLite + tool_calls 持久化）
│   │
│   └── MCP/                              #   MCP 工具子层
│       ├── IMcpServer.cs                 #     MCP 服务器接口（ListTools / CallTool）
│       ├── McpManager.cs                 #     MCP 管理器（注册/枚举/分发）
│       ├── LocalTodoMcpServer.cs         #     本进程待办 MCP（type=local）
│       ├── StdioMcpServer.cs              #     子进程 JSON-RPC MCP（type=stdio）
│       └── SseMcpServer.cs               #     HTTP POST MCP（type=sse）
│
├── Generators/                           # 程序化资源生成器
│   ├── PixelArtRenderer.cs               #   通用像素画渲染器（字符网格→PNG）
│   ├── LuffySpriteGenerator.cs           #   路飞 4×4=16 帧精灵图生成器
│   ├── ZoroSpriteGenerator.cs            #   索隆 4×4=16 帧精灵图生成器
│   ├── BgmGenerator.cs                   #   程序化 BGM 生成器（NAudio 波形合成）
│   ├── PaletteExporter.cs                #   ★ 调色板导出器（.gpl + .hex，供 Aseprite 导入）
│   ├── SpriteSheetValidator.cs          #   ★ 精灵图验证器（检查 sheet.png 网格规格）
│   ├── AiSpriteAssistant.cs              #   ★ AI 精灵图助手（用 AI 生成字符网格）
│   └── README.md                         #   生成器独立说明
│
├── Windows/                              # 表现层（XAML 窗口）
│   ├── Pet/                              #   宠物主窗口
│   │   ├── PetWindow.xaml                #     XAML（透明置顶 + TransformGroup + ParticleLayer）
│   │   └── PetWindow.xaml.cs             #     逻辑（拖拽/双击/右键菜单/动画/皮肤切换）
│   ├── Talk/                             #   聊天窗口
│   │   ├── TalkWindow.xaml               #     XAML（消息列表 + 输入框 + 历史按钮）
│   │   └── TalkWindow.xaml.cs            #     逻辑（发送/AI 响应/TTS/主题）
│   ├── Todo/                             #   待办窗口
│   │   ├── TodoWindow.xaml               #     XAML（列表 + 输入框 + 完成按钮）
│   │   └── TodoWindow.xaml.cs            #     逻辑（CRUD + 好感度奖励）
│   ├── Chat/                             #   对话历史窗口
│   │   └── ChatHistoryWindow.xaml(.cs)   #     历史浏览（按时间排序）
│   ├── Launcher/                         #   启动页
│   │   └── LauncherWindow.xaml(.cs)       #     用户选择/新建/模型配置
│   ├── PetManager/                       #   宠物管理窗口
│   │   ├── PetManagerWindow.xaml(.cs)     #     容器（Tab 切换 4 个 Panel）
│   │   └── Panels/
│   │       ├── ShopPanel.cs              #       商店面板（皮肤购买）
│   │       ├── InteractPanel.cs          #       互动面板（抚摸/击掌/玩耍）
│   │       ├── FeedPanel.cs              #       喂食面板（食物选择）
│   │       └── StatusPanel.cs            #       状态面板（好感度/饱食度/金币/等级）
│   └── Settings/                         #   设置相关窗口
│       ├── SettingsWindow.xaml(.cs)       #     设置窗口（自启/导出/导入/打开目录）
│       ├── ModelSettingsWindow.xaml(.cs)  #     AI 模型配置窗口
│       └── InputDialog.xaml(.cs)         #     通用输入对话框
│
├── static/                               # 静态资源（按角色/皮肤子目录）
│   ├── luffy/
│   │   └── classic/                       #   路飞经典皮肤
│   │       ├── sheet.png                  #     4×4=16 帧精灵图（512×512）
│   │       ├── icon.png                   #     托盘图标（128×128，第 0 帧）
│   │       └── bgm.mp3                   #     背景音乐（用户放入，否则无）
│   └── zoro/
│       └── classic/                       #   索隆经典皮肤
│           ├── sheet.png                  #     4×4=16 帧精灵图（512×512）
│           ├── icon.png                   #     托盘图标（128×128）
│           └── bgm.wav                    #     背景音乐（代码生成）
│
├── user_data/                            # 用户数据（运行时生成，不入库）
│   ├── users.yaml                        #   用户注册表（全局）
│   ├── users/<name>/                     #   各用户数据目录
│   │   ├── profile.yaml                   #     用户档案
│   │   ├── chat_history.db               #     对话记录 SQLite
│   │   └── todo.db                        #     待办 SQLite
│   └── logs/
│       └── app_YYYYMMDD.log              #   运行日志（按天 + 归档）
│
├── docs/                                 # 详细文档（按模块拆分）
│   ├── 启动与用户管理.md
│   ├── 宠物主窗口.md
│   ├── 聊天.md
│   ├── 待办.md
│   ├── 宠物管理.md
│   ├── 宠物管理数据库.md
│   ├── config配置.md
│   ├── 日志.md
│   └── AI与MCP.md
│
└── DesktopPetCSharp.Tests/                # ★ xUnit 单元测试项目
    ├── DesktopPetCSharp.Tests.csproj
    ├── TestModuleInitializer.cs          #   测试模块初始化（设置工作目录）
    ├── ConfigServiceTests.cs              #   配置服务测试
    ├── UserProfileTests.cs                #   用户档案测试
    ├── AffinityServiceTests.cs            #   好感度服务测试
    ├── TodoRepositoryTests.cs             #   待办仓储测试
    └── ChatHistoryRepositoryTests.cs      #   对话历史仓储测试
```

---

## 文件夹与文件详解

### `config/` —— 配置文件目录

7 份 YAML，由 `ConfigService` 统一管理，使用 `NullNamingConvention` 保持字段名原样对应（禁止 CamelCase/Underscored 转换）。详见 [docs/config配置.md](docs/config配置.md)。

| 文件 | 内容 | 入库 | 修改方式 |
|---|---|---|---|
| `app.yaml` | 应用常量（音频开关 / 精灵图 4×4 网格 / 动画节奏 / 窗口边距） | ✅ | 手编 |
| `characters.yaml` | 角色映射（中文→英文目录）、配色主题、动画参数 | ✅ | 手编 |
| `pet_management.yaml` | 皮肤价格 / 衰减常量 / 奖励值 / 冷却秒数 / 等级表 | ✅ | 手编 |
| `skins.yaml` | 皮肤定义（id / 精灵图 / 主题 / 声音） | ✅ | 手编 |
| `foods.yaml` | 食物定义（id / 名称 / 图标 / 价格 / 饱食度 / 角色偏好） | ✅ | 手编 |
| `animations.yaml` | 动作系统（15+ 动作 + mood_overrides + particles） | ✅ | 手编 |
| `secrets.yaml.example` | API_KEY / MCP 地址模板 | ✅ | 手编 |
| `secrets.yaml` | 真实 API_KEY / MCP 地址（含 `enabled: false` 跳过开关） | ❌ | UI 修改 |

### `Models/` —— 数据模型层

纯 POCO 类，对应 YAML 配置或运行时数据结构。所有模型使用 `[YamlMember(Alias = "snake_case")]` 显式映射 YAML 字段。派生属性（如 `BgMusicName`）必须加 `[YamlIgnore]` 否则序列化报错。

### `Services/` —— 服务层

#### `Services/Core/` —— 核心服务（全部单例）

| 服务 | 职责 | 触发时机 |
|---|---|---|
| `ConfigService` | YAML 加载 / 缓存 / 失效 / 保存 | 全程，启动时首次 Load |
| `Logger` | 日志写入（线程安全 + 按天滚动 + 10MB 归档 + 7 天清理） | `App.OnStartup` 第一步 `Logger.Init` |
| `UserService` | 多用户管理 / 角色偏好 / 用户目录隔离 | 启动页登录后 |
| `AffinityService` | 好感度加扣 / 衰减计时器 / 等级匹配 | 互动/喂食/聊天/待办完成时 |
| `AnimationService` | 动作播放（关键帧 / 动作链 / 缓动 / 粒子触发 / mood 集成） | 实例化于 PetWindow |
| `MoodService` | 情绪状态机（7 种 PetMood + 关键词识别 + 30s 自动重置） | 互动/喂食/聊天时触发 |
| `ParticleService` | 粒子生成（Canvas + TextBlock 上升淡出） | 动作播放时通过回调触发 |
| `SkinService` | 皮肤解锁 / 装备 / 获取目录 / 主题切换 | 商店购买 / 装备时 |
| `AudioService` | BGM 循环 + 一次性音效（NAudio） | 角色切换 / 互动音效 |
| `FeedService` | 喂食（饱食度恢复 + 好感度奖励 + 偏好触发情绪） | 喂食面板操作 |
| `InteractionService` | 互动（抚摸/击掌冷却 + 奖励 + 等级跃迁触发兴奋） | 互动面板操作 |
| `TTSService` | 文本转语音（System.Speech，中文优先） | AI 回复后可触发 |
| `SpeechRecognitionService` | 麦克风语音识别（System.Speech） | 聊天窗麦克风按钮 |
| `AutoStartService` | 开机自启（注册表 `Run` 键） | 设置窗口勾选 |
| `SingleInstance` | 单实例检测（Mutex） | `App.OnStartup` 最先检查 |
| `UserDataPorter` | 用户档案导出导入（ZIP 打包） | 设置窗口操作 |

#### `Services/Data/` —— 数据访问层

| 仓储 | 表结构 | 路径 |
|---|---|---|
| `TodoRepository` | `Todo(Id, Content, CreatedAt, UpdatedAt, IsDone)` | `user_data/users/<name>/todo.db` |
| `ChatHistoryRepository` | `ChatHistory(Id, Role, Content, ToolCalls, ToolCallId, CreatedAt)` | `user_data/users/<name>/chat_history.db` |

均使用 `Microsoft.Data.Sqlite`，连接字符串 `Data Source=<path>`，每次操作开新连接（无连接池），靠 SQLite 自身的文件锁保证写入安全。

#### `Services/AI/` —— AI 智能层

- `IAiProvider`：统一接口（`ChatAsync` / `SupportsToolCalling` / `ModelName`）
- `AiProviderRegistry`：注册表，启动时从 `secrets.yaml.ai_providers` 加载，**兜底逻辑**：无任何 provider 时回退到环境变量 `OPENAI_API_KEY`，仍无则注册 `LocalKleeProvider`
- `ChatOrchestrator`：对话编排器，负责消息历史管理 + 工具调用循环（最多 5 轮）+ 情绪关键词识别
- `Providers/LocalKleeProvider`：本地兜底 Provider，无 API_KEY 时返回固定回复，保证应用可运行
- `Providers/OpenAiCompatibleProvider`：OpenAI 兼容接口，支持 `/v1/chat/completions` + `tool_calls`，可接入 OpenAI / DeepSeek / Kimi / 智谱等

#### `Services/MCP/` —— MCP 工具层

- `IMcpServer`：统一接口（`ListToolsAsync` / `CallToolAsync`）
- `McpManager`：注册中心 + 工具枚举 + 分发调用
- `LocalTodoMcpServer`：本进程 MCP，把 `TodoRepository` 暴露为 4 个工具（add_todo / list_todos / complete_todo / delete_todo）
- `StdioMcpServer`：子进程 JSON-RPC MCP，启动外部可执行文件（如 `npx` / `python script.py`）
- `SseMcpServer`：HTTP POST MCP，对接 streamable_http 服务器

### `Generators/` —— 资源生成器

| 文件 | 职责 | 输出 |
|---|---|---|
| `PixelArtRenderer.cs` | 通用渲染器（字符网格 + 调色板 → PNG） | `sheet.png` + `icon.png` |
| `LuffySpriteGenerator.cs` | 路飞 32×32 像素画 × 16 帧（4×4 网格） | `static/luffy/classic/sheet.png` |
| `ZoroSpriteGenerator.cs` | 索隆 32×32 像素画 × 16 帧（4×4 网格） | `static/zoro/classic/sheet.png` |
| `BgmGenerator.cs` | 程序化 BGM（NAudio 正弦波 + 谐波合成） | `static/zoro/classic/bgm.wav` |
| `PaletteExporter.cs` | ★ 调色板导出器（GIMP/Aseprite 通用 .gpl + .hex） | `static/<角色>/<皮肤>/palette.gpl` |
| `SpriteSheetValidator.cs` | ★ 精灵图验证器（检查尺寸/网格整除/最小帧） | 日志验证结果 |
| `AiSpriteAssistant.cs` | ★ AI 精灵图助手（用 AI 对话生成 32×32 字符网格） | 临时 PNG 或 sheet.png |

**已存在则跳过**（便于用户手动替换），详见 [精灵图生成机制](#精灵图生成机制)。

### `Windows/` —— 表现层

WPF 透明置顶窗口（`AllowsTransparency=True` + `WindowStyle=None` + `Topmost=True`），全部使用 code-behind + 数据绑定混合模式。

- **PetWindow**：主窗口，承载宠物显示、动画、拖拽、右键菜单、托盘。含 `ParticleLayer` Canvas 叠层
- **TalkWindow**：聊天窗口，贴近宠物显示，支持文本/语音输入 + TTS 朗读
- **TodoWindow**：待办窗口，CRUD 操作 + 完成时好感度奖励
- **ChatHistoryWindow**：对话历史浏览，按时间倒序
- **LauncherWindow**：启动页，用户选择/新建/模型配置
- **PetManagerWindow**：宠物管理容器，4 个 Tab Panel（商店/互动/喂食/状态）
- **SettingsWindow**：设置窗口（开机自启 / 档案导出导入 / 打开用户目录）
- **ModelSettingsWindow**：AI 模型配置（新增/删除/默认选择）
- **InputDialog**：通用文本输入对话框

### `static/` —— 静态资源

```
static/<角色英文名>/<皮肤名>/
  ├── sheet.png    # 精灵图（4×4=16 帧，512×512）
  ├── icon.png     # 托盘图标（128×128，第 0 帧）
  ├── bgm.mp3      # 或 bgm.wav，背景音乐
  └── *.wav        # 时段语音（可选，如 greeting.wav）
```

用户放入同名文件即覆盖代码生成默认值，**禁止改路径约定**。

### `user_data/` —— 用户数据

按用户隔离，所有路径基于 `UserService.Instance.CurrentUserDir`，禁止跨用户目录访问。详见 [docs/启动与用户管理.md](docs/启动与用户管理.md)。

### `docs/` —— 详细文档

按功能模块拆分的 9 份 Markdown 文档，详见 [文档导航](#文档导航)。

### `DesktopPetCSharp.Tests/` —— 单元测试

xUnit 项目，覆盖 ConfigService / UserService / AffinityService / TodoRepository / ChatHistoryRepository 五个核心模块，使用临时目录隔离测试数据。

---

## 精灵图生成机制

### 整体流程

应用启动时，`App.OnStartup` 第 2 步遍历 `characters.yaml` 的所有角色，对每个角色调用 `SpriteGeneratorRegistry.EnsureGenerated`：

```
App.OnStartup
  └─ 遍历 characters.yaml.roles
      └─ SpriteGeneratorRegistry.EnsureGenerated(roleName, dirName, roleDir, spriteFile)
          └─ switch (dirName)
              ├─ "luffy" → LuffySpriteGenerator.EnsureGenerated(sheetPath)
              └─ "zoro"  → ZoroSpriteGenerator.EnsureGenerated(sheetPath)
                  └─ 检查 sheetPath 是否已存在
                      ├─ 已存在 → 跳过（便于用户手动替换）
                      └─ 不存在 → 构建 16 帧字符网格 → PixelArtRenderer.RenderSpriteSheetGrid
```

### 字符网格 + 调色板

每个角色用"字符网格"描述一帧像素画，每个字符代表一种颜色：

```csharp
// 调色板：字符 → ARGB 颜色
private static readonly Dictionary<char, Color> Palette = new()
{
    ['.'] = Color.FromArgb(0, 0, 0, 0),       // 透明
    ['T'] = Color.FromArgb(255, 232, 197, 71), // 草帽麦秆黄
    ['R'] = Color.FromArgb(255, 192, 57, 43),  // 帽带红
    ['H'] = Color.FromArgb(255, 26, 26, 26),   // 黑发
    ['S'] = Color.FromArgb(255, 244, 194, 161),// 肤色
    ['E'] = Color.FromArgb(255, 26, 26, 26),   // 眼睛
    ['M'] = Color.FromArgb(255, 139, 58, 43),  // 嘴
    // ...
};

// 一帧像素画：32 行 × 32 列字符
private static readonly string[] BaseRows =
{
    "................................",  // 第 0 行（全透明）
    "................................",  // 第 1 行
    "..........TTTTTT..............",   // 第 2 行（草帽顶部）
    ".........TTTTTTTT.............",   // 第 3 行
    // ... 共 32 行
};
```

### 30 帧 6×5 网格布局

每个角色的精灵图是 **6×5 = 30 帧** 网格（64×64 像素/帧，2 倍放大 = 128×128 输出），按行优先排列：

```
┌─────────┬─────────┬─────────┬─────────┬─────────┬─────────┐
│  帧 0   │  帧 1   │  帧 2   │  帧 3   │  帧 4   │  帧 5   │  ← 行 0 基础
│ 睁眼    │ 闭眼    │ 微笑    │ 张嘴    │ 撇眉    │ 半眯眼  │
├─────────┼─────────┼─────────┼─────────┼─────────┼─────────┤
│  帧 6   │  帧 7   │  帧 8   │  帧 9   │  帧 10  │  帧 11  │  ← 行 1 强烈
│ 大笑    │ 红脸    │ 眨眼过渡│ 微笑过渡│ 大笑过渡│ 蓄泪    │
├─────────┼─────────┼─────────┼─────────┼─────────┼─────────┤
│  帧 12  │  帧 13  │  帧 14  │  帧 15  │  帧 16  │  帧 17  │  ← 行 2 情绪
│ 流泪    │ 惊讶    │ 红脸渐变│ 张大嘴  │ 困倦    │ 说话    │
├─────────┼─────────┼─────────┼─────────┼─────────┼─────────┤
│  帧 18  │  帧 19  │  帧 20  │  帧 21  │  帧 22  │  帧 23  │  ← 行 3 看向
│ 看上    │ 看下    │ 看左    │ 看右    │ 看左上  │ 看右上  │
├─────────┼─────────┼─────────┼─────────┼─────────┼─────────┤
│  帧 24  │  帧 25  │  帧 26  │  帧 27  │  帧 28  │  帧 29  │  ← 行 4 看向续
│ 看左下  │ 看右下  │ 看前    │ 生气瞪眼│ 得意    │ 预留    │
└─────────┴─────────┴─────────┴─────────┴─────────┴─────────┘
```

- 单帧尺寸：64×64 像素（由 32×32 设计 Upscale2x 放大）
- 放大倍数：2 倍
- 单帧输出：128×128 像素
- 整图尺寸：768×640 像素

### 帧组合机制

每帧由「基础行 + 眼睛行 + 嘴行 + 可选红脸」组合而成，避免重复定义整张图：

```csharp
private static string[] BuildFrame(string eyes1, string eyes2, string mouth1, string mouth2, bool blush = false)
{
    var rows = (string[])BaseRows.Clone();  // 复制基础行
    rows[14] = eyes1;                        // 替换眼睛行 1
    rows[15] = eyes2;                        // 替换眼睛行 2
    rows[20] = mouth1;                       // 替换嘴行 1
    rows[21] = mouth2;                       // 替换嘴行 2
    if (blush)
    {
        rows[17] = ".HSSPPSSSSSSSSSSSSPPSSSSSH......"; // 脸颊红
        rows[18] = ".HSSPpPSSSSSSSSSSPPpSSSSH......";
    }
    return rows;
}

// 14 种眼睛变体 × 7 种嘴部变体 → 组合出 8 种情绪帧 + 8 个预留帧
var frames = new[]
{
    BuildFrame(EyesOpen1, EyesOpen2, MouthFlat1, MouthFlat2),                    // 0=睁眼
    BuildFrame(EyesClosed1, EyesClosed2, MouthFlat1, MouthFlat2),                // 1=闭眼
    BuildFrame(EyesSmile1, EyesSmile2, MouthSmile1, MouthSmile2),                // 2=微笑
    BuildFrame(EyesOpen1, EyesOpen2, MouthOpen1, MouthOpen2),                    // 3=张嘴
    BuildFrame(EyesFrown1, EyesFrown2, MouthFrown1, MouthFrown2),                // 4=撇眉
    BuildFrame(EyesSleepy1, EyesSleepy2, MouthFlat1, MouthFlat2),                // 5=半眯眼
    BuildFrame(EyesLaugh1, EyesLaugh2, MouthLaugh1, MouthLaugh2),                // 6=大笑
    BuildFrame(EyesBlush1, EyesBlush2, MouthSmall1, MouthSmall2, blush: true),    // 7=红脸
    // 8-15 预留（默认等同睁眼，可由用户手动替换）
};
```

### 渲染流程

`PixelArtRenderer.RenderSpriteSheetGrid` 完成像素写入：

1. **创建 WriteableBitmap**：`PixelFormats.Bgra32`，尺寸 `framePx × cols × scale` × `frameRows × rows × scale`（512×512）
2. **遍历 16 个帧槽位**：计算每帧的 `offsetX = col × framePx × scale`，`offsetY = row × frameRows × scale`
3. **逐像素填色**：对每个字符查调色板得到 ARGB，写入字节数组的对应位置（BGRA 字节序）
4. **写入 PNG**：`WriteableBitmap.WritePixels` + `PngBitmapEncoder.Save`
5. **生成 icon.png**：取第 0 帧单独输出为 128×128 正方形图标，用于系统托盘

### 加载与切帧

运行时 `PetWindow.LoadFrames` 用 `CroppedBitmap` 按配置的 cols×rows 切分 sheet.png：

```csharp
int fw = sheet.PixelWidth / cols;  // 单帧宽
int fh = sheet.PixelHeight / rows; // 单帧高
for (int r = 0; r < rows; r++)
    for (int c = 0; c < cols; c++)
    {
        var crop = new CroppedBitmap(sheet, new Int32Rect(c * fw, r * fh, fw, fh));
        _frames.Add(crop);
    }
```

**尺寸校验**：sheet.PixelWidth/Height 必须能被 cols/rows 整除，否则抛异常回退到多帧模式，避免切出残缺帧。

### 帧索引约定

帧索引与情绪状态机的映射（见 [config/app.yaml](config/app.yaml) 注释）：

| 帧索引 | 表情 | 对应 PetMood | 说明 |
|---|---|---|---|
| 0 | 睁眼 | Neutral | 默认 idle 帧 |
| 1 | 闭眼 | — | 眨眼用（非情绪） |
| 2 | 微笑 | Happy | 抚摸后切换 |
| 3 | 张嘴 | Curious | 聊天中思考/惊讶 |
| 4 | 撇眉 | Angry | 用户说"笨蛋"等 |
| 5 | 半眯眼 | Sad | 喂食讨厌食物 |
| 6 | 大笑 | Excited | 击掌/等级跃迁 |
| 7 | 红脸 | Shy | 说"喜欢"等 |
| 8-10 | 过渡帧 | — | 眨眼/微笑/大笑过渡 |
| 11-12 | 蓄泪/流泪 | Sad（增强） | 哭泣情绪 |
| 13 | 惊讶 | Curious（增强） | 强烈好奇 |
| 14 | 红脸渐变 | Shy（过渡） | 害羞消退 |
| 15 | 张大嘴 | — | 哈欠/大叫 |
| 16 | 困倦 | Sad（困倦） | 打哈欠 |
| 17 | 说话 | — | AI 回复时 |
| 18-25 | 8 方向看向 | — | 眼睛跟随鼠标（未来） |
| 26 | 看前 | — | 默认看向前方 |
| 27 | 生气瞪眼 | Angry（增强） | 强烈生气 |
| 28 | 得意 | Happy（增强） | 成就解锁 |
| 29 | 预留 | — | 可由用户自定义 |

`AnimationService.GetMoodIdleFrame()` 根据 `PetMood` 返回基础情绪帧（0/2/3/4/5/6/7），让宠物的 idle 表情跟着情绪走。

---

## 精灵图如何制作得更精细

当前精灵图采用 64×64 像素（由 32×32 设计 Upscale2x 放大）+ 2 倍放大 = 128×128 输出。以下是渐进式提升精细度的方向：

### 方向 1：提升像素分辨率（已实施）

**当前**：64×64 像素 / 帧（由 32×32 设计 Upscale2x 放大），2 倍放大 → 128×128 输出
**实施方式**：`LuffySpriteGenerator` / `ZoroSpriteGenerator` 保留 32×32 的 BaseRows 和变体（便于维护），`BuildFrame` 时调 `Upscale2x` 把每个字符放大为 2×2 像素块，输出 64×64 帧。

> **未来改进**：编写真正的 64×64 BaseRows（而非放大），在草帽渐变、脸部阴影、衣服褶皱等区域增加细节层次。

### 方向 2：增加调色板层次

**当前**：每种颜色只有 1-2 个层次（如肤色 `S` + 阴影 `s`）
**改进**：增加 3-5 个层次，模拟渐变与立体感

```csharp
// 当前
['S'] = Color.FromArgb(255, 244, 194, 161),  // 肤色
['s'] = Color.FromArgb(255, 220, 170, 130),  // 肤色阴影

// 改进后
['S'] = Color.FromArgb(255, 244, 194, 161),  // 肤色高光
['s'] = Color.FromArgb(255, 232, 184, 145),  // 肤色中间调
['h'] = Color.FromArgb(255, 220, 170, 130),  // 肤色阴影
['d'] = Color.FromArgb(255, 200, 150, 110),  // 肤色深阴影
['H'] = Color.FromArgb(255, 180, 130, 95),   // 肤色最暗部
```

在脸部字符网格中按位置使用不同层次：鼻梁/颧骨用高光 `S`，脸颊中间调 `s`，下颌阴影 `h`，脖子深阴影 `d`，头发投影 `H`。

### 方向 3：增加表情与动作帧数（已实施）

**当前**：30 帧（8 基础表情 + 3 过渡 + 6 情绪 + 8 方向看向 + 5 特殊）
**实施方式**：6×5=30 帧网格布局，新增眨眼过渡/微笑过渡/大笑过渡/蓄泪/流泪/惊讶/红脸渐变/张大嘴/困倦/说话/8 方向看向/生气瞪眼/得意等帧

帧布局见 [30 帧 6×5 网格布局](#30-帧-6x5-网格布局) 段。

> **未来改进**：扩展到 8×8=64 帧，增加更多过渡帧（如微笑→大笑的中间帧、害羞→惊讶的渐变）。

### 方向 4：引入抗锯齿与次像素渲染

**当前**：`NearestNeighbor` + `Aliased`（硬边像素）
**改进**：对 2D 旋转动画使用 `BitmapScalingMode.HighQuality`（仅旋转期间）

```csharp
// 旋转动画播放时切换为高质量
PetImage.RenderOptions.BitmapScalingMode = BitmapScalingMode.HighQuality;
// 旋转结束恢复像素风
PetImage.RenderOptions.BitmapScalingMode = BitmapScalingMode.NearestNeighbor;
```

**权衡**：会破坏纯像素风的硬边美学，建议仅用于大角度旋转（如 `spin` 动作）。

### 方向 5：分层精灵图（角色 + 装备）

**当前**：一帧包含全部元素（帽子 + 头发 + 脸 + 衣服 + 配饰）
**改进**：拆分为多层 PNG，运行时合成

```
static/luffy/classic/
  ├── base.png       # 身体基础层（脸 + 肤色）
  ├── hair.png       # 发型层
  ├── hat.png        # 帽子层
  ├── clothes.png    # 衣服层
  └── accessory.png  # 配饰层
```

PetWindow 用多个 `<Image>` 叠加，每层独立切换。优点：换装系统（戴不同帽子/衣服）无需重画整张精灵图。

### 方向 6：Aseprite 工具辅助（已实现）

**当前**：已集成调色板导出 + 精灵图验证

应用启动时自动生成 `static/<角色>/<皮肤>/palette.gpl`（GIMP/Aseprite 通用调色板格式），用户在 Aseprite 中导入即可用项目色彩绘制：

1. 启动应用 → 生成精灵图时同步导出 `palette.gpl` + `palette.hex`
2. Aseprite → `Palette → Load Palette` → 选择 `palette.gpl`
3. 在 Aseprite 中按项目调色板绘制 32×32 像素画
4. `Sprite → Sprite Size` 设为 128×128（或保持 32×32 后用 `Export` 缩放）
5. `File → Export Sprite Sheet`，设置 Horizontal/Vertical = 4×4，导出为 `sheet.png`
6. 放入 `static/<角色>/<皮肤>/sheet.png` 覆盖代码生成版本
7. 下次启动时 `SpriteSheetValidator` 自动验证网格规格，写入日志

Aseprite 优势：
- 图层与帧动画支持
- 调色板管理（限制为 N 色调色板）
- onion skinning（洋葱皮）查看前后帧
- 导出 sprite sheet（直接输出 4×4 网格）

### 方向 7：AI 辅助生成（已实现）

**当前**：已实现 `AiSpriteAssistant`，用 AI 对话生成字符网格代码

工作流：
1. 用户输入角色描述（如 "橙色头发的小女孩，戴草帽，大眼睛"）
2. `BuildPrompt` 把描述 + 调色板 + 帧规格组装成 AI 提示词
3. 复用现有 `IAiProvider`（OpenAI 兼容 / DeepSeek / Kimi 等）发送请求
4. `ParseFrames` 从 AI 回复中提取 ```pixels``` 代码块，解析为 32×32 字符网格
5. `PixelArtRenderer.RenderSpriteSheetGrid` 渲染成 PNG

代码示例：
```csharp
using DesktopPetCSharp.Generators;

// 单帧生成
var (ok, msg) = await AiSpriteAssistant.GenerateFrameAsync(
    "草帽海贼团船长，黑色短发，红色背心",
    LuffySpriteGenerator.Palette,
    @"D:\output\luffy_ai_draft.png"
);

// 16 帧精灵图生成（耗时较长）
var (ok2, msg2) = await AiSpriteAssistant.GenerateSpriteSheetAsync(
    "草帽海贼团船长，黑色短发，红色背心",
    LuffySpriteGenerator.Palette,
    @"D:\output\luffy_ai_sheet.png"
);
```

设计决策：
- **复用对话接口**：不依赖图像生成 API（DALL-E/SDXL），因为图像生成 API 输出栅格化 PNG 无法精确控制 32×32 像素
- **字符网格可读**：AI 生成的字符网格可编辑、可版本控制，便于人工微调
- **无需新增配置**：复用现有 `IAiProvider` 和 `secrets.yaml.ai_providers`

### 方向 8：动态光照与阴影

**当前**：每帧颜色固定
**改进**：根据时间动态调整调色板

```csharp
// 白天调色板
Palette['S'] = Color.FromArgb(255, 244, 194, 161);  // 肤色（明亮）

// 夜晚调色板
Palette['S'] = Color.FromArgb(255, 180, 140, 110);  // 肤色（偏蓝暗）
```

或用 WPF 的 `<DropShadowEffect>` 给 PetImage 加投影，根据时间调整投影方向与强度。

### 改进优先级建议

| 优先级 | 方向 | 难度 | 效果 | 状态 |
|---|---|---|---|---|
| ★★★ | 方向 6（Aseprite 工具辅助） | 低 | 高（手工细节大幅提升） | ✅ 已实现 |
| ★★★ | 方向 1（提升分辨率到 64×64） | 中 | 高（细节上限翻 4 倍） | ✅ 已实施（Upscale2x） |
| ★★★ | 方向 3（增加帧数到 30） | 中 | 高（动画更流畅） | ✅ 已实施（6×5=30 帧） |
| ★★ | 方向 7（AI 辅助生成） | 中 | 中（快速出稿，需修正） | ✅ 已实现 |
| ★★★ | 方向 2（调色板层次） | 低 | 中（立体感显著提升） | 待实施 |
| ★ | 方向 5（分层精灵图） | 高 | 高（换装系统） | 待实施 |
| ★ | 方向 8（动态光照） | 中 | 中（氛围感） | 待实施 |
| - | 方向 4（抗锯齿） | 低 | 低（破坏像素风美学，不推荐） | 不推荐 |

---

## 构建与运行

```powershell
# 还原依赖
dotnet restore

# 编译（含测试项目）
dotnet build DesktopPetCSharp.sln

# 运行
dotnet run --project DesktopPetCSharp.csproj

# 运行测试
dotnet test DesktopPetCSharp.sln
```

> 首次运行前请将 `config/secrets.yaml.example` 复制为 `config/secrets.yaml` 并填入真实 API_KEY（也可在应用启动后通过「⚙ 模型」按钮配置）。
>
> **首次启动会自动生成精灵图**：若 `static/<角色>/classic/sheet.png` 不存在，`App.OnStartup` 会调 `SpriteGeneratorRegistry.EnsureGenerated` 程序化生成 4×4=16 帧 512×512 PNG。若要替换为手工绘制的版本，直接覆盖该文件即可（已存在则跳过生成）。

---

## 核心特性速览

- **多角色 + 皮肤系统**：皮肤 = 精灵图 + 主题配色 + 声音，与角色绑定但可独立切换
- **动作系统独立**：所有动作定义在 `config/animations.yaml`，不同皮肤共用同一套动作
- **★ 30 帧 6×5 精灵图**：每个角色支持 8 种基础表情 + 过渡帧 + 情绪帧 + 8 方向看向帧 + 生气瞪眼/得意等特殊帧
- **★ 关键帧 + 动作链**：YAML 可定义任意复杂的程序化动画（多属性同步驱动 / 串行组合）
- **★ 情绪状态机**：PetMood（Neutral/Happy/Excited/Sad/Angry/Curious/Shy）由互动/喂食/聊天关键词触发，影响 idle 表情与随机动作权重
- **★ 粒子效果层**：动作播放时同步触发粒子（爱心/汗滴/✨/💤/❓/❗），由 ParticleService 管理生命周期
- **宠物管理面板**：右键菜单「宠物管理」打开，顶部 Tab 切换 商店 / 互动 / 喂食 / 状态
- **好感度系统**：等级（陌生人→挚友），影响皮肤折扣与 AI 对话风格；聊天/待办/喂食/互动均联动加成
- **喂食系统**：饱食度低于 20 持续一定时间会衰减好感度
- **多用户隔离**：`user_data/users/<name>/` 各自独立的角色偏好、对话记录、待办数据、宠物档案
- **配置集中**：`config/` 目录下 7 份 YAML（app/characters/pet_management/skins/foods/animations/secrets）
- **日志完善**：所有用户操作写入 `user_data/logs/app_YYYYMMDD.log`，线程安全 + 按天滚动 + 单文件 10MB 上限自动归档 + 7 天前日志启动时自动清理
- **★ 三种 MCP 传输**：`local`（本进程内）/ `stdio`（子进程 JSON-RPC）/ `sse`（HTTP POST）三类型 MCP 服务器均已实现，配置即用
- **★ OpenAI 多轮工具调用**：历史消息完整持久化 `tool_calls` 与 `tool_call_id`，连续多轮工具调用不再被严格服务拒绝
- **★ 单实例 + 原子写入**：Mutex 防止多实例同时写同一用户档案；用户档案与注册表都通过 `.tmp` + `File.Move` 原子写入，崩溃时不损坏文件
- **★ 全局异常兜底**：WPF UI 线程 / 非 UI 线程 / Task 未观察异常三层捕获，异常自动写入日志并提示用户，避免静默崩溃
- **★ 设置窗口**：托盘菜单「设置」打开，含开机自启、用户档案导出/导入（ZIP）、打开用户数据目录
- **★ 单元测试**：xUnit 测试项目覆盖 ConfigService / UserService / AffinityService / TodoRepository / ChatHistoryRepository

---

## 扩展指引

- **新增角色**：在 `Generators/` 创建 `XxxSpriteGenerator.cs` → 在 `App.xaml.cs` 的 `SpriteGeneratorRegistry.EnsureGenerated` 添加 case（并添加 `GetPalette` 分支以导出调色板） → 在 `config/characters.yaml` 添加 `中文名: 英文目录` 与配色主题。详见 [Generators/README.md](Generators/README.md)。
- **替换角色资源**：把同名文件放到 `static/<英文目录>/<皮肤名>/` 即可覆盖代码生成的默认资源（`sheet.png` / `icon.png` / `bgm.mp3` / `bgm.wav` / 时段语音）。**首次启动时该目录下还会自动生成 `palette.gpl` 与 `palette.hex`**，可导入 Aseprite 用项目色彩绘制。
- **★ 用 Aseprite 绘制精灵图**：
  1. 启动应用生成默认 `palette.gpl`（位于 `static/<角色>/<皮肤>/palette.gpl`）
  2. Aseprite → `Palette → Load Palette` 导入该文件
  3. 绘制 32×32 像素画，导出为 4×4=16 帧 512×512 的 `sheet.png`
  4. 放入 `static/<角色>/<皮肤>/sheet.png` 覆盖代码生成版本
  5. 下次启动时 `SpriteSheetValidator` 自动验证网格规格并写入日志
- **★ 用 AI 生成精灵图初稿**：调用 `AiSpriteAssistant.GenerateFrameAsync` 或 `GenerateSpriteSheetAsync`，传入角色描述与调色板，AI 会返回 32×32 字符网格代码，自动渲染为 PNG。详见 [精灵图如何制作得更精细](#精灵图如何制作得更精细) 的方向 7。
- **新增皮肤**：在 `config/skins.yaml` 添加条目，准备 `static/<角色>/<皮肤名>/` 资源目录（含 `sheet.png` / `icon.png` / 可选 `bgm.mp3`）。
- **新增食物**：在 `config/foods.yaml` 添加条目（含 id/name/icon/price/satiety_gain/role_preference）。
- **新增动作**：在 `config/animations.yaml` 添加条目（type=sprite_sheet 或 procedural）。支持关键帧序列（`keyframes`）、动作链（`chain`）、粒子触发（`particles`）三种增强模式。详见 [docs/config配置.md](docs/config配置.md) 的 animations 段。
- **接入 AI 后端**：在 `config/secrets.yaml` 的 `ai_providers` 列表新增条目，或通过「⚙ 模型」窗口的「➕ 新增」按钮添加。详见 [docs/AI与MCP.md](docs/AI与MCP.md)。
- **接入 MCP 服务器**：在 `config/secrets.yaml` 的 `mcp_servers` 列表新增条目（type=local/stdio/sse）。详见 [docs/AI与MCP.md](docs/AI与MCP.md)。

---

## 可完善的方向

### 1. 精灵图与视觉表现

- ✅ **分辨率升级到 64×64**（已实施）：由 32×32 设计 Upscale2x 放大，2 倍放大输出 128×128
- ✅ **帧数升级到 30**（已实施）：6×5 网格，含 8 基础表情 + 过渡帧 + 情绪帧 + 8 方向看向 + 特殊帧
- **调色板层次**：每种颜色增加 3-5 个过渡层次，提升立体感
- **分层精灵图**：身体 / 发型 / 帽子 / 衣服 / 配饰分层 PNG，运行时合成，支持换装系统
- **动态光照**：根据系统时间调整调色板（夜晚偏蓝暗），或加投影效果
- ✅ **Aseprite 工具辅助**（已实现）：自动导出 .gpl 调色板 + 精灵图验证
- ✅ **AI 辅助生成**（已实现）：AiSpriteAssistant 用 AI 对话生成字符网格代码
- **★ 真正的 64×64 设计**：当前是 32×32 放大，未来可编写真正的 64×64 BaseRows 增加草帽渐变/脸部阴影/衣服褶皱等细节

### 2. 动作系统

- **更多 idle 动作**：打哈欠、伸懒腰、看手表、挠头等，让宠物更生动
- **天气联动**：根据系统天气 API 切换动作（雨天打伞、晴天戴墨镜）
- **鼠标跟随**：宠物眼睛跟随鼠标位置（用方向帧）
- **拖拽物理**：松手后有惯性 + 反弹，而不是立即停止
- **碰撞检测**：宠物不能拖出屏幕，碰到边缘会"撞墙"反弹

### 3. AI 与对话

- **流式输出**：当前 AI 回复一次性返回，可改为 SSE 流式输出，逐字显示
- **多模态**：支持图片输入（让宠物"看"屏幕截图并评论）
- **情感分析**：用 AI 分析用户情绪，宠物表情跟着调整
- **长期记忆**：把关键对话摘要写入档案，跨会话记忆用户偏好
- **Voice Activity Detection**：替代按键录音，自动检测说话开始/结束

### 4. 宠物管理

- **小游戏**：实现预留的 `play` 互动（猜拳 / 抚摸节奏 / 喂食精准度）
- **成就系统**：解锁里程碑（首次满好感度 / 喂食 100 次 / 连续登录 7 天）
- **任务系统**：每日任务（喂食 3 次 / 聊天 5 条 / 完成 2 个待办）→ 奖励金币
- **宠物进化**：好感度达到挚友后，宠物外观升级（如路飞草帽换新）

### 5. MCP 与工具

- **更多 local MCP**：日历提醒、文件搜索、剪贴板历史等本进程工具
- **stdio MCP 示例**：提供 Python/Node 脚本模板，开箱即用
- **MCP 工具市场**：在线注册 MCP 服务器，一键安装
- **工具权限控制**：敏感工具（删除文件 / 发邮件）需用户确认

### 6. 用户体验

- **多宠物共存**：同时显示多个角色，他们之间会有互动动画
- **宠物对话气泡**：不打开聊天窗，宠物主动在屏幕上显示短消息
- **快捷键**：全局快捷键唤出聊天窗 / 喂食 / 互动
- **主题切换**：日间/夜间主题，跟随系统
- **多语言**：英文版支持

### 7. 性能与稳定性

- **数据库连接池**：当前每次操作开新连接，可引入 `Microsoft.Data.Sqlite` 连接池
- **帧缓存**：精灵图切帧后缓存 `CroppedBitmap`，避免重复切分
- **粒子对象池**：高频粒子场景下复用 TextBlock，减少 GC
- **崩溃恢复**：异常时自动保存当前状态，重启后恢复

### 8. 测试与质量

- **提高测试覆盖率**：当前仅覆盖 5 个核心模块，可扩展到 AnimationService / MoodService / ParticleService
- **UI 自动化测试**：用 FlaUI 或 Appium 做 WPF UI 自动化
- **压力测试**：模拟大量对话/待办/粒子，验证内存与性能
- **CI/CD**：GitHub Actions 自动 build + test

---

## 已知限制

- **音频资源**：路飞 BGM 需用户放入 `static/luffy/classic/bgm.mp3`；索隆 BGM 由代码生成。
- **TTS 音色**：依赖 Windows 已安装的中文语音包；未安装时使用系统默认语音。
- **stdio MCP 子进程依赖**：`type=stdio` 类型的 MCP 服务器依赖宿主机器已安装对应可执行文件（如 `npx`、`python` 等），命令不存在时握手会失败并记入日志。
- **sse MCP 协议简化**：`type=sse` 类型采用简化版 HTTP POST 实现，适合自托管的 streamable_http 服务器；完整 SSE 长连接 + 分块响应暂未支持。
- **精灵图分辨率**：当前 64×64 像素（由 32×32 设计 Upscale2x 放大）+ 2 倍放大 = 128×128 输出。进一步提升细节的方向见 [精灵图如何制作得更精细](#精灵图如何制作得更精细)。
- **多实例并发**：用户注册表与档案无并发锁，假定单实例运行；多实例同时写同一用户档案会冲突（已用 Mutex 防止多实例启动）。
- **OpenAI 多轮工具调用历史**：历史消息已持久化 `tool_calls` 结构化字段，但部分非标准服务（如自托管 vLLM）可能仍对长上下文 + 多工具调用敏感。

---

## 文档导航

按功能模块拆分，所有详细说明位于 [docs/](docs/) 目录：

### 核心交互

| 文档 | 内容 |
|---|---|
| [docs/启动与用户管理.md](docs/启动与用户管理.md) | App 入口、LauncherWindow 启动页、UserService 多用户隔离、ConfigService 加载机制、ModelSettingsWindow 模型设置 |
| [docs/宠物主窗口.md](docs/宠物主窗口.md) | PetWindow 动画系统、双击抚摸、拖拽、点击穿透、托盘菜单、右键菜单、皮肤切换 |
| [docs/聊天.md](docs/聊天.md) | TalkWindow 聊天窗口、ChatHistoryRepository、chat_history.db 表结构、ChatOrchestrator 工作流、好感度联动 |
| [docs/待办.md](docs/待办.md) | TodoWindow 待办窗口、TodoRepository、todo.db 表结构、LocalTodoMcpServer 工具、好感度联动 |

### 宠物管理

| 文档 | 内容 |
|---|---|
| [docs/宠物管理.md](docs/宠物管理.md) | PetManagerWindow 容器、ShopPanel 商店、InteractPanel 互动、FeedPanel 喂食、StatusPanel 状态 |
| [docs/宠物管理数据库.md](docs/宠物管理数据库.md) | profile.yaml 字段（皮肤数量/食物储备/金币/宠物状态/好感度）、数据流、衰减计时器、好感度等级表 |

### 基础设施

| 文档 | 内容 |
|---|---|
| [docs/config配置.md](docs/config配置.md) | 7 份 YAML 配置文件详解（app/characters/pet_management/skins/foods/animations/secrets） |
| [docs/日志.md](docs/日志.md) | 日志位置、格式、级别、分类、API、线程安全 |
| [docs/AI与MCP.md](docs/AI与MCP.md) | IAiProvider 接口、AiProviderRegistry、ChatOrchestrator、IMcpServer、McpManager、LocalTodoMcpServer |
