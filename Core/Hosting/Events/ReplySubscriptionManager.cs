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
        MessageReference message,
        TimeSpan duration,
        ReplyMessageHandler handler,
        bool disposeOnReply = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        ArgumentNullException.ThrowIfNull(message);
        ArgumentException.ThrowIfNullOrWhiteSpace(message.InstanceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(message.MessageId);
        ArgumentNullException.ThrowIfNull(message.Channel);
        ArgumentNullException.ThrowIfNull(handler);

        var id = Guid.NewGuid();
        var expiresAt = duration == Timeout.InfiniteTimeSpan
            ? (DateTimeOffset?)null
            : DateTimeOffset.UtcNow.Add(duration);
        var subscription = new ReplySubscription(id, ownerId, message, expiresAt, handler, disposeOnReply, Remove);
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
        if (quote is null || message.InstanceId is null) return;
        var reference = new MessageReference(message.InstanceId, message.Channel, quote.MessageId);

        var now = DateTimeOffset.UtcNow;
        var matches = new List<ReplySubscription>();
        foreach (var (id, subscription) in _subscriptions.ToArray())
        {
            if (subscription.ExpiresAt is not null && subscription.ExpiresAt <= now)
            {
                _subscriptions.TryRemove(id, out _);
                continue;
            }

            if (subscription.Reference.Matches(reference))
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
                ConsoleOutput.Error($"回复订阅处理失败: {subscription.OwnerId} messageId={subscription.Reference.MessageId} - {ex.Message}");
            }
        }
    }

    private void Remove(Guid id) => _subscriptions.TryRemove(id, out _);

    private sealed class ReplySubscription(
        Guid id,
        string ownerId,
        MessageReference reference,
        DateTimeOffset? expiresAt,
        ReplyMessageHandler handler,
        bool disposeOnReply,
        Action<Guid> remove) : IReplySubscription
    {
        private int _disposed;

        public string OwnerId { get; } = ownerId;
        public MessageReference Reference { get; } = reference;
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
