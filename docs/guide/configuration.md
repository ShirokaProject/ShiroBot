# 配置文件

ShiroBot 使用 TOML。默认核心配置文件位于宿主程序旁的 `config.toml`，文件保存后部分设置会自动热更新。

## 完整示例

```toml
enable_log = true
disable_console_input = false
github_proxy = ""
host_update_repository = "ShirokaProject/ShiroBot"
avalonia_theme = "Auto"

owner_list = ["qq-work:123456789"]
admin_list = ["qq-official:06E88C1E2090950724B8D9E8A4E097E3"]

[api]
enable = true
enable_dashboard = true # 内嵌 Dashboard 开关，修改后重启生效
listen_urls = ["http://127.0.0.1:7001"]
public_base_url = []
token = ""

[plugin_routes.default]
mode = "blacklist"
groups = []

[plugin_routes.plugins.ExamplePlugin]
mode = "whitelist"
groups = [10001, 10002]

```

## 基础设置

| 配置项 | 默认值 | 说明 |
| --- | --- | --- |
| `enable_log` | `true` | 是否显示普通日志 |
| `disable_console_input` | `false` | 是否禁用交互式控制台命令 |
| `github_proxy` | 空 | GitHub 下载代理前缀 |
| `host_update_repository` | `ShirokaProject/ShiroBot` | 宿主更新仓库 |
| `avalonia_theme` | `Auto` | `Light`、`Dark` 或 `Auto`（按时间切换，18:00–6:00 为深色） |
| `owner_list` | `[]` | 所有者身份列表（`instanceId:userId`）；自动拥有管理员权限，`Context.IsOwner` 和 `Context.IsAdmin` 均返回 `true` |
| `admin_list` | `[]` | 管理员身份列表（`instanceId:userId`）；owner 无需重复填写，插件通过 `Context.IsAdmin` 判断 |
| `protocols` | `[]` | 适配器包总开关：填写启用的包 ID，空列表全部关闭；也可填写开发用独立 DLL 路径 |

账号 ID 按平台原样填写，写成字符串：QQ 号、开放平台 OpenID、Telegram 用户名等都可以，不要求是数字。

`owner_list` 和 `admin_list` 供插件判断权限，不开放宿主控制台命令的私聊入口。禁用控制台输入后，仍可使用受鉴权保护的 Dashboard API 管理宿主。

### 开发时加载独立适配器 DLL

已安装包的总开关只由主配置 `protocols` 管理，例如 `protocols = ["qq-official", "milky"]`。Dashboard 打开包时添加其 ID，关闭时移除；包内实例的 `enabled` 与连接配置保存在适配器自己的 `config.toml`。只有包 ID 已列入且实例启用时才启动。旧总开关文件和旧实例注册表不读取、不迁移。手工修改总开关在重启时应用，Dashboard 开关立即操作运行状态。

开发适配器时，可以不打包安装，直接加载一个 DLL：

```toml
# 依次查找 adapters/MyAdapter.dll、adapters/MyAdapter/MyAdapter.dll，也可以写绝对路径
protocols = ["MyAdapter"]
```

或在启动时指定：`./ShiroBot --adapter /path/to/MyAdapter.dll`。找不到的条目会记录错误并跳过，不影响宿主启动。

## 适配器实例清单

v0.9.7 起把实例清单和连接配置放在各适配器自己的 `config.toml` 中，主配置不再保存实例。安装包后，在 Dashboard 展开适配器并添加唯一 ID，或直接编辑文件：

```toml
[[instances]]
id = "qq-work"
name = "工作机器人"
enabled = true

[instances.config]
# 当前实例的连接配置。

[[instances]]
id = "qq-home"
enabled = false

[instances.config]
# 另一个账号的连接配置。
```

实例 ID 全局唯一，enabled 默认 false，不再自动创建包 ID 对应的实例。`instances = []` 保留已安装包但不加载机器人。WebUI/CLI 创建、启停、删除写回同一份文件；修改实例清单后重启生效。没有 `[[instances]]` 的旧配置按单实例处理（实例 ID 为包 ID，根配置即连接配置），文件保持原样；新增实例、重命名或删除该实例时才改写为 `[[instances]]`，并保留 ID、启用状态和连接配置。

完整部署、备份和无需 WebUI 的管理方法见[配置与部署](/adapter/deployment#没有-webui-时)。

## 检查与安装更新

Dashboard 的“关于”页可手动检查宿主更新。找到适合当前平台、架构和运行形态的发布包后，可点击“更新并重启”。此功能适用于单文件 Release 发布包；开发运行或通过 `dotnet ShiroBot.dll` 启动时只检查版本。Docker 中会显示拉取新镜像的命令；systemd 服务更新后需要 `Restart=on-failure` 或 `Restart=always` 才能自动重启。

控制台可使用 `update check host`、`update check plugins`，再通过 `update confirm <id>` 执行更新。插件更新与 Dashboard 共用整包替换逻辑，保留 `config.toml` 和组件数据；无法卸载或文件被占用时暂存新包，重启宿主后应用。执行失败的更新请求仍可再次确认，或使用 `update cancel <id>` 取消。

Dashboard 删除插件时会取消同一插件的待确认更新和暂存更新。如果当前版本无法卸载或文件仍被占用，删除任务会写入 `plugins/.update/<id>/`，面板提示重启后删除。下次启动会在加载插件、应用更新之前执行删除；失败的删除任务保留，后续启动继续重试。

## 插件群路由

群路由用于限制插件在哪些群中接收事件。

```toml
[plugin_routes.default]
mode = "blacklist"
groups = [10001]
```

- `blacklist`：除列表中的群以外全部允许。
- `whitelist`：仅允许列表中的群。

可以按 `BotPluginAttribute` 中的插件 ID 覆盖默认规则：

```toml
[plugin_routes.plugins.GithubPlugin]
mode = "whitelist"
groups = [10001, 10002]
```

路由配置保存后会热更新，插件不需要重新加载。

## HTTP API

```toml
[api]
enable = true
listen_urls = ["http://127.0.0.1:7001", "http://[::1]:7001"]
public_base_url = ["https://bot.example.com", "https://bot-backup.example.com"]
token = ""
```

- `enable_dashboard` 默认为 `true`。设为 `false` 后不提供宿主 `/dashboard` 页面及其静态资源；HTTP API 和插件 Web 路由仍可使用，适合使用独立部署的 Dashboard。修改后重启宿主生效。`api.enable = false` 则关闭整个 HTTP 服务。
- `listen_urls` 是监听地址列表；只监听一个地址时也写成单元素数组，默认为 `["http://127.0.0.1:7001"]`。
- `public_base_url` 是反向代理后的外部地址数组，插件 `Context.WebHost` 生成链接使用第一个非空地址；空数组使用第一个监听地址。只接受数组，不兼容旧字符串格式。额外的公开地址需要自行配置 DNS、反向代理，数组本身不会新增监听或执行负载均衡。
- API 始终要求鉴权，配置使用 `[api].token`，不再提供 `[api.auth]` 或关闭鉴权的开关。`token` 为空时，宿主启动会自动生成随机令牌并写回配置；运行时不能通过配置 API 将令牌设为空。旧 `api.auth.key` 不再读取，需要将原值手动填入 `api.token`。

使用 Nginx、Caddy 等反向代理时，应同时配置 TLS 和访问控制。

## 适配器和插件配置

适配器与插件各自拥有独立配置文件，路径由宿主根据组件位置解析并规范化：

- 单 DLL 直接放在 `adapters/` 时：`adapters/config.toml`
- 安装包目录中的 Adapter：`adapters/{adapter-id}/config.toml`
- 插件：`plugins/{plugin-id}/config.toml`，或插件包入口程序集所在目录的 `config.toml`

具体字段由对应组件定义。缺少配置文件时，宿主按配置类型的属性默认值生成文件；Schema 中的 `default_value` 让配置界面在文件尚未生成时也能显示相同的初始值。`ConfigField` 可不填 `Default`：默认值优先级是显式 `Default`、配置类实例化后的属性值、最后按属性 CLR 类型取默认值（布尔 `false`、数值 `0`、字符串空值）。

插件/适配器配置类型上的 `ConfigFieldAttribute` 会在首次生成和 `Config.Save<T>()` 时输出到顶层 snake_case 键上方，例如：

```toml
# 请求超时
# Range: 1..120
timeout_seconds = 15
```

新插件可继承 `PluginBase<TConfig>`，由宿主在 `LoadAsync()` 前加载 `TConfig`。新 Adapter 实现 `IConfigurableAdapter` 和 `IConfigurableComponent<TConfig>`；宿主在调用 `StartAsync()` 前加载配置，并负责文件监听和热更新派发。组件只需在 `OnConfigChangedAsync` 中更新它缓存的运行资源。未迁移的旧组件仍可使用 `IConfigContext.Load<T>()` 和 `Watch<T>()`。

配置字段可通过 `ConfigFieldAttribute` 声明分类和顺序：`Group` 是稳定分类 ID，`GroupLabel` 是界面显示名称，`GroupIcon` 和 `GroupDescription` 提供分类图标与说明；Dashboard Schema 会返回这些分类元数据。Core、Plugin 和 Adapter 共用 TOML 读写器，但由宿主按各自生命周期加载：Core 在启动早期读取，Plugin/Adapter 在组件加载时读取。

依赖字段可以用 `ConfigVisibleWhen` 控制显示，用 `ConfigEnabledWhen` 控制可编辑状态。条件引用同一配置模型中的属性名，比较操作支持 `Equal`、`NotEqual`、`GreaterThan`、`GreaterThanOrEqual`、`LessThan` 和 `LessThanOrEqual`；多个条件按 AND 组合。宿主把它们作为 `schema[].conditions` 返回，例如：

```json
{ "effect": "visible", "field": "protocol", "operator": "eq", "value": "webhook" }
```

`value` 使用字符串形式；数值比较按不变区域格式解析，布尔值写成 `true` / `false`。这样 `WebhookUrl` 可在 `Protocol == "webhook"` 时显示，`ReconnectDelaySeconds` 可在 `ReconnectEnabled == true` 时开放编辑。隐藏字段的值保留在配置中。

宿主自己的 `CoreConfig` 也是显式配置模型，使用 `ConfigModel` / `ConfigField` 描述默认项和 Schema。Core 在服务启动前读取；启动后由 `CoreConfigWatcher` 按宿主运行时规则应用变更。它复用同一 TOML 存储和配置上下文，不包含按插件 ID 分支的默认值或字段校验。

配置 API：`GET/PATCH /api/v1/config` 管理宿主配置。GET 返回 `{ schema, config }`：`config` 使用 CoreConfig 对应的 snake_case 嵌套结构，`schema` 提供可编辑字段、默认值和分类元数据（稳定 ID、名称、图标、说明与顺序）。PATCH 请求使用 `{ "config": { ... } }`，字段键与 Schema 一致；Dashboard 按该 Schema 展示和生成更新内容，不维护另一份宿主配置字段表。由适配器总开关 API 管理的 `protocols` 和由专门路由 API 管理的 `plugin_routes` 不包含在可编辑 Schema 中。

`GET/PATCH /api/v1/plugins/{id}/config` 与 `GET/PATCH /api/v1/adapters/{id}/config` 管理组件配置。PATCH 响应的 `apply_status` 区分已应用、等待组件启动和旧组件仅保存文件的情况。

权限配置不接受裸用户 ID。冒号等保留字符按 URI 组件编码，例如用户 ID 内的 `:` 写成 `%3A`。
相同用户在两个实例共享权限时，分别配置两个条目；不同平台的相同 ID 不再互相授予权限。

开发期间仅支持当前宿主配置格式，不迁移旧字段、不修复旧版重复配置段。`protocols`、`listen_urls`、`public_base_url` 均使用数组，鉴权令牌使用 `api.token`。新生成配置将 `[api]` 放在 `[plugin_routes]` 之前；保存已有配置仍保留用户的段落顺序和注释。
