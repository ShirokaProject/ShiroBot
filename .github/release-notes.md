### 破坏性变更

- `IPluginServices` 删除 `RegisterSingleton<T>`、`GetService<T>`、`GetRequiredService<T>`，并移除 `IServiceProvider` 基接口。使用这些 typed 服务的插件需要迁移到 JSON 服务或生成契约并重新构建；这是二进制破坏性变更。
- 插件页面统一使用 `Context.Dashboard.AddPage` / `Ui` 组件树；试用过未发布的 provider 接口或路由标签页方案的插件需适配当前契约。

### 主要更新

- 生命周期作用域：宿主管理插件的后台任务、定时器和资源；支持生命周期感知的适配器在每次启动时获得独立作用域。
- JSON 插件互调与事件总线：服务按名称提供和调用，支持服务族发现与描述；参数和结果复制传递，插件可独立卸载与更新。
- SDK 自带契约生成器：提供方通过 `[PluginContract]` 声明契约，调用方导入提供方 DLL，由 SDK 生成类型与调用代码；`[PluginJson]` 生成 JSON 读写与 Schema，无需运行时共享契约程序集。
- Dashboard 组件页面：表格、图片网格、列表、统计、筛选、分页、标签页、表单及上传，通过宿主 Bearer 鉴权 API 交互；宿主检查声明、条目与表单值，不执行插件 HTML 或脚本。
- 互调清理按运行实例隔离，JSON 字节边界严格校验，避免替换实例的注册被延迟清理误删；修正 codec 注册造成的程序集引用和枚举名称处理。
- Docker 镜像改为与 Release 相同的单文件主程序，支持在 Dashboard 中更新主程序。新版本先下载到数据卷的 `host/` 目录并在当前容器中试运行，能正常启动才会安装并重启；需要新的 .NET 运行时或系统依赖时，提示执行 `docker compose -f container/compose.yaml pull && docker compose -f container/compose.yaml up -d`。镜像版本更新时自动改用镜像中的主程序。
- 新增每日自动更新：插件、适配器和主程序分别开关（默认关闭），执行时间默认 02:00。插件或适配器无法热替换时，默认自动重启宿主完成更新（可关闭）。
- 新增 Owner 通知（默认开启）：私聊推送自动更新结果、失败和重启等重要信息；待确认的更新可直接引用回复「确认」或「取消」，不再需要控制台命令。
- 新增插件优先级：数字越小越先收到事件，默认 `2`，旧插件无需修改。命令匹配或插件调用 `MarkHandled()` 后，数字更大的插件不再收到，可把 AI 聊天等兜底插件设为更大的数字，避免命令同时触发 AI 回复。插件在 `[BotPlugin(Priority = …)]` 中声明，Owner 可在 Dashboard 插件路由或 `plugin_routes.priorities` 中覆盖。
- 控制台命令按对象分组：`plugin list|market|install|load|unload|update|actions|action`、`adapter list|market|install|…`，`update` 只负责主程序；可从市场直接安装插件和适配器，`plugin refresh`、`adapter refresh` 重新拉取市场。控制台、Dashboard 和自动更新共用安装、更新流程。旧命令和 `help` 已移除，输入 `/` 列出全部命令。
- 控制台输入时在输入行上方浮出匹配的命令、参数和说明，覆盖日志且不移动输入行，适配浅色主题；↑↓ 选择，回车补全、再按回车执行，Tab 补全，Esc 收起。支持逐词补全和 `/` 前缀；新增 `dashboard` 命令，在默认浏览器中打开 Dashboard 并自动登录。
- 插件操作可返回 `Details`（可带 `Title`），Dashboard 在屏幕中间弹框、控制台在输入行上方浮出提示显示详细结果；操作出错时两边都显示异常原因，控制台不会因插件异常停止响应。
- Dashboard 的 Owner、Admin 和路由群组可从适配器的好友、群列表中搜索选择；插件配置字段声明 `Type = "users"` / `"groups"` 即可使用。
- 适配器包和实例列表 API 新增 `models`，从 DLL 引用读取 QQ、Discord、Telegram 等所属聊天平台，供 Dashboard 区分聊天平台与 Milky、OneBot 等协议名称。
- Owner、Admin 列表自动忽略空白项、去除 ID 两端空格并合并重复项，避免重复发送 Owner 通知；Dashboard 保存时统一权限条目格式。
- 核心配置和适配器接口返回具体、可读的错误原因；权限条目格式错误时指出出错条目和要求的 `实例ID:用户ID` 格式。
- 插件可通过 `Context.GetHostStatus()` 读取宿主状态：版本、运行方式、系统、架构、.NET 运行时、内存、运行时长、插件数和消息频率，与 Dashboard 概览相同。
- 启动日志和控制台 `api` 命令打印带 `?token=` 的 Dashboard 链接，打开即可直接登录；登录后密钥会从地址栏移除。
- 完整配置页可切换为直接编辑 `config.toml`，包括表单中没有的字段；保存前校验 TOML 格式。
- 插件配置中新增“监听”分区，显示插件注册的命令、前缀、@、@全体成员、回复、自定义条件、通用事件和平台事件。
- 内嵌 Dashboard 更新为 1.0.2。
- 主页的“最近错误”在 1 小时后自动消失，不再一直显示异常状态直到重启。
- 组件加载兼容性改为只按 DLL 实际引用的 SDK / Model ABI 判断；重载时刷新插件和适配器元数据。
- 回复订阅到期后主动释放回调及其捕获的数据；注销所属插件时同样释放，改善文件查询等功能的持续内存占用。
- 限制 Dashboard 慢日志客户端的缓存队列，避免日志持续堆积；直接输出 UTF-8 JSON，减少临时字符串。
- 将 Avalonia 应用、主题和字体初始化延迟至首次渲染，同时保留渲染前的图片解码能力，降低启动内存。
- Avalonia 相关依赖统一升级至 `12.1.4`，ILRepack 升级至 `2.0.48`。
- 模板的 `dev.ps1` 在 macOS 和 Linux 上下载对应平台的主程序。

- 新增插件自定义网页 `AddWebPage`：插件 DLL 内嵌自包含 HTML，Dashboard 用沙箱 iframe 展示，通过已鉴权、限定当前页面的桥接调用插件和读取资源。

### 升级说明

- 当前 SDK ABI 为 `1.4.0.0`，QQ、Discord、Telegram Model ABI 均为 `1.0.0.0`。本次包含 typed 服务删除这一破坏性变更，使用这些接口的组件需要迁移与重新构建；其他组件仍需按实际引用及运行行为验证。
- `ShiroBot.SDK` 与 `ShiroBot.Templates` 包版本同步为 1.0.2。
- Docker 用户需要手动执行一次 `docker compose -f container/compose.yaml pull && docker compose -f container/compose.yaml up -d` 升级到 1.0.2，之后可以在 Dashboard 中更新主程序。
- 容器默认使用 UTC 时区，自动更新时间也按 UTC 计算；需要本地时间时在 `container/compose.yaml` 中设置 `TZ`（文件中有注释示例）。
- 协议端运行在 Docker 宿主机上时，可通过 `SHIROBOT_DATA_DIR` 把宿主目录以相同路径挂载为数据目录，插件交给协议端的文件路径在宿主机上可以直接访问，见安装文档。
- 使用非自包含版时需安装 ASP.NET Core 10 Runtime 10.0.13 或更新补丁。
