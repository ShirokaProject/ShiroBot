# 配置文件

ShiroBot 使用 TOML。默认核心配置文件位于宿主程序旁的 `config.toml`，文件保存后部分设置会自动热更新。

## 完整示例

```toml
# adapters/MyAdapter.dll 或 adapters/MyAdapter/MyAdapter.dll
protocols = ["MyAdapter"]

enable_log = true
disable_console_input = false
github_proxy = ""
host_update_repository = "ShirokaProject/ShiroBot"
avalonia_theme = "Light"

owner_list = [123456789]
admin_list = [987654321]

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
| `protocols` | `[]` | 适配器程序集名称或路径列表；只用一个适配器时也写成单元素数组 |
| `enable_log` | `true` | 是否显示普通日志 |
| `disable_console_input` | `false` | 是否禁用交互式控制台命令 |
| `github_proxy` | 空 | GitHub 下载代理前缀 |
| `host_update_repository` | `ShirokaProject/ShiroBot` | 宿主更新仓库 |
| `avalonia_theme` | `Light` | `Light`、`Dark` 或 `Auto` |
| `owner_list` | `[]` | 所有者账号列表，供插件通过 `Context.IsOwner` / `Context.IsAdmin` 判断 |
| `admin_list` | `[]` | 管理员账号列表，插件可通过 `Context.IsAdmin` 判断 |

`owner_list` 和 `admin_list` 供插件判断权限，不开放宿主控制台命令的私聊入口。禁用控制台输入后，仍可使用受鉴权保护的 Dashboard API 管理宿主。

## 适配器实例清单

开发版本（v0.9.6 之后）在根 config.toml 用 `adapter_instances` 管理已安装适配器的运行实例。每个条目引用同一个已安装包，但有独立的连接配置：

```toml
[[adapter_instances]]
id = "qq-work"
package_id = "qq-official"
name = "工作机器人"
enabled = true

[[adapter_instances]]
id = "qq-home"
package_id = "qq-official"
name = "家庭机器人"
enabled = false
```

实例 ID 唯一；name 可省略，enabled 默认 false。此清单存在时只加载 enabled=true 的条目；未列出的默认实例不会启动，`protocols` 不覆盖这里的停用状态。新实例的连接配置位于 `adapters/.instances/<ID>/config.toml`；ID 等于包 ID 的默认实例继续使用原 DLL 目录配置。手动改清单重启生效，WebUI/CLI 管理会即时写回清单；旧记录在首次启动迁移。

无需 WebUI 的安装、创建、配置和启用命令见[配置与部署](/adapter/deployment#没有-webui-时)。备份时同时保留根 config.toml 与 adapters/.instances/。

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
