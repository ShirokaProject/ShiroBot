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
var messageId = sent.MessageId;
await Context.Message.DeleteMessageAsync(Channel.Group(groupId), messageId);
```

`Context.Message` 是 `IMessageContext`，也提供 `GetMessageAsync`、`GetHistoryMessagesAsync` 和 `GetResourceUrlAsync`。资源段支持的 URI 形式由适配器决定；不支持的方法会抛出 `NotSupportedException`。

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

事件处理期间，`Context` 自动绑定来源适配器实例，绑定跨 `await` 保留。`Context.Platform` 是平台类型，`Context.AdapterId` 是当前实例 ID；`message.SelfId` 是平台账号 ID。这三个值含义不同，即使两个实例的平台和账号相同，也按实例 ID 分别路由。

`groupId` 和 `userId` 只指定会话，不用于选择适配器。定时任务或 Dashboard Action 没有事件上下文时，用实例 ID 指定目标，否则使用宿主默认适配器：

```csharp
using (Context.UseAdapter("milky"))
{
    await Context.Message.SendGroupMessageAsync(groupId, "定时提醒");
}
```

当前每个适配器包对应一个实例，实例 ID 使用其 `BotAdapter` 声明的 ID，也是 Dashboard 中的适配器 ID，例如 `milky`、`qq-official`。不同 ID 的适配器可以使用相同 Platform；相同 ID 不允许重复加载。这次改动不包含同一个适配器包的多配置实例安装。

宿主 master 分支（v0.9.6 之后）中，入站事件的 `AdapterId` 由宿主写入。`ReplyAsync(message, ...)` 和 `QuoteReplyAsync(message, ...)` 自动使用这个来源实例，也适用于后台回复保存的消息。来源实例已卸载时抛出异常，不会切换到其他实例；回复订阅同样按实例隔离。

`UsePlatform("qq")` 仍可用于该平台只有一个运行实例的情况；多个实例时抛出异常，需使用 `UseAdapter(id)`。自行构造或旧事件没有 `AdapterId` 时，自动回复也按这个规则回退到 Platform。仅传入 Channel 和消息 ID 的发送、删除操作需自行选择目标实例。

这些实例选择接口尚未包含在宿主和 SDK v0.9.6 的发布包中。使用 v0.9.6 时，后台发送或回复仍需先使用 `Context.UsePlatform(message.Platform)`。

调用其他插件导出的共享服务使用 `Context.Services`；配置、日志和数据目录参见[上下文、配置与日志](/plugin/context-config)。
