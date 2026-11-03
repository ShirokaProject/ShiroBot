# 更新日志

## 1.0.2

对比 1.0.1。宿主、`ShiroBot.SDK` / `ShiroBot.Templates` 包和内嵌 Dashboard 均为 1.0.2；SDK ABI 为 `1.4.0.0`，包含下述 typed 服务移除这一破坏性变更，QQ、Discord、Telegram Model ABI 仍为 `1.0.0.0`。

### 破坏性变更

- 移除 `IPluginServices.RegisterSingleton<TService>()`、`GetService<TService>()`、`GetRequiredService<TService>()`，并移除 `IServiceProvider` 基接口；插件互调不再共享运行时服务对象。
- 使用上述 typed 服务的插件需要迁移到按名称调用的 JSON 服务或 SDK 生成的契约，并重新构建。这是二进制破坏性变更，仅升级宿主不能继续使用依赖这些成员的 DLL。
- 插件 Dashboard 使用 `Context.Dashboard.AddPage(...)` 和 `Ui` 组件树注册页面；测试过未发布的 `IPluginDashboardProvider` 或 route-backed 标签页方案的插件需适配当前契约。

### 新功能

#### 插件与适配器生命周期

- `IBotContext.Lifetime` 统一管理后台任务、循环、定时器和可释放资源；插件卸载时由宿主取消并收尾。
- 实现 `ILifetimeAwareAdapter` 的适配器在每次启动时获得新的作用域，实例停止或配置重启时清理该次启动登记的资源。

#### 插件互调与事件总线

- `Context.Services.Provide` / `CallAsync` 按服务名交换 JSON；`Find` 可按前缀发现带描述的服务，`IsAvailable` 检查可用性。
- `Context.Bus` 发布与订阅插件事件；服务和订阅随提供实例的生命周期注销。提供方与调用方可独立卸载、更新和重新加载。
- 参数、返回值、描述和事件负载经过 JSON 字节边界，不在插件间共享服务对象或程序集。

#### SDK 契约与 JSON 源生成器

- `[PluginContract]` 声明服务与事件契约；调用方将提供方 DLL 放入 `contracts/`，SDK 自动读取并生成本地契约类型及调用代码，DLL 仅作构建输入。
- `[PluginJson]` 生成消息类型的 JSON 读写与 Schema；生成器随 `ShiroBot.SDK` NuGet 包分发，无需单独安装契约程序集。
- 提供单服务、服务族、强类型事件和描述读取封装；生成的 JSON 读写保留字段默认值并处理枚举名称。

#### 插件 Dashboard 组件页面

- 插件通过 `Context.Dashboard.AddPage(...)` 返回 `Ui` 组件树，支持表格、图片网格、列表、统计、筛选、分页、标签页、按钮和表单。
- 表单可逐字段声明或按 record 生成；宿主在执行操作前检查组件、条目、字段类型和约束。页面随插件卸载注销。
- 上传和图片读取统一通过宿主 Bearer 鉴权 API，按声明检查 MIME 与文件大小，单文件上限 20 MB。插件无需公开网页路由，Dashboard 不执行插件的 HTML 或脚本。
- 页面位于配置窗口独立的“插件页面”区块，配置、插件页面和路由/监听分区显示，页面操作不依赖配置保存按钮。

#### 插件 Dashboard 自定义网页

- `DashboardWebPage.RequestAsync` 提供页面内通用 HTTP 入口，支持 JSON、文本、multipart 和二进制请求，返回业务状态码、响应头、文件流及 Range 响应。网页使用 `request(path, init)`，响应按需分块传输，支持取消和卸载清理。

- 新增 `AddWebPage` 与 `DashboardWebPage`，从插件 DLL 的嵌入资源加载自包含 HTML。
- Dashboard 使用独立来源的沙箱 iframe，通过页面专用桥接调用处理函数和读取资源；所有请求经宿主 Bearer 鉴权，API 密钥保留在父页面。
- 资源路径、请求与响应大小受限；页面刷新和插件卸载会取消请求。提供可运行示例及集成测试。

#### Docker 中直接更新主程序

- 镜像改为使用与 Release 相同的 `linux-musl` 不自带运行时单文件主程序，Dashboard「关于」页的主程序更新在 Docker 中也可使用。
- 新版本先下载到数据目录的 `host/`（默认 `/data/host`），在当前容器中执行 `--self-check` 试运行，能正常启动才会安装并重启。需要新的 .NET 运行时或系统依赖时不会替换，并提示执行 `docker compose -f container/compose.yaml pull && docker compose -f container/compose.yaml up -d`。
- 容器启动时比较镜像与 `host/` 中的版本：面板更新的版本更新且能运行时使用它，否则删除 `host/` 并使用镜像版本，因此拉取更新的镜像总是生效。
- 非 Docker 环境的主程序更新同样先试运行再替换，新版本无法在当前运行时启动时不会替换。

#### 每日自动更新

- 插件、适配器和主程序分别开关（`auto_update_plugins`、`auto_update_adapters`、`auto_update_host`），默认关闭；执行时间 `auto_update_time` 默认 `02:00`，使用宿主本地时区。
- 插件按其 GitHub 仓库更新；适配器按适配器市场更新，跳过预发布版本并校验 SHA-256；主程序更新流程与 Dashboard 相同。
- 插件或适配器无法热替换时新版本会暂存，`auto_update_restart_on_pending`（默认开启）会在更新结束后自动重启宿主完成替换。
- 每天只执行一次，执行日期记录在 `cache/auto-update-last-run`，更新导致的重启不会当天再次执行；到点后一小时内宿主未运行时当天跳过。单个项目失败不影响其他项目。

#### Owner 通知

- 新增 `owner_notifications`（默认开启），私聊 `owner_list` 中已加载实例的 Owner：自动更新的结果、失败、无法热替换、自动重启，以及主程序需要手动更新镜像等情况。没有更新时不发送；主程序更新前先发送通知再重启。
- 待确认的更新（控制台 `update`、`plugin update` 或插件通过 `IUpdater` 请求）可直接引用回复「确认」或「取消」，24 小时内有效，不再需要输入控制台命令。只接受 Owner 的回复，任意一位 Owner 处理后其他 Owner 的同一条通知随之失效，同一个更新不会执行两次。控制台的 `update confirm|cancel`（主程序）和 `plugin update confirm|cancel`（插件）仍可使用。

#### 插件优先级

- 插件按优先级接收事件：数字越小越先收到，相同数字并发执行。插件在 `[BotPlugin(Priority = …)]` 中声明，未声明时为 `2`，旧插件无需重新编译，行为与之前相同。
- 事件被处理后，数字更大的插件不再收到：命令（`MapExact`、`MapPrefix`、带条件的 `MapWhen`、@、回复）匹配时自动标记，插件也可调用 `MarkHandled()`；无条件的 `MapAll` 不标记。把 AI 聊天等兜底插件设为更大的数字，私聊命令就不会同时触发 AI 回复。
- 被回复订阅接收的引用回复（如 Owner 确认更新）按优先级 2 视为已处理，兜底插件不再收到。
- 一个优先级超过 5 秒未处理完时记录警告，并继续分发给后面的优先级。
- Owner 可在 Dashboard 插件配置的「路由」页或 `plugin_routes.priorities` 中按插件覆盖优先级，可恢复插件默认；插件详情显示已加载插件的优先级。

#### 控制台命令按对象分组

- 命令改为 `plugin list|market|install|load|unload|update|actions|action` 和 `adapter list|market|install|create|start|stop|reload|config|remove`；`update` 只负责主程序，插件更新改为 `plugin update`。
- `plugin install <市场 ID>`、`adapter install <市场 ID>` 从市场安装，`plugin market`、`adapter market` 浏览市场，`plugin refresh`、`adapter refresh` 重新拉取；补全会列出市场中未安装或可更新的条目。
- 旧命令 `plugins`、`load`、`unload`、`actions`、`action`、`adapters` 和 `help` 已移除，输入时提示新写法；输入 `/` 列出全部命令。
- 控制台、Dashboard 和自动更新共用同一套插件与适配器安装、更新流程。

#### 控制台命令补全菜单

- 输入时浮出匹配的命令及参数（插件、插件操作、适配器实例、待确认更新等）和说明，盖在日志上方而不移动输入行，关闭后恢复被盖住的日志；↑↓ 选择，回车补全为选中项、再按回车执行，Tab 补全并在后面还有参数时加空格，Esc 收起。命令前可以加 `/`，只输入 `/` 列出全部命令。
- 新增控制台命令 `dashboard`：在默认浏览器中打开 Dashboard 并自动登录。

#### 插件操作详细结果

- `PluginActionResult` 新增可选的 `Details` 与 `Title`。设置后，Dashboard 在屏幕中间弹出对话框显示详细结果，控制台在输入行上方浮出提示（Esc 关闭）；没有 `Details` 的操作仍使用简短提示。
- Dashboard 和控制台共用同一套插件操作查找与执行逻辑。操作抛出异常时两边都显示异常原因，不再只提示“插件操作执行失败”；控制台中出错的命令不会再让控制台停止响应。

#### 在 Dashboard 中选择好友和群

- Owner、Admin 列表和插件路由群组改为选择器：列出运行中适配器实例的好友或群，按名称搜索，保存的仍是 `实例ID:用户ID` 或群号；不支持读取列表的适配器会标出，仍可手动输入。
- 插件配置的 `List<string>` 字段可声明 `ConfigField(Type = "users")` 或 `Type = "groups"` 使用同一个选择器。
- 新增 `GET /api/v1/directory/users|groups`，按实例返回好友或群，缓存 1 分钟，`?refresh=1` 重新读取。

#### 插件读取宿主状态

- `IBotContext.GetHostStatus()` 返回与 Dashboard 概览相同的信息：宿主版本与 SDK ABI、启动时间与运行时长、运行方式、操作系统、架构、.NET 运行时、镜像标签、构建时间、工作集与托管堆内存、处理器数、插件与 Model 数量、消息总数、每 2 小时的消息频率以及健康状态。

#### 插件监听展示

- 插件配置新增「监听」分区，按群聊和私聊、群聊、私聊、事件分组显示插件注册的命令、前缀、@、@全体成员、回复、自定义条件、通用事件和平台事件。
- SDK 的路由记录监听条件：`MapMention`（含指定用户）、`MapMentionAll`、`MapReply`、`MapAll` 与 `MapWhen` 可以区分，`Events.MapPlatform` 记录 Kind。`MapWhen` 新增带说明文字的重载，用于在 Dashboard 中描述自定义条件。
- 旧 SDK 编译的插件无需重新编译即可显示具体的监听类型；只有未提供说明的 `MapWhen` 显示为“自定义条件”。

#### 直接编辑 config.toml

- 完整配置页右上角可切换为「编辑 config.toml」，直接编辑插件或适配器配置文件的全部内容，包括表单中没有的字段。
- 保存前校验 TOML 格式，格式错误不会写入并显示出错位置；保存后立即应用。打开编辑器后文件被其他地方修改时拒绝保存，避免覆盖。
- 插件编辑整个文件并保留注释；多实例适配器只编辑当前实例的配置表，保存后不保留注释。
- 只有声明了配置模型或已有 config.toml 的插件和适配器才提供该模式，没有配置的组件不会被写入新的配置文件。

#### 一键登录 Dashboard

- 启动日志和控制台 `api` 命令打印带 `?token=` 的 Dashboard 链接，打开即可直接登录；登录后 Dashboard 从地址栏和浏览历史中移除密钥。
- 监听地址为 `0.0.0.0` 等通配地址时，链接改用 `127.0.0.1`，以便浏览器直接打开。

#### Docker 宿主机上的协议端

- 设置 `SHIROBOT_DATA_DIR` 并以相同路径挂载宿主目录后，配置、适配器、插件、缓存和插件临时文件都放在该目录，插件交给协议端的文件路径在宿主机上可以直接访问。`container/compose.yaml` 中有注释示例。

### 改进

- 适配器包和实例列表新增 `models`，从适配器 DLL 实际引用的 `ShiroBot.Model.*` 读取所属聊天平台（如 `QQ`、`Discord`、`Telegram`），供 Dashboard 区分聊天平台与协议名称（如 Milky、OneBot）；无法读取时返回空列表。
- Owner、Admin 列表忽略空白项，去除实例 ID 和用户 ID 两端的空格并合并重复项；实例 ID 按不区分大小写去重，避免重复发送 Owner 通知。通过 Dashboard 保存时统一为 `实例ID:用户ID` 格式。
- Avalonia 相关依赖统一升级至 `12.1.4`，ILRepack 升级至 `2.0.48`。
- 组件加载兼容性改为只按 DLL 实际引用的 SDK / Model ABI 判断，不再检查组件声明的 API 版本范围；重载时刷新插件和适配器元数据。
- 主页的“最近错误”在 1 小时后自动消失，不再一直显示异常状态直到重启。
- 回复订阅到期后主动释放回调及其捕获的数据，注销所属插件时同样释放，改善文件查询等功能的持续内存占用。
- 限制 Dashboard 慢日志客户端的缓存队列，避免日志持续堆积；直接输出 UTF-8 JSON，减少临时字符串。
- Avalonia 应用、主题和字体初始化延迟至首次渲染，同时保留渲染前的图片解码能力，降低启动内存。
- Dashboard 插件市场按发布 DLL 的 ABI 显示兼容要求，不再显示旧的 API 版本声明。
- 宿主目录可通过 `SHIROBOT_HOME` 指定，与主程序文件所在位置无关。

### 修复

- 插件互调的服务和事件订阅按运行实例清理，超时卸载实例的延迟清理不会注销同 ID 替换实例的注册。
- 对服务描述、参数、事件和返回值检查 UTF-8 JSON；提供方返回无效数据时报告 `PluginServiceException`，正在卸载的提供方不再报告为可用。
- 插件生成的 JSON codec 不为不可回收的宿主或 SDK 类型留下静态注册，避免阻止插件卸载；枚举读写正确处理含下划线的名称。
- 核心配置和适配器接口的错误响应补充可读的 `message`，Dashboard 可显示具体失败原因；权限条目格式错误时指出出错条目和要求的 `实例ID:用户ID` 格式，核心配置错误保留 `msg` 兼容旧版 Dashboard。
- 模板的 `dev.ps1` 在 macOS 和 Linux 上下载对应平台的主程序，并恢复可执行权限。

### 新增配置

| 配置项 | 默认值 | 说明 |
| --- | --- | --- |
| `owner_notifications` | `true` | 私聊推送重要信息给 Owner |
| `auto_update_plugins` | `false` | 每天自动更新插件 |
| `auto_update_adapters` | `false` | 每天自动更新适配器包 |
| `auto_update_host` | `false` | 每天自动更新主程序 |
| `auto_update_time` | `"02:00"` | 自动更新时间，`HH:mm`，宿主本地时区 |
| `auto_update_restart_on_pending` | `true` | 有插件或适配器无法热替换时自动重启宿主 |

以上设置均可在 Dashboard 配置中心修改，保存后立即生效。另新增 `[plugin_routes.priorities]`，按插件 ID 覆盖插件优先级，在 Dashboard 插件路由中修改。

### SDK 接口（ABI 1.4.0.0）

- `IBotContext.Lifetime`、`IPluginLifetime`、`ILifetimeAwareAdapter`，以及插件事件总线 `IBotContext.Bus`。
- 按名称调用的 `IPluginServices`、UTF-8 JSON 服务与事件边界、服务发现及描述。
- `[PluginContract]`、`[PluginJson]`、生成的服务/事件调用代码和 JSON Schema。
- `IBotContext.Dashboard`、`IPluginDashboard`、`DashboardContext`、`DashboardResult`、`Ui` 组件和表单。
- `IBotContext.GetHostStatus()`，以及 `HostStatus`、`HostRuntimeInfo`、`MessageFrequencyBucket`。
- `MessageRouteDescriptor.Condition` / `ConditionArgument` / `Description`，`MessageRouteCondition` 枚举。
- `EventRouteDescriptor`、`EventRouter.Routes`、`PluginBase.GetEventRoutes()`。
- `CommandRouter.MapWhen`、`AllMapCommands.MapWhen`、`EventRouter.MapWhen` 的带说明文字重载。
- `PluginActionResult.Details` / `Title`。
- `BotPluginAttribute.Priority` 与 `BotPluginAttribute.DefaultPriority`，`BotEventHandling.MarkHandled()` / `IsHandled()` 扩展方法。

### HTTP API

- 插件页面统一使用 `GET /api/v1/plugins/{id}/pages`、`POST /api/v1/plugins/{id}/pages/{page}/render`、`POST .../events`、`POST .../upload` 和 `GET .../files`；全部要求 Bearer 鉴权。
- `GET /api/v1/adapter-packages` 和 `GET /api/v1/adapters` 的列表项新增 `models` 字符串数组，表示适配器引用的聊天平台 Model。
- 新增 `GET /api/v1/directory/users` 与 `GET /api/v1/directory/groups`，返回各运行中实例的好友或群列表（`entries`）以及无法读取的实例和原因（`unavailable`）；缓存 1 分钟，`?refresh=1` 强制重新读取。
- `GET /api/v1/plugins/{id}` 新增 `listeners` 字段，插件未加载时为 `null`。
- 插件配置响应的 `routes` 新增 `priority`、`priority_configured`、`declared_priority`；`PATCH` 的 `routes.priority` 设置覆盖值，`null` 恢复插件默认，只含 `priority` 时不改动群路由。`GET /api/v1/plugins/list` 中已加载插件新增 `priority`。
- 新增 `GET` / `PUT /api/v1/plugins/{id}/config/raw` 与 `/api/v1/adapters/{id}/config/raw`，读写完整 `config.toml`；`PUT` 携带读取时的 `revision`，文件已变化时返回 `409 config_changed`，格式错误返回 `400 invalid_toml`，组件没有配置时返回 `400 no_config`。插件和适配器配置的 `GET` 响应新增 `raw_config`，表示是否可以编辑 config.toml。
- 核心配置 `GET` / `PATCH /api/v1/config` 支持上述新增配置项。

### 升级说明

- 使用已移除 typed 服务 API 的插件必须迁移并重新构建；使用生命周期、JSON 互调、生成契约或 Dashboard 组件页面的组件需要本次宿主与 SDK。其他组件仍按其实际 SDK / Model 引用检查，并应验证运行行为。
- Docker 用户需要手动执行一次 `docker compose -f container/compose.yaml pull && docker compose -f container/compose.yaml up -d` 升级到 1.0.2，之后可以在 Dashboard 中更新主程序。
- 容器默认使用 UTC 时区，自动更新时间也按 UTC 计算；需要本地时间时在 `container/compose.yaml` 中设置 `TZ`，例如 `TZ: Asia/Shanghai`。
- `rebuild-container` 工作流只能重建 1.0.2 及之后的版本：更早版本的构建产物仍是多文件结构，与新的镜像构建方式不兼容。
- 使用非自包含版时需安装 ASP.NET Core 10 Runtime 10.0.13 或更新补丁。
