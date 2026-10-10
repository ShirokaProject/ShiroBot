# 上下文、配置与日志

宿主在加载插件时注入 `IBotContext`，继承 `PluginBase` 后可以通过 `Context` 使用。

## 上下文能力

| 属性 | 用途 |
| --- | --- |
| `Context.Platform` | 当前实例的平台类型，例如 qq 或 qq-official |
| `Context.InstanceId` | 当前适配器运行实例 ID（v0.9.7 起） |
| `Context.UseInstance(id)` | 临时选择指定实例，作用域结束后恢复（v0.9.7 起） |
| `Context.AdapterInstance` / `Context.GetAdapterInstances()` | 当前实例与全部运行实例的信息（v0.9.7 起） |
| `Context.Message` | 发送、回复、撤回、查询消息 |
| `Context.Channel` | 群或频道信息与成员管理 |
| `Context.User` | 机器人、用户和好友信息与请求 |
| `Context.GetAdapterExtension<T>()` | 探测平台特有扩展 API |
| `Context.Config` | 插件独立 TOML 配置 |
| `Context.WebHost` | 宿主 HTTP 服务与公开地址 |
| `Context.Updater` | 插件更新能力 |
| `Context.Render` | 可选图片渲染服务 |
| `Context.PluginDirectory` | 插件稳定数据目录 |
| `Context.OwnerList` | 宿主所有者身份列表（`UserReference`） |
| `Context.AdminList` | 宿主管理员身份列表（`UserReference`） |

权限判断由 SDK 的 `Context.IsOwner` / `Context.IsAdmin` 接口完成。宿主中 owner 自动拥有 admin 权限：`IsAdmin(id)` 对 `owner_list` 或 `admin_list` 中的账号均返回 `true`，无需将 owner 重复加入 `admin_list`。两份列表热重载后，后续检查使用新配置。`Context.AdminList` 返回显式配置的管理员列表，不包含自动授予权限的 owner；插件应调用 `IsAdmin`，不要直接用 `AdminList.Contains` 判断权限，也不需要自行维护管理员名单。

权限判断：

```csharp
if (!Context.IsAdmin(message.Sender.Id))
{
    await Context.Message.ReplyAsync(message, "权限不足");
    return;
}
```

适配器可以只实现自己支持的服务方法。调用不支持的方法时会抛出 `NotSupportedException`，插件应按需要捕获并提供友好提示。

事件处理期间这些服务绑定来源实例，跨 await 保留。后台发送、自动回复、回复订阅和旧 DLL 兼容性见[调用 API](/plugin/apis#适配器实例与后台发送)。平台、账号和实例的区别见[通用 Model](/plugin/models#事件)。

## 插件配置

定义配置类型：

```csharp
using ShiroBot.SDK.Config;

public sealed class HelloConfig
{
    [ConfigField("回复文本", Label = "Ping 回复", Placeholder = "pong")]
    public string ReplyText { get; set; } = "pong";

    [ConfigField("请求超时秒数", Min = 1, Max = 120)]
    public int TimeoutSeconds { get; set; } = 15;

    [ConfigField("是否启用", Type = "boolean")]
    public bool Enabled { get; set; } = true;
}
```

加载并监听配置：

```csharp
private HelloConfig _config = new();
private IDisposable? _configWatcher;

protected override Task LoadAsync()
{
    _config = Context.Config.Load<HelloConfig>();
    Context.Config.Save(_config);

    _configWatcher = Context.Config.Watch<HelloConfig>(updated =>
    {
        _config = updated;
        BotLog.Info("配置已热更新");
    });

    return Task.CompletedTask;
}

protected override Task OnUnloadAsync()
{
    _configWatcher?.Dispose();
    return Task.CompletedTask;
}
```

`Watch<T>()` 返回的订阅必须释放，否则文件监听器会阻止插件完整卸载。

`Config.Save<T>()` 和首次 `Config.Load<T>()` 生成文件时，会把顶层属性的 `ConfigFieldAttribute` 写到对应 snake_case 键上方。`Label`、`Description`、`Options`、`Min`/`Max` 和 `Placeholder` 会转换为合法 `#` 注释；重复保存不会重复堆叠注释。

只更新一个字段并尽量保留 TOML 注释和格式：

```csharp
Context.Config.SetValue("timeout_seconds", 30);
```

## 数据文件

不要把运行时数据写到当前工作目录。使用插件数据目录：

```csharp
var databasePath = Path.Combine(Context.PluginDirectory, "data.db");
var cacheDirectory = Path.Combine(Context.PluginDirectory, "cache");
Directory.CreateDirectory(cacheDirectory);
```

即使单 DLL 位于 `plugins` 根目录，宿主也会为它提供稳定的 `plugins/<插件 ID>` 数据目录。

当前 SDK 没有单独的 `CacheDirectory` API，插件使用上述 `Context.PluginDirectory` 下的 `cache` 子目录。该目录由插件创建和清理，只存放可重建数据；数据库、订阅和用户配置等持久数据不要放入缓存。宿主自身的 `cache/adapters` 用于适配器包元数据，不供插件存储数据。

## 日志

```csharp
BotLog.Info("普通信息");
BotLog.Success("操作成功");
BotLog.Warning("可恢复问题");
BotLog.Error("操作失败");
```

宿主会为插件设置日志作用域，日志中心可以按插件 ID 区分来源。避免记录访问令牌、API 密钥和用户隐私数据。

## 回复订阅

发送消息后可以等待用户回复：

```csharp
var sent = await Context.Message.SendGroupMessageAsync(groupId, "请回复 yes");

var subscription = Context.Message.SubscribeReply(
    sent.Reference ?? throw new InvalidOperationException("发送失败，不能订阅回复"),
    "yes",
    TimeSpan.FromMinutes(1),
    async reply =>
    {
        await Context.Message.ReplyAsync(reply, "已确认");
    });
```

默认在匹配一次后自动释放。长期订阅应保存返回值，并在插件卸载时主动 `Dispose()`。

## 身份与消息作用域（SDK ABI 1.0）

`UserReference(InstanceId, UserId)` 用于保存身份。`IsOwner(userId)` / `IsAdmin(userId)` 自动使用当前实例；
后台权限检查可以传 `UserReference`。`OwnerList` / `AdminList` 是带实例的身份列表，Owner 自动继承 Admin。

`ChannelReference(InstanceId, Channel)` 与 `MessageReference(InstanceId, Channel, MessageId)` 可用于后台路由。
成功发送后 `SentMessage.Reference` 携带来源；失败时为 null。入站消息的 `Reference` 同样保留来源。
回复订阅匹配实例、会话类型、会话 ID、所属 GuildId 和消息 ID，不比较显示名称。

```csharp
var sent = await Context.Message.SendMessageAsync(
    new ChannelReference("qq-work", Channel.Group(groupId)),
    [new TextSegment("请回复确认")], cancellationToken);
if (sent.Reference is { } reference)
    Context.Message.SubscribeReply(reference, TimeSpan.FromMinutes(1), HandleReplyAsync);
```

取消继续抛 `OperationCanceledException`。通用发送的适配器失败返回 `IsSuccess = false` 并由宿主记录；
其他扩展操作正常抛异常，批量操作返回逐项结果。文件或 Base64 全量内容不得写入错误日志。

SDK 的 `IMessageService`、`IChannelService`、`IUserService` 异步操作均支持 `CancellationToken`。

宿主的单条消息及历史消息查询会为返回结果补上查询时捕获的实例和平台来源，
因此查询结果也可直接使用 `Reference`、`DeleteMessageAsync(message)` 和 `ReplyAsync(message, ...)`。


## 更新时保护用户文件

宿主更新插件包时，已有 `config.toml` 和插件根目录 `cookies/`、`data/`、`cache/`、`tmp/`、`logs/` 内的文件不会被新包覆盖或按旧安装清单删除。即使早期发布包将 Cookie 文件记录为程序资源，更新后也会保留，并从程序文件清单中移除。立即更新与重启后应用的暂存更新遵循同一规则。

插件发布包应仅包含程序和必要的静态资源，不要打包空 Cookie 文件。新插件应把可编辑的 Cookie 放在配置中（字段使用 `ConfigField(Type = "password")`），并明确配置文件中的值仍为明文。已有非空 Cookie 文件应进行一次性、不删除原文件的迁移；主动清空配置后不能再次自动导入旧凭据。迁移完成标记属于持久数据，不应放进可清理的缓存目录。

未被旧安装清单登记的同名用户文件也不会被新包覆盖或认领；旧版无清单安装的 DLL 仍可更新。程序静态资源请放在独立的资源目录，避免使用上述运行数据目录。


## 宿主管理的临时目录（SDK 1.0.0）

```csharp
var temp = Context.CreateTempDirectory(TimeSpan.FromMinutes(10));
var path = Path.Combine(temp.Path, "video.mp4");
// 下载、渲染或写入到 path，并在 temp.ExpiresAt 前发送。
// 无需插件删除目录或 Dispose；到期由宿主统一清理。
```

每次调用生成独立目录，位于宿主 `cache/plugin-temp`，插件不能通过参数指定其他插件的目录。
保留时长必须大于零，从创建时开始计算；宿主每 30 秒扫描到期目录，文件被占用时后续重试。
Ctrl+C/正常退出时尝试清理；强制终止时遗留的文件，下次启动会在加载插件之前全部清理，未到期也不续存。
清理只针对专用临时区，不影响 `cache/adapters`、Cookie 或插件持久数据。订阅记录、登录凭据等不要保存在临时目录。
新接口要求宿主 1.0.0；不需要升级适配器才能使用宿主提供的此服务。
