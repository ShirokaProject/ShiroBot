# 配置文件

ShiroBot 使用 TOML。默认核心配置文件位于宿主程序旁的 `config.toml`，文件保存后部分设置会自动热更新。

## 完整示例

```toml
enable_log = true
disable_console_input = false
github_proxy = ""
host_update_repository = "ShirokaProject/ShiroBot"
avalonia_theme = "Auto"

owner_list = ["123456789"]
admin_list = ["06E88C1E2090950724B8D9E8A4E097E3"]

[plugin_routes.default]
mode = "blacklist"
groups = []

[plugin_routes.plugins.ExamplePlugin]
mode = "whitelist"
groups = [10001, 10002]

[api]
enable = true
listen_urls = ["http://127.0.0.1:7001"]
public_base_url = ""

[api.auth]
enable = true
key = ""
```

## 基础设置

| 配置项 | 默认值 | 说明 |
| --- | --- | --- |
| `enable_log` | `true` | 是否显示普通日志 |
| `disable_console_input` | `false` | 是否禁用交互式控制台命令 |
| `github_proxy` | 空 | GitHub 下载代理前缀 |
| `host_update_repository` | `ShirokaProject/ShiroBot` | 宿主更新仓库 |
| `avalonia_theme` | `Auto` | `Light`、`Dark` 或 `Auto`（按时间切换，18:00–6:00 为深色） |
| `owner_list` | `[]` | 所有者账号列表，供插件通过 `Context.IsOwner` / `Context.IsAdmin` 判断 |
| `admin_list` | `[]` | 管理员账号列表，插件可通过 `Context.IsAdmin` 判断 |
| `protocols` | `[]` | 仅用于开发：额外加载未安装成适配器包的独立 DLL（名称或路径），见下文 |

账号 ID 按平台原样填写，写成字符串：QQ 号、开放平台 OpenID、Telegram 用户名等都可以，不要求是数字。

`owner_list` 和 `admin_list` 供插件判断权限，不开放宿主控制台命令的私聊入口。禁用控制台输入后，仍可使用受鉴权保护的 Dashboard API 管理宿主。

### 开发时加载独立适配器 DLL

已安装的适配器及其实例在 Dashboard「适配器」页或 [CLI](/adapter/deployment) 管理，是否启动由实例开关和适配器总开关决定，不需要也不应写进 `protocols`；Dashboard 的配置中心不显示此项。

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
public_base_url = "https://bot.example.com"

[api.auth]
enable = true
key = ""
```

- `listen_urls` 是监听地址列表；只监听一个地址时也写成单元素数组，默认为 `["http://127.0.0.1:7001"]`。
- 旧配置中的 `protocol` 和 `api.listen_url` 会在读取时迁移为数组字段。
- `public_base_url` 是反向代理后的外部地址，会提供给插件的 `Context.WebHost`。
- 开启鉴权且 `key` 为空时，宿主会自动生成随机密钥并写回配置。

不要在公网监听时关闭鉴权。使用 Nginx、Caddy 等反向代理时，应同时配置 TLS 和访问控制。

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

配置字段可通过 `ConfigFieldAttribute` 声明分类和顺序：`Group` 是稳定分类 ID，`GroupLabel` 是界面显示名称；Dashboard Schema 会同时返回两者。Core、Plugin 和 Adapter 共用 TOML 读写器，但由宿主按各自生命周期加载：Core 在启动早期读取，Plugin/Adapter 在组件加载时读取。

依赖字段可以用 `ConfigVisibleWhen` 控制显示，用 `ConfigEnabledWhen` 控制可编辑状态。条件引用同一配置模型中的属性名，比较操作支持 `Equal`、`NotEqual`、`GreaterThan`、`GreaterThanOrEqual`、`LessThan` 和 `LessThanOrEqual`；多个条件按 AND 组合。宿主把它们作为 `schema[].conditions` 返回，例如：

```json
{ "effect": "visible", "field": "protocol", "operator": "eq", "value": "webhook" }
```

`value` 使用字符串形式；数值比较按不变区域格式解析，布尔值写成 `true` / `false`。这样 `WebhookUrl` 可在 `Protocol == "webhook"` 时显示，`ReconnectDelaySeconds` 可在 `ReconnectEnabled == true` 时开放编辑。隐藏字段的值保留在配置中。

宿主自己的 `CoreConfig` 也是显式配置模型，使用 `ConfigModel` / `ConfigField` 描述默认项和 Schema。Core 在服务启动前读取；启动后由 `CoreConfigWatcher` 按宿主运行时规则应用变更。它复用同一 TOML 存储和配置上下文，不包含按插件 ID 分支的默认值或字段校验。

配置 API：`GET/PATCH /api/v1/config` 管理宿主配置；`GET/PATCH /api/v1/plugins/{id}/config` 与 `GET/PATCH /api/v1/adapters/{id}/config` 管理组件配置。PATCH 响应的 `apply_status` 区分已应用、等待组件启动和旧组件仅保存文件的情况。
