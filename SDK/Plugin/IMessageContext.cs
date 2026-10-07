using ShiroBot.SDK.Adapter;
using ShiroBot.SDK.Models;

namespace ShiroBot.SDK.Plugin;

public interface IMessageContext : IMessageService
{
    IReplySubscription SubscribeReply(
        MessageReference message,
        TimeSpan duration,
        ReplyMessageHandler handler,
        bool disposeOnReply = true);

    IReplySubscription SubscribeReply(
        MessageReference message,
        string text,
        TimeSpan duration,
        ReplyMessageHandler handler,
        bool disposeOnReply = true)
    {
        IReplySubscription? subscription = null;
        // ReSharper disable once AccessToModifiedClosure
        subscription = SubscribeReply(message, duration, async reply =>
        {
            if (reply.Segments.OfType<TextSegment>().All(segment => segment.Text != text))
            {
                return;
            }

            try
            {
                await handler(reply);
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

    MessageCapabilities GetMessageCapabilities(ChannelReference channel);
    MessageSendAssessment AssessMessage(ChannelReference channel, OutgoingMessage message);
    Task<SentMessage> SendMessageAsync(ChannelReference channel, OutgoingMessage message, CancellationToken cancellationToken = default);
    Task<SentMessage> ReplyAsync(MessageEvent message, OutgoingMessage content, CancellationToken cancellationToken = default);
    Task<SentMessage> ReplyAsync(InteractionEvent interaction, OutgoingMessage content, CancellationToken cancellationToken = default);

    Task<SentMessage> SendMessageAsync(ChannelReference channel, IReadOnlyList<MessageSegment> segments, CancellationToken cancellationToken = default);
    Task DeleteMessageAsync(MessageReference message, CancellationToken cancellationToken = default);
    Task<MessageEvent?> GetMessageAsync(MessageReference message, CancellationToken cancellationToken = default);

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

    Task<SentMessage> SendDirectMessageAsync(string userId, IReadOnlyList<MessageSegment> segments, CancellationToken cancellationToken = default) =>
        SendMessageAsync(Channel.Direct(userId), segments, cancellationToken);

    Task<SentMessage> SendGroupMessageAsync(string groupId, IReadOnlyList<MessageSegment> segments, CancellationToken cancellationToken = default) =>
        SendMessageAsync(Channel.Group(groupId), segments, cancellationToken);

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

    Task<SentMessage> ReplyAsync(MessageEvent message, IReadOnlyList<MessageSegment> segments, CancellationToken cancellationToken = default);

    Task<SentMessage> QuoteReplyAsync(MessageEvent message, IReadOnlyList<MessageSegment> segments, CancellationToken cancellationToken = default);

    // ─── 其他 ───

    Task DeleteMessageAsync(MessageEvent message, CancellationToken cancellationToken = default) =>
        DeleteMessageAsync(message.Reference, cancellationToken);

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
