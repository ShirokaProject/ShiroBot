using ShiroBot.Model.Common;

namespace ShiroBot.SDK.Plugin;

public interface IReplySubscription : IDisposable
{
    long MessageSeq { get; }

    DateTimeOffset? ExpiresAt { get; }
}

public delegate Task ReplyMessageHandler(IncomingMessage message);
