using ShiroBot.SDK.Models;
using ShiroBot.SDK.Plugin;

namespace ShiroBot.Hosting.Events;

internal static class MessageTriggerPrefix
{
    internal static MessageEvent? Strip(MessageEvent message, string prefix)
    {
        var text = message.GetPlainText();
        var trimmed = text.AsSpan().TrimStart();
        if (!trimmed.StartsWith(prefix, StringComparison.Ordinal)) return null;
        var consumed = text.Length - trimmed.Length + prefix.Length;
        consumed += text.AsSpan(consumed).Length - text.AsSpan(consumed).TrimStart().Length;
        var segments = new List<MessageSegment>(message.Segments.Count);
        foreach (var segment in message.Segments)
        {
            if (segment is TextSegment part && consumed > 0)
            {
                var take = Math.Min(consumed, part.Text.Length);
                consumed -= take;
                if (take < part.Text.Length) segments.Add(part with { Text = part.Text[take..] });
            }
            else segments.Add(segment);
        }
        var stripped = message with { Segments = segments.ToArray() };
        BotEventHandling.ShareHandlingState(message, stripped);
        return stripped;
    }
}
