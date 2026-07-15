# 消息路由与事件

`PluginBase` 提供群消息路由、好友消息路由和通用事件路由。宿主会在事件进入插件前完成第一层匹配，避免把每条消息广播给所有插件。

## 消息命令路由

```csharp
protected override void ConfigureRoutes()
{
    GroupCommands.MapExact("#ping", HandlePingAsync);
    GroupCommands.MapPrefix("#echo ", HandleEchoAsync);
    GroupCommands.MapWhen(
        message => message.GetPlainText().Contains("ShiroBot"),
        HandleKeywordAsync);

    FriendCommands.MapExact("帮助", HandleHelpAsync);
}
```

| 方法 | 匹配方式 |
| --- | --- |
| `MapExact` | 去除首尾空白后完全匹配，不区分大小写 |
| `MapPrefix` / `Map` | 前缀匹配，不区分大小写 |
| `MapWhen` | 使用消息对象执行自定义条件 |
| `MapAll` | 接收此类型的全部消息 |

同一个 `CommandRouter` 按注册顺序查找，第一条匹配的路由执行后停止继续匹配。

### Mention 与回复路由

SDK 提供常用扩展：

```csharp
GroupCommands.MapMention(HandleAnyMentionAsync);
GroupCommands.MapMention(botUserId, HandleMentionBotAsync);
GroupCommands.MapMentionAll(HandleMentionAllAsync);
GroupCommands.MapReply(HandleReplyAsync);
GroupCommands.MapReplyTo(userId, HandleReplyToUserAsync);
GroupCommands.MapReplyMessage(messageSeq, HandleSpecificReplyAsync);
```

好友消息也支持 `MapMention`、`MapReply`、`MapReplyTo` 和 `MapReplyMessage`。

### 读取文本与消息段

```csharp
var text = message.GetPlainText();
var mention = message.Segments.OfType<MentionIncomingSegment>().FirstOrDefault();
var reply = message.GetReply();
var images = message.Segments.OfType<ImageIncomingSegment>().ToArray();
```

发送回复：

```csharp
await Context.Message.ReplyAsync(message, "普通回复");
await Context.Message.QuoteReplyAsync(message, "引用回复");

await Context.Message.ReplyAsync(
    message,
    new TextOutgoingSegment("结果："),
    new ImageOutgoingSegment("https://example.com/image.png"));
```

## 通用事件路由

推荐在 `ConfigureRoutes()` 中映射事件：

```csharp
protected override void ConfigureRoutes()
{
    Events.Map<GroupMemberIncreaseEvent>(HandleMemberIncreaseAsync);

    Events.MapWhen<GroupMessageReactionEvent>(
        reaction => reaction.IsAdd,
        HandleReactionAddedAsync);
}
```

群消息和好友消息也可以重写 `PluginBase` 提供的方法。这样会让插件接收对应类型的全部消息；其他事件应使用 `Events.Map`：

```csharp
protected override Task OnGroupMessageAsync(GroupIncomingMessage message)
{
    return Context.Message.ReplyAsync(message, "收到了群消息");
}
```

常用事件包括：

- `FriendRequestEvent`
- `GroupJoinRequestEvent`
- `GroupInvitedJoinRequestEvent`
- `GroupInvitationEvent`
- `FriendNudgeEvent` / `GroupNudgeEvent`
- `FriendFileUploadEvent` / `GroupFileUploadEvent`
- `GroupAdminChangeEvent`
- `GroupMemberIncreaseEvent` / `GroupMemberDecreaseEvent`
- `GroupMessageReactionEvent`
- `GroupMuteEvent` / `GroupWholeMuteEvent`
- `MessageRecallEvent`
- `PeerPinChangeEvent`
- `GroupDisbandEvent`
- `BotOfflineEvent`

插件的有效订阅直接由已注册命令路由、事件映射和重写的方法对应的模型 `Type` 推断，不再经过有限的 flags 位图。Model 新增事件后，插件可以直接 `Events.Map<NewEvent>()`。

## 群路由限制

宿主会在分发前应用核心配置中的 `plugin_routes`。如果某插件只应在指定群运行：

```toml
[plugin_routes.plugins.HelloPlugin]
mode = "whitelist"
groups = [10001, 10002]
```

插件 ID 必须与 `[BotPlugin("HelloPlugin")]` 一致。

## 并发行为

- 不同插件的同一事件由宿主并发分发。
- 单个插件可能同时收到多条事件；插件内部共享状态需要自行同步。
- 热卸载会等待已经进入该插件的分发结束。
- 不要在事件处理器中长时间同步阻塞；使用真正的异步 I/O。
