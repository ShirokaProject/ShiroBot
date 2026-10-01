using ShiroBot.SDK.Adapter;
using ShiroBot.SDK.Models;
using ShiroBot.SDK.Plugin;
using ShiroBot.Hosting.Events;

namespace ShiroBot.Hosting.Context;

internal sealed class MessageContext(
    Func<IMessageService> getMessageService,
    Func<string> getPlatform,
    Func<string, IDisposable> usePlatform,
    ReplySubscriptionManager replySubscriptions,
    string ownerId) : IMessageContext
{
    public IReplySubscription SubscribeReply(
        string messageId,
        TimeSpan duration,
        ReplyMessageHandler handler,
        bool disposeOnReply = true) =>
        replySubscriptions.Subscribe(ownerId, getPlatform(), messageId, duration, handler, disposeOnReply);

    public Task<SentMessage> SendMessageAsync(Channel channel, IReadOnlyList<MessageSegment> segments) =>
        getMessageService().SendMessageAsync(channel, segments);

    public Task<SentMessage> ReplyAsync(MessageEvent message, params MessageSegment[] segments) =>
        SendReplyAsync(message, segments);

    public Task<SentMessage> ReplyAsync(MessageEvent message, string text, params MessageSegment[] additionalSegments) =>
        SendReplyAsync(message, [new TextSegment(text), .. additionalSegments]);

    public Task<SentMessage> QuoteReplyAsync(MessageEvent message, params MessageSegment[] segments) =>
        SendReplyAsync(message, [new QuoteSegment(message.MessageId), .. segments]);

    public Task<SentMessage> QuoteReplyAsync(MessageEvent message, string text, params MessageSegment[] segments) =>
        SendReplyAsync(message, [new QuoteSegment(message.MessageId), new TextSegment(text), .. segments]);

    private async Task<SentMessage> SendReplyAsync(MessageEvent message, IReadOnlyList<MessageSegment> segments)
    {
        ArgumentNullException.ThrowIfNull(message);
        using var scope = usePlatform(message.Platform);
        return await getMessageService().SendMessageAsync(message.Channel, segments).ConfigureAwait(false);
    }

    public Task DeleteMessageAsync(Channel channel, string messageId) =>
        getMessageService().DeleteMessageAsync(channel, messageId);

    public Task<MessageEvent?> GetMessageAsync(Channel channel, string messageId) =>
        getMessageService().GetMessageAsync(channel, messageId);

    public Task<IReadOnlyList<MessageEvent>> GetHistoryMessagesAsync(Channel channel, string? beforeMessageId = null, int limit = 20) =>
        getMessageService().GetHistoryMessagesAsync(channel, beforeMessageId, limit);

    public Task<string> GetResourceUrlAsync(string resourceId) =>
        getMessageService().GetResourceUrlAsync(resourceId);
}
