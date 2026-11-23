using ShiroBot.SDK.Models;

namespace ShiroBot.SDK.Adapter;

/// <summary>
///     群 / 频道及成员管理服务。
/// </summary>
public interface IChannelService
{
    Task<IReadOnlyList<Channel>> GetChannelsAsync(CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException($"Current adapter does not support '{nameof(GetChannelsAsync)}'.");
    }

    Task<Channel?> GetChannelAsync(string channelId, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException($"Current adapter does not support '{nameof(GetChannelAsync)}'.");
    }

    Task<IReadOnlyList<Member>> GetMembersAsync(string channelId, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException($"Current adapter does not support '{nameof(GetMembersAsync)}'.");
    }

    Task<Member?> GetMemberAsync(string channelId, string userId, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException($"Current adapter does not support '{nameof(GetMemberAsync)}'.");
    }

    Task SetChannelNameAsync(string channelId, string name, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException($"Current adapter does not support '{nameof(SetChannelNameAsync)}'.");
    }

    Task KickMemberAsync(string channelId, string userId, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException($"Current adapter does not support '{nameof(KickMemberAsync)}'.");
    }

    Task MuteMemberAsync(string channelId, string userId, TimeSpan duration,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException($"Current adapter does not support '{nameof(MuteMemberAsync)}'.");
    }

    /// <summary>封禁成员：移出并阻止其再次加入，直到 <see cref="UnbanMemberAsync"/>。与 <see cref="KickMemberAsync"/> 不同，被踢出的成员可以重新加入。</summary>
    Task BanMemberAsync(string channelId, string userId, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException($"Current adapter does not support '{nameof(BanMemberAsync)}'.");
    }

    /// <summary>解除封禁，不会把成员拉回。</summary>
    Task UnbanMemberAsync(string channelId, string userId, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException($"Current adapter does not support '{nameof(UnbanMemberAsync)}'.");
    }

    Task LeaveChannelAsync(string channelId, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException($"Current adapter does not support '{nameof(LeaveChannelAsync)}'.");
    }
}