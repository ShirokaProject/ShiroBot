using ShiroBot.SDK.Models;

namespace ShiroBot.SDK.Adapter;

/// <summary>
///     平台无关的消息服务。适配器负责把通用消息段映射为平台原生格式。
/// </summary>
public interface IMessageService
{
    MessageCapabilities GetMessageCapabilities(Channel channel)
    {
        return new MessageCapabilities();
    }

    MessageSendAssessment AssessMessage(Channel channel, OutgoingMessage message)
    {
        return MessagePreparation.Assess(message, GetMessageCapabilities(channel));
    }

    async Task<SentMessage> SendMessageAsync(Channel channel, OutgoingMessage message,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var prepared = MessagePreparation.Prepare(message, GetMessageCapabilities(channel));
        if (prepared.Message.Buttons is not null)
            throw new NotSupportedException("Adapter must implement native button sending.");
        if (prepared.Message.ReplyToInteraction is not null)
            throw new NotSupportedException("Adapter must implement native interaction replies.");
        var result = await SendMessageAsync(channel, prepared.Message.Segments, cancellationToken)
            .ConfigureAwait(false);
        return result with { Transformations = prepared.Transformations };
    }

    Task<SentMessage> SendMessageAsync(Channel channel, IReadOnlyList<MessageSegment> segments,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException($"Current adapter does not support '{nameof(SendMessageAsync)}'.");
    }

    Task DeleteMessageAsync(Channel channel, string messageId, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException($"Current adapter does not support '{nameof(DeleteMessageAsync)}'.");
    }

    /// <summary>
    ///     删除多条消息。默认逐条调用 <see cref="DeleteMessageAsync"/>；平台有批量接口时适配器应重写，
    ///     并自行处理平台限制（如 Discord 只能批量删除 14 天内的消息）。
    /// </summary>
    async Task DeleteMessagesAsync(Channel channel, IReadOnlyList<string> messageIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messageIds);
        foreach (var messageId in messageIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await DeleteMessageAsync(channel, messageId, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     编辑机器人已发送的消息，<paramref name="message"/> 为编辑后的完整内容。
    ///     各平台能编辑的部分不同（如 Telegram 只能改文本或说明，不能把文本改成图片），不支持时抛出 <see cref="NotSupportedException"/>。
    /// </summary>
    Task EditMessageAsync(Channel channel, string messageId, OutgoingMessage message,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException($"Current adapter does not support '{nameof(EditMessageAsync)}'.");
    }

    /// <summary>置顶消息。</summary>
    Task PinMessageAsync(Channel channel, string messageId, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException($"Current adapter does not support '{nameof(PinMessageAsync)}'.");
    }

    /// <summary>取消置顶消息。</summary>
    Task UnpinMessageAsync(Channel channel, string messageId, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException($"Current adapter does not support '{nameof(UnpinMessageAsync)}'.");
    }

    /// <summary>显示“正在输入”。平台通常在几秒后或机器人发出消息时自动结束，长任务需要定时重复调用。</summary>
    Task SendTypingAsync(Channel channel, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException($"Current adapter does not support '{nameof(SendTypingAsync)}'.");
    }

    Task<MessageEvent?> GetMessageAsync(Channel channel, string messageId,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException($"Current adapter does not support '{nameof(GetMessageAsync)}'.");
    }

    Task<IReadOnlyList<MessageEvent>> GetHistoryMessagesAsync(Channel channel, string? beforeMessageId = null,
        int limit = 20, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException($"Current adapter does not support '{nameof(GetHistoryMessagesAsync)}'.");
    }

    /// <summary>把接收到的资源段解析为可下载的临时 URL。</summary>
    Task<string> GetResourceUrlAsync(string resourceId, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException($"Current adapter does not support '{nameof(GetResourceUrlAsync)}'.");
    }
}