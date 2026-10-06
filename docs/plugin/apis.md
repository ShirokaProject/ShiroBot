# 调用 SDK API

插件继承 `PluginBase` 后，通过 `Context` 调用当前适配器的服务。通用接口位于 `ShiroBot.SDK`；平台专有接口通过 `GetAdapterExtension<T>()` 按需取得。

## 发消息与回复

```csharp
using ShiroBot.SDK.Models;

await Context.Message.SendDirectMessageAsync(userId, "你好");
await Context.Message.SendGroupMessageAsync(groupId, "你好");
await Context.Message.SendGroupMessageAsync(
    groupId, "查看图片：", new ImageSegment("https://example.com/a.png"));

await Context.Message.ReplyAsync(message, "收到");
await Context.Message.QuoteReplyAsync(message, "引用这条消息");

var sent = await Context.Message.SendGroupMessageAsync(groupId, "已发送");
if (sent.IsSuccess)
{
    await Context.Message.DeleteMessageAsync(Channel.Group(groupId), sent.MessageId);
}
```

`Context.Message` 是 `IMessageContext`，也提供 `GetMessageAsync`、`GetHistoryMessagesAsync` 和 `GetResourceUrlAsync`。资源段支持的 URI 形式由适配器决定。

宿主在通用发送、回复和引用回复入口拦截适配器发送异常，按适配器实例记录错误，并返回 `IsSuccess = false`、空 `MessageId` 和 `ErrorMessage`，原始异常不再传给插件。依赖发送成功的操作（例如缓存消息 ID、订阅回复、撤回）应先检查 `IsSuccess`。取消、参数为空和来源实例选择错误仍会抛出异常。其他消息操作、用户/频道服务和平台专有扩展保留原有异常语义，不支持的方法仍会抛出 `NotSupportedException`。

## 查询用户与频道

```csharp
var self = await Context.User.GetSelfAsync();
var user = await Context.User.GetUserAsync(userId);
var friends = await Context.User.GetFriendsAsync();

var channels = await Context.Channel.GetChannelsAsync();
var members = await Context.Channel.GetMembersAsync(groupId);
var member = await Context.Channel.GetMemberAsync(groupId, userId);
```

`Context.Channel` 还定义了改名、踢人、禁言和退出频道等操作；`Context.User` 定义了好友请求处理。是否可用取决于适配器与协议。

## 平台特有能力

```csharp
using ShiroBot.Model.QQ;

var groupApi = Context.GetAdapterExtension<IQGroupApi>();
if (groupApi is not null && long.TryParse(groupId, out var qqGroupId))
    await groupApi.SendNudgeAsync(qqGroupId, userId: 123456789);
```

可选扩展未实现时返回 `null`。例如 Milky 可以实现 `IQGroupApi`，QQ 官方适配器可以实现 `IQOfficialMessageApi`。分别参见 [QQ 特有能力](/plugin/qq-model)与[官方 Markdown 与按钮](/plugin/qq-official)。

## 适配器实例与后台发送

事件处理期间，`Context` 自动绑定来源适配器实例，绑定跨 `await` 保留。`Context.Platform` 是平台类型，`Context.InstanceId` 是当前实例 ID；`message.SelfId` 是平台账号 ID。这三个值含义不同，即使两个实例的平台和账号相同，也按实例 ID 分别路由。

`groupId` 和 `userId` 只指定会话，不用于选择适配器。定时任务或 Dashboard Action 没有事件上下文时，用实例 ID 指定目标，否则使用宿主默认适配器：

```csharp
using (Context.UseInstance("milky"))
{
    await Context.Message.SendGroupMessageAsync(groupId, "定时提醒");
}
```

适配器**包 ID** 来自 `BotAdapter` 声明，表示安装的 DLL；**实例 ID** 表示运行的一份适配器及其配置。新安装的包没有实例，在 Dashboard 适配器列表展开包后点击「添加实例」创建，例如 `milky`、`qq-work`、`qq-home`，不需要再次上传 DLL；实例 ID 在宿主内唯一，可在实例的「配置 → 实例」中修改。没有 `[[instances]]` 的旧配置按单实例运行，实例 ID 等于包 ID。

### 获取适配器与实例信息

`Context.AdapterInstance` 是当前作用域的实例（事件来源、`UseInstance` 所选实例或默认适配器），`Context.GetAdapterInstances()` 列出全部已加载实例。两者均为 `AdapterInstanceInfo`：

| 属性 | 含义 |
|---|---|
| `Id` | 实例 ID，传给 `UseInstance` |
| `Name` | 实例显示名称，未设置时为实例 ID |
| `PackageId` | 适配器包 ID，同一包的多个实例相同 |
| `AdapterName` | 适配器名称，如 `OneBot Adapter` |
| `Version` | 适配器程序集版本 |
| `Platform` | 平台 ID，如 `qq` |
| `Protocol` | 适配器声明的协议，未声明时为 `null` |

```csharp
// 按协议或包挑选实例，而不是写死实例 ID。
var target = Context.GetAdapterInstances().FirstOrDefault(item => item.Protocol == "onebot");
if (target is not null)
{
    using (Context.UseInstance(target.Id))
        await Context.Message.SendGroupMessageAsync(groupId, $"来自 {target.Name} 的提醒");
}

// 事件处理中查看来源实例。
BotLog.Info($"消息来自 {Context.AdapterInstance?.Name}（{Context.AdapterInstance?.AdapterName}）");
```

只列出运行中的实例；已停止、被总开关关闭或未加载的实例不在其中。旧宿主上 `AdapterInstance` 为 `null`、`GetAdapterInstances()` 返回空列表。

### 按来源回复

```csharp
// 命令、事件处理器中直接使用，跨 await 仍指向来源实例。
await Context.Message.ReplyAsync(message, "收到");
await Context.Message.QuoteReplyAsync(message, "引用回复");
await Context.Message.ReplyAsync(message, "图片", new ImageSegment("https://example.com/a.png"));

// 后台回复保存的原消息，也按原消息的 InstanceId 选择实例。
await Context.Message.ReplyAsync(savedMessage, "处理完成");
```

入站事件的 `InstanceId` 由宿主写入。上述回复只在调用期间选择来源实例，完成或失败后恢复外层选择。来源实例已停止、删除或未加载时抛出异常，不会误发到其他实例。自行构造的消息若没有 InstanceId，回退到 Platform：该平台只有一个运行实例时可以回复，否则抛出异常。

### 指定实例调用服务

`UseInstance` 同时选择 `Message`、`User`、`Channel` 和 `GetAdapterExtension<T>()` 的来源。作用域支持嵌套，跨 await 保留，各个并发执行流互不覆盖。

```csharp
using (Context.UseInstance("qq-work"))
{
    var self = await Context.User.GetSelfAsync();
    var members = await Context.Channel.GetMembersAsync(groupId);
    var sent = await Context.Message.SendGroupMessageAsync(groupId, "工作提醒");
    await Context.Message.DeleteMessageAsync(Channel.Group(groupId), sent.MessageId);
}

// 保存的事件用于查询、撤回等操作时，明确选择其来源。
using (Context.UseInstance(savedMessage.InstanceId!))
{
    await Context.Message.GetMessageAsync(savedMessage.Channel, savedMessage.MessageId);
    await Context.Message.DeleteMessageAsync(savedMessage);
}
```

自动选择来源仅适用于 `ReplyAsync` 和 `QuoteReplyAsync`；`DeleteMessageAsync(message)` 当前只是 Channel/MessageId 的便捷封装，后台调用仍须选择实例。仅有 groupId、userId、Channel 或 MessageId 不能自行推断适配器。

只能按实例 ID 选择实例，`UsePlatform` 已移除；需要按平台或协议挑选时，从 `Context.GetAdapterInstances()` 中筛选后传其 `Id`。独立后台任务、加载钩子及 Dashboard Action 没有来源事件时，未显式选择就使用第一个已加载适配器；给指定账号发消息不要依赖加载顺序。

### 回复订阅

订阅绑定**创建订阅时**的实例。同平台同账号且消息 ID 相同的其他实例不会触发它。事件处理器已经有来源上下文；后台发送和订阅应放在同一个实例作用域内：

```csharp
using (Context.UseInstance("qq-work"))
{
    var sent = await Context.Message.SendGroupMessageAsync(groupId, "请回复确认");
    Context.Message.SubscribeReply(sent.MessageId, TimeSpan.FromMinutes(1), async reply =>
    {
        await Context.Message.ReplyAsync(reply, "已确认");
    });
}
```

### 多实例状态与旧插件兼容性

需要按机器人隔离的缓存、任务或去重记录，用 `(InstanceId, ChannelId)`、`(InstanceId, MessageId)` 等键。只用 Platform、SelfId 或群号不能区分同平台同账号的多个实例；`(Platform, SelfId)` 表示平台账号，InstanceId 表示运行来源。

新增上下文成员有默认实现，未调用 `UsePlatform` 的旧插件 DLL 可直接加载。已用公开发布的 DemoPlugin v0.6.1 DLL 验证加载和双实例回复。`UsePlatform` 已移除：调用它的旧插件 DLL 仍可加载，但执行到该调用时抛出 `MissingMethodException`，需改用 `UseInstance` 并引用新版 SDK 重新编译。旧插件若自行按平台缓存状态，也需按上述规则调整；并未逐一测试全部第三方插件。

::: info 版本范围
实例接口、自动回复和同 DLL 多配置管理从宿主/SDK v0.9.7 开始提供，同时移除了 `UsePlatform`。v0.9.6 及更早版本仍使用平台作用域，后台回复需先 `Context.UsePlatform(message.Platform)`；面向 v0.9.7 及以后的插件请引用 SDK v0.9.7 并改用 `UseInstance`。
:::

调用其他插件导出的共享服务使用 `Context.Services`；配置、日志和数据目录参见[上下文、配置与日志](/plugin/context-config)。
