using ShiroBot.SDK.Adapter;
using ShiroBot.SDK.Models;
using ShiroBot.SDK.Plugin;

namespace ShiroBot.Hosting.Context;

internal sealed class MessageContext(
    Func<IMessageService> getMessageService,
    Func<string> getPlatform,
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

    public Task DeleteMessageAsync(Channel channel, string messageId) =>
        getMessageService().DeleteMessageAsync(channel, messageId);

    public Task<MessageEvent?> GetMessageAsync(Channel channel, string messageId) =>
        getMessageService().GetMessageAsync(channel, messageId);

    public Task<IReadOnlyList<MessageEvent>> GetHistoryMessagesAsync(Channel channel, string? beforeMessageId = null, int limit = 20) =>
        getMessageService().GetHistoryMessagesAsync(channel, beforeMessageId, limit);

    public Task<string> GetResourceUrlAsync(string resourceId) =>
        getMessageService().GetResourceUrlAsync(resourceId);
}
