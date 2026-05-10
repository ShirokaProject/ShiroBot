using ShiroBot.Model.Common;

namespace ShiroBot.SDK.Plugin;

public static class MessageCommandRouterExtensions
{
    public static void MapMention(
        this CommandRouter<GroupIncomingMessage> router,
        Func<GroupIncomingMessage, Task> handler) =>
        router.MapWhen(message => message.HasMention(), handler);

    public static void MapMention(
        this CommandRouter<GroupIncomingMessage> router,
        long userId,
        Func<GroupIncomingMessage, Task> handler) =>
        router.MapWhen(message => message.HasMention(userId), handler);

    public static void MapMentionAll(
        this CommandRouter<GroupIncomingMessage> router,
        Func<GroupIncomingMessage, Task> handler) =>
        router.MapWhen(message => message.HasMentionAll(), handler);

    public static void MapReply(
        this CommandRouter<GroupIncomingMessage> router,
        Func<GroupIncomingMessage, Task> handler) =>
        router.MapWhen(message => message.GetReply() is not null, handler);

    public static void MapReplyTo(
        this CommandRouter<GroupIncomingMessage> router,
        long senderId,
        Func<GroupIncomingMessage, Task> handler) =>
        router.MapWhen(message => message.GetReply()?.SenderId == senderId, handler);

    public static void MapReplyMessage(
        this CommandRouter<GroupIncomingMessage> router,
        long messageSeq,
        Func<GroupIncomingMessage, Task> handler) =>
        router.MapWhen(message => message.GetReply()?.MessageSeq == messageSeq, handler);

    public static void MapMention(
        this CommandRouter<FriendIncomingMessage> router,
        Func<FriendIncomingMessage, Task> handler) =>
        router.MapWhen(message => message.HasMention(), handler);

    public static void MapMention(
        this CommandRouter<FriendIncomingMessage> router,
        long userId,
        Func<FriendIncomingMessage, Task> handler) =>
        router.MapWhen(message => message.HasMention(userId), handler);

    public static void MapReply(
        this CommandRouter<FriendIncomingMessage> router,
        Func<FriendIncomingMessage, Task> handler) =>
        router.MapWhen(message => message.GetReply() is not null, handler);

    public static void MapReplyTo(
        this CommandRouter<FriendIncomingMessage> router,
        long senderId,
        Func<FriendIncomingMessage, Task> handler) =>
        router.MapWhen(message => message.GetReply()?.SenderId == senderId, handler);

    public static void MapReplyMessage(
        this CommandRouter<FriendIncomingMessage> router,
        long messageSeq,
        Func<FriendIncomingMessage, Task> handler) =>
        router.MapWhen(message => message.GetReply()?.MessageSeq == messageSeq, handler);
}
