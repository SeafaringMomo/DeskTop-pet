# config 配置文件夹

应用所有配置集中存放在 `config/` 目录，共 7 份 YAML 文件。敏感信息与常量分离，非敏感文件入库。

## 配置文件总览

| 文件 | 用途 | 是否入库 | 模型类 |
|---|---|---|---|
| `config/app.yaml` | 应用常量（非敏感）：音频开关、精灵图配置、动画参数、窗口边距 | ✅ 入库 | `PetConfig`（合并后） |
| `config/characters.yaml` | 角色定义（非敏感）：default_role、roles 映射、frame_scale、themes 配色 | ✅ 入库 | `PetConfig`（合并后） |
| `config/pet_management.yaml` | 宠物管理常量：皮肤价格、衰减规则、奖励值、冷却时间、好感度等级 | ✅ 入库 | `PetManagementConfig` |
| `config/skins.yaml` | 皮肤定义：每个皮肤的精灵图目录、主题、声音、角色绑定 | ✅ 入库 | `SkinConfig` |
| `config/foods.yaml` | 食物定义：名称、图标、价格、饱食度增益、角色偏好 | ✅ 入库 | `FoodConfig` |
| `config/animations.yaml` | 动作系统定义：idle/breath/float/drag/pat/highfive/feed/hungry/sleep | ✅ 入库 | `AnimationConfig` |
| `config/secrets.yaml` | 敏感信息：AI 提供方（含 API_KEY）、MCP 服务器地址端口 | ❌ 不入库 | `SecretsConfig` |
| `config/secrets.yaml.example` | 敏感信息模板（复制为 `secrets.yaml` 后填值） | ✅ 入库 | — |

`.gitignore` 已配置排除 `config/secrets.yaml`。

## ConfigService 加载机制

`Services/Core/ConfigService.cs` 提供加载与缓存：

| 方法 | 用途 |
|---|---|
| `ConfigService.Load()` | 合并 `app.yaml` + `characters.yaml` 为 `PetConfig`，并用用户偏好覆盖默认角色 |
| `ConfigService.LoadSecrets()` | 读取 `secrets.yaml` |
| `ConfigService.SaveSecrets(secrets)` | 写入 `secrets.yaml`（模型设置窗口编辑 provider 后调用） |
| `ConfigService.Save(config)` | 写入 `app.yaml` + `characters.yaml`（切换角色后持久化） |
| `ConfigService.LoadPetManagement()` | 读取 `pet_management.yaml` |
| `ConfigService.LoadSkins()` | 读取 `skins.yaml` |
| `ConfigService.LoadFoods()` | 读取 `foods.yaml` |
| `ConfigService.LoadAnimations()` | 读取 `animations.yaml` |
| `ConfigService.InvalidateCache()` | 失效 `PetConfig` 缓存（用户登录后调用，让 `Load()` 用 `CurrentUser.Role` 覆盖） |
| `ConfigService.InvalidateSecretsCache()` | 失效 `SecretsConfig` 缓存 |
| `ConfigService.InvalidatePetManagementCache()` | 失效 `PetManagementConfig` 缓存 |

所有加载方法带缓存（`lock` + 静态字段），避免重复读文件。

---

## 1. app.yaml

应用级常量（非敏感），调整动画节奏、精灵图切分、窗口边距等行为。

```yaml
# 应用级常量（非敏感，可入库）
audio: true              # 是否启用人物语音
bg_music: true           # 是否播放当前角色的背景音乐
static_path: static      # 静态资源根目录（相对项目根）

# 精灵图（Sprite Sheet）配置
sprite:
  file: sheet.png        # 精灵图文件名
  cols: 2                # 列数（横向帧数）
  rows: 1                # 行数（纵向帧数）

# 动画参数
animation:
  blink_interval_ms: 3000   # 眨眼间隔毫秒
  default_scale: 0.4        # 默认缩放比例（基础尺寸 300px × 0.4 = 120px 显示）

# 窗口定位
window:
  edge_margin: 20        # 距屏幕边缘的留白像素
```

### PetConfig 合并后字段

| 字段 | 类型 | 说明 | 来源 |
|---|---|---|---|
| `audio` | bool | 是否启用人物语音 | app.yaml |
| `bg_music` | bool | 是否播放当前角色的背景音乐 | app.yaml |
| `role` | string | 当前角色名（默认 `路飞`） | characters.yaml → 用户 profile 覆盖 |
| `static_path` | string | 静态资源根目录（默认 `static`） | app.yaml |
| `roles` | map | 角色中文名 → 英文目录名映射（如 `路飞: luffy`） | characters.yaml |
| `themes` | map | 角色配色主题（详见下节） | characters.yaml |
| `frame_scale` | map | 每个角色的 `[眨眼帧时长(ms), 缩放比例]`，默认缩放 0.4 | characters.yaml |
| `sprite_file` | string | 精灵图文件名（默认 `sheet.png`） | app.yaml |
| `sprite_cols` | int | 精灵图列数（默认 2） | app.yaml |
| `sprite_rows` | int | 精灵图行数（默认 1） | app.yaml |
| `blink_interval_ms` | int | 眨眼间隔毫秒（默认 3000） | app.yaml |

---

## 2. characters.yaml

角色定义（非敏感），含角色映射、配色主题、各角色动画参数。

```yaml
default_role: 路飞       # 默认角色（首次启动且无用户偏好时使用）

# 角色中文名 → 英文目录名映射
roles:
  路飞: luffy
  索隆: zoro

# 各角色动画参数：[眨眼帧时长(ms), 缩放比例]
frame_scale:
  路飞:
  - 150
  - 0.4
  索隆:
  - 150
  - 0.4

# 角色配色主题（与角色绑定，切换角色即切换配色）
themes:
  路飞:
    primary: "#F5D547"      # 草帽黄
    accent: "#E74C3C"       # 红色（帽带）
    background: "#3D2817"   # 深棕
    surface: "#5C3A1E"      # 中棕
    text: "#FFF3D9"         # 米白
    border: "#F5D547"
  索隆:
    primary: "#3A7D3A"      # 深绿
    accent: "#8B4513"       # 棕色
    background: "#1A2E1A"   # 深绿黑
    surface: "#2A3F2A"
    text: "#D9E8D9"
    border: "#3A7D3A"
```

### RoleTheme 字段

| 字段 | 含义 | 路飞默认 | 索隆默认 |
|---|---|---|---|
| `primary` | 主色（边框、强调、标题栏渐变起点） | `#F5D547` 草帽黄 | `#3A7D3A` 深绿 |
| `accent` | 强调色（标题栏渐变终点、删除按钮） | `#E74C3C` 红 | `#8B4513` 棕 |
| `background` | 窗口背景色 | `#3D2817` 深棕 | `#1A2E1A` 深绿黑 |
| `surface` | 输入框/列表表面色 | `#5C3A1E` 中棕 | `#2A3F2A` |
| `text` | 文字颜色 | `#FFF3D9` 米白 | `#D9E8D9` |
| `border` | 边框颜色 | `#F5D547` | `#3A7D3A` |

应用于：聊天窗、待办窗、右键菜单、模型设置窗、宠物管理窗。

---

## 3. pet_management.yaml

宠物管理系统常量（非敏感），集中管理皮肤价格、衰减规则、奖励值、冷却时间、好感度等级。

```yaml
# 常量
constants:
  skin_price: 400              # 皮肤价格（金币）
  food_default_price: 20       # 食物默认价格
  initial_coins: 100           # 新用户初始金币
  initial_affinity: 50         # 初始好感度
  initial_satiety: 50          # 初始饱食度

# 衰减规则
decay:
  satiety_loss_per_6h: 10            # 饱食度每 6 小时 -10
  affinity_loss_per_day: 2           # 好感度每日 -2（无互动）
  satiety_low_threshold: 20          # 饱食度低阈值
  satiety_low_affinity_loss_minutes: 120  # 饱食度<20 持续 2 小时扣 1 好感度
  muted_affinity_loss_per_hour: 1    # 静音状态每小时 -1

# 好感度奖励
rewards:
  feed_loved: 8                # 喂食喜欢 +8
  feed_normal: 5               # 喂食普通 +5
  feed_disliked: 1             # 喂食讨厌 +1
  pat: 1                       # 抚摸 +1
  highfive: 2                  # 击掌 +2
  chat_per_message: 1          # 聊天 +1
  todo_complete: 2             # 完成待办 +2
  idle_per_hour: 1             # 挂机每小时 +1
  satiety_high_bonus: 1.2      # 饱食度>80 时互动好感度加成

# 冷却（秒）
cooldown:
  feed: 5
  pat: 5
  highfive: 30
  play: 300

# 好感度等级（从高到低匹配，第一个满足 min 的等级生效）
levels:
  - min: 80
    name: 挚友
    skin_discount: 0.7
    description: 专属台词
  - min: 60
    name: 亲密
    skin_discount: 0.8
    description: 表达亲近
  - min: 40
    name: 友好
    skin_discount: 0.9
    description: 主动关心
  - min: 20
    name: 认识
    skin_discount: 0.95
    description: 偶尔热情
  - min: 0
    name: 陌生人
    skin_discount: 1.0
    description: 冷淡称呼
```

详见 [宠物管理数据库.md](宠物管理数据库.md) 的好感度变化表。

---

## 4. skins.yaml

皮肤定义（非敏感）。**皮肤系统包含：声音、主题配色、角色**，与角色绑定但可独立切换。

```yaml
skins:
  - id: luffy_classic              # 皮肤唯一 ID
    role: 路飞                      # 所属角色中文名
    name: 经典路飞                  # 显示名称
    sprite_dir: static/luffy/classic  # 精灵图目录（相对 BaseDir）
    theme:                          # 皮肤配色主题（独立于角色，皮肤级）
      primary: "#F5D547"
      accent: "#E74C3C"
      background: "#3D2817"
      surface: "#5C3A1E"
      text: "#FFF3D9"
      border: "#F5D547"
    audio:                          # 皮肤声音配置（路径相对 sprite_dir）
      bgm: bgm.mp3
      greeting_morning: 早上好.mp3
      greeting_noon: 中午好.mp3
      greeting_evening: 晚上好.mp3
      farewell: 晚安.mp3
      feed_sfx: eat.mp3             # 喂食音效（可选）
    description: 草帽小子初登场
    is_default: true                # 该角色的默认皮肤
```

### Skin 字段

| 字段 | 类型 | 说明 |
|---|---|---|
| `id` | string | 皮肤唯一 ID（如 `luffy_classic`） |
| `role` | string | 所属角色中文名 |
| `name` | string | 皮肤显示名称 |
| `sprite_dir` | string | 精灵图目录（相对 BaseDir，如 `static/luffy/classic`） |
| `theme` | RoleTheme | 皮肤配色主题（覆盖角色主题） |
| `audio` | SkinAudio | 皮肤声音配置 |
| `description` | string | 皮肤描述 |
| `is_default` | bool | 是否为该角色的默认皮肤 |

### SkinAudio 字段

| 字段 | 说明 | 缺失时 |
|---|---|---|
| `bgm` | 背景音乐文件名 | 不播放 BGM |
| `greeting_morning` | 早上问候语音 | 静默跳过 |
| `greeting_noon` | 中午问候语音 | 静默跳过 |
| `greeting_evening` | 晚上问候语音 | 静默跳过 |
| `farewell` | 退出语音 | 直接退出 |
| `feed_sfx` | 喂食音效 | 跳过音效 |

### 默认皮肤

`is_default: true` 的皮肤在新用户创建时自动解锁并装备（`SkinService.InitDefaultSkins`）。

### 资源目录结构

```
static/
├── luffy/
│   ├── classic/          # luffy_classic 皮肤
│   │   ├── sheet.png     # 精灵图
│   │   ├── icon.png      # 托盘图标
│   │   └── bgm.mp3       # 背景音乐
│   └── gear4/            # luffy_gear4 皮肤（需用户自行放入素材）
└── zoro/
    ├── classic/          # zoro_classic 皮肤
    │   ├── sheet.png
    │   ├── icon.png
    │   └── bgm.wav
    └── ronin/            # zoro_ronin 皮肤（需用户自行放入素材）
```

---

## 5. foods.yaml

食物定义（非敏感），含角色偏好。

```yaml
foods:
  - id: meat              # 食物 ID
    name: 烤肉            # 显示名称
    icon: 🍖              # 图标（emoji）
    price: 20             # 价格（金币）
    satiety_gain: 30      # 饱食度增益
    role_preference:      # 角色偏好
      路飞: loved          # loved: 好感度 +8
      索隆: normal         # normal: 好感度 +5
    description: 路飞的最爱
```

### Food 字段

| 字段 | 类型 | 说明 |
|---|---|---|
| `id` | string | 食物唯一 ID |
| `name` | string | 显示名称 |
| `icon` | string | 图标（emoji） |
| `price` | int | 价格（金币） |
| `satiety_gain` | int | 饱食度增益 |
| `role_preference` | map<string,string> | 角色中文名 → 偏好等级（loved/normal/disliked） |
| `description` | string | 食物描述 |

### 偏好等级

| 等级 | 好感度奖励 |
|---|---|
| `loved` | +8（`Rewards.FeedLoved`） |
| `normal` | +5（`Rewards.FeedNormal`） |
| `disliked` | +1（`Rewards.FeedDisliked`） |

---

## 6. animations.yaml

动作系统定义（非敏感）。**动作系统独立于皮肤**：不同皮肤共用同一套动作定义，只是帧图不同。

```yaml
animations:
  idle:                          # 待机循环
    type: sprite_sheet
    interval_ms: 3000
    frames: [0, 1]

  breath:                        # 呼吸
    type: procedural
    target: scale_y
    from: 1.0
    to: 1.03
    duration_s: 2.0
    auto_reverse: true

  float:                         # 漂浮
    type: procedural
    target: translate_y
    from: 0
    to: -3
    duration_s: 2.0
    auto_reverse: true

  drag:                          # 拖拽姿态
    type: procedural
    target: scale
    scale_x: 0.96
    scale_y: 1.08
    duration_ms: 120

  pat:                           # 抚摸（一次性）
    type: sprite_sheet
    frames: [0, 1, 0, 1, 0]
    interval_ms: 200
    repeat: 1

  highfive:                      # 击掌（一次性）
    type: procedural
    target: translate_y
    from: 0
    to: -15
    duration_ms: 200
    auto_reverse: true

  feed:                          # 喂食（一次性）
    type: sprite_sheet
    frames: [1]
    duration_ms: 1000
    repeat: 1

  hungry:                        # 饥饿（持续循环）
    type: sprite_sheet
    frames: [1]
    repeat: forever

  sleep:                         # 睡眠（持续循环）
    type: sprite_sheet
    frames: [1]
    repeat: forever
```

### 动作类型

| 类型 | 说明 | 驱动目标 |
|---|---|---|
| `sprite_sheet` | 帧动画（按 frames 索引切换 PetImage.Source） | `PetImage.Source` |
| `procedural` | 程序化动画（WPF DoubleAnimation） | `BreathScale` / `BobTranslate` |

### AnimationDef 字段

| 字段 | 类型 | 说明 |
|---|---|---|
| `type` | string | `sprite_sheet` 或 `procedural` |
| `frames` | list<int> | 帧索引列表（sprite_sheet 类型） |
| `interval_ms` | int | 帧切换间隔毫秒（sprite_sheet 类型） |
| `target` | string | 驱动目标：`scale_y` / `translate_y` / `scale`（procedural 类型） |
| `from` / `to` | double | 动画起止值（procedural 类型） |
| `duration_s` | double | 持续时间秒（procedural 类型） |
| `duration_ms` | int | 持续时间毫秒（procedural 类型，优先于 duration_s） |
| `auto_reverse` | bool | 是否自动反向（procedural 类型） |
| `scale_x` / `scale_y` | double | 缩放值（target=scale 时） |
| `repeat` | string | 重复次数：`1` / `forever` |

### 状态驱动

`AnimationService.SetState(PetState)` 根据宠物状态切换循环：
- `Idle`：idle + breath + float（默认）
- `Hungry`：hungry（饱食度低于阈值，持续闭眼）
- `Sleep`：sleep（长时间无互动）

### 一次性动作

`AnimationService.Play(animationId)` 播放一次性动作（pat/highfive/feed），结束后自动回到当前状态循环。

---

## 7. secrets.yaml

敏感信息，**不入库**（.gitignore 排除）。包含 AI 提供方与 MCP 服务器配置。

### 字段结构

```yaml
ai_providers:              # AI 提供方列表
  - name: local-klee       # 提供方名（唯一）
    type: local            # local 或 openai-compatible
    description: 本地占位实现
    enabled: true

  - name: deepseek
    type: openai-compatible
    base_url: https://api.deepseek.com/v1
    api_key: sk-xxx        # ★ 敏感
    model: deepseek-chat
    description: DeepSeek 在线对话
    enabled: false

mcp_servers:               # MCP 服务器列表
  - name: local-todo
    type: local            # local / stdio / sse
    enabled: true

  - name: weather          # stdio 类型（预留接口，暂未实现）
    type: stdio
    command: python
    args: ["-m", "weather_mcp_server"]
    enabled: false
```

### AiProviderConfig 字段

| 字段 | 类型 | 说明 |
|---|---|---|
| `name` | string | 提供方名（唯一） |
| `type` | string | `local`（本地占位）或 `openai-compatible`（HTTP） |
| `base_url` | string | API 基础 URL（openai-compatible 类型） |
| `api_key` | string | API 密钥（敏感，openai-compatible 类型） |
| `model` | string | 模型名（如 `gpt-4o-mini`） |
| `description` | string | 描述 |
| `enabled` | bool | 是否启用 |

### McpServerConfig 字段

| 字段 | 类型 | 说明 |
|---|---|---|
| `name` | string | 服务器名（唯一） |
| `type` | string | `local` / `stdio` / `sse` |
| `enabled` | bool | 是否启用 |
| `command` | string | 子进程命令（stdio 类型） |
| `args` | list<string> | 子进程参数（stdio 类型） |
| `url` | string | SSE 端点 URL（sse 类型） |

### 运行时编辑

通过聊天窗口标题栏「⚙ 模型」按钮打开 `ModelSettingsWindow`：
- 列出所有已注册 provider，显示名称、是否支持工具调用、描述
- 选中后点「应用」切换默认提供方
- 底部 ➕ 新增 / ✏ 编辑 / 🗑 删除 三个按钮，编辑界面用 `PasswordBox` 输入 API_KEY
- 变更即时写回 `secrets.yaml`，并同步 `AiProviderRegistry`
- `local-klee` 为内置兜底，不可编辑/删除
- 删除当前默认 provider 后自动回退到 `local-klee`

### 加载流程

`App.RegisterAiProviders`：
1. 从 `secrets.yaml` 加载所有 `enabled: true` 的 provider
2. `type=local` → 注册 `LocalKleeProvider`
3. `type=openai-compatible` 且 `api_key` 非空 → 注册 `OpenAiCompatibleProvider`
4. 若 `secrets.yaml` 无任何 provider，回退到环境变量 `OPENAI_API_KEY`
5. 兜底：始终保证有 `LocalKleeProvider`

`App.RegisterMcpServers`：
1. 从 `secrets.yaml` 加载所有 `enabled: true` 的 MCP 服务器
2. `type=local` 且 `name=local-todo` → 注册 `LocalTodoMcpServer`
3. `type=local` 其他 → 记录警告（未实现）
4. `type=stdio` / `sse` → 预留接口，记录警告跳过
5. 兜底：无配置时注册 `local-todo`

### secrets.yaml.example

提供配置模板，含 `local-klee` / `deepseek` / `openai` 三个 provider 示例与 `local-todo` / `weather` / `remote-search` 三个 MCP 示例。首次使用前复制为 `secrets.yaml` 并填入真实 API_KEY。
