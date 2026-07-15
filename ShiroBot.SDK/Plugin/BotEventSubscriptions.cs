namespace ShiroBot.SDK.Plugin;

/// <summary>
/// Legacy plugin ABI surface. The host uses concrete event types for subscriptions.
/// </summary>
[Flags]
public enum BotEventSubscriptions
{
    None = 0,
    GroupMessage = 1 << 0,
    FriendMessage = 1 << 1,
    MessageRecall = 1 << 2,
    FriendRequest = 1 << 3,
    GroupJoinRequest = 1 << 4,
    GroupInvitedJoinRequest = 1 << 5,
    GroupInvitation = 1 << 6,
    FriendNudge = 1 << 7,
    FriendFileUpload = 1 << 8,
    GroupAdminChange = 1 << 9,
    GroupEssenceMessageChange = 1 << 10,
    GroupMemberIncrease = 1 << 11,
    GroupMemberDecrease = 1 << 12,
    GroupNameChange = 1 << 13,
    GroupMessageReaction = 1 << 14,
    GroupMute = 1 << 15,
    GroupWholeMute = 1 << 16,
    GroupNudge = 1 << 17,
    GroupFileUpload = 1 << 18,
    PeerPinChange = 1 << 19,
    GroupDisband = 1 << 20,
    BotOffline = 1 << 21
}
