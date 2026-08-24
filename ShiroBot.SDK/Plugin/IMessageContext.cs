using ShiroBot.SDK.Adapter;
using ShiroBot.SDK.Models;

namespace ShiroBot.SDK.Plugin;

public interface IMessageContext : IMessageService
{
    IReplySubscription SubscribeReply(
        string messageId,
        TimeSpan duration,
        ReplyMessageHandler handler,
        bool disposeOnReply = true);

    IReplySubscription SubscribeReply(
        string messageId,
        string text,
        TimeSpan duration,
        ReplyMessageHandler handler,
        bool disposeOnReply = true)
    {
        IReplySubscription? subscription = null;
        // ReSharper disable once AccessToModifiedClosure
        subscription = SubscribeReply(messageId, duration, async message =>
        {
            if (message.Segments.OfType<TextSegment>().All(segment => segment.Text != text))
            {
                return;
            }

            try
            {
                await handler(message);
            }
            finally
            {
                if (disposeOnReply)
                {
                    subscription?.Dispose();
                }
            }
        }, disposeOnReply: false);

        return subscription;
    }

    // ─── 发送 ───

    Task<SentMessage> SendMessageAsync(Channel channel, params MessageSegment[] segments) =>
        SendMessageAsync(channel, (IReadOnlyList<MessageSegment>)segments);

    Task<SentMessage> SendMessageAsync(Channel channel, string text, params MessageSegment[] additionalSegments) =>
        SendMessageAsync(channel, BuildSegments(text, additionalSegments));

    Task<SentMessage> SendDirectMessageAsync(string userId, params MessageSegment[] segments) =>
        SendMessageAsync(Channel.Direct(userId), segments);

    Task<SentMessage> SendDirectMessageAsync(string userId, string text, params MessageSegment[] additionalSegments) =>
        SendMessageAsync(Channel.Direct(userId), BuildSegments(text, additionalSegments));

    Task<SentMessage> SendGroupMessageAsync(string groupId, params MessageSegment[] segments) =>
        SendMessageAsync(Channel.Group(groupId), segments);

    Task<SentMessage> SendGroupMessageAsync(string groupId, string text, params MessageSegment[] additionalSegments) =>
        SendMessageAsync(Channel.Group(groupId), BuildSegments(text, additionalSegments));

    // ─── 回复 ───

    Task<SentMessage> ReplyAsync(MessageEvent message, params MessageSegment[] segments) =>
        SendMessageAsync(message.Channel, segments);

    Task<SentMessage> ReplyAsync(MessageEvent message, string text, params MessageSegment[] additionalSegments) =>
        SendMessageAsync(message.Channel, BuildSegments(text, additionalSegments));

    Task<SentMessage> QuoteReplyAsync(MessageEvent message, params MessageSegment[] segments) =>
        SendMessageAsync(
            message.Channel,
            segments.Prepend(new QuoteSegment(message.MessageId)).ToArray());

    Task<SentMessage> QuoteReplyAsync(MessageEvent message, string text, params MessageSegment[] segments) =>
        QuoteReplyAsync(message, segments.Prepend(new TextSegment(text)).ToArray());

    // ─── 其他 ───

    Task DeleteMessageAsync(MessageEvent message) =>
        DeleteMessageAsync(message.Channel, message.MessageId);

    private static MessageSegment[] BuildSegments(string text, IReadOnlyList<MessageSegment> additionalSegments)
    {
        var segments = new MessageSegment[additionalSegments.Count + 1];
        segments[0] = new TextSegment(text);

        for (var i = 0; i < additionalSegments.Count; i++)
        {
            segments[i + 1] = additionalSegments[i];
        }

        return segments;
    }
}
