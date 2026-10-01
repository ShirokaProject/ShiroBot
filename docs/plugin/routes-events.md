# 接收消息与事件

插件继承 `PluginBase` 后，在 `ConfigureRoutes()` 注册消息命令与事件。普通入站消息的类型是 `MessageEvent`；其他通用事件和平台特有事件通过 `Events` 路由。

## 接收消息

```csharp
using ShiroBot.SDK.Models;

protected override void ConfigureRoutes()
{
    GroupCommands.MapExact("#ping", HandlePingAsync);
    GroupCommands.MapPrefix("#echo ", HandleEchoAsync);
    GroupCommands.MapMention(HandleMentionAsync);
    DirectCommands.MapExact("帮助", HandleHelpAsync);
}

private Task HandlePingAsync(MessageEvent message) =>
    Context.Message.ReplyAsync(message, "pong");

private Task HandleEchoAsync(MessageEvent message) =>
    Context.Message.ReplyAsync(message, message.GetPlainText()["#echo ".Length..]);
```

`GroupCommands` 接收群聊或频道消息，`DirectCommands` 接收私聊消息。`MapExact` 完全匹配，`MapPrefix` 按前缀匹配，`MapWhen` 使用自定义条件，`MapAll` 接收全部消息；同一路由器按注册顺序执行第一条匹配的处理器。

消息辅助方法包括 `GetPlainText()`、`HasMention()`、`HasMention(userId)`、`HasMentionAll()` 与 `GetQuote()`。还可以读取 `message.Channel`、`message.Sender`、`message.Member` 和有序的 `message.Segments`。消息段类型见[通用 Model](/plugin/models)。

如果需要接收某一场景的所有消息，也可重写 `OnGroupMessageAsync(MessageEvent)` 或 `OnDirectMessageAsync(MessageEvent)`。重写后若仍需执行命令路由，应调用基类实现。

## 接收通用事件

```csharp
protected override void ConfigureRoutes()
{
    Events.Map<MemberJoinedEvent>(async evt =>
    {
        await Context.Message.SendGroupMessageAsync(evt.Channel.Id, $"欢迎 {evt.UserId}");
    });

    Events.MapWhen<MemberLeftEvent>(
        evt => evt.Channel.Type == ChannelType.Group,
        evt => HandleMemberLeftAsync(evt));
}
```

可订阅的通用事件包括 `MessageDeletedEvent`、`MemberJoinedEvent`、`MemberLeftEvent`、`FriendRequestEvent`、`GuildInviteEvent` 和 `BotOfflineEvent`。所有类型继承 `BotEvent`，并带有 `Platform`、可选 `SelfId` 与 `Raw`。新增类型只需用 `Events.Map<TEvent>()` 注册。

## 接收平台特有事件

适配器把无法映射成通用事件的内容放进 `PlatformEvent`，用 `Kind` 区分事件类型，`Raw` 保存平台 Model 的负载：

```csharp
using ShiroBot.Model.QQ;

Events.MapPlatform(QEventKinds.OfficialButtonInteraction, evt =>
{
    if (evt.Raw is not QOfficialButtonInteraction click)
        return Task.CompletedTask;

    return HandleButtonAsync(click.ButtonData, click.UserId);
});
```

QQ 事件详见 [QQ Model](/plugin/qq-model)，官方按钮事件详见[官方 Markdown 与按钮](/plugin/qq-official)。

## 并发与范围

- 事件处理时 `Context.Platform` 和 `Context.AdapterId` 指向事件来源平台及实例；后台任务用 `Context.UseAdapter(id)` 指定目标。同平台仅一个运行实例时也可用 `UsePlatform(platform)`。实例接口见[通用 API](/plugin/apis)。
- 不同插件可并发处理同一个事件，同一个插件也可能同时收到多条事件；共享状态应自行同步。
- 群路由限制由宿主配置 `plugin_routes` 控制。收到事件后不要长时间同步阻塞。
