using ShiroBot.SDK.Models;

namespace ShiroBot.SDK.Adapter;

/// <summary>
/// 群 / 频道及成员管理服务。
/// </summary>
public interface IChannelService
{
    Task<IReadOnlyList<Channel>> GetChannelsAsync(CancellationToken cancellationToken = default)
        => throw new NotSupportedException($"Current adapter does not support '{nameof(GetChannelsAsync)}'.");

    Task<Channel?> GetChannelAsync(string channelId, CancellationToken cancellationToken = default)
        => throw new NotSupportedException($"Current adapter does not support '{nameof(GetChannelAsync)}'.");

    Task<IReadOnlyList<Member>> GetMembersAsync(string channelId, CancellationToken cancellationToken = default)
        => throw new NotSupportedException($"Current adapter does not support '{nameof(GetMembersAsync)}'.");

    Task<Member?> GetMemberAsync(string channelId, string userId, CancellationToken cancellationToken = default)
        => throw new NotSupportedException($"Current adapter does not support '{nameof(GetMemberAsync)}'.");

    Task SetChannelNameAsync(string channelId, string name, CancellationToken cancellationToken = default)
        => throw new NotSupportedException($"Current adapter does not support '{nameof(SetChannelNameAsync)}'.");

    Task KickMemberAsync(string channelId, string userId, CancellationToken cancellationToken = default)
        => throw new NotSupportedException($"Current adapter does not support '{nameof(KickMemberAsync)}'.");

    Task MuteMemberAsync(string channelId, string userId, TimeSpan duration, CancellationToken cancellationToken = default)
        => throw new NotSupportedException($"Current adapter does not support '{nameof(MuteMemberAsync)}'.");

    Task LeaveChannelAsync(string channelId, CancellationToken cancellationToken = default)
        => throw new NotSupportedException($"Current adapter does not support '{nameof(LeaveChannelAsync)}'.");
}
