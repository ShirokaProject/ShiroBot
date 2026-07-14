# 配置文件

ShiroBot 使用 TOML。默认核心配置文件位于宿主程序旁的 `config.toml`，文件保存后部分设置会自动热更新。

## 完整示例

```toml
# adapters/MyAdapter.dll 或 adapters/MyAdapter/MyAdapter.dll
protocol = "MyAdapter"

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
listen_url = "http://127.0.0.1:7001"
listen_urls = []
public_base_url = ""

[api.auth]
enable = true
key = ""
```

## 基础设置

| 配置项 | 默认值 | 说明 |
| --- | --- | --- |
| `protocol` | 空 | 适配器程序集名称，不含 `.dll` |
| `enable_log` | `true` | 是否显示普通日志 |
| `disable_console_input` | `false` | 是否禁用交互式控制台命令 |
| `github_proxy` | 空 | GitHub 下载代理前缀 |
| `host_update_repository` | `ShirokaProject/ShiroBot` | 宿主更新仓库 |
| `avalonia_theme` | `Light` | `Light`、`Dark` 或 `Auto` |
| `owner_list` | `[]` | 所有者账号列表，拥有宿主管理命令权限 |
| `admin_list` | `[]` | 管理员账号列表，插件可通过 `Context.IsAdmin` 判断 |

::: warning 所有者命令
所有者可以通过好友私聊执行插件加载、卸载和 API 管理命令。请只填写可信账号。
:::

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
listen_url = "http://127.0.0.1:7001"
listen_urls = ["http://127.0.0.1:7001", "http://[::1]:7001"]
public_base_url = "https://bot.example.com"

[api.auth]
enable = true
key = ""
```

- `listen_url` 是单地址兼容配置。
- `listen_urls` 非空时可配置多个监听地址。
- `public_base_url` 是反向代理后的外部地址，会提供给插件的 `Context.WebHost`。
- 开启鉴权且 `key` 为空时，宿主会自动生成随机密钥并写回配置。

不要在公网监听时关闭鉴权。使用 Nginx、Caddy 等反向代理时，应同时配置 TLS 和访问控制。

## 适配器和插件配置

适配器与插件各自拥有独立配置上下文：

- 根目录适配器：`adapters/MyAdapter.toml`
- 目录适配器：`adapters/MyAdapter/config.toml`
- 插件：`plugins/MyPlugin/config.toml` 或插件稳定数据目录下的 `config.toml`

具体字段由对应组件定义。缺少配置文件时，调用 `Config.Load<T>()` 会按配置类型默认值生成文件。

插件/适配器配置类型上的 `ConfigFieldAttribute` 会在首次生成和 `Config.Save<T>()` 时输出到顶层 snake_case 键上方，例如：

```toml
# 请求超时
# Range: 1..120
timeout_seconds = 15
```
