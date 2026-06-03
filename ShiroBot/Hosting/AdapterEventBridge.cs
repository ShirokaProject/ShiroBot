using System.Reflection;
using ShiroBot.Core;
using ShiroBot.Model.Common;
using ShiroBot.SDK.Adapter;
using CH = ShiroBot.Core.ConsoleHelper;

namespace ShiroBot.Hosting;

internal sealed class AdapterEventBridge
{
    private static readonly MethodInfo CreateAdapterEventBridgeHandlerMethod =
        typeof(AdapterEventBridge).GetMethod(nameof(CreateAdapterEventBridgeHandler), BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException($"Failed to locate {nameof(CreateAdapterEventBridgeHandler)}.");

    private static readonly MethodInfo PublishAdapterEventAsyncMethod =
        typeof(AdapterEventBridge).GetMethod(nameof(PublishAdapterEventAsync), BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException($"Failed to locate {nameof(PublishAdapterEventAsync)}.");

    private readonly HostEventDispatcher _eventDispatcher;

    public AdapterEventBridge(HostEventDispatcher eventDispatcher)
    {
        _eventDispatcher = eventDispatcher;
    }

    public void Bridge(
        IEventService eventService,
        string pluginRoot,
        PluginRouteConfig routePolicy,
        Func<FriendIncomingMessage, Task> friendMessageHandler)
    {
        foreach (var eventInfo in typeof(IEventService).GetEvents())
        {
            var payloadType = GetAdapterEventPayloadType(eventInfo);
            var handler = CreateAdapterEventBridgeDelegate(
                eventInfo,
                payloadType,
                pluginRoot,
                routePolicy,
                friendMessageHandler);

            eventInfo.AddEventHandler(eventService, handler);
        }
    }

    private Delegate CreateAdapterEventBridgeDelegate(
        EventInfo eventInfo,
        Type payloadType,
        string pluginRoot,
        PluginRouteConfig routePolicy,
        Func<FriendIncomingMessage, Task> friendMessageHandler)
    {
        var eventName = GetAdapterEventDisplayName(eventInfo.Name, payloadType);

        if (payloadType == typeof(FriendIncomingMessage))
            return CreateAdapterEventBridgeHandler<FriendIncomingMessage>(
                message => friendMessageHandler(message),
                eventName);

        if (!typeof(Event).IsAssignableFrom(payloadType))
            throw new InvalidOperationException(
                $"Adapter event '{eventInfo.Name}' payload '{payloadType.Name}' does not implement '{nameof(Event)}'.");

        var factory = CreateAdapterEventBridgeHandlerMethod.MakeGenericMethod(payloadType);
        return (Delegate)factory.Invoke(null, [CreateEventPublisher(_eventDispatcher, payloadType), eventName])!;
    }

    private static Type GetAdapterEventPayloadType(EventInfo eventInfo)
    {
        var invokeMethod = eventInfo.EventHandlerType?.GetMethod("Invoke");
        if (invokeMethod is null)
            throw new InvalidOperationException(
                $"Adapter event '{eventInfo.Name}' does not expose an invokable handler type.");

        var parameters = invokeMethod.GetParameters();
        if (invokeMethod.ReturnType != typeof(Task) || parameters.Length != 1)
            throw new InvalidOperationException(
                $"Adapter event '{eventInfo.Name}' must use a handler shaped like Func<TEvent, Task>.");

        return parameters[0].ParameterType;
    }

    private static Func<TEvent, Task> CreateAdapterEventBridgeHandler<TEvent>(Func<TEvent, Task> dispatcher,
        string eventName)
    {
        return message => DispatchAdapterEventInBackground(() => dispatcher(message), eventName);
    }

    private static object CreateEventPublisher(HostEventDispatcher eventSink, Type payloadType)
    {
        var publisherType = typeof(Func<,>).MakeGenericType(payloadType, typeof(Task));
        var publishMethod = PublishAdapterEventAsyncMethod.MakeGenericMethod(payloadType);

        return Delegate.CreateDelegate(publisherType, eventSink, publishMethod);
    }

    private static Task PublishAdapterEventAsync<TEvent>(HostEventDispatcher eventSink, TEvent message)
        where TEvent : Event
    {
        return eventSink.PublishAsync(message);
    }

    private static string GetAdapterEventDisplayName(string eventName, Type payloadType)
    {
        if (payloadType == typeof(GroupIncomingMessage)) return "群消息";

        if (payloadType == typeof(FriendIncomingMessage)) return "好友消息";

        return payloadType.Name switch
        {
            nameof(MessageRecallEvent) => "消息撤回",
            nameof(FriendRequestEvent) => "好友请求",
            nameof(GroupJoinRequestEvent) => "入群请求",
            nameof(GroupInvitedJoinRequestEvent) => "群成员邀请他人入群请求",
            nameof(GroupInvitationEvent) => "他人邀请自身入群",
            nameof(FriendNudgeEvent) => "好友戳一戳",
            nameof(FriendFileUploadEvent) => "好友文件上传",
            nameof(GroupAdminChangeEvent) => "群管理员变更",
            nameof(GroupEssenceMessageChangeEvent) => "群精华消息变更",
            nameof(GroupMemberIncreaseEvent) => "群成员增加",
            nameof(GroupMemberDecreaseEvent) => "群成员减少",
            nameof(GroupNameChangeEvent) => "群名称变更",
            nameof(GroupMessageReactionEvent) => "群消息表情回应",
            nameof(GroupMuteEvent) => "群禁言",
            nameof(GroupWholeMuteEvent) => "群全体禁言",
            nameof(GroupNudgeEvent) => "群戳一戳",
            nameof(GroupFileUploadEvent) => "群文件上传",
            _ => eventName
        };
    }

    private static Task DispatchAdapterEventInBackground(Func<Task> handler, string eventName)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await handler();
            }
            catch (Exception ex)
            {
                CH.Error($"适配器事件后台分发失败: {eventName} - {ex.Message}");
            }
        });

        return Task.CompletedTask;
    }
}
