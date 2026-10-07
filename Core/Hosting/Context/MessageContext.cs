using ShiroBot.SDK.Adapter;
using ShiroBot.SDK.Models;
using ShiroBot.SDK.Plugin;
using ShiroBot.Hosting.Events;
using ShiroBot.Hosting.Logging;

namespace ShiroBot.Hosting.Context;

internal sealed class MessageContext(
    Func<(IMessageService Service, string Platform, string? InstanceId)> getMessageRoute,
    Func<MessageEvent, IDisposable> useMessageSource,
    Func<string, IDisposable> useInstance,
    ReplySubscriptionManager replySubscriptions,
    string ownerId,
    HostLogHub? logHub = null) : IMessageContext
{
    public IReplySubscription SubscribeReply(
        MessageReference message,
        TimeSpan duration,
        ReplyMessageHandler handler,
        bool disposeOnReply = true) =>
        replySubscriptions.Subscribe(ownerId, message, duration, handler, disposeOnReply);

    public async Task<SentMessage> SendMessageAsync(Channel channel, IReadOnlyList<MessageSegment> segments, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(channel);
        ArgumentNullException.ThrowIfNull(segments);
        cancellationToken.ThrowIfCancellationRequested();
        // Resolve routing before the boundary: selecting a missing instance is a caller error.
        var (service, platform, instanceId) = getMessageRoute();
        var adapterId = instanceId ?? platform;
        try
        {
            var result = await service.SendMessageAsync(channel, segments, cancellationToken: cancellationToken).ConfigureAwait(false);
            return result with { Reference = result.IsSuccess && !string.IsNullOrWhiteSpace(result.MessageId)
                ? new MessageReference(adapterId, channel, result.MessageId) : null };
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

    public async Task<SentMessage> SendMessageAsync(ChannelReference channel, IReadOnlyList<MessageSegment> segments, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(channel);
        using var scope = useInstance(channel.InstanceId);
        return await SendMessageAsync(channel.Channel, segments, cancellationToken).ConfigureAwait(false);
    }

    public MessageCapabilities GetMessageCapabilities(Channel channel) => getMessageRoute().Service.GetMessageCapabilities(channel);
    public MessageCapabilities GetMessageCapabilities(ChannelReference channel)
    {
        using var scope = useInstance(channel.InstanceId);
        return GetMessageCapabilities(channel.Channel);
    }
    public MessageSendAssessment AssessMessage(Channel channel, OutgoingMessage message)
    {
        using var scope = SelectReplySource(channel, message);
        return getMessageRoute().Service.AssessMessage(channel, message);
    }
    public MessageSendAssessment AssessMessage(ChannelReference channel, OutgoingMessage message)
    {
        ValidateExplicitTarget(channel, message);
        using var scope = useInstance(channel.InstanceId);
        return AssessMessage(channel.Channel, message);
    }
    public async Task<SentMessage> SendMessageAsync(ChannelReference channel, OutgoingMessage message, CancellationToken cancellationToken = default)
    {
        ValidateExplicitTarget(channel, message);
        using var scope = useInstance(channel.InstanceId);
        return await SendMessageAsync(channel.Channel, message, cancellationToken).ConfigureAwait(false);
    }
    public async Task<SentMessage> SendMessageAsync(Channel channel, OutgoingMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(channel);
        ArgumentNullException.ThrowIfNull(message);
        cancellationToken.ThrowIfCancellationRequested();
        using var scope = SelectReplySource(channel, message);
        var (service, platform, instanceId) = getMessageRoute();
        var adapterId = instanceId ?? platform;
        try
        {
            var result = await service.SendMessageAsync(channel, message, cancellationToken).ConfigureAwait(false);
            return result with { Reference = result.IsSuccess && !string.IsNullOrWhiteSpace(result.MessageId) ? new(adapterId, channel, result.MessageId) : null };
        }
        catch (Exception error) when (error is not OperationCanceledException and not OutOfMemoryException)
        {
            new ConsoleLogger($"[Adapter:{adapterId}]", logHub).Error($"适配器发送消息失败: instance={adapterId}, caller={ownerId}, {error.GetType().Name}: {DescribeError(error)}");
            return new(string.Empty) { IsSuccess = false, ErrorMessage = $"适配器 {adapterId} 发送消息失败，详情请查看适配器日志。" };
        }
    }
    public Task<SentMessage> ReplyAsync(MessageEvent message, OutgoingMessage content, CancellationToken cancellationToken = default) =>
        SendMessageAsync(message.Channel, content with { ReplyTo = message.Reference, ReplyToInteraction = null }, cancellationToken);

    public Task<SentMessage> ReplyAsync(InteractionEvent interaction, OutgoingMessage content, CancellationToken cancellationToken = default) =>
        SendMessageAsync(interaction.Channel ?? throw new ArgumentException("Interaction has no channel."),
            content with { ReplyTo = null, ReplyToInteraction = interaction.Reference }, cancellationToken);

    private IDisposable? SelectReplySource(Channel channel, OutgoingMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (message.ReplyToInteraction is { } interaction)
        {
            if (!new ChannelReference(interaction.InstanceId, channel).Matches(new(interaction.InstanceId, interaction.Channel)))
                throw new ArgumentException("Interaction belongs to another target channel.");
            return useInstance(interaction.InstanceId);
        }
        if (message.ReplyTo is not { } reply) return null;
        if (!new ChannelReference(reply.InstanceId, channel).Matches(new(reply.InstanceId, reply.Channel)))
            throw new ArgumentException("ReplyTo must belong to the target channel.");
        return useInstance(reply.InstanceId);
    }
    private static void ValidateExplicitTarget(ChannelReference channel, OutgoingMessage message)
    {
        ArgumentNullException.ThrowIfNull(channel);
        ArgumentNullException.ThrowIfNull(message);
        if (message.ReplyToInteraction is { } interaction && !new ChannelReference(interaction.InstanceId, interaction.Channel).Matches(channel))
            throw new ArgumentException("Interaction belongs to a different target instance or channel.");
        if (message.ReplyTo is { } reply && !new ChannelReference(reply.InstanceId, reply.Channel).Matches(channel))
            throw new ArgumentException("ReplyTo belongs to a different target instance or channel.");
    }

    public async Task DeleteMessageAsync(MessageReference message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        using var scope = useInstance(message.InstanceId);
        await DeleteMessageAsync(message.Channel, message.MessageId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<MessageEvent?> GetMessageAsync(MessageReference message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        using var scope = useInstance(message.InstanceId);
        return await GetMessageAsync(message.Channel, message.MessageId, cancellationToken).ConfigureAwait(false);
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

    public Task<SentMessage> ReplyAsync(MessageEvent message, IReadOnlyList<MessageSegment> segments, CancellationToken cancellationToken = default) =>
        SendReplyAsync(message, segments, cancellationToken);

    public Task<SentMessage> QuoteReplyAsync(MessageEvent message, IReadOnlyList<MessageSegment> segments, CancellationToken cancellationToken = default) =>
        SendReplyAsync(message, [new QuoteSegment(message.MessageId), .. segments], cancellationToken);

    private async Task<SentMessage> SendReplyAsync(MessageEvent message, IReadOnlyList<MessageSegment> segments, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        using var scope = useMessageSource(message);
        return await SendMessageAsync(message.Channel, segments, cancellationToken).ConfigureAwait(false);
    }

    public Task DeleteMessageAsync(Channel channel, string messageId, CancellationToken cancellationToken = default) =>
        getMessageRoute().Service.DeleteMessageAsync(channel, messageId, cancellationToken: cancellationToken);

    public async Task<MessageEvent?> GetMessageAsync(Channel channel, string messageId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(channel);
        cancellationToken.ThrowIfCancellationRequested();
        var (service, platform, instanceId) = getMessageRoute();
        var message = await service.GetMessageAsync(channel, messageId, cancellationToken).ConfigureAwait(false);
        return message is null ? null : StampQuerySource(message, platform, instanceId);
    }

    public async Task<IReadOnlyList<MessageEvent>> GetHistoryMessagesAsync(Channel channel, string? beforeMessageId = null, int limit = 20, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(channel);
        cancellationToken.ThrowIfCancellationRequested();
        var (service, platform, instanceId) = getMessageRoute();
        var messages = await service.GetHistoryMessagesAsync(channel, beforeMessageId, limit, cancellationToken).ConfigureAwait(false);
        return messages.Select(message => StampQuerySource(message, platform, instanceId)).ToArray();
    }

    private static MessageEvent StampQuerySource(MessageEvent message, string platform, string? instanceId) =>
        message with { Platform = platform, InstanceId = instanceId ?? throw new InvalidOperationException("Message query has no source instance.") };

    public Task<string> GetResourceUrlAsync(string resourceId, CancellationToken cancellationToken = default) =>
        getMessageRoute().Service.GetResourceUrlAsync(resourceId, cancellationToken: cancellationToken);
}
