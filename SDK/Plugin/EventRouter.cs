using ShiroBot.SDK.Models;

namespace ShiroBot.SDK.Plugin;

public sealed class EventRouter
{
    private readonly List<RouteEntry> _routes = [];

    public bool HasRoutes => _routes.Count > 0;
    public IReadOnlyCollection<Type> EventTypes => _routes.Select(route => route.Descriptor.EventType).Distinct().ToArray();
    public IReadOnlyList<EventRouteDescriptor> Routes => _routes.Select(route => route.Descriptor).ToArray();

    public void Map<TEvent>(Func<TEvent, Task> handler)
        where TEvent : BotEvent
    {
        Add(new EventRouteDescriptor(typeof(TEvent)), _ => true, handler);
    }

    public void MapWhen<TEvent>(Func<TEvent, bool> predicate, Func<TEvent, Task> handler)
        where TEvent : BotEvent
    {
        Add(new EventRouteDescriptor(typeof(TEvent)) { Filtered = true }, predicate, handler);
    }

    /// <param name="description">Shown in the Dashboard's plugin details.</param>
    public void MapWhen<TEvent>(Func<TEvent, bool> predicate, string description, Func<TEvent, Task> handler)
        where TEvent : BotEvent
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        Add(new EventRouteDescriptor(typeof(TEvent)) { Filtered = true, Description = description }, predicate, handler);
    }

    /// <summary>按 Kind 订阅平台特有事件。</summary>
    public void MapPlatform(string kind, Func<PlatformEvent, Task> handler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        Add(new EventRouteDescriptor(typeof(PlatformEvent)) { PlatformKind = kind },
            evt => string.Equals(evt.Kind, kind, StringComparison.OrdinalIgnoreCase), handler);
    }

    private void Add<TEvent>(EventRouteDescriptor descriptor, Func<TEvent, bool> predicate, Func<TEvent, Task> handler)
        where TEvent : BotEvent
    {
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentNullException.ThrowIfNull(handler);

        _routes.Add(new RouteEntry(
            descriptor,
            evt => predicate((TEvent)evt),
            evt => handler((TEvent)evt)));
    }

    public bool HasRoute<TEvent>()
        where TEvent : BotEvent
    {
        var eventType = typeof(TEvent);
        return _routes.Any(route => route.Descriptor.EventType.IsAssignableFrom(eventType));
    }

    public async Task<bool> DispatchAsync(BotEvent evt)
    {
        var matched = false;

        foreach (var route in _routes.Where(route => route.Descriptor.EventType.IsInstanceOfType(evt) && route.Predicate(evt)))
        {
            matched = true;
            await route.Handler(evt);
        }

        return matched;
    }

    public void Clear()
    {
        _routes.Clear();
    }

    private sealed record RouteEntry(
        EventRouteDescriptor Descriptor,
        Func<BotEvent, bool> Predicate,
        Func<BotEvent, Task> Handler);
}
