using ShiroBot.SDK.Adapter;
using ShiroBot.SDK.Config;
using ShiroBot.SDK.Plugin;

namespace ShiroBot.SDK.Core;

public interface IBotAdapter
{
    string Name { get; }

    [Obsolete("Declare BotAdapterAttribute on the adapter type. Legacy Metadata implementations remain supported.")]
    BotComponentMetadata Metadata => BotAdapterMetadata.FromAdapterType(GetType(), Name);
    IConfigContext Config { get; set; }
    IConsoleLogger Logger { get; set; }
    public IFileService File { get; }
    public IFriendService Friend { get; }
    public IGroupService Group { get; }
    public IMessageService Message { get; }
    public ISystemService System { get; }
    public IEventService Event { get; }

    Task StartAsync();

    Task StopAsync() => Task.CompletedTask;
}
