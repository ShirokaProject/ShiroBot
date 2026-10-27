using System.Collections.Concurrent;
using ShiroBot.Hosting.Context;
using ShiroBot.Console;
using ShiroBot.SDK.Models;
using ShiroBot.SDK.Plugin;

namespace ShiroBot.Hosting.Events;

internal sealed class ReplySubscriptionManager
{
    private readonly ConcurrentDictionary<Guid, ReplySubscription> _subscriptions = [];
    private readonly Lock _expirationTimerLock = new();
    private Timer? _expirationTimer;

    private void EnsureExpirationTimer()
    {
        lock (_expirationTimerLock)
        {
            // The timer must not keep temporary BotContexts or unloaded plugins alive.
            _expirationTimer ??= new Timer(static state =>
            {
                if (((WeakReference<ReplySubscriptionManager>)state!).TryGetTarget(out var manager))
                    manager.RemoveExpired();
            }, new WeakReference<ReplySubscriptionManager>(this), TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        }
    }

    private void RemoveExpired()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var (_, subscription) in _subscriptions)
            if (subscription.ExpiresAt is { } expiry && expiry <= now) subscription.Dispose();
    }

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
        if (expiresAt is not null) EnsureExpirationTimer();
        return subscription;
    }

    public void UnregisterOwner(string ownerId)
    {
        if (string.IsNullOrWhiteSpace(ownerId)) return;

        foreach (var (_, subscription) in _subscriptions)
        {
            if (string.Equals(subscription.OwnerId, ownerId, StringComparison.OrdinalIgnoreCase))
            {
                subscription.Dispose();
            }
        }
    }

    /// <summary>Delivers a quote-reply to its subscriptions; returns true when any subscription received it.</summary>
    public async Task<bool> PublishAsync(MessageEvent message)
    {
        var quote = message.GetQuote();
        if (quote is null || message.InstanceId is null) return false;
        var reference = new MessageReference(message.InstanceId, message.Channel, quote.MessageId);

        var now = DateTimeOffset.UtcNow;
        var matches = new List<(ReplySubscription Subscription, ReplyMessageHandler Handler)>();
        foreach (var (_, subscription) in _subscriptions)
        {
            if (subscription.ExpiresAt is not null && subscription.ExpiresAt <= now)
            {
                subscription.Dispose();
                continue;
            }

            if (subscription.Reference.Matches(reference) && subscription.Handler is { } handler)
            {
                matches.Add((subscription, handler));
            }
        }

        foreach (var (subscription, handler) in matches)
        {
            if (subscription.DisposeOnReply)
            {
                subscription.Dispose();
            }

            try
            {
                await handler(message).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                ConsoleOutput.Error($"回复订阅处理失败: {subscription.OwnerId} messageId={subscription.Reference.MessageId} - {ex.Message}");
            }
        }

        return matches.Count > 0;
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
        private ReplyMessageHandler? _handler = handler;

        public string OwnerId { get; } = ownerId;
        public MessageReference Reference { get; } = reference;
        public DateTimeOffset? ExpiresAt { get; } = expiresAt;
        public ReplyMessageHandler? Handler => Volatile.Read(ref _handler);
        public bool DisposeOnReply { get; } = disposeOnReply;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                Interlocked.Exchange(ref _handler, null);
                remove(id);
            }
        }
    }
}
