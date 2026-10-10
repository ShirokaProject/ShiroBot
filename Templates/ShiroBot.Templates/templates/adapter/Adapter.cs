using ShiroBot.SDK.Adapter;
using ShiroBot.SDK.Config;
using ShiroBot.SDK.Core;
using ShiroBot.SDK.Models;
using ShiroBot.SDK.Plugin;

[assembly: ShiroBotApiCompatibility("1.0", "1.0")]

#if (useQq)
[assembly: RequiresShiroBotPackage("shirobot.model.qq", MinimumVersion = "0.9.4")]
#endif
#if (useDiscord)
[assembly: RequiresShiroBotPackage("shirobot.model.discord", MinimumVersion = "0.9.4")]
#endif
#if (useTelegram)
[assembly: RequiresShiroBotPackage("shirobot.model.telegram", MinimumVersion = "0.9.4")]
#endif

namespace AdapterTemplate;

[BotAdapter("AdapterTemplate", Name = "AdapterTemplate", Version = "TemplateVersion", Author = "TemplateAuthor")]
public sealed class Adapter : IBotAdapter, IConfigurableAdapter, IConfigurableComponent<AdapterConfig>
{
    private readonly AdapterEventService _events = new();

    public IConfigContext Config { get; set; } = null!;
    public IConsoleLogger Logger { get; set; } = null!;
    public AdapterConfig CurrentConfigValue { get; private set; } = new();
    ConfigApplyMode IConfigurableAdapter.ApplyMode => ConfigApplyMode.RestartComponent;
    public string Platform => "template-platform";
    public IMessageService Message { get; } = new AdapterMessageService();
    public IChannelService Channel { get; } = new AdapterChannelService();
    public IUserService User { get; } = new AdapterUserService();
    public IEventService Event => _events;

    public Task StartAsync()
    {
        Logger.Info($"AdapterTemplate started: {CurrentConfigValue.Endpoint}");
        // Connect to the platform and publish mapped events through _events.PublishAsync(...).
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        Logger.Info("AdapterTemplate stopped.");
        return Task.CompletedTask;
    }

    public Task OnConfigLoadedAsync(AdapterConfig config, CancellationToken cancellationToken)
    {
        CurrentConfigValue = config;
        return Task.CompletedTask;
    }

    public Task OnConfigChangedAsync(AdapterConfig previous, AdapterConfig current, CancellationToken cancellationToken)
    {
        CurrentConfigValue = current;
        return Task.CompletedTask;
    }
}

[ConfigModel]
public sealed class AdapterConfig
{
    [ConfigField("Platform API endpoint.", Label = "Endpoint", Group = "connection", GroupLabel = "Connection", GroupOrder = 10, Order = 10)]
    public string Endpoint { get; set; } = "https://example.invalid/";
}

internal sealed class AdapterMessageService : IMessageService
{
    public Task<SentMessage> SendMessageAsync(Channel channel, IReadOnlyList<MessageSegment> segments, CancellationToken cancellationToken = default)
    {
        // Map common message segments to the platform API and return its message ID.
        throw new NotImplementedException();
    }
}

internal sealed class AdapterChannelService : IChannelService;

internal sealed class AdapterUserService : IUserService;

internal sealed class AdapterEventService : IEventService
{
    public event Func<BotEvent, Task> EventReceived = delegate { return Task.CompletedTask; };

    public async Task PublishAsync(BotEvent botEvent)
    {
        foreach (var handler in EventReceived.GetInvocationList().Cast<Func<BotEvent, Task>>())
        {
            await handler(botEvent);
        }
    }
}
