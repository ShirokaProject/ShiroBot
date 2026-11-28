using ShiroBot.SDK.Models;

namespace ShiroBot.Hosting.Context;

/// <summary>A friend or group an adapter instance can see, for picking IDs in the Dashboard.</summary>
internal sealed record DirectoryEntry(string Instance, string InstanceName, string Id, string? Name);

/// <summary>An instance whose list could not be read, and why.</summary>
internal sealed record DirectoryGap(string Instance, string InstanceName, string Reason);

internal sealed record DirectoryListing(IReadOnlyList<DirectoryEntry> Entries, IReadOnlyList<DirectoryGap> Unavailable);

/// <summary>
/// Friends and groups of every running adapter instance, so the Dashboard can offer them by name instead of
/// asking for raw IDs. Lists come from the adapters (<see cref="SDK.Adapter.IUserService.GetFriendsAsync"/>,
/// <see cref="SDK.Adapter.IChannelService.GetChannelsAsync"/>) and are kept for a minute, since a large account's
/// lists are slow to fetch from the protocol side.
/// </summary>
internal sealed class ContactDirectory(BotContext bot)
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan InstanceTimeout = TimeSpan.FromSeconds(15);
    private readonly Dictionary<DirectoryKind, (DateTimeOffset At, DirectoryListing Listing)> _cache = [];
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<DirectoryListing> GetAsync(DirectoryKind kind, bool refresh, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!refresh && _cache.TryGetValue(kind, out var cached) && DateTimeOffset.UtcNow - cached.At < CacheDuration)
                return cached.Listing;

            var entries = new List<DirectoryEntry>();
            var unavailable = new List<DirectoryGap>();
            foreach (var instance in bot.GetAdapterInstances())
            {
                var name = string.IsNullOrWhiteSpace(instance.Name) ? instance.Id : instance.Name;
                try
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    timeout.CancelAfter(InstanceTimeout);
                    entries.AddRange((await ReadAsync(instance.Id, kind, timeout.Token).ConfigureAwait(false))
                        .Select(item => new DirectoryEntry(instance.Id, name, item.Id, item.Name)));
                }
                catch (NotSupportedException)
                {
                    unavailable.Add(new DirectoryGap(instance.Id, name, kind == DirectoryKind.Users ? "不支持读取好友列表" : "不支持读取群列表"));
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    unavailable.Add(new DirectoryGap(instance.Id, name, "读取超时"));
                }
                catch (Exception ex) when (ex is not OperationCanceledException and not OutOfMemoryException)
                {
                    unavailable.Add(new DirectoryGap(instance.Id, name, ex.Message));
                }
            }

            var listing = new DirectoryListing(entries, unavailable);
            _cache[kind] = (DateTimeOffset.UtcNow, listing);
            return listing;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<IEnumerable<(string Id, string? Name)>> ReadAsync(string instanceId, DirectoryKind kind, CancellationToken cancellationToken)
    {
        using var scope = bot.UseInstance(instanceId);
        if (kind == DirectoryKind.Users)
            return (await bot.User.GetFriendsAsync(cancellationToken).ConfigureAwait(false)).Select(user => (user.Id, user.Name));
        return (await bot.Channel.GetChannelsAsync(cancellationToken).ConfigureAwait(false))
            .Where(channel => channel.Type == ChannelType.Group)
            .Select(channel => (channel.Id, channel.Name));
    }
}

internal enum DirectoryKind { Users, Groups }
