using ShiroBot.Model.Common;

namespace ShiroBot.SDK.Adapter;

public interface IEventService
{
    event Func<Event, Task> EventReceived;
}
