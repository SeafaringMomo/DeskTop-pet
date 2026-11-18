# 配置文件

配置由 [ConfigService](../Services/Core/ConfigService.cs) 统一加载。角色定义不再来自 `config/`，而是扫描 `static/` 下的 `theme.yaml`。

## 配置文件清单

| 文件 | 内容 | 是否入库 | 模型 |
|---|---|---|---|
| `config/app.yaml` | 应用常量（音频、动画间隔、窗口边距） | ✅ | `GentConfig` |
| `config/gent_management.yaml` | 养成常量（奖励、冷却、衰减、等级） | ✅ | `GentManagementConfig` |
| `config/foods.yaml` | 食物定义 | ✅ | `FoodConfig` |
| `config/secrets.yaml` | API_KEY、MCP 地址 | ❌ | `SecretsConfig` |
| `static/<角色目录>/theme.yaml` | 角色显示名 + 主题配色 | ✅ | `RoleTheme` |

已删除：`characters.yaml`、`skins.yaml`、`animations.yaml`。

## app.yaml

```yaml
audio: true
bg_music: true
static_path: static
animation:
  frame_interval_ms: 120
  random_action_interval_ms: 9000
window:
  edge_margin: 20
```

| 字段 | 默认 | 说明 |
|---|---|---|
| `frame_interval_ms` | 120 | 帧切换间隔 |
| `random_action_interval_ms` | 9000 | 随机扣鼻孔触发间隔（带抖动） |

## gent_management.yaml

集中管理奖励（`rewards`）、冷却（`cooldown`）、衰减（`decay`）、等级（`levels`）与初始值（`constants`）。数值均从配置读取，禁止硬编码。

## foods.yaml

每个食物包含 `id`、`name`、`icon`、`price`、`satiety_gain`、`role_preference`、`description`。

## secrets.yaml

复制 `config/secrets.yaml.example` 创建，存放 `ai_providers`、`mcp_servers` 与 `agent`。该文件不入库、不入日志。

### agent 块（Python Agent sidecar）

```yaml
agent:
  enabled: true                # false = 完全关闭 Python Agent（回退纯 C# 链路）
  auto_start: true             # 应用启动时自动拉起 sidecar
  python_path: ""              # 空 = 自动探测 <项目根>/.venv
  workspace_root: ""           # 空 = <用户目录>/agent_workspace，支持 {user} 占位
  max_tool_iterations: 8       # 单轮最多工具往返次数
  request_timeout_seconds: 120 # 健康检查与单次请求超时
  enable_exec: false           # 是否允许 exec_run 工具（默认关闭）
  log_level: INFO
  brave_api_key: ""            # 启用 web_search 时填写
```

| 字段 | 类型 | 默认 | 说明 |
|---|---|---|---|
| `enabled` | bool | true | 总开关；false 时不启动 sidecar、不注册 `python-agent` |
| `auto_start` | bool | true | false 时保留 Provider 但不自动拉起（供手动启动调试） |
| `python_path` | string | "" | 显式指定解释器；优先级高于自动探测 |
| `workspace_root` | string | "" | 人格/记忆文件根目录，按用户隔离 |
| `max_tool_iterations` | int | 8 | 防止模型无限调用工具 |
| `request_timeout_seconds` | int | 120 | 同时用于健康检查上限（至少 5 秒） |
| `enable_exec` | bool | false | 开启后 `exec_run` 仅允许白名单命令 |
| `log_level` | string | INFO | 传给 Python 日志级别 |
| `brave_api_key` | string | "" | 为空则不注册 `web_search` |

> `brave_api_key` 与 `ai_providers[].api_key` 一样**禁止写日志**。
> sidecar 复用 `ai_providers` 中第一个启用的 `openai-compatible` 的模型与密钥，无需重复配置。

## theme.yaml（角色目录）

```yaml
name: 默认角色
primary: "#F5D547"
accent: "#E74C3C"
background: "#3D2817"
surface: "#5C3A1E"
text: "#FFF3D9"
border: "#F5D547"
```

- `name`：角色显示名（在「角色」菜单显示）。
- 其余字段为主题配色，用于聊天、待办、助手管理窗口与右键菜单。

## ConfigService 角色扫描

`ConfigService.ScanRoles` 流程：

1. 遍历 `static/` 下每个子目录。
2. 读取 `theme.yaml`；缺失或解析失败则跳过。
3. `name` 作为角色显示名，目录名作为英文目录名，写入 `Roles` / `Themes` / `FrameScale`。

若没有任何角色，兜底注册 `默认角色 → default_role`。

## 缓存与失效

| 方法 | 失效项 |
|---|---|
| `InvalidateCache()` | 应用配置与角色扫描结果 |
| `InvalidateSecretsCache()` | secrets |
| `InvalidateGentManagementCache()` | gent_management、foods |

导入角色或修改 `theme.yaml` 后必须调用 `ConfigService.InvalidateCache()`。

## YAML 反序列化约定

- 使用 `NullNamingConvention.Instance`，字段名与 C# 属性原样对应。
- YAML 字段 `snake_case`，C# 属性 PascalCase，用 `[YamlMember(Alias = "...")]` 映射。
