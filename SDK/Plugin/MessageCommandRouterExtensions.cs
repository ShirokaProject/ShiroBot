using ShiroBot.SDK.Models;

namespace ShiroBot.SDK.Plugin;

public static class MessageCommandRouterExtensions
{
    public static void MapMention(
        this CommandRouter<MessageEvent> router,
        Func<MessageEvent, Task> handler) =>
        router.MapWhen(message => message.HasMention(), handler);

    public static void MapMention(
        this CommandRouter<MessageEvent> router,
        string userId,
        Func<MessageEvent, Task> handler) =>
        router.MapWhen(message => message.HasMention(userId), handler);

    public static void MapMentionAll(
        this CommandRouter<MessageEvent> router,
        Func<MessageEvent, Task> handler) =>
        router.MapWhen(message => message.HasMentionAll(), handler);

    public static void MapReply(
        this CommandRouter<MessageEvent> router,
        Func<MessageEvent, Task> handler) =>
        router.MapWhen(message => message.GetQuote() is not null, handler);

    public static void MapReplyMessage(
        this CommandRouter<MessageEvent> router,
        string messageId,
        Func<MessageEvent, Task> handler) =>
        router.MapWhen(message => message.GetQuote()?.MessageId == messageId, handler);
}
