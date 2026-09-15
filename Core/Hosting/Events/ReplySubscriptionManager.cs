using System.Collections.Concurrent;
using ShiroBot.Hosting.Context;
using ShiroBot.Console;
using ShiroBot.SDK.Models;
using ShiroBot.SDK.Plugin;

namespace ShiroBot.Hosting.Events;

internal sealed class ReplySubscriptionManager
{
    private readonly ConcurrentDictionary<Guid, ReplySubscription> _subscriptions = [];

    public IReplySubscription Subscribe(
        string ownerId,
        string platform,
        string messageId,
        TimeSpan duration,
        ReplyMessageHandler handler,
        bool disposeOnReply = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(messageId);
        ArgumentNullException.ThrowIfNull(handler);

        var id = Guid.NewGuid();
        var expiresAt = duration == Timeout.InfiniteTimeSpan
            ? (DateTimeOffset?)null
            : DateTimeOffset.UtcNow.Add(duration);
        var subscription = new ReplySubscription(id, ownerId, platform, messageId, expiresAt, handler, disposeOnReply, Remove);
        _subscriptions[id] = subscription;
        return subscription;
    }

    public void UnregisterOwner(string ownerId)
    {
        if (string.IsNullOrWhiteSpace(ownerId)) return;

        foreach (var (id, subscription) in _subscriptions.ToArray())
        {
            if (string.Equals(subscription.OwnerId, ownerId, StringComparison.OrdinalIgnoreCase))
            {
                _subscriptions.TryRemove(id, out _);
            }
        }
    }

    public async Task PublishAsync(MessageEvent message)
    {
        var quote = message.GetQuote();
        if (quote is null) return;

        var now = DateTimeOffset.UtcNow;
        var matches = new List<ReplySubscription>();
        foreach (var (id, subscription) in _subscriptions.ToArray())
        {
            if (subscription.ExpiresAt is not null && subscription.ExpiresAt <= now)
            {
                _subscriptions.TryRemove(id, out _);
                continue;
            }

            if (subscription.MessageId == quote.MessageId &&
                string.Equals(subscription.Platform, message.Platform, StringComparison.OrdinalIgnoreCase))
            {
                matches.Add(subscription);
            }
        }

        foreach (var subscription in matches)
        {
            if (subscription.DisposeOnReply)
            {
                subscription.Dispose();
            }

            try
            {
                await subscription.Handler(message).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                ConsoleOutput.Error($"回复订阅处理失败: {subscription.OwnerId} messageId={subscription.MessageId} - {ex.Message}");
            }
        }
    }

    private void Remove(Guid id) => _subscriptions.TryRemove(id, out _);

    private sealed class ReplySubscription(
        Guid id,
        string ownerId,
        string platform,
        string messageId,
        DateTimeOffset? expiresAt,
        ReplyMessageHandler handler,
        bool disposeOnReply,
        Action<Guid> remove) : IReplySubscription
    {
        private int _disposed;

        public string OwnerId { get; } = ownerId;
        public string Platform { get; } = platform;
        public string MessageId { get; } = messageId;
        public DateTimeOffset? ExpiresAt { get; } = expiresAt;
        public ReplyMessageHandler Handler { get; } = handler;
        public bool DisposeOnReply { get; } = disposeOnReply;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                remove(id);
            }
        }
    }
}
