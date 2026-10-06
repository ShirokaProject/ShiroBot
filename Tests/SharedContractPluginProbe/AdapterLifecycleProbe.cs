using ShiroBot.SDK.Adapter;
using ShiroBot.SDK.Config;
using ShiroBot.SDK.Core;
using ShiroBot.SDK.Plugin;

namespace ShiroBot.SharedContractPluginProbe;

// A real collectible adapter for testing the host's load/config/watch/unload boundary.
[BotAdapter("lifecycle-probe")]
public sealed class AdapterLifecycleProbe : IBotAdapter, IConfigurableAdapter,
    IConfigurableComponent<AdapterLifecycleProbeConfig>
{
    public string Platform => "lifecycle-probe";
    public IMessageService Message { get; } = new ProbeMessages();
    public IChannelService Channel { get; } = new ProbeChannels();
    public IUserService User { get; } = new ProbeUsers();
    public IEventService Event { get; } = new ProbeEvents();
    public IConfigContext Config { get; set; } = null!;
    public IConsoleLogger Logger { get; set; } = null!;
    public AdapterLifecycleProbeConfig CurrentConfigValue { get; private set; } = new();
    public ConfigApplyMode ApplyMode => ConfigApplyMode.Live;
    public async Task StartAsync() => await Task.Delay(10).ConfigureAwait(false);
    public async Task StopAsync() => await Task.Delay(10).ConfigureAwait(false);
    public TService? GetExtension<TService>() where TService : class => null;
    public Task OnConfigLoadedAsync(AdapterLifecycleProbeConfig config, CancellationToken cancellationToken)
    {
        CurrentConfigValue = config;
        Config.Save(config);
        return Task.CompletedTask;
    }
    public Task OnConfigChangedAsync(AdapterLifecycleProbeConfig previous, AdapterLifecycleProbeConfig current,
        CancellationToken cancellationToken) => OnConfigLoadedAsync(current, cancellationToken);

    private sealed class ProbeMessages : IMessageService;
    private sealed class ProbeChannels : IChannelService;
    private sealed class ProbeUsers : IUserService;
    private sealed class ProbeEvents : IEventService
    {
        public event Func<ShiroBot.SDK.Models.BotEvent, Task>? EventReceived { add { } remove { } }
    }
}

public sealed class AdapterLifecycleProbeConfig
{
    public string Endpoint { get; set; } = "https://probe.invalid/";
}
