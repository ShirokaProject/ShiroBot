using ShiroBot.Model.Common;
using ShiroBot.Model.Message.Requests;
using ShiroBot.Model.Message.Responses;
using ShiroBot.SDK.Adapter;

namespace ShiroBot.SDK.Plugin;

public interface IMessageContext : IMessageService
{
    IReplySubscription SubscribeReply(long messageSeq, TimeSpan duration, ReplyMessageHandler handler);

    Task<SendPrivateMessageResponse> SendPrivateMessageAsync(long userId, params OutgoingSegment[] segments) =>
        SendPrivateMessageAsync(new SendPrivateMessageRequest(userId, segments));

    Task<SendGroupMessageResponse> SendGroupMessageAsync(long groupId, params OutgoingSegment[] segments) =>
        SendGroupMessageAsync(new SendGroupMessageRequest(groupId, segments));

    Task<SendPrivateMessageResponse> SendPrivateMessageAsync(
        long userId,
        string text,
        params OutgoingSegment[] additionalSegments) =>
        SendPrivateMessageAsync(userId, BuildSegments(text, additionalSegments));

    Task<SendGroupMessageResponse> SendGroupMessageAsync(
        long groupId,
        string text,
        params OutgoingSegment[] additionalSegments) =>
        SendGroupMessageAsync(groupId, BuildSegments(text, additionalSegments));

    Task<SendPrivateMessageResponse> ReplyAsync(
        FriendIncomingMessage message,
        string text,
        params OutgoingSegment[] additionalSegments) =>
        SendPrivateMessageAsync(message.SenderId, BuildSegments(text, additionalSegments));

    Task<SendGroupMessageResponse> ReplyAsync(
        GroupIncomingMessage message,
        string text,
        params OutgoingSegment[] additionalSegments) =>
        SendGroupMessageAsync(message.Group.GroupId, BuildSegments(text, additionalSegments));
    
    Task<SendPrivateMessageResponse> ReplyAsync(
        FriendIncomingMessage message,
        params OutgoingSegment[] segments) =>
        SendPrivateMessageAsync(message.SenderId, segments);
    
    Task<SendGroupMessageResponse> ReplyAsync(
        GroupIncomingMessage message,
        params OutgoingSegment[] segments) =>
        SendGroupMessageAsync(message.Group.GroupId, segments);
    
    Task<SendPrivateMessageResponse> QuoteReplyAsync(
        FriendIncomingMessage message,
        string text,
        params OutgoingSegment[] segments) =>
        SendPrivateMessageAsync(message.SenderId,  segments.Prepend(new TextOutgoingSegment(text)).Prepend(new ReplyOutgoingSegment(message.MessageSeq)).ToArray());
    
    Task<SendGroupMessageResponse> QuoteReplyAsync(
        GroupIncomingMessage message,
        string text,
        params OutgoingSegment[] segments) =>
        SendGroupMessageAsync(message.Group.GroupId, segments.Prepend(new TextOutgoingSegment(text)).Prepend(new ReplyOutgoingSegment(message.MessageSeq)).ToArray());
    
    Task<SendPrivateMessageResponse> QuoteReplyAsync(
        FriendIncomingMessage message,
        params OutgoingSegment[] segments) =>
        SendPrivateMessageAsync(message.SenderId, segments.Prepend(new ReplyOutgoingSegment(message.MessageSeq)).ToArray());
    
    Task<SendGroupMessageResponse> QuoteReplyAsync(
        GroupIncomingMessage message,
        params OutgoingSegment[] segments) =>
        SendGroupMessageAsync(message.Group.GroupId, segments.Prepend(new ReplyOutgoingSegment(message.MessageSeq)).ToArray());

    Task RecallPrivateMessageAsync(long userId, long messageSeq) =>
        RecallPrivateMessageAsync(new RecallPrivateMessageRequest(userId, messageSeq));

    Task RecallGroupMessageAsync(long groupId, long messageSeq) =>
        RecallGroupMessageAsync(new RecallGroupMessageRequest(groupId, messageSeq));

    Task<GetMessageResponse> GetMessageAsync(GetMessageRequestMessageScene messageScene, long peerId, long messageSeq) =>
        GetMessageAsync(new GetMessageRequest(messageScene, peerId, messageSeq));

    Task<GetHistoryMessagesResponse> GetHistoryMessagesAsync(GetHistoryMessagesRequestMessageScene messageScene, long peerId, long? startMessageSeq = null, int limit = 20) =>
        GetHistoryMessagesAsync(new GetHistoryMessagesRequest(messageScene, peerId, startMessageSeq, limit));

    Task<GetResourceTempUrlResponse> GetResourceTempUrlAsync(string resourceId) =>
        GetResourceTempUrlAsync(new GetResourceTempUrlRequest(resourceId));

    Task<GetForwardedMessagesResponse> GetForwardedMessagesAsync(string forwardId) =>
        GetForwardedMessagesAsync(new GetForwardedMessagesRequest(forwardId));

    Task MarkMessageAsReadAsync(MarkMessageAsReadRequestMessageScene messageScene, long peerId, long messageSeq) =>
        MarkMessageAsReadAsync(new MarkMessageAsReadRequest(messageScene, peerId, messageSeq));

    private static OutgoingSegment[] BuildSegments(string text, IReadOnlyList<OutgoingSegment> additionalSegments)
    {
        var segments = new OutgoingSegment[additionalSegments.Count + 1];
        segments[0] = new TextOutgoingSegment(text);

        for (var i = 0; i < additionalSegments.Count; i++)
        {
            segments[i + 1] = additionalSegments[i];
        }

        return segments;
    }
}
