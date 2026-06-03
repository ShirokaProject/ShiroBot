using ShiroBot.Hosting.Context;
using CH = ShiroBot.Core.ConsoleHelper;

namespace ShiroBot.Core;

internal sealed class CoreConfigWatcher : IDisposable
{
    private readonly CoreConfig _activeConfig;
    private readonly BotContext _botContext;
    private readonly IDisposable _watcherSubscription;

    public CoreConfigWatcher(string coreConfigPath, CoreConfig initialConfig, BotContext botContext)
    {
        _activeConfig = initialConfig;
        _botContext = botContext;

        _watcherSubscription = ConfigContext.ForCore(coreConfigPath).Watch<CoreConfig>(updated =>
        {
            ApplyCoreConfigChange(_activeConfig, updated);
        });
    }

    private void ApplyCoreConfigChange(CoreConfig active, CoreConfig updated)
    {
        if (active is null || updated is null) return;

        var changes = new List<string>();

        if (!ArrayEquals(active.OwnerList, updated.OwnerList))
        {
            active.OwnerList = updated.OwnerList;
            _botContext?.UpdateOwnerList(updated.OwnerList);
            changes.Add($"owners[{updated.OwnerList.Length}]");
        }

        if (!ArrayEquals(active.AdminList, updated.AdminList))
        {
            active.AdminList = updated.AdminList;
            _botContext?.UpdateAdminList(updated.AdminList);
            changes.Add($"admins[{updated.AdminList.Length}]");
        }

        if (active.EnableLog != updated.EnableLog)
        {
            active.EnableLog = updated.EnableLog;
            CH.IsEnabled = updated.EnableLog;
            changes.Add($"enable_log={updated.EnableLog}");
        }

        if (active.DisableConsoleInput != updated.DisableConsoleInput)
        {
            // 控制台输入循环是启动期一次性决定的，运行期改这个值不会即时生效。
            active.DisableConsoleInput = updated.DisableConsoleInput;
            changes.Add("disable_console_input(下次启动生效)");
        }

        if (!string.Equals(active.Protocol, updated.Protocol, StringComparison.OrdinalIgnoreCase))
        {
            active.Protocol = updated.Protocol;
            changes.Add("protocol(下次启动生效)");
        }

        if (updated.PluginRoutes is not null)
        {
            active.PluginRoutes.CopyFrom(updated.PluginRoutes);
            changes.Add("plugin_routes");
        }

        if (changes.Count > 0)
        {
            CH.Success("核心配置热重载完成: " + string.Join(", ", changes));
        }
        else
        {
            CH.Info("核心配置热重载: 无字段变化。");
        }
    }

    private static bool ArrayEquals(long[]? left, long[]? right)
    {
        if (ReferenceEquals(left, right)) return true;
        if (left is null || right is null) return false;
        if (left.Length != right.Length) return false;
        for (var i = 0; i < left.Length; i++)
        {
            if (left[i] != right[i]) return false;
        }

        return true;
    }

    public void Dispose()
    {
        _watcherSubscription.Dispose();
    }
}
