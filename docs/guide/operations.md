# 运行与维护

## 控制台命令

交互模式下输入 `help` 可以查看命令。

| 命令 | 说明 |
| --- | --- |
| `plugins` | 显示已加载插件、程序集文件大小和宿主进程工作集 |
| `adapters` | 显示已安装适配器 |
| `adapter start\|stop\|reload <ID>` | 启动、停止或重载指定适配器实例，ID 见 Dashboard |
| `actions` | 列出已加载插件注册的操作 |
| `action <插件 ID> <操作 ID>` | 执行插件操作 |
| `load <插件名或路径>` | 热加载插件 |
| `unload <插件 ID>` | 热卸载插件 |
| `restart` | 启动替代进程并退出当前进程 |
| `api` | 显示或设置 API 鉴权信息 |
| `update` | 查看或处理待确认更新 |
| `path` | 打开程序目录 |
| `log` | 开关普通日志输出 |
| `clear` | 清空控制台 |
| `exit` / `quit` | 正常退出 |

进程退出会停止宿主 HTTP 服务、适配器和 Avalonia dispatcher，但不会逐个热卸载插件。
插件程序集和剩余资源由操作系统随进程直接回收。

宿主命令只能从交互式控制台输入；好友私聊消息会交给插件的消息路由，不会被解释为宿主命令。`disable_console_input = true` 或 `--no-console` 会关闭交互式控制台输入。

插件可实现 `IPluginActionProvider`，把操作同时提供给控制台与已鉴权的 Dashboard。`help` 会列出插件操作，`actions` 只列出插件操作；输入 `action <插件 ID> <操作 ID>` 执行。带 `RequiresConfirmation` 的操作会在控制台提示确认。写法见[插件操作与控制台](/plugin/actions)。

## 热加载与热卸载

把插件 DLL 放入插件目录后，可以执行：

```text
load HelloPlugin
```

卸载时使用插件 ID，而不是显示名称：

```text
unload HelloPlugin
```

宿主会先停止为插件路由新事件，等待正在执行的事件结束，再调用插件清理逻辑并尝试回收程序集加载上下文。

如果日志提示程序集未完全卸载，通常表示插件仍有以下引用：

- 未停止的后台任务或计时器。
- 未释放的配置监听、事件订阅或回调。
- 静态字段持有插件对象或插件类型。
- native 库或第三方框架保存了托管回调。

插件应在 `OnUnloadAsync()` 中释放热卸载或更新时需要立即释放的资源。进程退出不会调用每个插件的
`OnUnloadAsync()`；需要持久化的数据应在运行过程中及时写入，不能依赖退出回调。

Dashboard 的插件 actions 也使用同一套 active-dispatch 计数。执行中的 action 不会与热卸载并发销毁插件对象，插件抛出的异常会被限制在对应 HTTP 请求中。

## 插件市场

`GET /api/v1/plugin-market/plugins` 从 awesome-shirobot 的 `marketplace.v1.json` 获取列表，并为每项增加 `installed`。缓存同时保存在内存和宿主 `cache/plugin-marketplace.v1.json`，有效期约 24 小时；远端失败时使用 last-known-good，不会影响宿主启动。

`POST /api/v1/plugins/install/github` 接受 `repository`，也接受市场提供的 `assetUrl` / `assetName` / `assetSha256`。市场安装会校验 URL 确实属于对应仓库的 GitHub Release、限制下载为 100 MiB、校验 SHA-256，并限制 ZIP 条目数和解压后总大小。两种方式都会先生成 upload preview，再调用原有 confirm 接口安装；`includePrerelease=true` 会从 GitHub releases 列表中包含预发布版本。

通过 Dashboard 上传或市场安装时，插件统一放在 `plugins/<插件 ID>/`，入口 DLL 可与插件 ID 不同名。替换已有插件会保留其 `config.toml`；选择暂不启用时，安装后的入口 DLL 会被禁用，之后可在 Dashboard 中启用。ZIP 包只接受根目录中的入口 DLL，或唯一顶层文件夹中的入口 DLL。手动放入插件目录的规则见[安装插件](/guide/installation#安装插件)。

插件更新包中的 DLL 优先按已安装入口文件名匹配；若文件名不同，ZIP 中只有一个 DLL 时仍可用于更新。ZIP 中有多个 DLL 且找不到原入口文件名时，更新会拒绝选择，以免替换错误的程序集。

## 适配器实例管理

开发版本支持一个适配器包创建多个配置实例。在 Dashboard 适配器详情点击「添加实例」，填写唯一 ID，配置后启动。根 config.toml 的 adapter_instances 是统一实例清单，WebUI/CLI 管理都会写回；手动编辑后重启生效。每个实例独立启停、重载和删除；重新上传同一包用于更新 DLL，增加账号请使用「添加实例」。更新会覆盖该包下所有实例使用的版本。

HTTP API 均需要宿主鉴权：

| 接口 | 用途 |
| --- | --- |
| `GET /api/v1/adapters` | 实例列表：id 为实例 ID，package_id 为共享包 ID，config_path 为独立配置路径 |
| `POST /api/v1/adapters/<包 ID>/instances` | 创建停用实例，JSON 为 `{"id":"qq-work","name":"工作机器人"}`，name 可省略 |
| `GET/PATCH /api/v1/adapters/<实例 ID>/config` | 读/写实例配置，PATCH 使用 `{"config":{...}}` |
| `POST /api/v1/adapters/<实例 ID>/start` | 启动并保存启用状态 |
| `POST /api/v1/adapters/<实例 ID>/stop` | 停止并保存停用状态 |
| `POST /api/v1/adapters/<实例 ID>/reload` | 重载指定运行实例 |
| `DELETE /api/v1/adapters/<实例 ID>` | 删除实例；最后一个实例删除时同时卸载包 |

默认实例 ID 仍为包 ID，已有单实例 API 调用无需修改。实例 ID 不能重复或包含路径字符，新增配置不复制凭据。目录、备份和生命周期见[配置与部署](/adapter/deployment#同一个-dll-配置多个实例)，自动回复、后台发送和兼容性见[调用 API](/plugin/apis#适配器实例与后台发送)。这些功能尚未包含在已发布的 v0.9.6 中。

离线管理支持 `adapter create <包 ID> <实例 ID>`、`adapter config <实例 ID>`、`adapter enable|disable <实例 ID>` 和 `adapter remove <实例 ID>`。离线命令在宿主停止时使用，运行中的控制台用 start/stop 即时启停。完整示例见[无 WebUI 管理](/adapter/deployment#没有-webui-时)。

## native 依赖缓存

自动下载的 native 文件位于：

```text
plugins/<插件数据目录>/.shirobot/native/<包名>/<版本>/<RID>/
```

缓存包含 `.complete` 校验标记。删除对应版本目录后，下次加载会重新下载和校验。下载过程中的临时 `.nupkg` 会在成功或失败后删除。

## 内存指标

`plugins` 命令中的“程序集文件大小”是磁盘 DLL 大小，不是运行时内存。进程工作集包含：

- ShiroBot 宿主与 .NET 运行时。
- 适配器和所有插件。
- GC 堆、JIT 代码和线程栈。
- Avalonia、Skia、HarfBuzz 等 native 内存。

插件使用独立 ALC，但不使用独立 GC 堆或独立进程，因此宿主无法通过普通运行时 API 精确拆分每个插件的完整工作集。

## 更新前备份

升级宿主、插件或适配器前，建议备份：

```text
config.toml
adapters/**/config.toml
adapters/*.toml
plugins/**/config.toml
plugins/**/其他业务数据
```

`.shirobot/native` 是可重新生成的缓存，通常不需要备份。
