using ShiroBot.SDK.Models;

namespace ShiroBot.SDK.Adapter;

/// <summary>
/// 平台无关的消息服务。适配器负责把通用消息段映射为平台原生格式。
/// </summary>
public interface IMessageService
{
    Task<SentMessage> SendMessageAsync(Channel channel, IReadOnlyList<MessageSegment> segments)
        => throw new NotSupportedException($"Current adapter does not support '{nameof(SendMessageAsync)}'.");

    Task DeleteMessageAsync(Channel channel, string messageId)
        => throw new NotSupportedException($"Current adapter does not support '{nameof(DeleteMessageAsync)}'.");

    Task<MessageEvent?> GetMessageAsync(Channel channel, string messageId)
        => throw new NotSupportedException($"Current adapter does not support '{nameof(GetMessageAsync)}'.");

    Task<IReadOnlyList<MessageEvent>> GetHistoryMessagesAsync(Channel channel, string? beforeMessageId = null, int limit = 20)
        => throw new NotSupportedException($"Current adapter does not support '{nameof(GetHistoryMessagesAsync)}'.");

    /// <summary>把接收到的资源段解析为可下载的临时 URL。</summary>
    Task<string> GetResourceUrlAsync(string resourceId)
        => throw new NotSupportedException($"Current adapter does not support '{nameof(GetResourceUrlAsync)}'.");
}
