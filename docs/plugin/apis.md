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

## 后台任务选择平台

事件处理期间，`Context` 自动指向事件来源适配器。定时任务或 Dashboard Action 没有事件上下文时，可显式指定：

```csharp
using (Context.UsePlatform("qq"))
{
    await Context.Message.SendGroupMessageAsync(groupId, "定时提醒");
}
```

`Context.Platform` 可读取当前平台 ID。调用其他插件导出的共享服务使用 `Context.Services`；配置、日志和数据目录参见[上下文、配置与日志](/plugin/context-config)。
