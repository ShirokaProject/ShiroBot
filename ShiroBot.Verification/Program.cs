using ShiroBot.Core;
using ShiroBot.SDK.Config;
using ShiroBot.Hosting;
using ShiroBot.Hosting.Context;
using ShiroBot.SDK.Adapter;
using ShiroBot.SDK.Core;
using ShiroBot.SDK.Models;
using ShiroBot.SDK.Plugin;
using ShiroBot.Model.QQ;

[assembly: ShiroBotApiCompatibility("0.8", "0.8")]

var serviceRegistry = new PluginServiceRegistry();
using var providerServices = new PluginServiceScope(serviceRegistry, "provider");
using var consumerServices = new PluginServiceScope(serviceRegistry, "consumer");
var verificationService = new VerificationService();
providerServices.RegisterSingleton<IVerificationService>(verificationService);

if (!ReferenceEquals(consumerServices.GetRequiredService<IVerificationService>(), verificationService) ||
    !serviceRegistry.GetConsumers("provider").SequenceEqual(["consumer"]))
{
    throw new InvalidOperationException("Plugin service registration or dependency tracking failed.");
}

consumerServices.Dispose();
if (serviceRegistry.GetConsumers("provider").Count != 0)
{
    throw new InvalidOperationException("Plugin service consumer cleanup failed.");
}

providerServices.Dispose();
if (serviceRegistry.GetService("consumer", typeof(IVerificationService)) is not null)
{
    throw new InvalidOperationException("Plugin service provider cleanup failed.");
}

Console.WriteLine("Plugin service registry verification passed.");

ComponentApiCompatibility.EnsureCompatible("Plugin", "compatible", "0.8", "0.8.0");
AssertThrows<InvalidOperationException>(() =>
    ComponentApiCompatibility.EnsureCompatible("Plugin", "future", "0.9", "0.9"));
AssertThrows<InvalidOperationException>(() =>
    ComponentApiCompatibility.EnsureCompatible("Plugin", "invalid", "0.9", "0.8"));
AssertThrows<InvalidOperationException>(() =>
    ComponentApiCompatibility.EnsureCompatible("Plugin", "malformed", "preview", "0.8"));
Console.WriteLine("Component API version verification passed.");

var builtInModelRoot = Path.Combine(
    Path.GetTempPath(),
    "ShiroBot.Verification",
    Guid.NewGuid().ToString("N"),
    "models");
try
{
    var modelRegistry = new ModelPackageRegistry(new SharedAssemblyResolver());
    modelRegistry.RegisterBuiltIn(typeof(QGroup).Assembly);
    modelRegistry.LoadFromDirectory(builtInModelRoot);
    var builtInModel = modelRegistry.GetPackages().Single();
    if (builtInModel is not
        {
            Id: "shirobot.model.qq",
            Version: "0.8.0",
            Source: "built_in",
            Reloadable: false,
            AssemblyPath: null
        })
    {
        throw new InvalidOperationException("Built-in Model package metadata is incorrect.");
    }

    await modelRegistry.ReloadAsync();
    if (modelRegistry.GetPackages().Single().Source != "built_in")
    {
        throw new InvalidOperationException("Reload removed the built-in Model package.");
    }

    var duplicatePath = Path.Combine(builtInModelRoot, "ShiroBot.Model.QQ.dll");
    Directory.CreateDirectory(builtInModelRoot);
    File.Copy(typeof(QGroup).Assembly.Location, duplicatePath);
    AssertThrows<InvalidOperationException>(() => modelRegistry.LoadFromDirectory(builtInModelRoot));
    await AssertThrowsAsync<SharedAssemblyRestartRequiredException>(() =>
        modelRegistry.InstallAsync(duplicatePath));

    Console.WriteLine("Built-in Model package verification passed.");
}
finally
{
    var builtInRoot = Directory.GetParent(builtInModelRoot)?.FullName;
    if (builtInRoot is not null && Directory.Exists(builtInRoot))
        Directory.Delete(builtInRoot, recursive: true);
}

var qqAdapter = new VerificationAdapter("qq");
var discordAdapter = new VerificationAdapter("discord");
var botContext = new BotContext(null, [], [], new WebHostContext("http://127.0.0.1", false));
botContext.RegisterAdapter(qqAdapter);
botContext.RegisterAdapter(discordAdapter);

await Task.WhenAll(
    SendInAdapterScopeAsync(qqAdapter, "qq-message"),
    SendInAdapterScopeAsync(discordAdapter, "discord-message"));

if (!qqAdapter.MessageService.Messages.SequenceEqual(["qq-message"]) ||
    !discordAdapter.MessageService.Messages.SequenceEqual(["discord-message"]))
{
    throw new InvalidOperationException("Multi-adapter event scope routed a message to the wrong adapter.");
}

Console.WriteLine("Multi-adapter message routing verification passed.");

var explicitPlatformDirectory = Path.Combine(
    Path.GetTempPath(),
    "ShiroBot.Verification",
    Guid.NewGuid().ToString("N"));
var explicitPlatformContext = new PluginContext(
    botContext,
    "platform-selection",
    explicitPlatformDirectory,
    new HostLogHub(),
    new PluginServiceRegistry());
using (explicitPlatformContext.UsePlatform("discord"))
{
    await explicitPlatformContext.Message.SendMessageAsync(
        Channel.Group("channel"),
        [new TextSegment("explicit-discord")]);
}

AssertThrows<InvalidOperationException>(() => explicitPlatformContext.UsePlatform("missing"));
explicitPlatformContext.Dispose();
if (Directory.Exists(explicitPlatformDirectory)) Directory.Delete(explicitPlatformDirectory, recursive: true);
if (!discordAdapter.MessageService.Messages.Contains("explicit-discord") ||
    qqAdapter.MessageService.Messages.Contains("explicit-discord"))
{
    throw new InvalidOperationException("Explicit adapter platform selection routed to the wrong adapter.");
}

Console.WriteLine("Explicit adapter platform selection verification passed.");

var tempRoot = Path.Combine(Path.GetTempPath(), "ShiroBot.Verification", Guid.NewGuid().ToString("N"));
var configPath = Path.Combine(tempRoot, "config.toml");

try
{
    var eventLogHub = new HostLogHub();
    var eventDispatcher = new HostEventDispatcher(
        new Lock(),
        botContext.ReplySubscriptions,
        new HostRuntimeState(DateTimeOffset.UtcNow),
        eventLogHub);
    var directSubscriber = new DirectSubscriberPlugin();
    var routedPlugin = new RoutedPlugin();
    var directHandle = await CreatePluginHandleAsync(directSubscriber, "direct-subscriber", eventLogHub);
    var routedHandle = await CreatePluginHandleAsync(routedPlugin, "routed-plugin", eventLogHub);
    eventDispatcher.RegisterPlugin(directHandle);
    eventDispatcher.RegisterPlugin(routedHandle);
    eventDispatcher.MarkInitialPluginsReady();

    await eventDispatcher.PublishAsync(new MemberJoinedEvent
    {
        Platform = "verification",
        Channel = Channel.Group("group"),
        UserId = "user"
    });
    await eventDispatcher.PublishAsync(new MessageEvent
    {
        Platform = "verification",
        MessageId = "message",
        Channel = Channel.Group("group"),
        Sender = new User("user"),
        Segments = [new TextSegment("hello")]
    });

    if (directSubscriber.Events.Count != 2 ||
        directSubscriber.Events[0] is not MemberJoinedEvent ||
        directSubscriber.Events[1] is not MessageEvent)
    {
        throw new InvalidOperationException("Direct IBotEventSubscriber did not receive all event types exactly once.");
    }

    if (routedPlugin.BaseEventCount != 2 || routedPlugin.MemberJoinedCount != 1)
    {
        throw new InvalidOperationException("Polymorphic event routing failed or dispatched a plugin more than once.");
    }

    Console.WriteLine("Plugin event compatibility verification passed.");

    directSubscriber.Events.Clear();
    var queuedEventService = new VerificationEventService();
    var adapterProbe = AdapterContractProbe.ReadMetadata(typeof(VerificationAdapter).Assembly.Location)
                       ?? throw new InvalidOperationException("Adapter metadata probe failed.");
    if (adapterProbe is not { Id: "verification", MinimumApiVersion: "0.8", MaximumApiVersion: "0.8" })
    {
        throw new InvalidOperationException("Adapter API version metadata was read incorrectly.");
    }

    var eventBridge = new AdapterEventBridge(eventDispatcher);
    var bridgeSubscription = eventBridge.Bridge("verification", queuedEventService, _ => Task.CompletedTask);
    await queuedEventService.RaiseAsync(CreateMemberJoinedEvent("first"));
    await queuedEventService.RaiseAsync(CreateMemberJoinedEvent("second"));
    await bridgeSubscription.DisposeAsync();
    if (directSubscriber.Events.OfType<MemberJoinedEvent>().Select(e => e.UserId).ToArray() is not ["first", "second"])
    {
        throw new InvalidOperationException("Adapter event queue did not drain in FIFO order.");
    }

    Console.WriteLine("Adapter event queue verification passed.");

    var watchContext = new PluginContext(
        botContext,
        "watch-owner",
        Path.Combine(tempRoot, "watch-owner"),
        eventLogHub,
        new PluginServiceRegistry());
    var ownedWatch = (ConfigWatchSubscription)watchContext.Config.Watch<VerificationConfig>(_ => { });
    watchContext.Dispose();
    if (!ownedWatch.IsDisposed)
    {
        throw new InvalidOperationException("Plugin-owned config watcher was not disposed with its context.");
    }

    Console.WriteLine("Plugin config watcher ownership verification passed.");

    var blockedDispatchPlugin = new BlockingDispatchPlugin();
    var blockedDispatchHandle = await CreatePluginHandleAsync(
        blockedDispatchPlugin,
        "blocked-dispatch",
        eventLogHub,
        TimeSpan.FromMilliseconds(50),
        TimeSpan.FromMilliseconds(50));
    var blockedDispatch = blockedDispatchHandle.DispatchAsync<IBlockingDispatchPlugin>(plugin => plugin.DispatchAsync());
    await blockedDispatchPlugin.DispatchStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));

    var dispatchTimeoutResult = await blockedDispatchHandle.UnloadAsync().WaitAsync(TimeSpan.FromSeconds(1));
    AssertUnloadTimedOut(dispatchTimeoutResult, "active plugin dispatches");
    if (blockedDispatchHandle.Supports<IBlockingDispatchPlugin>() ||
        await blockedDispatchHandle.DispatchAsync<IBlockingDispatchPlugin>(_ => Task.CompletedTask))
    {
        throw new InvalidOperationException("Plugin accepted a new dispatch after unload started.");
    }

    blockedDispatchPlugin.ReleaseDispatch.TrySetResult();
    await blockedDispatch.WaitAsync(TimeSpan.FromSeconds(1));
    await blockedDispatchPlugin.UnloadCompleted.Task.WaitAsync(TimeSpan.FromSeconds(1));
    Console.WriteLine("Blocked plugin dispatch unload timeout verification passed.");

    var blockedUnloadPlugin = new BlockingUnloadPlugin();
    var blockedUnloadHandle = await CreatePluginHandleAsync(
        blockedUnloadPlugin,
        "blocked-unload",
        eventLogHub,
        TimeSpan.FromMilliseconds(50),
        TimeSpan.FromMilliseconds(50));
    var unloadTimeoutResult = await blockedUnloadHandle.UnloadAsync().WaitAsync(TimeSpan.FromSeconds(1));
    AssertUnloadTimedOut(unloadTimeoutResult, "plugin OnUnload");
    blockedUnloadPlugin.ReleaseUnload.TrySetResult();
    await blockedUnloadPlugin.UnloadCompleted.Task.WaitAsync(TimeSpan.FromSeconds(1));
    Console.WriteLine("Blocked plugin OnUnload timeout verification passed.");

    var manager = new ConfigManager(configPath);
    var config = manager.LoadConfig<VerificationConfig>(configPath, "verification")
                 ?? throw new InvalidOperationException("Default TOML generation failed.");
    var generatedToml = File.ReadAllText(configPath);
    AssertBefore(generatedToml, "# Request timeout", "timeout_seconds = 15");
    AssertBefore(generatedToml, "# Options: compact, detailed", "output_mode = \"compact\"");

    manager.SaveConfig(configPath, config);
    manager.SaveConfig(configPath, config);

    var toml = File.ReadAllText(configPath);
    AssertBefore(toml, "# Request timeout", "timeout_seconds = 15");
    AssertBefore(toml, "# Timeout in seconds", "timeout_seconds = 15");
    AssertBefore(toml, "# Range: 1..120", "timeout_seconds = 15");
    AssertBefore(toml, "# Options: compact, detailed", "output_mode = \"compact\"");
    AssertBefore(toml, "# Placeholder: compact", "output_mode = \"compact\"");
    AssertSingle(toml, "# Request timeout");
    AssertSingle(toml, "# Options: compact, detailed");

    _ = manager.LoadConfig<VerificationConfig>(configPath, "verification")
        ?? throw new InvalidOperationException("Generated TOML did not deserialize.");
    Console.WriteLine("Config comment verification passed.");

    var preservingPath = Path.Combine(tempRoot, "preserving", "config.toml");
    Directory.CreateDirectory(Path.GetDirectoryName(preservingPath)!);
    File.WriteAllText(preservingPath, """
        # preserved root comment
        timeout_seconds = 1 # preserved known inline comment
        output_mode = "legacy"
        future_flag = "keep" # unknown top-level

        [retry]
        # preserved nested comment
        count = 2
        future_nested = "keep" # unknown nested key

        [future_section]
        enabled = true # unknown section
        """);

    var saveContext = new PluginContext(
        botContext,
        "config-save",
        Path.GetDirectoryName(preservingPath)!,
        eventLogHub,
        new PluginServiceRegistry());
    var preservingConfig = new VerificationConfig
    {
        TimeoutSeconds = 42,
        OutputMode = "detailed",
        Retry = new VerificationRetryConfig { Count = 7 }
    };
    saveContext.Config.Save(preservingConfig);
    saveContext.Config.Save(preservingConfig);

    var preservedToml = File.ReadAllText(preservingPath);
    AssertContains(preservedToml, "timeout_seconds = 42 # preserved known inline comment");
    AssertContains(preservedToml, "output_mode = \"detailed\"");
    AssertContains(preservedToml, "count = 7");
    AssertContains(preservedToml, "future_flag = \"keep\" # unknown top-level");
    AssertContains(preservedToml, "future_nested = \"keep\" # unknown nested key");
    AssertContains(preservedToml, "[future_section]");
    AssertContains(preservedToml, "enabled = true # unknown section");
    AssertSingle(preservedToml, "# preserved root comment");
    AssertSingle(preservedToml, "# preserved nested comment");
    AssertSingle(preservedToml, "# preserved known inline comment");
    var reloadedPreservingConfig = saveContext.Config.Load<VerificationConfig>();
    if (reloadedPreservingConfig is not { TimeoutSeconds: 42, OutputMode: "detailed", Retry.Count: 7 })
    {
        throw new InvalidOperationException("Preserved plugin TOML did not deserialize with the saved values.");
    }
    saveContext.Dispose();
    Console.WriteLine("Plugin config preserving-save verification passed.");

    var coreConfigPath = Path.Combine(tempRoot, "core", "config.toml");
    Directory.CreateDirectory(Path.GetDirectoryName(coreConfigPath)!);
    File.WriteAllText(coreConfigPath, """
        # preserved core comment
        enable_log = true
        future_core_value = "keep"

        [api]
        enable = true
        listen_url = "http://127.0.0.1:7001"
        future_api_value = "keep"

        [future_core_section]
        value = 9
        """);
    var coreManager = new ConfigManager(coreConfigPath);
    var coreConfig = await coreManager.LoadCoreConfig();
    coreConfig.EnableLog = false;
    coreConfig.Api.ListenUrl = "http://127.0.0.1:7999";
    coreManager.SaveConfig(coreConfigPath, coreConfig);
    coreManager.SaveConfig(coreConfigPath, coreConfig);

    var preservedCoreToml = File.ReadAllText(coreConfigPath);
    AssertContains(preservedCoreToml, "enable_log = false");
    AssertContains(preservedCoreToml, "listen_url = \"http://127.0.0.1:7999\"");
    AssertContains(preservedCoreToml, "future_core_value = \"keep\"");
    AssertContains(preservedCoreToml, "future_api_value = \"keep\"");
    AssertContains(preservedCoreToml, "[future_core_section]");
    AssertContains(preservedCoreToml, "value = 9");
    AssertSingle(preservedCoreToml, "# preserved core comment");
    var reloadedCoreConfig = await coreManager.LoadCoreConfig();
    if (reloadedCoreConfig.EnableLog || reloadedCoreConfig.Api.ListenUrl != "http://127.0.0.1:7999")
    {
        throw new InvalidOperationException("Preserved core TOML did not deserialize with the saved values.");
    }
    Console.WriteLine("Core config preserving-save verification passed.");

    async Task<LoadedPluginHandle> CreatePluginHandleAsync(
        IBotPlugin plugin,
        string id,
        HostLogHub logHub,
        TimeSpan? activeDispatchDrainTimeout = null,
        TimeSpan? pluginOnUnloadTimeout = null)
    {
        var pluginDirectory = Path.Combine(tempRoot, id);
        var context = new PluginContext(botContext, id, pluginDirectory, logHub, new PluginServiceRegistry());
        await plugin.OnLoad(context);
        return new LoadedPluginHandle(
            plugin,
            context,
            new DllLoader<IBotPlugin>(),
            typeof(Program).Assembly.Location,
            new PluginProbeInfo(
                plugin.GetType().FullName!,
                id,
                id,
                "1.0.0",
                null,
                null,
                PluginCategory.Other,
                null,
                false,
                [],
                []),
            logHub,
            activeDispatchDrainTimeout: activeDispatchDrainTimeout,
            pluginOnUnloadTimeout: pluginOnUnloadTimeout);
    }
}
finally
{
    if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true);
}

static void AssertBefore(string text, string comment, string key)
{
    var commentIndex = text.IndexOf(comment, StringComparison.Ordinal);
    var keyIndex = text.IndexOf(key, StringComparison.Ordinal);
    if (commentIndex < 0 || keyIndex < 0 || commentIndex > keyIndex)
    {
        throw new InvalidOperationException($"Expected '{comment}' above '{key}'.\n{text}");
    }
}

static void AssertSingle(string text, string value)
{
    if (text.Split(value).Length - 1 != 1)
    {
        throw new InvalidOperationException($"Expected exactly one '{value}' comment.\n{text}");
    }
}

static void AssertContains(string text, string value)
{
    if (!text.Contains(value, StringComparison.Ordinal))
    {
        throw new InvalidOperationException($"Expected '{value}'.\n{text}");
    }
}

static void AssertThrows<TException>(Action action) where TException : Exception
{
    try
    {
        action();
    }
    catch (TException)
    {
        return;
    }

    throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
}

static async Task AssertThrowsAsync<TException>(Func<Task> action) where TException : Exception
{
    try
    {
        await action();
    }
    catch (TException)
    {
        return;
    }

    throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
}

static void AssertUnloadTimedOut(PluginUnloadResult result, string expectedPhase)
{
    if (result.Unloaded ||
        result.AssemblyLoadContextWeakReference is not null ||
        result.Error is not TimeoutException timeoutException ||
        !timeoutException.Message.Contains(expectedPhase, StringComparison.Ordinal) ||
        !timeoutException.Message.Contains("restarted", StringComparison.Ordinal))
    {
        throw new InvalidOperationException($"Unload did not report the expected restart-required {expectedPhase} timeout.");
    }
}

static MemberJoinedEvent CreateMemberJoinedEvent(string userId) => new()
{
    Platform = "verification",
    Channel = Channel.Group("group"),
    UserId = userId
};

async Task SendInAdapterScopeAsync(VerificationAdapter adapter, string text)
{
    using var _ = AdapterExecutionContext.Enter(adapter.Platform);
    await botContext.Message.SendMessageAsync(Channel.Group("channel"), [new TextSegment(text)]);
}

internal sealed class VerificationConfig
{
    [ConfigField("Timeout in seconds", Label = "Request timeout", Min = 1, Max = 120)]
    public int TimeoutSeconds { get; set; } = 15;

    [ConfigField("Output format", Options = ["compact", "detailed"], Placeholder = "compact")]
    public string OutputMode { get; set; } = "compact";

    public VerificationRetryConfig Retry { get; set; } = new();
}

internal sealed class VerificationRetryConfig
{
    public int Count { get; set; } = 3;
}

internal interface IVerificationService;

internal sealed class VerificationService : IVerificationService;

internal sealed class DirectSubscriberPlugin : IBotPlugin, IBotEventSubscriber
{
    public string Name => nameof(DirectSubscriberPlugin);
    public List<BotEvent> Events { get; } = [];
    public Task OnLoad(IBotContext context) => Task.CompletedTask;
    public Task OnUnload() => Task.CompletedTask;

    public Task OnEventAsync(BotEvent e)
    {
        Events.Add(e);
        return Task.CompletedTask;
    }
}

internal sealed class RoutedPlugin : PluginBase
{
    public int BaseEventCount { get; private set; }
    public int MemberJoinedCount { get; private set; }

    protected override void ConfigureRoutes()
    {
        Events.Map<BotEvent>(_ =>
        {
            BaseEventCount++;
            return Task.CompletedTask;
        });
        Events.Map<MemberJoinedEvent>(_ =>
        {
            MemberJoinedCount++;
            return Task.CompletedTask;
        });
    }
}

internal interface IBlockingDispatchPlugin
{
    Task DispatchAsync();
}

internal sealed class BlockingDispatchPlugin : IBotPlugin, IBlockingDispatchPlugin
{
    public TaskCompletionSource DispatchStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource ReleaseDispatch { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource UnloadCompleted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public string Name => nameof(BlockingDispatchPlugin);
    public Task OnLoad(IBotContext context) => Task.CompletedTask;

    public Task OnUnload()
    {
        UnloadCompleted.TrySetResult();
        return Task.CompletedTask;
    }

    public async Task DispatchAsync()
    {
        DispatchStarted.TrySetResult();
        await ReleaseDispatch.Task;
    }
}

internal sealed class BlockingUnloadPlugin : IBotPlugin
{
    public TaskCompletionSource ReleaseUnload { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource UnloadCompleted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public string Name => nameof(BlockingUnloadPlugin);
    public Task OnLoad(IBotContext context) => Task.CompletedTask;

    public async Task OnUnload()
    {
        await ReleaseUnload.Task;
        UnloadCompleted.TrySetResult();
    }
}

[BotAdapter("verification")]
internal sealed class VerificationAdapter(string platform) : IBotAdapter
{
    public VerificationMessageService MessageService { get; } = new();
    public string Platform { get; } = platform;
    public IMessageService Message => MessageService;
    public IChannelService Channel { get; } = new VerificationChannelService();
    public IUserService User { get; } = new VerificationUserService();
    public IEventService Event { get; } = new VerificationEventService();
    public IConfigContext Config { get; set; } = null!;
    public IConsoleLogger Logger { get; set; } = null!;
    public TService? GetExtension<TService>() where TService : class => this as TService;
    public Task StartAsync() => Task.CompletedTask;
    public Task StopAsync() => Task.CompletedTask;
}

internal sealed class VerificationMessageService : IMessageService
{
    public List<string> Messages { get; } = [];
    public Task<SentMessage> SendMessageAsync(Channel channel, IReadOnlyList<MessageSegment> segments)
    {
        Messages.Add(string.Concat(segments.OfType<TextSegment>().Select(segment => segment.Text)));
        return Task.FromResult(new SentMessage("sent"));
    }
}

internal sealed class VerificationChannelService : IChannelService;
internal sealed class VerificationUserService : IUserService;
internal sealed class VerificationEventService : IEventService
{
    public event Func<BotEvent, Task>? EventReceived;

    public async Task RaiseAsync(BotEvent botEvent)
    {
        if (EventReceived is null) return;

        foreach (var handler in EventReceived.GetInvocationList().Cast<Func<BotEvent, Task>>())
        {
            await handler(botEvent);
        }
    }
}
