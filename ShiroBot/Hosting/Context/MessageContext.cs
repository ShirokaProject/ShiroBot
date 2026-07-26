using ShiroBot.SDK.Adapter;
using ShiroBot.SDK.Models;
using ShiroBot.SDK.Plugin;

namespace ShiroBot.Hosting.Context;

internal sealed class MessageContext(IMessageService message, ReplySubscriptionManager replySubscriptions, string ownerId) : IMessageContext
{
    public IReplySubscription SubscribeReply(
        string messageId,
        TimeSpan duration,
        ReplyMessageHandler handler,
        bool disposeOnReply = true) =>
        replySubscriptions.Subscribe(ownerId, messageId, duration, handler, disposeOnReply);

    public Task<SentMessage> SendMessageAsync(Channel channel, IReadOnlyList<MessageSegment> segments) =>
        message.SendMessageAsync(channel, segments);

    public Task DeleteMessageAsync(Channel channel, string messageId) =>
        message.DeleteMessageAsync(channel, messageId);

    public Task<MessageEvent?> GetMessageAsync(Channel channel, string messageId) =>
        message.GetMessageAsync(channel, messageId);

    public Task<IReadOnlyList<MessageEvent>> GetHistoryMessagesAsync(Channel channel, string? beforeMessageId = null, int limit = 20) =>
        message.GetHistoryMessagesAsync(channel, beforeMessageId, limit);

    public Task<string> GetResourceUrlAsync(string resourceId) =>
        message.GetResourceUrlAsync(resourceId);
}
