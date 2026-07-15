using ShiroBot.SDK.Adapter;
using ShiroBot.SDK.Config;
using ShiroBot.SDK.Plugin;

namespace ShiroBot.SDK.Core;

public interface IBotAdapter
{
    IConfigContext Config { get; set; }
    IConsoleLogger Logger { get; set; }
    public IFileService File { get; }
    public IFriendService Friend { get; }
    public IGroupService Group { get; }
    public IMessageService Message { get; }
    public ISystemService System { get; }
    public IEventService Event { get; }

    Task StartAsync() => Task.CompletedTask;
    Task StopAsync() => Task.CompletedTask;
}
