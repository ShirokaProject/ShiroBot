using ShiroBot.SDK.Models;

namespace ShiroBot.SDK.Adapter;

/// <summary>
/// 用户 / 好友关系服务。
/// </summary>
public interface IUserService
{
    /// <summary>获取机器人自身信息。</summary>
    Task<User> GetSelfAsync()
        => throw new NotSupportedException($"Current adapter does not support '{nameof(GetSelfAsync)}'.");

    Task<User?> GetUserAsync(string userId)
        => throw new NotSupportedException($"Current adapter does not support '{nameof(GetUserAsync)}'.");

    /// <summary>获取好友 / 私聊联系人列表。</summary>
    Task<IReadOnlyList<User>> GetFriendsAsync()
        => throw new NotSupportedException($"Current adapter does not support '{nameof(GetFriendsAsync)}'.");

    Task AcceptFriendRequestAsync(string token)
        => throw new NotSupportedException($"Current adapter does not support '{nameof(AcceptFriendRequestAsync)}'.");

    Task RejectFriendRequestAsync(string token, string? reason = null)
        => throw new NotSupportedException($"Current adapter does not support '{nameof(RejectFriendRequestAsync)}'.");
}
