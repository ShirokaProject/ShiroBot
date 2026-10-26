using ShiroBot.Configuration;
using ShiroBot.SDK.Abstractions;
using ShiroBot.SDK.Models;
using ShiroBot.SDK.Plugin;

namespace ShiroBot.Hosting.Context;

/// <summary>An owner's quote-reply to a notification, with a way to answer in the same chat.</summary>
internal sealed record OwnerReply(UserReference Owner, string Text, Func<string, Task> RespondAsync);

/// <summary>Handles a reply; returns true once the notification is resolved, which ends it for every owner.</summary>
internal delegate Task<bool> OwnerReplyHandler(OwnerReply reply);

/// <summary>Sends a notification to the owners, optionally accepting replies.</summary>
internal delegate Task OwnerNotify(string text, OwnerReplyHandler? onReply, CancellationToken cancellationToken);

/// <summary>
/// Direct messages to the configured owners about things they should know (updates, failures, restarts),
/// gated by <see cref="CoreConfig.OwnerNotifications"/>. A notification can accept quote-replies, e.g. to
/// confirm a pending update; owner commands outside a reply can build on the same handler later.
/// </summary>
internal sealed class OwnerNotifier(BotContext bot, CoreConfig config)
{
    /// <summary>How long a notification accepts replies.</summary>
    internal static readonly TimeSpan ReplyWindow = TimeSpan.FromHours(24);

    public async Task NotifyAsync(string text, OwnerReplyHandler? onReply = null, CancellationToken cancellationToken = default)
    {
        if (!config.OwnerNotifications) return;
        var instances = bot.GetAdapterInstances().Select(instance => instance.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        // Every owner's copy shares one resolution: the first handled reply ends them all.
        var subscriptions = new List<IReplySubscription>();
        var resolved = 0;

        foreach (var owner in bot.OwnerList.Where(owner => instances.Contains(owner.InstanceId)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                SentMessage sent;
                using (bot.UseInstance(owner.InstanceId))
                    sent = await bot.Message.SendDirectMessageAsync(owner.UserId, [new TextSegment(text)], cancellationToken).ConfigureAwait(false);
                if (onReply is null || sent.Reference is not { } reference) continue;

                lock (subscriptions)
                    subscriptions.Add(bot.Message.SubscribeReply(reference, ReplyWindow, async message =>
                    {
                        if (Volatile.Read(ref resolved) != 0 || message.InstanceId is not { } instanceId) return;
                        var sender = new UserReference(instanceId, message.Sender.Id);
                        if (!bot.IsOwner(sender)) return;
                        var reply = new OwnerReply(sender, message.GetPlainText().Trim(), async answer =>
                        {
                            using (bot.UseInstance(instanceId))
                                await bot.Message.ReplyAsync(message, new OutgoingMessage { Segments = [new TextSegment(answer)] }).ConfigureAwait(false);
                        });
                        if (!await onReply(reply).ConfigureAwait(false) || Interlocked.Exchange(ref resolved, 1) != 0) return;
                        lock (subscriptions)
                            foreach (var subscription in subscriptions) subscription.Dispose();
                    }, disposeOnReply: false));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                BotLog.Warning($"通知 Owner {owner.ToConfigString()} 失败: {ex.Message}");
            }
        }
    }
}
