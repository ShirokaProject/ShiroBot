using ShiroBot.Model.Common;
using ShiroBot.SDK.Core;
using System.Reflection;

namespace ShiroBot.SDK.Plugin;

public sealed record MessageRouteDescriptor(MessageRouteMatchType MatchType, string? Pattern);
public enum MessageRouteMatchType
{
    Exact,
    Prefix,
    All
}

public abstract class PluginBase : IBotPlugin, IBotEventSubscriber
{
    private static readonly MethodInfo CreateEventDispatcherMethod =
        typeof(PluginBase).GetMethod(nameof(CreateEventDispatcher), BindingFlags.Static | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException($"Failed to locate {nameof(CreateEventDispatcher)}.");

    private static readonly IReadOnlyDictionary<Type, Func<PluginBase, Event, Task>> EventDispatchers =
        CreateEventDispatchers();

    protected IBotContext Context { get; private set; } = null!;
    protected CommandRouter<GroupIncomingMessage> GroupCommands { get; } = new();
    protected CommandRouter<FriendIncomingMessage> FriendCommands { get; } = new();
    protected AllMapCommands AllCommands { get; }
    protected EventRouter Events { get; } = new();
    private static BotEventSubscriptions Subscriptions => BotEventSubscriptions.None;
    public virtual string Name => GetType().Name;

    protected PluginBase()
    {
        AllCommands = new AllMapCommands(GroupCommands, FriendCommands);
    }

    public Task OnLoad(IBotContext context)
    {
        Context = context;
        ConfigureRoutes();
        return LoadAsync();
    }

    public async Task OnUnload()
    {
        await OnUnloadAsync();
        GroupCommands.Clear();
        FriendCommands.Clear();
        Events.Clear();
        Context = null!;
    }

    /// <summary>
    /// Registers command and event routes synchronously before asynchronous initialization.
    /// </summary>
    protected virtual void ConfigureRoutes() { }

    protected virtual Task LoadAsync() => Task.CompletedTask;
    protected virtual Task OnUnloadAsync() => Task.CompletedTask;

    protected virtual async Task OnGroupMessageAsync(GroupIncomingMessage message)
    {
        if (await BeforeDispatchGroupCommandAsync(message))
        {
            await GroupCommands.DispatchAsync(message.GetPlainText().Trim(), message);
        }
    }
    protected virtual async Task OnFriendMessageAsync(FriendIncomingMessage message)
    {
        if (await BeforeDispatchFriendCommandAsync(message))
        {
            await FriendCommands.DispatchAsync(message.GetPlainText().Trim(), message);
        }
    }

    protected virtual Task<bool> BeforeDispatchGroupCommandAsync(GroupIncomingMessage message) =>
        Task.FromResult(true);

    protected virtual Task<bool> BeforeDispatchFriendCommandAsync(FriendIncomingMessage message) =>
        Task.FromResult(true);

    async Task IBotEventSubscriber.OnEventAsync(Event e)
    {
        if (EventDispatchers.TryGetValue(e.GetType(), out var dispatcher))
        {
            await dispatcher(this, e);
        }

        await Events.DispatchAsync(e);
    }
    public IReadOnlyList<MessageRouteDescriptor> GetGroupMessageRoutes() => GroupCommands.Routes;
    public IReadOnlyList<MessageRouteDescriptor> GetFriendMessageRoutes() => FriendCommands.Routes;
    public bool RequiresGroupMessageBroadcast() =>
        Overrides<GroupIncomingMessage>(GetType(), nameof(OnGroupMessageAsync)) ||
        Events.HasRoute<GroupIncomingMessage>();
    public bool RequiresFriendMessageBroadcast() =>
        Overrides<FriendIncomingMessage>(GetType(), nameof(OnFriendMessageAsync)) ||
        Events.HasRoute<FriendIncomingMessage>();

    public BotEventSubscriptions GetEffectiveSubscriptions()
    {
        var subscriptions = Subscriptions;

        if (FriendCommands.HasRoutes)
        {
            subscriptions |= BotEventSubscriptions.FriendMessage;
        }

        if (GroupCommands.HasRoutes)
        {
            subscriptions |= BotEventSubscriptions.GroupMessage;
        }

        subscriptions |= InferOverriddenEventSubscriptions();
        subscriptions |= InferMappedEventSubscriptions();

        return subscriptions;
    }

    private BotEventSubscriptions InferMappedEventSubscriptions()
    {
        var subscriptions = BotEventSubscriptions.None;

        foreach (var eventType in Events.EventTypes)
        {
            subscriptions |= GetSubscriptionForEvent(eventType);
        }

        return subscriptions;
    }

    private BotEventSubscriptions InferOverriddenEventSubscriptions()
    {
        var subscriptions = BotEventSubscriptions.None;
        var runtimeType = GetType();

        if (Overrides<GroupIncomingMessage>(runtimeType, nameof(OnGroupMessageAsync)))
        {
            subscriptions |= BotEventSubscriptions.GroupMessage;
        }

        if (Overrides<FriendIncomingMessage>(runtimeType, nameof(OnFriendMessageAsync)))
        {
            subscriptions |= BotEventSubscriptions.FriendMessage;
        }

        return subscriptions;
    }

    private static BotEventSubscriptions GetSubscriptionForEvent(Type eventType)
    {
        if (eventType == typeof(GroupIncomingMessage)) return BotEventSubscriptions.GroupMessage;
        if (eventType == typeof(FriendIncomingMessage)) return BotEventSubscriptions.FriendMessage;

        var name = eventType.Name.EndsWith("Event", StringComparison.Ordinal)
            ? eventType.Name[..^"Event".Length]
            : eventType.Name;

        return Enum.TryParse<BotEventSubscriptions>(name, out var subscription)
            ? subscription
            : BotEventSubscriptions.None;
    }

    private static bool Overrides<TEvent>(Type runtimeType, string methodName)
    {
        var method = runtimeType.GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            types: [typeof(TEvent)],
            modifiers: null);

        return method is not null && method.DeclaringType != method.GetBaseDefinition().DeclaringType;
    }

    private static IReadOnlyDictionary<Type, Func<PluginBase, Event, Task>> CreateEventDispatchers()
    {
        var dispatchers = new Dictionary<Type, Func<PluginBase, Event, Task>>();

        foreach (var method in typeof(PluginBase).GetMethods(BindingFlags.Instance | BindingFlags.NonPublic))
        {
            if (!method.Name.StartsWith("On", StringComparison.Ordinal) ||
                !method.Name.EndsWith("Async", StringComparison.Ordinal) ||
                method.ReturnType != typeof(Task))
            {
                continue;
            }

            var parameters = method.GetParameters();
            if (parameters.Length != 1 || !typeof(Event).IsAssignableFrom(parameters[0].ParameterType))
            {
                continue;
            }

            var eventType = parameters[0].ParameterType;
            dispatchers[eventType] = (Func<PluginBase, Event, Task>)CreateEventDispatcherMethod
                .MakeGenericMethod(eventType)
                .Invoke(null, [method])!;
        }

        return dispatchers;
    }

    private static Func<PluginBase, Event, Task> CreateEventDispatcher<TEvent>(MethodInfo method)
        where TEvent : Event
    {
        var handler = (Func<PluginBase, TEvent, Task>)Delegate.CreateDelegate(
            typeof(Func<PluginBase, TEvent, Task>),
            method);

        return (plugin, e) => handler(plugin, (TEvent)e);
    }
}
