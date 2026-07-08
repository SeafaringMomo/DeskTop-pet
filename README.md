# DesktopPetCSharp

一个基于 WPF (.NET 8) 的桌面宠物应用。主角为**像素风格的路飞 / 索隆**，由代码程序化生成 32×32 像素画放大而成，用户可手动替换。支持精灵图动画、程序化呼吸/漂浮动画、拖拽反应、系统托盘、背景音乐、聊天对话（含历史持久化）、待办管理、**宠物管理（商店/互动/喂食/状态）**、好感度系统、多用户隔离与日志系统。AI 对话层为通用接口，可接入多种大模型；MCP 服务器接口可扩展工具能力。

> **本文档为总览与导航枢纽。** 各模块的详细说明已拆分到 [docs/](docs/) 目录下，按功能模块独立维护。

## 技术栈

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
| 程序化动画 | WPF DoubleAnimation | 呼吸/漂浮/拖拽姿态/互动反应 |

## 目录结构

```
DesktopPetCSharp/
├── DesktopPetCSharp.csproj          # 工程文件
├── App.xaml / App.xaml.cs           # 入口：日志初始化、精灵图生成、AI/MCP 注册、启动页、主窗口
├── config/                          # ★ 配置文件目录（7 份 YAML，详见 docs/config配置.md）
│   ├── app.yaml                     #   应用常量
│   ├── characters.yaml              #   角色定义与配色主题
│   ├── pet_management.yaml          #   宠物管理常量（皮肤价格、衰减、奖励、冷却、等级）
│   ├── skins.yaml                   #   皮肤定义（精灵图 + 主题 + 声音）
│   ├── foods.yaml                   #   食物定义
│   ├── animations.yaml              #   动作系统定义（独立于皮肤）
│   ├── secrets.yaml.example         #   敏感信息模板
│   └── secrets.yaml                 #   敏感信息（不入库）
├── Generators/                      # 程序化资源生成器（附单独 README）
├── static/                          # 静态资源（按角色/皮肤子目录）
│   ├── luffy/classic/               #   路飞经典皮肤（sheet.png/icon.png/bgm.mp3）
│   └── zoro/classic/                #   索隆经典皮肤（sheet.png/icon.png/bgm.wav）
├── user_data/                       # 用户数据（运行时生成，按用户隔离）
│   ├── users.yaml                   #   用户注册表
│   ├── users/<name>/                #   各用户数据（profile.yaml + chat_history.db + todo.db）
│   └── logs/app_YYYYMMDD.log        #   运行日志（按天滚动）
├── Models/                          # 数据模型
├── Services/                        # 服务层（Core / Data / AI / MCP）
├── Windows/                         # 窗口层（Pet / Talk / Todo / Settings / PetManager）
└── docs/                            # ★ 详细文档（按模块拆分，见下文导航）
```

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

## 构建 & 运行

```powershell
# 还原依赖
dotnet restore

# 编译
dotnet build

# 运行
dotnet run --project DesktopPetCSharp.csproj
```

> 首次运行前请将 `config/secrets.yaml.example` 复制为 `config/secrets.yaml` 并填入真实 API_KEY（也可在应用启动后通过「⚙ 模型」按钮配置）。

## 核心特性速览

- **多角色 + 皮肤系统**：皮肤 = 精灵图 + 主题配色 + 声音，与角色绑定但可独立切换
- **动作系统独立**：所有动作定义在 `config/animations.yaml`，不同皮肤共用同一套动作
- **宠物管理面板**：右键菜单「宠物管理」打开，顶部 Tab 切换 商店 / 互动 / 喂食 / 状态
- **好感度系统**：等级（陌生人→挚友），影响皮肤折扣与 AI 对话风格；聊天/待办/喂食/互动均联动加成
- **喂食系统**：饱食度低于 20 持续一定时间会衰减好感度
- **多用户隔离**：`user_data/users/<name>/` 各自独立的角色偏好、对话记录、待办数据、宠物档案
- **配置集中**：`config/` 目录下 7 份 YAML（app/characters/pet_management/skins/foods/animations/secrets）
- **日志完善**：所有用户操作写入 `user_data/logs/app_YYYYMMDD.log`，线程安全 + 按天滚动

## 扩展指引

- **新增角色**：在 `Generators/` 创建 `XxxSpriteGenerator.cs` → 在 `App.xaml.cs` 的 `SpriteGeneratorRegistry.EnsureGenerated` 添加 case → 在 `config/characters.yaml` 添加 `中文名: 英文目录` 与配色主题。详见 [Generators/README.md](Generators/README.md)。
- **替换角色资源**：把同名文件放到 `static/<英文目录>/<皮肤名>/` 即可覆盖代码生成的默认资源（`sheet.png` / `icon.png` / `bgm.mp3` / `bgm.wav` / 时段语音）。
- **新增皮肤**：在 `config/skins.yaml` 添加条目，准备 `static/<角色>/<皮肤名>/` 资源目录（含 `sheet.png` / `icon.png` / 可选 `bgm.mp3`）。
- **新增食物**：在 `config/foods.yaml` 添加条目（含 id/name/icon/price/satiety_gain/role_preference）。
- **新增动作**：在 `config/animations.yaml` 添加条目（type=sprite_sheet 或 procedural）。
- **接入 AI 后端**：在 `config/secrets.yaml` 的 `ai_providers` 列表新增条目，或通过「⚙ 模型」窗口的「➕ 新增」按钮添加。详见 [docs/AI与MCP.md](docs/AI与MCP.md)。
- **接入 MCP 服务器**：在 `config/secrets.yaml` 的 `mcp_servers` 列表新增条目（type=local/stdio/sse）。详见 [docs/AI与MCP.md](docs/AI与MCP.md)。

## 已知限制

- **OpenAI 工具调用多轮对话**：历史消息只保存文本，未携带 `tool_calls` 结构化字段；连续多轮工具调用的第二次请求可能被部分严格的 OpenAI 兼容服务拒绝。
- **音频资源**：路飞 BGM 需用户放入 `static/luffy/classic/bgm.mp3`；索隆 BGM 由代码生成。
- **TTS 音色**：依赖 Windows 已安装的中文语音包；未安装时使用系统默认语音。
- **stdio/sse MCP**：当前仅实现 `local` 类型；`stdio`/`sse` 为预留接口。
- **多用户并发**：用户注册表与档案无并发锁（单实例运行场景）；多实例同时写同一用户档案可能冲突。
