using ShiroBot.SDK.Models;

namespace ShiroBot.SDK.Plugin;

/// <summary>
/// Receives bot events from the host. Implementations that do not derive from
/// <see cref="PluginBase"/> receive all event types and may filter them in
/// <see cref="OnEventAsync"/>.
/// </summary>
public interface IBotEventSubscriber
{
    Task OnEventAsync(BotEvent e);
}
