using ShiroBot.SDK.Adapter;
using ShiroBot.SDK.Models;
using CH = ShiroBot.Core.ConsoleHelper;

namespace ShiroBot.Hosting;

internal sealed class AdapterEventBridge(HostEventDispatcher eventDispatcher)
{
    public void Bridge(
        IEventService eventService,
        Func<MessageEvent, Task> directMessageHandler)
    {
        eventService.EventReceived += message => DispatchAdapterEventInBackground(
            () => message is MessageEvent { IsDirect: true } directMessage
                ? directMessageHandler(directMessage)
                : eventDispatcher.PublishAsync(message),
            message.GetType().Name);
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
