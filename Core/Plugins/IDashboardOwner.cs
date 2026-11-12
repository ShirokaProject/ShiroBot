using ShiroBot.Plugins.Dashboard;
namespace ShiroBot.Plugins;
internal interface IDashboardOwner
{
    string Name { get; }
    PluginDashboardScope? Dashboard { get; }
}
