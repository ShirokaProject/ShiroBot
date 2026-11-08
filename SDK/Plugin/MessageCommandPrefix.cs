using System.Runtime.CompilerServices;
using ShiroBot.SDK.Models;

namespace ShiroBot.SDK.Plugin;

/// <summary>Host-only command view; observers retain the original message and segments.</summary>
internal static class MessageCommandPrefix
{
    private sealed record CommandView(MessageEvent? Message);
    private static readonly ConditionalWeakTable<MessageEvent, CommandView> Views = new();

    internal static MessageEvent CreateDispatchView(MessageEvent original, MessageEvent? command)
    {
        var view = original with { };
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
}
