# AGENTS.md

面向 AI Agent 的**开发规则手册**。本文件规定整个项目开发生命周期必须遵守的规则、易错点与边界条件，不重复 [README.md](README.md) 的功能介绍。读代码前先读此文件。

## 1. 总则

- **中文优先**：所有指令、代码注释、XML 文档注释、日志消息、文档均用中文；技术术语、类名、字段名保留英文。
- **最小改动**：优先编辑现有文件，不主动创建新文件（尤其文档）。不增加未被请求的抽象、配置项、错误处理。
- **分阶段实施**：复杂任务按 P1→Pn 推进，**每阶段完成必须 `dotnet build` 验证 0 错误**，再进入下一阶段。
- **不破坏向后兼容**：删除旧配置/接口前先实现迁移逻辑（见 §4）。
- **不主动 commit**：除非用户明确要求。
- **不创建文档**：不主动创建 `.md` 文件（除非用户明确要求或本就属于任务目标）。

## 2. 日志规则

`Logger` 是静态类，位于 [Services/Core/Logger.cs](Services/Core/Logger.cs)。

### 2.1 必须记录的操作
- 所有用户操作：拖拽、菜单点击、快捷键、聊天发消息、待办增删改、喂食、互动、皮肤切换、角色切换
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
- **category 用 PascalCase 短词**：如 `App` / `User` / `Affinity` / `Feed` / `Interaction` / `Skin` / `Chat` / `Todo` / `MCP` / `AI`。
- **日志消息含上下文**：如 `$"用户登录：{name}，角色={profile.Role}"`，不要只写「登录成功」。
- **文件路径**：`<UserDataDir>/logs/app_YYYYMMDD.log`，按天滚动，线程安全（内部加锁）。**禁止改路径结构**。
- **初始化时机**：必须在 `App.OnStartup` 第一步调用 `Logger.Init(UserDataDir)`，后续步骤才能被记录。

## 3. 配置文件规则

7 份 YAML 全部位于 `config/` 目录，由 [ConfigService](Services/Core/ConfigService.cs) 统一管理。

### 3.1 文件分工（禁止混淆）
| 文件 | 内容 | 是否入库 | 修改方式 |
|---|---|---|---|
| `app.yaml` | 应用常量（音频/精灵图/动画/窗口边距） | ✅ 入库 | 手编 |
| `characters.yaml` | 角色映射、配色主题、动画参数 | ✅ 入库 | 手编 |
| `pet_management.yaml` | 价格/衰减/奖励/冷却/等级常量 | ✅ 入库 | 手编 |
| `skins.yaml` | 皮肤定义（精灵图+主题+声音） | ✅ 入库 | 手编 |
| `foods.yaml` | 食物定义 | ✅ 入库 | 手编 |
| `animations.yaml` | 动作系统（独立于皮肤） | ✅ 入库 | 手编 |
| `secrets.yaml` | API_KEY、MCP 地址 | ❌ 不入库 | UI 修改 |

### 3.2 序列化硬规则
- **必须用 `NullNamingConvention.Instance`**：保持 YAML 字段名与 C# 属性名原样对应，**禁止用 CamelCaseConvention / UnderscoredNamingConvention**（会破坏字段对应）。
- **`PetConfig` 的派生属性 `BgMusicName` / `BgMusicEnabled` 必须加 `[YamlIgnore]`**：否则序列化报错。
- **字段名用 snake_case**：YAML 文件内字段名用 `snake_case`（如 `satiety_loss_per_6h`），C# 属性用 PascalCase，通过 `[YamlMember(Alias = "...")]` 显式映射。
- **解析失败回退默认值**：ConfigService 已实现「文件缺失/解析失败 → 返回 `new T()`」，新增配置文件遵循同样模式（见 `LoadYamlFile<T>`）。

### 3.3 缓存与失效规则
- **所有 Load 方法都缓存**：`Load()` / `LoadSecrets()` / `LoadPetManagement()` / `LoadSkins()` / `LoadFoods()` / `LoadAnimations()` 返回的是缓存实例。
- **修改配置文件后必须 Invalidate**：
  - `InvalidateCache()` — PetConfig
  - `InvalidateSecretsCache()` — secrets
  - `InvalidatePetManagementCache()` — 同时失效 pet_management/skins/foods/animations 四份
- **运行时切换角色必须调用 `ConfigService.InvalidateCache()`**：否则 `Load()` 仍返回旧角色。App.OnStartup 第 6 步已做示范。
- **UI 修改 secrets 必须用 `SaveSecrets`**：不要手写文件，否则缓存与文件不一致。

### 3.4 新增配置字段流程
1. 在对应 Model 类（如 `PetManagementConfig`）加属性 + `[YamlMember(Alias = "snake_case")]`
2. 在 `config/xxx.yaml` 加默认值（带注释）
3. 若有向后兼容需求，在 `LoadXxx` 中处理旧字段
4. `dotnet build` 验证

### 3.5 敏感信息规则
- **API_KEY 与 MCP 地址只存 `config/secrets.yaml`**：禁止硬编码、禁止写入其他配置、禁止写入日志。
- **`secrets.yaml` 必须被 `.gitignore` 排除**（已配置），只提交 `secrets.yaml.example` 模板。
- **API_KEY 不入日志**：Logger 调用禁止带 apiKey 字段。

## 4. 用户数据隔离规则

[UserService](Services/Core/UserService.cs) 是单例（`UserService.Instance`），管理 `user_data/users/<name>/`。

### 4.1 目录结构（禁止跨界）
```
user_data/
├── users.yaml                    # 用户注册表（全局）
└── users/<name>/
    ├── profile.yaml              # 用户档案（偏好、好感度、饱食度、皮肤、金币）
    ├── chat_history.db           # 该用户对话记录（SQLite）
    └── todo.db                   # 该用户待办（SQLite）
```

### 4.2 硬规则
- **所有用户数据写入 `CurrentUserDir`**：`UserService.Instance.CurrentUserDir`，禁止写到全局目录。
- **数据库路径必须基于 `CurrentUserDir`**：App.OnStartup 第 7 步已做示范：`Path.Combine(userDir, "todo.db")`。
- **当前用户为 null 时不得操作用户数据**：所有读写用户数据的服务（AffinityService、FeedService 等）必须先判 `UserService.Instance.CurrentUser == null`。
- **修改 profile 后必须保存**：调 `UserService.Instance.SaveCurrentProfile()`，否则丢失。
- **单例字段访问**：`UserService.Instance.CurrentUser` 可能为 null，**每次访问都要判空**。
- **并发限制**：用户注册表与档案**无并发锁**，假定单实例运行；多实例同时写同一用户档案会冲突。

### 4.3 角色偏好规则
- **角色偏好存 `profile.yaml`，不存 `config/`**：`UserService.SaveCurrentRole(role)` 写入当前用户档案。
- **未登录时角色偏好暂存内存**：`_pendingRole`，登录后 `ApplyPendingRole()` 应用。禁止未登录时写档案。
- **启动恢复角色优先级**：`CurrentUser.Role` > `_pendingRole` > 旧版 `user_data/config_pet.yaml` > `characters.yaml` 默认值。
- **登录后必须 `InvalidateCache`**：让 ConfigService 重新 Load 时用 CurrentUser.Role 覆盖默认角色。

## 5. 好感度规则

[AffinityService](Services/Core/AffinityService.cs) 是单例，数值常量在 `config/pet_management.yaml`。

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

**禁止在代码里硬编码这些数值**，必须从 `ConfigService.LoadPetManagement()` 读。

### 5.2 等级匹配规则
- 等级从高到低匹配，**第一个满足 `min` 的等级生效**（`OrderByDescending(l => l.Min)`）。
- 等级影响：皮肤折扣（`skin_discount`）、AI 对话风格（`GetSystemPromptHint` 注入系统提示）。
- 修改等级表 → 编辑 `config/pet_management.yaml` 的 `levels`，不要改代码。

### 5.3 衰减计时器规则
- `DispatcherTimer` 1 分钟 / 次，**只能在 App.OnStartup 用户登录后启动**（`AffinityService.Instance.Start()`）。
- 衰减扣款后必须**更新 `LastInteraction[role]` 或 `SatietyLowSince[role]` 到当前时间**，避免下次 tick 重复扣。
- 饱食度恢复时必须清空 `SatietyLowSince[role] = null`。

### 5.4 新增好感度来源流程
1. 在 `pet_management.yaml` 的 `rewards` 加字段 + 默认值
2. 在 `RewardsConfig` Model 加属性
3. 在触发位置调 `AffinityService.Instance.AddAffinity(role, delta, applySatietyBonus: ...)`
4. `dotnet build` 验证

## 6. 动画与窗口规则

### 6.1 程序化动画参数（不得改动）
- 呼吸：垂直缩放 1.0↔1.03，2 秒周期
- 漂浮：Y 轴 ±3px，2 秒周期
- 拖拽姿态：垂直拉伸 1.08 + 水平压缩 0.96
- 像素渲染：NearestNeighbor 插值 + Aliased 文本

### 6.2 反应动画 vs 循环动画冲突
**这是高频错误点**。播反应动画前必须：
```csharp
// 1. 先清当前动画
element.BeginAnimation(property, null);
// 2. 播反应动画
var anim = new DoubleAnimation { /* ... */ };
anim.Completed += (_, _) => {
    // 3. Completed 回调恢复程序化动画
    RestoreProceduralAnimation();
};
element.BeginAnimation(property, anim);
```
**禁止省略步骤 1**，否则反应动画不生效；**禁止省略步骤 3**，否则循环动画永久停止。

### 6.3 双击 vs 拖拽冲突
`OnMouseLeftButtonDown` 中**必须先检测 `ClickCount >= 2`**：
```csharp
if (e.ClickCount >= 2)
{
    e.Handled = true;
    // 触发抚摸
    return;  // ← 必须 return，否则进入拖拽流程
}
// 拖拽流程...
```

### 6.4 窗口定位规则
- **必须用逻辑 DIP 坐标**，禁止用物理屏幕坐标（高 DPI 会越界）。
- **聊天/待办窗口贴近宠物**，且不超出屏幕边界：用 `SystemParameters.WorkArea` + DPI 缩放转换。
- **窗口拖拽**必须用 `PointToScreen(e.GetPosition(this))`，**禁止用 `e.GetPosition(null)`**（会位置跳跃）。

### 6.5 透明置顶窗口规则
- 右键菜单**必须用 WPF 原生 `ContextMenu` 属性**（自绘 Popup 不可见）。
- 右键菜单项固定为：聊天 / 待办 / 宠物管理 / 隐藏 / 退出。**静音只在托盘菜单**。
- 右键菜单主题（背景/边框/文字色/悬停高亮）从角色主题读，禁止硬编码颜色。

### 6.6 应用退出规则
- `Close` 事件中**仅隐藏窗口**会阻塞退出，必须实现 `ForceClose()` 路径做实际清理。
- 退出时必须停止 `AffinityService` 计时器、释放音频资源、保存当前用户档案。

## 7. AI / MCP 规则

### 7.1 Provider 注册规则
- 从 `config/secrets.yaml` 的 `ai_providers` 加载，**支持 `enabled: false` 跳过**。
- **兜底逻辑**：若无任何 provider 注册，回退到环境变量 `OPENAI_API_KEY`；仍无则注册 `LocalKleeProvider`。**禁止移除兜底**。
- `type=local` 注册 `LocalKleeProvider`；`type=openai-compatible` 注册 `OpenAiCompatibleProvider`。新增 type 必须在 `RegisterAiProviders` 加分支。

### 7.2 MCP 注册规则
- 当前**仅实现 `local` 类型**，`stdio`/`sse` 为预留接口（记 Warn 跳过）。
- `local` 类型目前**仅支持 `local-todo`**，其他 name 记 Warn 跳过。
- 兜底：若无任何 MCP 注册，注册默认 `local-todo`。

### 7.3 OpenAI 兼容接口已知限制
- 历史消息**只保存文本，未携带 `tool_calls` 结构化字段**；连续多轮工具调用的第二次请求可能被严格服务拒绝。修改此限制需同时改 `ChatHistoryRepository` 表结构与 `ChatOrchestrator` 消息构造。

## 8. 资源生成与皮肤规则

### 8.1 程序化生成规则
- 角色精灵图由 `Generators/<Role>SpriteGenerator.cs` 生成到 `static/<角色英文名>/classic/sheet.png`。
- **已存在则跳过**（便于用户手动替换），禁止强制覆盖。
- 新增角色必须在 `SpriteGeneratorRegistry.EnsureGenerated` 的 switch 加 case。

### 8.2 皮肤系统定义
- **皮肤 = 精灵图 + 主题配色 + 声音**，与角色绑定但可独立切换。
- **动作系统独立于皮肤**：所有动作定义在 `config/animations.yaml`，不同皮肤共用。
- 皮肤价格 400 金币（常量 `constants.skin_price`），禁止硬编码。

### 8.3 静态资源目录规则
- 路径固定：`static/<角色英文名>/<皮肤名>/`（如 `static/luffy/classic/`）。
- 文件名固定：`sheet.png` / `icon.png` / `bgm.mp3`（或 `bgm.wav`）。
- 用户放入同名文件即覆盖代码生成默认值，**禁止改路径约定**。

## 9. 命名与代码风格

- **C# 命名**：PascalCase（类、属性、方法）、camelCase（局部变量、私有字段前缀 `_`）。
- **YAML 字段名**：snake_case，C# 用 `[YamlMember(Alias = "snake_case")]` 映射。
- **namespace**：`DesktopPetCSharp.<子模块>`（如 `DesktopPetCSharp.Services`、`DesktopPetCSharp.Generators`）。
- **文件组织**：一个 Model / Service / Window 一个文件，文件名 = 类名。
- **单例模式**：`public static XxxService Instance { get; } = new();` + 私有构造。

## 10. 易错点清单

| # | 现象 | 原因 | 解决 |
|---|---|---|---|
| 1 | YamlDotNet 序列化报错 | 派生属性未加 `[YamlIgnore]` | 给 `BgMusicName`/`BgMusicEnabled` 等派生属性加 `[YamlIgnore]` |
| 2 | 右键菜单不可见 | 透明窗口用自绘 Popup | 改用 WPF 原生 `ContextMenu` 属性 |
| 3 | 拖拽位置跳跃 | 用 `e.GetPosition(null)` | 改用 `PointToScreen(e.GetPosition(this))` |
| 4 | 应用无法退出 | Close 仅 Hide 窗口 | 实现 `ForceClose()` 清理路径 |
| 5 | 高 DPI 下窗口越界 | 用物理屏幕坐标 | 用逻辑 DIP + DPI 缩放转换 |
| 6 | 反应动画不生效 | 未先清当前动画 | 先 `BeginAnimation(prop, null)` 再播 |
| 7 | 循环动画永久停止 | 反应动画 Completed 未恢复 | Completed 回调调 `RestoreProceduralAnimation` |
| 8 | 双击触发拖拽 | 未先检测 ClickCount | 先判 `e.ClickCount >= 2`，`e.Handled = true` 后 return |
| 9 | 切换角色后配置未刷新 | 缓存未失效 | 调 `ConfigService.InvalidateCache()` |
| 10 | 登录后角色不对 | 未失效缓存或未 ApplyPendingRole | OnStartup 第 6、9 步必须执行 |
| 11 | 好感度重复扣 | 衰减后未更新 LastInteraction | 扣款后 `user.LastInteraction[role] = now` |
| 12 | 饱食度恢复后仍扣好感 | SatietyLowSince 未清空 | 恢复时 `SatietyLowSince[role] = null` |
| 13 | ChatHistoryWindow 绑定报错 | DisplayTime 只读属性 TwoWay 绑定 | 改用 OneWay 绑定 |
| 14 | InteractPanel 行为失效 | helper 方法重赋字段引用 | 传入并修改属性，Click 在调用点绑定 |
| 15 | OpenAI 第二次工具调用被拒 | 历史消息缺 tool_calls 字段 | 已知限制，需改表结构 |

## 11. 开发流程检查清单

每个任务完成前确认：
- [ ] `dotnet build` 0 错误 0 警告
- [ ] 新增用户操作有 Logger 记录
- [ ] 新增配置字段走 §3.4 流程
- [ ] 修改好感度数值走 §5.4 流程，不硬编码
- [ ] 修改用户数据后调 `SaveCurrentProfile()`
- [ ] 修改配置后调对应 `InvalidateXxxCache()`
- [ ] 新增动画遵循 §6.2 反应动画规则
- [ ] 不引入硬编码颜色 / 数值 / 路径
- [ ] 异常路径有 `Logger.Error(..., ex)`
- [ ] 中文注释 + 中文日志消息

## 12. 不应做的事

- ❌ 用 `Console.WriteLine` / `Debug.WriteLine` 替代日志
- ❌ 硬编码好感度数值、皮肤价格、路径
- ❌ 把 API_KEY 写入非 secrets.yaml 的地方
- ❌ 用 `e.GetPosition(null)` 拖拽
- ❌ 直接物理屏幕坐标定位窗口
- ❌ 自绘右键菜单（透明窗口下不可见）
- ❌ 未登录时写用户档案
- ❌ 跨用户目录读写数据
- ❌ 强制覆盖用户手动放入的 `static/` 资源
- ❌ 移除 AI Provider / MCP 的兜底注册逻辑
- ❌ 在反应动画 Completed 回调里不恢复程序化动画
- ❌ 改 `logs/` 目录结构或日志文件命名格式
- ❌ 用非 `NullNamingConvention` 的 YAML 命名约定
