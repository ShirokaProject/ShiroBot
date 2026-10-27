using System.Runtime.CompilerServices;
using ShiroBot.SDK.Models;

namespace ShiroBot.SDK.Plugin;

/// <summary>
/// Marks an event as handled so plugins with a larger <see cref="ShiroBot.SDK.Core.BotPluginAttribute.Priority"/>
/// skip it; plugins with the same priority still receive it. <see cref="PluginBase"/> marks a message itself when
/// a command route other than an unconditional <c>MapAll</c> matches.
/// </summary>
public static class BotEventHandling
{
    // Keyed by instance: events are records, so the mark must not take part in equality or `with` copies.
    private static readonly ConditionalWeakTable<BotEvent, object> Handled = new();

    public static void MarkHandled(this BotEvent e)
    {
        ArgumentNullException.ThrowIfNull(e);
        Handled.TryAdd(e, Handled);
    }

    public static bool IsHandled(this BotEvent e)
    {
        ArgumentNullException.ThrowIfNull(e);
        return Handled.TryGetValue(e, out _);
    }
}
