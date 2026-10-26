namespace ShiroBot.SDK.Plugin;

public sealed class CommandRouter<TMessage>(StringComparison comparison = StringComparison.OrdinalIgnoreCase)
{
    private readonly List<RouteEntry> _routes = [];
    public bool HasRoutes => _routes.Count > 0;
    public IReadOnlyList<MessageRouteDescriptor> Routes => _routes.Select(route => route.Descriptor).ToArray();

    public void Map(string prefix, Func<TMessage, Task> handler)
    {
        MapPrefix(prefix, handler);
    }

    public void MapExact(string command, Func<TMessage, Task> handler)
    {
        AddRoute(MessageRouteMatchType.Exact, command, handler);
    }

    public void MapPrefix(string prefix, Func<TMessage, Task> handler)
    {
        AddRoute(MessageRouteMatchType.Prefix, prefix, handler);
    }

    public void MapAll(Func<TMessage, Task> handler)
    {
        MapCondition(MessageRouteCondition.None, null, null, _ => true, handler);
    }

    public void MapWhen(Func<TMessage, bool> predicate, Func<TMessage, Task> handler)
    {
        MapCondition(MessageRouteCondition.Custom, null, null, predicate, handler);
    }

    /// <param name="description">Shown in the Dashboard's plugin details, e.g. "包含关键词 天气".</param>
    public void MapWhen(Func<TMessage, bool> predicate, string description, Func<TMessage, Task> handler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        MapCondition(MessageRouteCondition.Custom, null, description, predicate, handler);
    }

    internal void MapCondition(
        MessageRouteCondition condition,
        string? argument,
        string? description,
        Func<TMessage, bool> predicate,
        Func<TMessage, Task> handler)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        AddRoute(MessageRouteMatchType.All, null, handler, predicate, condition, argument, description);
    }

    private void AddRoute(
        MessageRouteMatchType matchType,
        string? pattern,
        Func<TMessage, Task> handler,
        Func<TMessage, bool>? predicate = null,
        MessageRouteCondition condition = MessageRouteCondition.None,
        string? argument = null,
        string? description = null)
    {
        ArgumentNullException.ThrowIfNull(handler);

        if (matchType != MessageRouteMatchType.All)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(pattern);
        }

        var descriptor = new MessageRouteDescriptor(matchType, pattern)
        {
            Condition = condition,
            ConditionArgument = argument,
            Description = description
        };
        _routes.Add(new RouteEntry(descriptor, handler, predicate));
    }

    public async Task<bool> DispatchAsync(string text, TMessage message)
    {
        foreach (var route in _routes.Where(route => Matches(route, text, message)))
        {
            await route.Handler(message);
            return true;
        }

        return false;
    }

    public void Clear()
    {
        _routes.Clear();
    }

    private bool Matches(RouteEntry route, string text, TMessage message)
    {
        if (route.Predicate is not null && !route.Predicate(message))
        {
            return false;
        }

        return route.Descriptor.MatchType switch
        {
            MessageRouteMatchType.All => true,
            MessageRouteMatchType.Exact => string.Equals(text, route.Descriptor.Pattern, comparison),
            MessageRouteMatchType.Prefix => text.StartsWith(route.Descriptor.Pattern!, comparison),
            _ => false
        };
    }

    private sealed record RouteEntry(
        MessageRouteDescriptor Descriptor,
        Func<TMessage, Task> Handler,
        Func<TMessage, bool>? Predicate);
}
