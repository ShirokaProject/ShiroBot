using ShiroBot.Model.Common;

namespace ShiroBot.SDK.Adapter;

public interface IEventService
{
    event Func<GroupIncomingMessage, Task> GroupMessageReceived
    {
        add { }
        remove { }
    }

    event Func<FriendIncomingMessage, Task> FriendMessageReceived
    {
        add { }
        remove { }
    }

    event Func<MessageRecallEvent, Task> MessageRecall
    {
        add { }
        remove { }
    }

    event Func<FriendRequestEvent, Task> FriendRequest
    {
        add { }
        remove { }
    }

    event Func<GroupJoinRequestEvent, Task> GroupJoinRequest
    {
        add { }
        remove { }
    }

    event Func<GroupInvitedJoinRequestEvent, Task> GroupInvitedJoinRequest
    {
        add { }
        remove { }
    }

    event Func<GroupInvitationEvent, Task> GroupInvitation
    {
        add { }
        remove { }
    }

    event Func<FriendNudgeEvent, Task> FriendNudge
    {
        add { }
        remove { }
    }

    event Func<FriendFileUploadEvent, Task> FriendFileUpload
    {
        add { }
        remove { }
    }

    event Func<GroupAdminChangeEvent, Task> GroupAdminChange
    {
        add { }
        remove { }
    }

    event Func<GroupEssenceMessageChangeEvent, Task> GroupEssenceMessageChange
    {
        add { }
        remove { }
    }

    event Func<GroupMemberIncreaseEvent, Task> GroupMemberIncrease
    {
        add { }
        remove { }
    }

    event Func<GroupMemberDecreaseEvent, Task> GroupMemberDecrease
    {
        add { }
        remove { }
    }

    event Func<GroupNameChangeEvent, Task> GroupNameChange
    {
        add { }
        remove { }
    }

    event Func<GroupMessageReactionEvent, Task> GroupMessageReaction
    {
        add { }
        remove { }
    }

    event Func<GroupMuteEvent, Task> GroupMute
    {
        add { }
        remove { }
    }

    event Func<GroupWholeMuteEvent, Task> GroupWholeMute
    {
        add { }
        remove { }
    }

    event Func<GroupNudgeEvent, Task> GroupNudge
    {
        add { }
        remove { }
    }

    event Func<GroupFileUploadEvent, Task> GroupFileUpload
    {
        add { }
        remove { }
    }

    event Func<PeerPinChangeEvent, Task> PeerPinChange
    {
        add { }
        remove { }
    }

    event Func<GroupDisbandEvent, Task> GroupDisband
    {
        add { }
        remove { }
    }

    event Func<BotOfflineEvent, Task> BotOffline
    {
        add { }
        remove { }
    }
}
