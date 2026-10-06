using ShiroBot.SDK.Adapter;
using ShiroBot.SDK.Models;
using ShiroBot.SDK.Plugin;
using ShiroBot.Hosting.Events;
using ShiroBot.Hosting.Logging;

namespace ShiroBot.Hosting.Context;

internal sealed class MessageContext(
    Func<IMessageService> getMessageService,
    Func<string> getPlatform,
    Func<string?> getAdapterId,
    Func<MessageEvent, IDisposable> useMessageSource,
    ReplySubscriptionManager replySubscriptions,
    string ownerId,
    HostLogHub? logHub = null) : IMessageContext
{
    public IReplySubscription SubscribeReply(
        string messageId,
        TimeSpan duration,
        ReplyMessageHandler handler,
        bool disposeOnReply = true) =>
        replySubscriptions.Subscribe(ownerId, getPlatform(), messageId, duration, handler, disposeOnReply, getAdapterId());

    public async Task<SentMessage> SendMessageAsync(Channel channel, IReadOnlyList<MessageSegment> segments)
    {
        ArgumentNullException.ThrowIfNull(channel);
        ArgumentNullException.ThrowIfNull(segments);
        // Resolve routing before the boundary: selecting a missing instance is a caller error.
        var service = getMessageService();
        var platform = getPlatform();
        var adapterId = getAdapterId() ?? platform;
        try
        {
            return await service.SendMessageAsync(channel, segments).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not OutOfMemoryException)
        {
            // Avoid logging message segments, which may contain an entire Base64 file.
            var error = $"适配器发送消息失败: instance={adapterId}, platform={platform}, caller={ownerId}, " +
                $"channel={channel.Type}:{channel.Id}, {ex.GetType().Name}: {DescribeError(ex)}";
            new ConsoleLogger($"[Adapter:{adapterId}]", logHub).Error(error);
            return new SentMessage(string.Empty)
            {
                IsSuccess = false,
                ErrorMessage = $"适配器 {adapterId} 发送消息失败，详情请查看适配器日志。"
            };
        }
    }

    private static string DescribeError(Exception exception)
    {
        var message = exception.Message;
        // Some adapters include the input URI in a filesystem exception.
        var base64Index = message.IndexOf("base64:", StringComparison.OrdinalIgnoreCase);
        if (base64Index >= 0) message = message[..base64Index] + "[Base64 内容已省略]";
        const int maxLength = 1024;
        return message.Length > maxLength ? message[..maxLength] + "…" : message;
    }

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
        using var scope = useMessageSource(message);
        return await SendMessageAsync(message.Channel, segments).ConfigureAwait(false);
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
