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

## 只处理指定适配器实例

默认命令路由可以接收所有实例的消息，服务调用自动绑定每条消息的来源。如果插件只服务某个机器人，可以按 InstanceId 过滤：

```csharp
GroupCommands.MapWhen(
    message => message.InstanceId == "qq-work" && message.GetPlainText() == "#ping",
    message => Context.Message.ReplyAsync(message, "工作机器人在线"));
```

也可在通用事件处理器中检查 `evt.InstanceId`。

## 在 Dashboard 中显示监听内容

Dashboard 的插件详情会列出插件注册的监听：前缀、完整命令、`MapMention`（及指定用户）、`MapMentionAll`、`MapReply`、`MapAll`、通用事件类型和 `MapPlatform` 的 Kind。`MapWhen` 的条件是代码，默认只显示为“自定义条件”；需要说明时，传入描述文字（SDK ABI 1.3.0.0 起）：

```csharp
GroupCommands.MapWhen(
    message => message.GetPlainText().Contains("天气"),
    "包含关键词 天气",
    HandleWeatherAsync);

Events.MapWhen<MemberLeftEvent>(
    evt => evt.Channel.Type == ChannelType.Group,
    "群成员退群",
    HandleMemberLeftAsync);
```

重写 `OnGroupMessageAsync` / `OnDirectMessageAsync` 或订阅 `Events.Map<MessageEvent>` 的插件会显示为接收全部群聊或私聊消息。宿主接口 `GET /api/v1/plugins/{id}` 的 `listeners` 字段返回这些信息，插件未加载时为 `null`。

旧 SDK 编译的插件无需重新编译：`MapMention`、`MapReply`、`MapPlatform` 等由宿主的 SDK 执行，升级宿主后即可显示具体类型。不要仅用 Platform 或 SelfId 判断运行来源：它们都可能相同。后台发送、保存消息后的回复和订阅见[调用 API](/plugin/apis#适配器实例与后台发送)。

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

## 优先级与已处理

同一事件按插件优先级依次分发：数字越小越先收到，相同数字的插件并发执行。在 `BotPlugin` 上声明，未声明（包括此前编译的插件）时为 `2`：

```csharp
// AI 聊天作为兜底：只在其他插件没有处理消息时回复
[BotPlugin("ai-chat", Name = "AI Chat", Priority = 10)]
public sealed class AiChatPlugin : PluginBase { }
```

事件被标记为已处理后，数字更大的插件不再收到，相同数字的插件不受影响：

- `MapExact`、`MapPrefix`、带条件的 `MapWhen` 以及 `MapMention`、`MapMentionAll`、`MapReply` 匹配时自动标记，在处理器执行前生效。无条件的 `MapAll` 只算旁听，不标记，日志、统计类插件不会拦住其他插件。
- 重写 `OnGroupMessageAsync` / `OnDirectMessageAsync` 自行解析命令，或在 `Events` 中处理事件后，调用 `e.MarkHandled()` 手动标记；`e.IsHandled()` 读取当前状态。
- 引用回复被[回复订阅](/plugin/apis#回复订阅)接收时，按优先级 `2` 视为已处理：普通插件仍会收到，数字大于 2 的兜底插件不再收到。

一个优先级的插件超过 5 秒仍未处理完时，宿主记录警告并继续分发给后面的优先级。Owner 可以在 Dashboard 插件路由或 `plugin_routes.priorities` 中覆盖插件声明的优先级，见[宿主配置](/guide/configuration#插件优先级)。

`Priority` 只是 attribute 参数，旧宿主读取时会忽略；调用 `MarkHandled()` / `IsHandled()` 的插件需要宿主 1.0.2。

## 并发与范围

- 事件处理时 `Context.Platform` 和 `Context.InstanceId` 指向事件来源平台及实例；后台任务用 `Context.UseInstance(id)` 按实例 ID 指定目标。实例接口见[通用 API](/plugin/apis)。
- 相同优先级的插件并发处理同一个事件，同一个插件也可能同时收到多条事件；共享状态应自行同步。
- 群路由限制由宿主配置 `plugin_routes` 控制。收到事件后不要长时间同步阻塞。
