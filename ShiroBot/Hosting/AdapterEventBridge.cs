using ShiroBot.Core;
using ShiroBot.Model.Common;
using ShiroBot.SDK.Adapter;
using CH = ShiroBot.Core.ConsoleHelper;

namespace ShiroBot.Hosting;

internal sealed class AdapterEventBridge(HostEventDispatcher eventDispatcher)
{
    public void Bridge(
        IEventService eventService,
        Func<FriendIncomingMessage, Task> friendMessageHandler)
    {
        eventService.EventReceived += message => DispatchAdapterEventInBackground(
            () => message is FriendIncomingMessage friendMessage
                ? friendMessageHandler(friendMessage)
                : eventDispatcher.PublishAsync(message),
            GetAdapterEventDisplayName(message));
    }

    private static string GetAdapterEventDisplayName(Event message) =>
        EventMetadataRegistry.TryGet(message.GetType(), out var metadata)
            ? metadata.Description
            : message.GetType().Name;

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
