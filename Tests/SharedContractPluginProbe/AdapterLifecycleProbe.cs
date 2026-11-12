using ShiroBot.SDK.Adapter;
using ShiroBot.SDK.Config;
using ShiroBot.SDK.Core;
using ShiroBot.SDK.Plugin;

namespace ShiroBot.SharedContractPluginProbe;

// A real collectible adapter for testing the host's load/config/watch/unload boundary.
[BotAdapter("lifecycle-probe")]
public sealed class AdapterLifecycleProbe : IBotAdapter, IConfigurableAdapter,
    IConfigurableComponent<AdapterLifecycleProbeConfig>, ILifetimeAwareAdapter, IPluginActionProvider
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
    public IPluginLifetime Lifetime { get; set; } = null!;
    public IReadOnlyList<PluginActionDescriptor> Actions =>
        [new("ping", "Ping"), new("ping", "Duplicate"), new("", "Invalid")];
    public async Task<PluginActionResult> ExecuteActionAsync(string actionId, CancellationToken cancellationToken = default)
    {
        await Task.Delay(50, cancellationToken);
        return new PluginActionResult(true, actionId) { Details = CurrentConfigValue.Endpoint };
    }
    private int _ticks;

    // 模拟接收循环和心跳：只登记到 Lifetime，StopAsync 不做清理。宿主没停掉它们的话，
    // 运行中的计时器会经由闭包拉住适配器和它的 ALC，热重载和卸载验证会失败。
    public async Task StartAsync()
    {
        await Task.Delay(10).ConfigureAwait(false);
        _ = Lifetime.Run(async token =>
        {
            while (true)
            {
                await Task.Delay(20, token).ConfigureAwait(false);
                _ticks++;
            }
        });
        Lifetime.SetInterval(TimeSpan.FromMilliseconds(20), _ =>
        {
            _ticks++;
            return Task.CompletedTask;
        });
    }
    public async Task StopAsync() => await Task.Delay(10).ConfigureAwait(false);
    public TService? GetExtension<TService>() where TService : class => this as TService;
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

    // 元素类型定义在适配器自己的程序集里：生成默认配置时不能让宿主的 JSON 缓存记住它。
    [ConfigField("Probe modes.", Default = "[\"Second\", 0]")]
    public AdapterLifecycleProbeMode[] Modes { get; set; } = [];
}

public enum AdapterLifecycleProbeMode
{
    First = 0,
    Second = 1
}
