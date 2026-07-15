using ShiroBot.Model.Common;
using ShiroBot.SDK.Plugin;

namespace ShiroBot.LegacyPluginFixture;

public sealed class LegacyEventPlugin : PluginBase
{
    public LegacyEventPlugin()
    {
        Events.Map<FriendNudgeEvent>(_ => Task.CompletedTask);
    }
}
