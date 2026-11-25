### 主要更新

- 宿主、SDK、模板和 Dashboard 同步为 1.0.4。
- SDK ABI 升至 `1.5.0.0`，补齐多个平台都有、此前只能按平台分别调用的操作：`IMessageService` 新增编辑消息 `EditMessageAsync`、批量删除 `DeleteMessagesAsync`、置顶 `PinMessageAsync` / `UnpinMessageAsync` 和输入状态 `SendTypingAsync`，`IChannelService` 新增封禁 `BanMemberAsync` / `UnbanMemberAsync`；`IMessageContext` 提供按 `MessageReference` / `ChannelReference` 路由到来源实例的重载。新增通用事件 `MessageEditedEvent`，字段与 `MessageEvent` 相同，编辑不会再次触发命令。适配器未实现时调用抛出 `NotSupportedException`。
- 扩展 `ShiroBot.Model.Telegram`：会话、成员与权限、管理员权限、话题、邀请链接、机器人命令等实体；投票、位置、地点、联系人、骰子、贴纸、相册、发票等原生内容；内联键盘、自定义键盘、强制回复；inline 模式结果与回调提示。
- 新增可选扩展接口 `ITelegramClientApi`（账号类型、传输方式、文件上限、自身资料）、`ITelegramChatApi`、`ITelegramMessageApi`，以及仅机器人可用的 `ITelegramInlineApi`、`ITelegramPaymentApi`、`ITelegramBotProfileApi` 和只使用 System.Text.Json 类型的 `ITelegramBotApi`（Bot API 原始调用），插件无需引用适配器程序集即可调用任意 Bot API 方法。
- Telegram 与 Discord Model 都支持机器人和用户账号两种适配器：新增仅用户账号的接口 `ITelegramAccountApi`、`IDiscordAccountApi`；Telegram 会话 ID 统一为 Bot API 格式，`TelegramChatIds` 负责与 MTProto peer 互转，原始 JSON 通过 `JsonFormat` 说明结构。
- 新增 Telegram 事件负载和 `TelegramEventKinds`：成员与机器人状态变化、入群申请、inline 查询、投票、付款确认、收货查询和助力；`TelegramMessage` 提供消息原生资料，`TelegramSegmentKinds` 约定位置、投票、付款等入站内容的 `RawSegment`。
- `TelegramUser` 新增 `LanguageCode` 与 `IsPremium`。
- 扩展 `ShiroBot.Model.QQ`：群黑名单、机器人在群内的状态和更丰富的群资料；用户资料新增头像、`UnionId` 与主动消息开关；QQ 官方图片素材接口；两种 QQ 异常共用 `QPlatformException` 基类。仅为 QQPlatform 0.5.0 新增、与现有契约重复的类型改为复用已有契约（用户资料走 `IQSystemApi`，图文卡片使用 SDK 的 `CardSegment`）。
- 扩展 `ShiroBot.Model.Discord`：服务器、频道、角色、成员、权限位、表情、邀请、封禁等实体；Embed、按钮、下拉菜单、文本输入、投票、附件、允许提及等原生消息内容；交互详情、应用命令定义与弹窗。
- 新增可选扩展接口 `IDiscordClientApi`（账号类型、自身资料、在线状态）、`IDiscordGuildApi`、`IDiscordMessageApi`、仅机器人的 `IDiscordInteractionApi`、仅用户账号的 `IDiscordAccountApi`，以及只使用 System.Text.Json 类型的 `IDiscordRawApi`（任意 REST 路由、Gateway 指令和原始 Dispatch）。
- 新增 Discord 事件负载和 `DiscordEventKinds`：服务器、频道与子区、成员、角色、封禁、语音状态、输入、Reaction 清除、投票和邀请；`DiscordMessage` 提供消息原生资料，`DiscordSegmentKinds` 约定 Embed、贴纸、投票和组件的 `RawSegment`。`DiscordUser` 新增 `AvatarUrl`。
- 容器镜像改为自带运行时的单文件主程序（linux-musl self-contained），基础镜像换成只含系统依赖的 `runtime-deps:11.0-alpine`。在 Dashboard 中更新主程序时下载同样的自包含包，新版本即使需要更新的 .NET 运行时也能直接更新，不必等新镜像。
- 插件和适配器模板的发布工作流改用 `actions/setup-dotnet@v6.0.0`：v4 不接受 `latest`，用模板生成的项目在还原前就会失败。已有项目可照此修改 `.github/workflows/release.yml`。
- Avalonia 相关依赖升级至 `12.2.0`。
- 文档新增 Telegram 与 Discord 的 C# 接口参考与类型参考，按通用接口、机器人接口、用户账号接口分组，结构与 QQ Model 相同；由 `scripts/generate_model_reference.py` 从源码生成。

### 修复

- 插件更新后新版本加载失败时自动还原并重新加载旧版本，不再留下无法启动也无法再更新的插件；没有文件清单的安装也会替换 deps.json、原生库和嵌套程序集。
- `OnUnload` 抛出异常的插件立即删除文件，只有卸载超时才推迟到下次启动；被其他已加载插件依赖的插件拒绝删除；更新接口接受加载失败的已安装插件。
- 记录为待删除的插件或适配器不再被列出、按名称或路径解析、启动扫描、文件监听或手动加载重新带回；更新回滚触发的删除事件不再卸载刚还原的插件。
- 删除适配器包或实例不再依赖 `StopAsync` 成功，仍被占用的文件在下次启动时清理，损坏的包也能删除；文件监听只重载 DLL 真正变化的适配器，不再在宿主自身安装、删除后重启全部适配器。
- 组件用反射序列化过自己的类型后，System.Text.Json 的进程级缓存会一直持有其程序集，导致卸载等不到回收；卸载时清理这些缓存。
- 自动更新中请求超时被当作取消，剩余插件、适配器和宿主的更新被静默跳过；现在 GitHub API 请求 30 秒超时并给出可读错误，限流会单独说明。
- API 端点抛出异常时返回 JSON 错误并写入宿主日志，不再只回空的 500；携带 API Key 的调用方能看到具体原因。
- 在 Dashboard 保存插件路由、触发前缀或前缀模式时，含点号或引号的插件 ID 不再被当作配置路径拆开，配置会写到正确的插件下。

### 升级说明

- 容器用户需要拉取一次新镜像（`docker compose -f container/compose.yaml pull && docker compose -f container/compose.yaml up -d`）切换到自包含主程序，之后可以在 Dashboard 中更新。旧镜像在面板里更新时仍下载不带运行时的包。
- SDK ABI 升至 `1.5.0.0`，QQ、Discord、Telegram Model ABI 均升至 `1.1.0.0`；都只新增成员，新增的服务成员带默认实现，按 `1.4.0.0` / `1.0.0.0` 编译的插件和适配器无需重新构建。
- 引用 SDK 1.5 新成员或新增 QQ、Discord、Telegram 类型的组件需要 1.0.4 及以上的宿主；只使用原有成员的组件不受影响。
