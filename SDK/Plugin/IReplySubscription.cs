using ShiroBot.SDK.Models;

namespace ShiroBot.SDK.Plugin;

public interface IReplySubscription : IDisposable
{
    MessageReference Reference { get; }

    DateTimeOffset? ExpiresAt { get; }
}

public delegate Task ReplyMessageHandler(MessageEvent message);
