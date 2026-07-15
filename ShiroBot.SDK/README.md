# ShiroBot.SDK

`ShiroBot.SDK` provides the core contracts and helper APIs for ShiroBot plugins and adapters.

## Install

```xml
<PackageReference Include="ShiroBot.SDK" Version="0.7.0" />
```

Plugins that use Avalonia controls should additionally reference `ShiroBot.AvaloniaSdk`. Keeping
the rendering contracts in that package prevents ordinary plugins and adapters from taking an
unnecessary Avalonia compile-time dependency.

## Plugin Usage

Most plugins should inherit `PluginBase`.

```csharp
using ShiroBot.Model.Common;
using ShiroBot.SDK.Abstractions;
using ShiroBot.SDK.Core;
using ShiroBot.SDK.Plugin;

[BotPlugin(
    "HelloPlugin",
    Name = "HelloPlugin",
    Version = "1.0.0",
    Description = "Example plugin",
    GithubRepo = "example/HelloPlugin",
    IsPluginSingleFile = false)]
public sealed class HelloPlugin : PluginBase
{
    public override string Name => "HelloPlugin";

    protected override void ConfigureRoutes()
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

Override `ConfigureRoutes()` for synchronous command/event route registration. Override
`LoadAsync()` only when the plugin has real asynchronous initialization, and override
`OnUnloadAsync()` for cleanup.

```csharp
protected override void ConfigureRoutes()
{
    GroupCommands.MapExact("#ping", HandlePingAsync);
    GroupCommands.MapPrefix("#echo", HandleEchoAsync);
}

protected override async Task LoadAsync()
{
    await InitializeDatabaseAsync();
}
```

`ConfigureRoutes()` runs after `Context` is assigned and before `LoadAsync()`. Existing plugins
that register routes in `LoadAsync()` remain compatible. `PluginBase` automatically clears
command routes, event routes and `Context` after unload.

`OnUnloadAsync()` is called for hot unload and plugin updates. Process exit does not walk every
plugin, so persistent state should be saved during normal operation rather than only on unload.

## Automatic plugin packaging

When a plugin or adapter references the published `ShiroBot.SDK` NuGet package, its
`buildTransitive` targets automatically prepare a single-DLL component:

- assemblies carrying `BotPluginAttribute` or `BotAdapterAttribute` are detected automatically;
- managed NuGet/project dependencies are merged into the plugin DLL with ILRepack;
- host-shared contracts and rendering assemblies are not merged;
- native NuGet package ID, exact version, SHA512, RID and asset paths are embedded as
  `ShiroBot.PluginRuntimeManifest.json`;
- native files and merged managed dependency DLLs are removed from the plugin output;
- the generated `.deps.json` is removed after the plugin has been bundled.
- Avalonia, SkiaSharp, HarfBuzzSharp and MicroCom managed/native/runtime assets are removed from
  plugin build and publish outputs because the host supplies them.

At runtime ShiroBot reads the embedded manifest without executing plugin code, downloads the
required NuGet package, verifies SHA512, extracts only the best matching RID assets and deletes
the temporary `.nupkg`. Extracted native files are cached under the plugin's `.shirobot/native`
directory.

No ILRepack target or native manifest needs to be added to each component project. The defaults can
be customized when required:

```xml
<PropertyGroup>
  <!-- Disable all automatic packaging in a non-component library. -->
  <ShiroBotPluginPackagingEnabled>false</ShiroBotPluginPackagingEnabled>

  <!-- Defaults to the NuGet.org v3 flat-container endpoint. -->
  <ShiroBotNativePackageSource>https://packages.example.com/v3-flatcontainer</ShiroBotNativePackageSource>

  <!-- Semicolon-separated assembly/package prefixes supplied by the host. -->
  <ShiroBotSharedAssemblyPrefixes>$(ShiroBotSharedAssemblyPrefixes);Example.Shared</ShiroBotSharedAssemblyPrefixes>
  <ShiroBotSharedNativePackagePrefixes>$(ShiroBotSharedNativePackagePrefixes);Example.Native.Host</ShiroBotSharedNativePackagePrefixes>
</PropertyGroup>
```

Automatic targets are a NuGet `buildTransitive` feature. A source-level `ProjectReference` to
`ShiroBot.SDK.csproj` does not import the packed targets; use the SDK package when validating the
final distributable plugin.

Avalonia compile references and AXAML build support flow from `ShiroBot.AvaloniaSdk`, not this base
package. The host intentionally does not enable `PublishTrimmed`; plugin discovery, configuration
metadata and collectible loading use reflection paths that are not trimming-safe.

## Dashboard Actions

Implement `IPluginWebActionProvider` to expose authenticated dashboard operations without exposing
ASP.NET `HttpContext` to plugin code:

```csharp
public IReadOnlyList<PluginWebActionDescriptor> WebActions { get; } =
[
    new("reload", "Reload data", Tone: "primary")
];

public Task<PluginWebActionResult> ExecuteWebActionAsync(
    string actionId,
    CancellationToken cancellationToken = default) =>
    Task.FromResult(new PluginWebActionResult(true, "Reloaded", Refresh: true));
```

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

Map strongly typed events in `ConfigureRoutes()`.

```csharp
protected override void ConfigureRoutes()
{
    Events.Map<GroupMemberIncreaseEvent>(HandleMemberIncreaseAsync);
    Events.MapWhen<GroupMessageReactionEvent>(e => e.IsAdd, HandleReactionAddAsync);
}
```

`OnFriendMessageAsync` and `OnGroupMessageAsync` remain available for whole-message broadcasts.
Use `Events.Map<TEvent>()` for every other event, including newly generated Model events.

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

Top-level properties annotated with `ConfigFieldAttribute` are written as stable `#` comments above
their snake_case TOML keys. Labels, descriptions, options, ranges and placeholders are included when
present, and repeated saves do not stack duplicate comments.

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

[BotAdapter(
    "MyAdapter",
    Name = "MyAdapter",
    Version = "1.0.0",
    Protocol = "example",
    ProtocolVersionRange = ">=1.0 <2.0")]
public sealed class MyAdapter : IBotAdapter
{
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

`BotAdapterAttribute` is required and is the only adapter metadata source. `IEventService` exposes
one `Func<Event, Task>` event named `EventReceived`; adapters publish every concrete model event
through that stream. `StartAsync()` and `StopAsync()` are optional no-op lifecycle hooks. Adapter ABI
is versioned as a breaking contract, while plugin and model ABI compatibility remain preserved.
