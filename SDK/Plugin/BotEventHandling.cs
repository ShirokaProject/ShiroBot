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
    private static readonly ConditionalWeakTable<BotEvent, SharedMark> Shared = new();

    private sealed class SharedMark { internal bool IsHandled; }

    // A host-created prefix-stripped view represents the same incoming event. Ordinary
    // record `with` copies still have independent handling state, as before.
    internal static void ShareHandlingState(BotEvent source, BotEvent view)
    {
        // Share only the mark: retaining a view must not retain the original event/segments.
        var mark = Shared.GetValue(source, static _ => new SharedMark());
        if (Handled.TryGetValue(source, out _)) Volatile.Write(ref mark.IsHandled, true);
        Shared.Add(view, mark);
    }

    public static void MarkHandled(this BotEvent e)
    {
        ArgumentNullException.ThrowIfNull(e);
        Handled.TryAdd(e, Handled);
        if (Shared.TryGetValue(e, out var mark)) Volatile.Write(ref mark.IsHandled, true);
    }

    public static bool IsHandled(this BotEvent e)
    {
        ArgumentNullException.ThrowIfNull(e);
        return Handled.TryGetValue(e, out _) ||
               (Shared.TryGetValue(e, out var mark) && Volatile.Read(ref mark.IsHandled));
    }
}
