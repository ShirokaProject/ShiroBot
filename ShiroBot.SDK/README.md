# ShiroBot.SDK

`ShiroBot.SDK` provides the core contracts and helper APIs for ShiroBot plugins and adapters.

## Plugin Usage

Most plugins should inherit `PluginBase`.

```csharp
using ShiroBot.Model.Common;
using ShiroBot.SDK.Abstractions;
using ShiroBot.SDK.Core;
using ShiroBot.SDK.Plugin;

[BotPlugin(id: "HelloPlugin",
    Name = "HelloPlugin",
    Version = "1.0.0",
    Description = "Example plugin",
    GithubRepo = "example/HelloPlugin",
    IsPluginSingleFile = false)
]
public sealed class HelloPlugin : PluginBase
{
    public override string Name => "HelloPlugin";

    protected override async Task LoadAsync()
    {
        GroupCommands.MapPrefix("#hello", async message =>
        {
            await Context.Message.ReplyAsync(message, "hello");
        });

        BotLog.Info("HelloPlugin loaded.");
    }
}
```

## Lifecycle

Override `LoadAsync()` and `OnUnloadAsync()` for plugin setup and cleanup.

```csharp
protected override Task LoadAsync()
{
    GroupCommands.MapExact("#ping", HandlePingAsync);
    GroupCommands.MapPrefix("#echo", HandleEchoAsync);
    return Task.CompletedTask;
}

protected override Task OnUnloadAsync()
{
    return Task.CompletedTask;
}
```

`PluginBase` automatically clears command routes, event routes and `Context` after unload.

## Command Routes

`PluginBase` exposes two command routers:

```csharp
protected CommandRouter<GroupIncomingMessage> GroupCommands { get; }
protected CommandRouter<FriendIncomingMessage> FriendCommands { get; }
```

Supported route types:

```csharp
GroupCommands.MapExact("#ping", HandlePingAsync);
GroupCommands.MapPrefix("#echo", HandleEchoAsync);
GroupCommands.MapAll(HandleAnyGroupMessageAsync);
GroupCommands.MapWhen(message => message.HasMention(), HandleMentionAsync);
GroupCommands.MapMention(botUserId, HandleMentionAsync);
GroupCommands.MapReply(HandleReplyAsync);
```

Example handler:

```csharp
private Task HandlePingAsync(GroupIncomingMessage message) =>
    Context.Message.ReplyAsync(message, "pong");
```

## Event Routes

You can either override built-in event methods or map events in `LoadAsync()`.

```csharp
protected override Task LoadAsync()
{
    Events.Map<GroupMemberIncreaseEvent>(HandleMemberIncreaseAsync);
    Events.MapWhen<GroupMessageReactionEvent>(e => e.IsAdd, HandleReactionAddAsync);
    return Task.CompletedTask;
}
```

Common overridable methods include:

- `OnFriendMessageAsync`
- `OnGroupMessageAsync`
- `OnFriendRequestAsync`
- `OnGroupJoinRequestAsync`
- `OnGroupInvitedJoinRequestAsync`
- `OnGroupInvitationAsync`
- `OnFriendNudgeAsync`
- `OnFriendFileUploadAsync`
- `OnGroupAdminChangeAsync`
- `OnGroupEssenceMessageChangeAsync`
- `OnGroupMemberIncreaseAsync`
- `OnGroupMemberDecreaseAsync`
- `OnGroupNameChangeAsync`
- `OnGroupMessageReactionAsync`
- `OnGroupMuteAsync`
- `OnGroupWholeMuteAsync`
- `OnGroupNudgeAsync`
- `OnGroupFileUploadAsync`
- `OnMessageRecallAsync`

## Messages

`Context.Message` exposes adapter message APIs plus helper overloads.

```csharp
await Context.Message.SendPrivateMessageAsync(userId, "hello");
await Context.Message.SendGroupMessageAsync(groupId, "hello");
await Context.Message.ReplyAsync(friendMessage, "pong");
await Context.Message.ReplyAsync(groupMessage, "pong");
await Context.Message.QuoteReplyAsync(groupMessage, "quoted reply");
```

Subscribe to replies for a sent message (default: automatically dispose on reply):

```csharp
var sent = await Context.Message.SendGroupMessageAsync(groupId, "reply to me");

Context.Message.SubscribeReply(
    sent.MessageSeq,
    TimeSpan.FromMinutes(5),
    async reply => await Context.Message.ReplyAsync(reply, "got it"));
```

Pass `disposeOnReply: false` to keep listening until the timeout expires or the subscription is disposed manually:

```csharp
var subscription = Context.Message.SubscribeReply(
    sent.MessageSeq,
    TimeSpan.FromMinutes(5),
    async reply => await Context.Message.ReplyAsync(reply, "still listening"),
    disposeOnReply: false);
```

Subscribe only to replies containing a matching text segment:

```csharp
Context.Message.SubscribeReply(
    sent.MessageSeq,
    "confirm",
    TimeSpan.FromMinutes(5),
    async reply => await Context.Message.ReplyAsync(reply, "confirmed"));
```

Send segments directly:

```csharp
await Context.Message.SendGroupMessageAsync(
    groupId,
    new TextOutgoingSegment("hello"),
    new ImageOutgoingSegment(fileUri));
```

## Incoming Message Helpers

```csharp
var text = message.GetPlainText();
var reply = message.GetReply();
var mentioned = message.HasMention(userId);
var mentionAll = message.HasMentionAll();
```

## Config

Plugin config is stored at:

```text
plugins/<PluginFolder>/config.toml
```

Load and save:

```csharp
var config = Context.Config.Load<MyPluginConfig>();
Context.Config.Save(config);
```

Watch for config changes:

```csharp
private IDisposable? _configWatcher;

protected override Task LoadAsync()
{
    _configWatcher = Context.Config.Watch<MyPluginConfig>(updated =>
    {
        // Apply new config.
    });

    return Task.CompletedTask;
}

protected override Task OnUnloadAsync()
{
    _configWatcher?.Dispose();
    return Task.CompletedTask;
}
```

`Watch<T>()` returns an `IDisposable`; dispose it during unload to avoid leaking a `FileSystemWatcher`.

## Logging

Use `BotLog` inside plugin code. The host scopes plugin dispatches to the plugin logger.

```csharp
using ShiroBot.SDK.Abstractions;

BotLog.Info("plugin loaded");
BotLog.Warning("something happened");
BotLog.Error("operation failed");
```

## Bot Context

`IBotContext` provides:

- `File`
- `Friend`
- `Group`
- `Message`
- `System`
- `Updater`
- `Config`
- `OwnerList`
- `AdminList`
- `Render`

Admin helpers:

```csharp
if (!Context.IsAdmin(message.SenderId))
{
    await Context.Message.ReplyAsync(message, "permission denied");
    return;
}
```

## Adapter Usage

Adapters implement `IBotAdapter`.

```csharp
using ShiroBot.SDK.Adapter;
using ShiroBot.SDK.Config;
using ShiroBot.SDK.Core;
using ShiroBot.SDK.Plugin;

public sealed class MyAdapter : IBotAdapter
{
    public string Name => "MyAdapter";

    public BotComponentMetadata Metadata { get; } = new()
    {
        Name = "MyAdapter",
        Version = "1.0.0"
    };

    public IConfigContext Config { get; set; } = null!;
    public IConsoleLogger Logger { get; set; } = null!;

    public IFileService File { get; } = new MyFileService();
    public IFriendService Friend { get; } = new MyFriendService();
    public IGroupService Group { get; } = new MyGroupService();
    public IMessageService Message { get; } = new MyMessageService();
    public ISystemService System { get; } = new MySystemService();
    public IEventService Event { get; } = new MyEventService();

    public Task StartAsync()
    {
        var config = Config.Load<MyAdapterConfig>();
        Logger.Info("adapter started");
        return Task.CompletedTask;
    }
}
```

Adapter config is loaded from the adapter directory through `Config.Load<T>()`.
