using System.Runtime.CompilerServices;
using ShiroBot.SDK.Models;

namespace ShiroBot.SDK.Plugin;

/// <summary>
///     Host-only dispatch view for a plugin that requires a trigger prefix. A message that carried the prefix reaches
///     the plugin with it stripped, so command routes, MapAll and the plugin's own parsing all see the same text; one
///     that did not keeps its original segments and is marked as not being a command.
/// </summary>
internal static class MessageCommandPrefix
{
    private static readonly ConditionalWeakTable<MessageEvent, CommandView> Views = new();

    internal static MessageEvent CreateDispatchView(MessageEvent original, MessageEvent? command)
    {
        var view = (command ?? original) with { };
        BotEventHandling.ShareHandlingState(original, view);
        Views.Add(view, new CommandView(command));
        return view;
    }

    internal static bool TryGetCommand(MessageEvent original, out MessageEvent command)
    {
        if (!Views.TryGetValue(original, out var view))
        {
            command = original;
            return true;
        }

        command = view.Message!;
        return view.Message is not null;
    }

    private sealed record CommandView(MessageEvent? Message);
}