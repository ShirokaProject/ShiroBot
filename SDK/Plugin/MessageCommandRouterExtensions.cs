using ShiroBot.SDK.Models;

namespace ShiroBot.SDK.Plugin;

public static class MessageCommandRouterExtensions
{
    public static void MapMention(
        this CommandRouter<MessageEvent> router,
        Func<MessageEvent, Task> handler) =>
        router.MapCondition(MessageRouteCondition.Mention, null, null, message => message.HasMention(), handler);

    public static void MapMention(
        this CommandRouter<MessageEvent> router,
        string userId,
        Func<MessageEvent, Task> handler) =>
        router.MapCondition(MessageRouteCondition.Mention, userId, null, message => message.HasMention(userId), handler);

    public static void MapMentionAll(
        this CommandRouter<MessageEvent> router,
        Func<MessageEvent, Task> handler) =>
        router.MapCondition(MessageRouteCondition.MentionAll, null, null, message => message.HasMentionAll(), handler);

    public static void MapReply(
        this CommandRouter<MessageEvent> router,
        Func<MessageEvent, Task> handler) =>
        router.MapCondition(MessageRouteCondition.Reply, null, null, message => message.GetQuote() is not null, handler);

    public static void MapReplyMessage(
        this CommandRouter<MessageEvent> router,
        string messageId,
        Func<MessageEvent, Task> handler) =>
        router.MapCondition(MessageRouteCondition.Reply, messageId, null, message => message.GetQuote()?.MessageId == messageId, handler);
}
