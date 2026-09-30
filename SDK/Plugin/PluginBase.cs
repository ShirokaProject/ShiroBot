using ShiroBot.SDK.Core;
using ShiroBot.SDK.Models;
using ShiroBot.SDK.Config;
using System.Reflection;

namespace ShiroBot.SDK.Plugin;

public sealed record MessageRouteDescriptor(MessageRouteMatchType MatchType, string? Pattern);
public enum MessageRouteMatchType
{
    Exact = 0,
    Prefix = 1,
    All = 2
}

public abstract class PluginBase : IBotPlugin, IBotEventSubscriber
{
    protected IBotContext Context { get; private set; } = null!;

    /// <summary>群聊 / 频道消息命令路由。</summary>
    protected CommandRouter<MessageEvent> GroupCommands { get; } = new();

    /// <summary>私聊消息命令路由。</summary>
    protected CommandRouter<MessageEvent> DirectCommands { get; } = new();

    protected AllMapCommands AllCommands { get; }
    protected EventRouter Events { get; } = new();
    public virtual string Name => GetType().Name;

    protected PluginBase()
    {
        AllCommands = new AllMapCommands(GroupCommands, DirectCommands);
    }

    public async Task OnLoad(IBotContext context)
    {
        Context = context;
        if (this is IConfigurableComponent configurable)
        {
            await configurable.InitializeConfigAsync(context.Config).ConfigureAwait(false);
        }
        ConfigureRoutes();
        await LoadAsync().ConfigureAwait(false);
    }

    public async Task OnUnload()
    {
        await OnUnloadAsync();
        GroupCommands.Clear();
        DirectCommands.Clear();
        Events.Clear();
        Context = null!;
    }

    /// <summary>
    /// Registers command and event routes synchronously before asynchronous initialization.
    /// </summary>
    protected virtual void ConfigureRoutes() { }

    protected virtual Task LoadAsync() => Task.CompletedTask;
    protected virtual Task OnUnloadAsync() => Task.CompletedTask;

    protected virtual async Task OnGroupMessageAsync(MessageEvent message)
    {
        if (await BeforeDispatchGroupCommandAsync(message))
        {
            await GroupCommands.DispatchAsync(message.GetPlainText().Trim(), message);
        }
    }

    protected virtual async Task OnDirectMessageAsync(MessageEvent message)
    {
        if (await BeforeDispatchDirectCommandAsync(message))
        {
            await DirectCommands.DispatchAsync(message.GetPlainText().Trim(), message);
        }
    }

    protected virtual Task<bool> BeforeDispatchGroupCommandAsync(MessageEvent message) =>
        Task.FromResult(true);

    protected virtual Task<bool> BeforeDispatchDirectCommandAsync(MessageEvent message) =>
        Task.FromResult(true);

    async Task IBotEventSubscriber.OnEventAsync(BotEvent e)
    {
        if (e is MessageEvent message)
        {
            if (message.IsDirect)
            {
                await OnDirectMessageAsync(message);
            }
            else
            {
                await OnGroupMessageAsync(message);
            }
        }

        await Events.DispatchAsync(e);
    }

    public IReadOnlyList<MessageRouteDescriptor> GetGroupMessageRoutes() => GroupCommands.Routes;
    public IReadOnlyList<MessageRouteDescriptor> GetDirectMessageRoutes() => DirectCommands.Routes;

    public bool RequiresGroupMessageBroadcast() =>
        Overrides(GetType(), nameof(OnGroupMessageAsync)) ||
        Events.HasRoute<MessageEvent>();

    public bool RequiresDirectMessageBroadcast() =>
        Overrides(GetType(), nameof(OnDirectMessageAsync)) ||
        Events.HasRoute<MessageEvent>();

    public IReadOnlyCollection<Type> GetEffectiveEventTypes()
    {
        var eventTypes = Events.EventTypes.ToHashSet();
        if (GroupCommands.HasRoutes || DirectCommands.HasRoutes ||
            RequiresGroupMessageBroadcast() || RequiresDirectMessageBroadcast())
        {
            eventTypes.Add(typeof(MessageEvent));
        }

        return eventTypes;
    }

    private static bool Overrides(Type runtimeType, string methodName)
    {
        var method = runtimeType.GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            types: [typeof(MessageEvent)],
            modifiers: null);

        return method is not null && method.DeclaringType != method.GetBaseDefinition().DeclaringType;
    }
}

/// <summary>
/// Plugin base with an explicit configuration model. The host context loads this model before
/// the plugin's LoadAsync hook; the host owns change watching and dispatch.
/// </summary>
public abstract class PluginBase<TConfig> : PluginBase, IConfigurableComponent<TConfig>
    where TConfig : class, new()
{
    protected TConfig Settings { get; private set; } = new();

    protected virtual Task OnConfigLoadedAsync(TConfig config, CancellationToken cancellationToken) => Task.CompletedTask;

    protected virtual Task OnConfigChangedAsync(TConfig previous, TConfig current, CancellationToken cancellationToken) => Task.CompletedTask;

    Type IConfigurableComponent.ConfigType => typeof(TConfig);

    object? IConfigurableComponent.CurrentConfig => Settings;

    TConfig IConfigurableComponent<TConfig>.CurrentConfigValue => Settings;

    async Task<object> IConfigurableComponent.InitializeConfigAsync(IConfigContext context, CancellationToken cancellationToken)
    {
        Settings = context.Load<TConfig>();
        await OnConfigLoadedAsync(Settings, cancellationToken).ConfigureAwait(false);
        return Settings;
    }

    async Task IConfigurableComponent<TConfig>.OnConfigChangedAsync(
        TConfig previous,
        TConfig current,
        CancellationToken cancellationToken)
    {
        await OnConfigChangedAsync(previous, current, cancellationToken).ConfigureAwait(false);
        Settings = current;
    }
}
