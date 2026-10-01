using ShiroBot.SDK.Core;
using ShiroBot.SDK.Plugin;
using ShiroBot.SDK.Adapter;
using ShiroBot.SDK.Config;
using ShiroBot.SDK.Models;

[assembly: ShiroBotApiCompatibility("0.9", "0.9")]
namespace ShiroBot.UpdateProbe;

internal static class BuildInfo
{
#if PROBE_SECOND_INSTANCE
    public const string AdapterId = "update-probe.second-adapter";
#else
    public const string AdapterId = "update-probe.adapter";
#endif
#if PROBE_V2
    public const string Version = "2.0.0";
#else
    public const string Version = "1.0.0";
#endif
}

[BotPlugin("update-probe.plugin", Name = "Update integration probe", Version = BuildInfo.Version,
    GithubRepo = "update-integration/plugin")]
public sealed class ProbePlugin : PluginBase
{
    public override string Name => "update-probe.plugin";
    protected override Task OnUnloadAsync()
    {
        if (File.Exists(Path.Combine(Path.GetDirectoryName(Context.Config.ConfigPath)!, "hold-unload")))
            throw new InvalidOperationException("Integration probe deliberately refused unload.");
        return Task.CompletedTask;
    }
}

[BotAdapter(BuildInfo.AdapterId, Name = "Update integration adapter", Version = BuildInfo.Version,
    GithubRepo = "update-integration/adapter", Protocol = "update-probe")]
public sealed class ProbeAdapter : IBotAdapter
{
    public IConfigContext Config { get; set; } = null!;
    public IConsoleLogger Logger { get; set; } = null!;
    public string Platform => "update-probe";
    public IMessageService Message { get; } = new ProbeMessages();
    public IChannelService Channel { get; } = new ProbeChannels();
    public IUserService User { get; } = new ProbeUsers();
    public IEventService Event { get; } = new ProbeEvents();
    public Task StartAsync()
    {
        if (File.Exists(Path.Combine(Path.GetDirectoryName(Config.ConfigPath)!, "fail-start")))
            throw new InvalidOperationException("Integration probe deliberately refused startup.");
        return Task.CompletedTask;
    }
    public Task StopAsync() => Task.CompletedTask;
}
internal sealed class ProbeMessages : IMessageService
{
    public Task<SentMessage> SendMessageAsync(Channel channel, IReadOnlyList<MessageSegment> segments) =>
        Task.FromResult(new SentMessage("probe"));
}
internal sealed class ProbeChannels : IChannelService;
internal sealed class ProbeUsers : IUserService;
internal sealed class ProbeEvents : IEventService
{
    public event Func<BotEvent, Task> EventReceived { add { } remove { } }
}
