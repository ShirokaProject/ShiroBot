using System.Collections.Concurrent;
using ShiroBot.Plugins.Services;
using ShiroBot.SDK.Dashboard;

namespace ShiroBot.Plugins.Dashboard;

/// <summary>插件注册的一个页面。</summary>
internal sealed record DashboardPageRegistration(
    string Id,
    string Title,
    string? Icon,
    string? Description,
    int Order,
    Func<DashboardContext, CancellationToken, Task<UiNode>> Render,
    Func<string, CancellationToken, Task<DashboardFile?>>? Files,
    DashboardWebPage? Web = null,
    IReadOnlyDictionary<string, string>? WebConfirmations = null);

/// <summary>
///     一个插件的 <see cref="IPluginDashboard" />：页面登记在插件的生命周期上，卸载时注销；
///     渲染和处理函数通过插件的调用器执行，计入插件正在处理的调用，卸载会等它们结束。
/// </summary>
internal sealed class PluginDashboardScope(PluginInvoker invoke, PluginLifetimeScope lifetime) : IPluginDashboard
{
    private static readonly string[] WebMethods = ["GET", "HEAD", "POST", "PUT", "PATCH", "DELETE", "OPTIONS"];

    private readonly ConcurrentDictionary<string, DashboardPageRegistration> _pages =
        new(StringComparer.OrdinalIgnoreCase);

    public CancellationToken Stopping => lifetime.Stopping;

    /// <summary>按顺序排列的页面。</summary>
    public IReadOnlyList<DashboardPageRegistration> Pages =>
        [.. _pages.Values.OrderBy(page => page.Order).ThenBy(page => page.Id, StringComparer.OrdinalIgnoreCase)];

    public IDisposable AddWebPage(string id, string title, DashboardWebPage page)
    {
        ArgumentNullException.ThrowIfNull(page);
        return Register(new DashboardPageRegistration(id, title, page.Icon, page.Description, page.Order,
            (_, _) => Task.FromResult(Ui.Column()), null, page, ValidateConfirmations(page.Confirmations)));
    }

    public IDisposable AddPage(string id, string title, Func<DashboardContext, CancellationToken, Task<UiNode>> render,
        string? icon = null, string? description = null, int order = 100,
        Func<string, CancellationToken, Task<DashboardFile?>>? files = null)
    {
        return Register(new DashboardPageRegistration(id, title, icon, description, order, render, files));
    }

    /// <summary>检查并复制需要确认的操作；注册后插件再修改原字典不影响宿主。</summary>
    private static Dictionary<string, string> ValidateConfirmations(IReadOnlyDictionary<string, string>? confirmations)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (confirmations is null) return result;
        if (confirmations.Count > 64) throw new ArgumentException("一个网页最多声明 64 个需要确认的操作。", nameof(confirmations));
        foreach (var (key, message) in confirmations)
        {
            if (string.IsNullOrWhiteSpace(message) || message.Length > 200 || message.Any(char.IsControl))
                throw new ArgumentException($"操作 {key} 的确认提示应为 1 到 200 个字符。", nameof(confirmations));
            var space = key.IndexOf(' ');
            var valid = space < 0
                ? key.Length is > 0 and <= 100 && !key.Any(c => char.IsControl(c) || char.IsWhiteSpace(c))
                : WebMethods.Contains(key[..space]) && key[(space + 1)..] is { Length: > 0 and <= 512 } path &&
                  path.StartsWith('/') && PluginDashboard.TryWebTarget(path, out _, out var query) && !query.HasValue;
            if (!valid)
                throw new ArgumentException($"需要确认的操作 {key} 无效：应为 action 名，或 \"DELETE /路径\" 这样的大写 HTTP 方法加页面内路径。",
                    nameof(confirmations));
            result[key] = message;
        }

        return result;
    }

    private Registration Register(DashboardPageRegistration page)
    {
        var (id, title, _, _, _, render, _, _, _) = page;
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(render);
        if (id.Length > 64 || id.Any(ch => !(char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.')))
            throw new ArgumentException("页面 ID 只能用字母、数字、-、_ 和 .，最长 64 个字符。", nameof(page));

        return !_pages.TryAdd(id, page)
            ? throw new InvalidOperationException($"页面 {id} 已经注册过。")
            : lifetime.Track(new Registration(() =>
                _pages.TryRemove(new KeyValuePair<string, DashboardPageRegistration>(id, page))));
    }

    public DashboardPageRegistration? Find(string id)
    {
        return _pages.GetValueOrDefault(id);
    }

    /// <summary>在插件内执行；插件正在卸载时返回 Ran = false，不执行。</summary>
    public async Task<(bool Ran, T? Value)> RunAsync<T>(Func<Task<T>> work)
    {
        T? value = default;
        var ran = await invoke(async () => value = await work().ConfigureAwait(false)).ConfigureAwait(false);
        return (ran, value);
    }

    private sealed class Registration(Action remove) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0) remove();
        }
    }
}