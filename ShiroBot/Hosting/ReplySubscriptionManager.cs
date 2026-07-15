using System.Collections.Concurrent;
using ShiroBot.Core;
using ShiroBot.Model.Common;
using ShiroBot.SDK.Plugin;

namespace ShiroBot.Hosting;

internal sealed class ReplySubscriptionManager
{
    private readonly ConcurrentDictionary<Guid, ReplySubscription> _subscriptions = [];

    public IReplySubscription Subscribe(
        string ownerId,
        long messageSeq,
        TimeSpan duration,
        ReplyMessageHandler handler,
        bool disposeOnReply = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        ArgumentNullException.ThrowIfNull(handler);

        var id = Guid.NewGuid();
        var expiresAt = duration == Timeout.InfiniteTimeSpan
            ? (DateTimeOffset?)null
            : DateTimeOffset.UtcNow.Add(duration);
        var subscription = new ReplySubscription(id, ownerId, messageSeq, expiresAt, handler, disposeOnReply, Remove);
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

    public async Task PublishAsync(IncomingMessage message)
    {
        var reply = GetReply(message);
        if (reply is null) return;

        var now = DateTimeOffset.UtcNow;
        var matches = new List<ReplySubscription>();
        foreach (var (id, subscription) in _subscriptions.ToArray())
        {
            if (subscription.ExpiresAt is not null && subscription.ExpiresAt <= now)
            {
                _subscriptions.TryRemove(id, out _);
                continue;
            }

            if (subscription.MessageSeq == reply.MessageSeq)
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
                ConsoleHelper.Error($"回复订阅处理失败: {subscription.OwnerId} msgseq={subscription.MessageSeq} - {ex.Message}");
            }
        }
    }

    private void Remove(Guid id) => _subscriptions.TryRemove(id, out _);

    private static ReplyIncomingSegment? GetReply(IncomingMessage message) =>
        message switch
        {
            FriendIncomingMessage friend => friend.Segments.OfType<ReplyIncomingSegment>().FirstOrDefault(),
            GroupIncomingMessage group => group.Segments.OfType<ReplyIncomingSegment>().FirstOrDefault(),
            TempIncomingMessage temp => temp.Segments.OfType<ReplyIncomingSegment>().FirstOrDefault(),
            _ => null
        };

    private sealed class ReplySubscription(
        Guid id,
        string ownerId,
        long messageSeq,
        DateTimeOffset? expiresAt,
        ReplyMessageHandler handler,
        bool disposeOnReply,
        Action<Guid> remove) : IReplySubscription
    {
        private int _disposed;

        public string OwnerId { get; } = ownerId;
        public long MessageSeq { get; } = messageSeq;
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
