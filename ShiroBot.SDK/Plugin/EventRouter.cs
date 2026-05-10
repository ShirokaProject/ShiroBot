using ShiroBot.Model.Common;

namespace ShiroBot.SDK.Plugin;

public sealed class EventRouter
{
    private readonly List<RouteEntry> _routes = [];

    public bool HasRoutes => _routes.Count > 0;
    public IReadOnlyCollection<Type> EventTypes => _routes.Select(route => route.EventType).Distinct().ToArray();

    public void Map<TEvent>(Func<TEvent, Task> handler)
        where TEvent : Event
    {
        MapWhen<TEvent>(_ => true, handler);
    }

    public void MapWhen<TEvent>(Func<TEvent, bool> predicate, Func<TEvent, Task> handler)
        where TEvent : Event
    {
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentNullException.ThrowIfNull(handler);

        _routes.Add(new RouteEntry(
            typeof(TEvent),
            evt => predicate((TEvent)evt),
            evt => handler((TEvent)evt)));
    }

    public bool HasRoute<TEvent>()
        where TEvent : Event
    {
        var eventType = typeof(TEvent);
        return _routes.Any(route => route.EventType == eventType);
    }

    public async Task<bool> DispatchAsync(Event evt)
    {
        var matched = false;

        foreach (var route in _routes.Where(route => route.EventType == evt.GetType() && route.Predicate(evt)))
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
        Type EventType,
        Func<Event, bool> Predicate,
        Func<Event, Task> Handler);
}
