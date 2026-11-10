using ShiroBot.Hosting.Context;
using ShiroBot.Hosting.Logging;
using ShiroBot.Integrations.Avalonia;
using ShiroBot.SDK.Models;
using ShiroBot.Update;
using CH = ShiroBot.Terminal.ConsoleOutput;

namespace ShiroBot.Configuration;

internal sealed class CoreConfigWatcher : IDisposable
{
    private readonly BotContext _botContext;
    private readonly string? _hostDirectory;
    private readonly HostLogHub? _logs;
    private readonly IDisposable _watcherSubscription;

    public CoreConfigWatcher(string coreConfigPath, CoreConfig initialConfig, BotContext botContext,
        HostLogHub? logs = null, string? hostDirectory = null)
    {
        var activeConfig = initialConfig;
        _botContext = botContext;
        _logs = logs;
        _hostDirectory = hostDirectory;

        _watcherSubscription = ConfigContext.ForCore(coreConfigPath).Watch<CoreConfig>(updated =>
        {
            ApplyCoreConfigChange(activeConfig, updated);
        });
    }

    public void Dispose()
    {
        _watcherSubscription.Dispose();
    }

    private void ApplyCoreConfigChange(CoreConfig? active, CoreConfig? updated)
    {
        if (active is null || updated is null) return;
        updated.Logs.Validate();

        // Validate both lists before applying either permission or any runtime settings.
        foreach (var entry in updated.OwnerList.Concat(updated.AdminList)
                     .Where(entry => !string.IsNullOrWhiteSpace(entry)))
            UserReference.Parse(entry);
        if (!string.IsNullOrWhiteSpace(active.Api.Token) && string.IsNullOrWhiteSpace(updated.Api.Token))
            throw new InvalidOperationException("运行中的 API 令牌不能设为空；修改未应用。");
        var changes = new List<string>();
        if (active.Logs.SaveToFile != updated.Logs.SaveToFile ||
            active.Logs.RetentionDays != updated.Logs.RetentionDays)
        {
            if (_hostDirectory is not null) _logs?.Configure(updated.Logs, _hostDirectory);
            active.Logs = updated.Logs;
            changes.Add("logs");
        }

        if (!ArrayEquals(active.OwnerList, updated.OwnerList))
        {
            active.OwnerList = updated.OwnerList;
            _botContext.UpdateOwnerList(updated.OwnerList);
            changes.Add($"owners[{updated.OwnerList.Length}]");
        }

        if (!ArrayEquals(active.AdminList, updated.AdminList))
        {
            active.AdminList = updated.AdminList;
            _botContext.UpdateAdminList(updated.AdminList);
            changes.Add($"admins[{updated.AdminList.Length}]");
        }

        if (active.EnableLog != updated.EnableLog)
        {
            active.EnableLog = updated.EnableLog;
            CH.IsEnabled = updated.EnableLog;
            changes.Add($"enable_log={updated.EnableLog}");
        }

        if (active.Showid != updated.Showid)
        {
            active.Showid = updated.Showid;
            changes.Add($"showid={updated.Showid}");
        }

        if (active.DisableConsoleInput != updated.DisableConsoleInput)
        {
            // 控制台输入循环是启动期一次性决定的，运行期改这个值不会即时生效。
            active.DisableConsoleInput = updated.DisableConsoleInput;
            changes.Add("disable_console_input(下次启动生效)");
        }

        if (!string.Equals(active.AvaloniaTheme, updated.AvaloniaTheme, StringComparison.OrdinalIgnoreCase))
        {
            active.AvaloniaTheme = updated.AvaloniaTheme;
            AvaloniaIntegration.SetThemeMode(updated.AvaloniaTheme);
            changes.Add($"avalonia_theme={updated.AvaloniaTheme}");
        }

        if (!string.Equals(active.GithubProxy, updated.GithubProxy, StringComparison.Ordinal))
        {
            active.GithubProxy = updated.GithubProxy;
            Updater.SetGithubProxy(updated.GithubProxy);
            changes.Add("github_proxy");
        }

        if (!string.Equals(active.HostUpdateRepository, updated.HostUpdateRepository, StringComparison.Ordinal))
        {
            active.HostUpdateRepository = updated.HostUpdateRepository;
            changes.Add("host_update_repository");
        }

        if (active.OwnerNotifications != updated.OwnerNotifications)
        {
            active.OwnerNotifications = updated.OwnerNotifications;
            changes.Add("owner_notifications");
        }

        if (!active.Update.SameAs(updated.Update))
        {
            active.Update = updated.Update;
            changes.Add("update");
        }

        if (!string.Equals(active.Api.Token, updated.Api.Token, StringComparison.Ordinal))
        {
            active.Api.Token = updated.Api.Token;
            changes.Add("api.token");
        }

        if (active.Api.Enable != updated.Api.Enable || active.Api.EnableDashboard != updated.Api.EnableDashboard
                                                    || !ArrayEquals(active.Api.ListenUrls, updated.Api.ListenUrls)
                                                    || !ArrayEquals(active.Api.PublicBaseUrl,
                                                        updated.Api.PublicBaseUrl))
        {
            active.Api.Enable = updated.Api.Enable;
            active.Api.EnableDashboard = updated.Api.EnableDashboard;
            active.Api.ListenUrls = updated.Api.ListenUrls;
            active.Api.PublicBaseUrl = updated.Api.PublicBaseUrl;
            changes.Add("api 服务与地址设置(下次启动生效)");
        }

        if (!active.Protocols.SequenceEqual(updated.Protocols, StringComparer.OrdinalIgnoreCase))
        {
            active.Protocols = updated.Protocols;
            changes.Add("protocols(下次启动生效)");
        }

        active.PluginRoutes.CopyFrom(updated.PluginRoutes);
        changes.Add("plugin_routes");

        if (changes.Count > 0)
            CH.Success("核心配置热重载完成: " + string.Join(", ", changes));
        else
            CH.Info("核心配置热重载: 无字段变化。");
    }

    private static bool ArrayEquals(string[]? left, string[]? right)
    {
        if (ReferenceEquals(left, right)) return true;
        if (left is null || right is null) return false;
        return left.Length == right.Length && left.SequenceEqual(right, StringComparer.Ordinal);
    }
}