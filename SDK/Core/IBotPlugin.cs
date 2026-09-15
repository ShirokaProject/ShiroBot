using ShiroBot.SDK.Plugin;

namespace ShiroBot.SDK.Core;

public interface IBotPlugin
{
    string Name { get; }

    Task OnLoad(IBotContext context);
    Task OnUnload();
}
