using System.Reflection;
using ShiroBot.SDK.Config;
using ShiroBot.SDK.Core;
using ShiroBot.SDK.Models;

namespace ShiroBot.SDK.Plugin;

public sealed record MessageRouteDescriptor(MessageRouteMatchType MatchType, string? Pattern)
{
    /// <summary>What an <see cref="MessageRouteMatchType.All" /> route filters on; dispatch does not use it.</summary>
    public MessageRouteCondition Condition { get; init; }

    /// <summary>The mentioned user ID or replied message ID the condition is limited to, if any.</summary>
    public string? ConditionArgument { get; init; }

    /// <summary>Plugin-supplied text shown for the route, e.g. for a <see cref="MessageRouteCondition.Custom" /> predicate.</summary>
    public string? Description { get; init; }
}

public enum MessageRouteMatchType
{
    Exact = 0,
    Prefix = 1,
    All = 2
}

/// <summary>Describes the filter of a route that receives every message, for display in the Dashboard.</summary>
public enum MessageRouteCondition
{
    /// <summary>No filter, or an exact/prefix route.</summary>
    None = 0,

    /// <summary>A predicate the host cannot describe; see <see cref="MessageRouteDescriptor.Description" />.</summary>
    Custom = 1,
    Mention = 2,
    MentionAll = 3,
    Reply = 4
}

/// <summary>An event subscription registered through <see cref="EventRouter" />.</summary>
public sealed record EventRouteDescriptor(Type EventType)
{
    /// <summary>The <see cref="ShiroBot.SDK.Models.PlatformEvent.Kind" /> of a <see cref="EventRouter.MapPlatform" /> route.</summary>
    public string? PlatformKind { get; init; }

    /// <summary>True when the route only handles events matching a predicate.</summary>
    public bool Filtered { get; init; }

    /// <summary>Plugin-supplied text shown for the route.</summary>
    public string? Description { get; init; }
}

public abstract class PluginBase : IBotPlugin, IBotEventSubscriber
{
    protected PluginBase()
    {
        AllCommands = new AllMapCommands(GroupCommands, DirectCommands);
    }

    protected IBotContext Context { get; private set; } = null!;

    /// <summary>群聊 / 频道消息命令路由。</summary>
    protected CommandRouter<MessageEvent> GroupCommands { get; } = new();

    /// <summary>私聊消息命令路由。</summary>
    protected CommandRouter<MessageEvent> DirectCommands { get; } = new();

    protected AllMapCommands AllCommands { get; }
    protected EventRouter Events { get; } = new();

    async Task IBotEventSubscriber.OnEventAsync(BotEvent e)
    {
        if (e is MessageEvent message)
        {
            if (message.IsDirect)
                await OnDirectMessageAsync(message);
            else
                await OnGroupMessageAsync(message);
        }

        await Events.DispatchAsync(e);
    }

    public virtual string Name => GetType().Name;

    public async Task OnLoad(IBotContext context)
    {
        Context = context;
        if (this is IConfigurableComponent configurable)
            await configurable.InitializeConfigAsync(context.Config).ConfigureAwait(false);
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
    ///     Registers command and event routes synchronously before asynchronous initialization.
    /// </summary>
    protected virtual void ConfigureRoutes()
    {
    }

    protected virtual Task LoadAsync()
    {
        return Task.CompletedTask;
    }

    protected virtual Task OnUnloadAsync()
    {
        return Task.CompletedTask;
    }

    protected virtual async Task OnGroupMessageAsync(MessageEvent message)
    {
        if (await BeforeDispatchGroupCommandAsync(message))
            await GroupCommands.DispatchAsync(message.GetPlainText().Trim(), message,
                route => MarkIfCommand(message, route));
    }

    protected virtual async Task OnDirectMessageAsync(MessageEvent message)
    {
        if (await BeforeDispatchDirectCommandAsync(message))
            await DirectCommands.DispatchAsync(message.GetPlainText().Trim(), message,
                route => MarkIfCommand(message, route));
    }

    // A matched command answers the message; an unconditional MapAll only observes it, e.g. for logging.
    private static void MarkIfCommand(MessageEvent message, MessageRouteDescriptor route)
    {
        if (route.MatchType != MessageRouteMatchType.All || route.Condition != MessageRouteCondition.None)
            message.MarkHandled();
    }

    protected virtual Task<bool> BeforeDispatchGroupCommandAsync(MessageEvent message)
    {
        return Task.FromResult(true);
    }

    protected virtual Task<bool> BeforeDispatchDirectCommandAsync(MessageEvent message)
    {
        return Task.FromResult(true);
    }

    public IReadOnlyList<MessageRouteDescriptor> GetGroupMessageRoutes()
    {
        return GroupCommands.Routes;
    }

    public IReadOnlyList<MessageRouteDescriptor> GetDirectMessageRoutes()
    {
        return DirectCommands.Routes;
    }

    public IReadOnlyList<EventRouteDescriptor> GetEventRoutes()
    {
        return Events.Routes;
    }

    public bool RequiresGroupMessageBroadcast()
    {
        return Overrides(GetType(), nameof(OnGroupMessageAsync)) ||
               Events.HasRoute<MessageEvent>();
    }

    public bool RequiresDirectMessageBroadcast()
    {
        return Overrides(GetType(), nameof(OnDirectMessageAsync)) ||
               Events.HasRoute<MessageEvent>();
    }

    public IReadOnlyCollection<Type> GetEffectiveEventTypes()
    {
        var eventTypes = Events.EventTypes.ToHashSet();
        if (GroupCommands.HasRoutes || DirectCommands.HasRoutes ||
            RequiresGroupMessageBroadcast() || RequiresDirectMessageBroadcast())
            eventTypes.Add(typeof(MessageEvent));

        return eventTypes;
    }

    private static bool Overrides(Type runtimeType, string methodName)
    {
        var method = runtimeType.GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.NonPublic,
            null,
            [typeof(MessageEvent)],
            null);

        return method is not null && method.DeclaringType != method.GetBaseDefinition().DeclaringType;
    }
}

/// <summary>
///     Plugin base with an explicit configuration model. The host context loads this model before
///     the plugin's LoadAsync hook; the host owns change watching and dispatch.
/// </summary>
public abstract class PluginBase<TConfig> : PluginBase, IConfigurableComponent<TConfig>
    where TConfig : class, new()
{
    protected TConfig Settings { get; private set; } = new();

    Type IConfigurableComponent.ConfigType => typeof(TConfig);

    object IConfigurableComponent.CurrentConfig => Settings;

    TConfig IConfigurableComponent<TConfig>.CurrentConfigValue => Settings;

    async Task<object> IConfigurableComponent.InitializeConfigAsync(IConfigContext context,
        CancellationToken cancellationToken)
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

    // The protected plugin hook is invoked by the explicit configuration initialization implementation.
    // ReSharper disable once MemberHidesInterfaceMemberWithDefaultImplementation
    protected virtual Task OnConfigLoadedAsync(TConfig config, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    protected virtual Task OnConfigChangedAsync(TConfig previous, TConfig current, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}