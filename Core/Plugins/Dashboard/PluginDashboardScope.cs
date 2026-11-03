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
    DashboardWebPage? Web = null);

/// <summary>
/// 一个插件的 <see cref="IPluginDashboard"/>：页面登记在插件的生命周期上，卸载时注销；
/// 渲染和处理函数通过插件的调用器执行，计入插件正在处理的调用，卸载会等它们结束。
/// </summary>
internal sealed class PluginDashboardScope(PluginInvoker invoke, PluginLifetimeScope lifetime) : IPluginDashboard
{
    private readonly ConcurrentDictionary<string, DashboardPageRegistration> _pages = new(StringComparer.OrdinalIgnoreCase);

    public CancellationToken Stopping => lifetime.Stopping;

    public IDisposable AddWebPage(string id, string title, DashboardWebPage page)
    {
        ArgumentNullException.ThrowIfNull(page);
        return Register(new DashboardPageRegistration(id, title, page.Icon, page.Description, page.Order,
            (_, _) => Task.FromResult<UiNode>(Ui.Column()), null, page));
    }

    public IDisposable AddPage(string id, string title, Func<DashboardContext, CancellationToken, Task<UiNode>> render,
        string? icon = null, string? description = null, int order = 100,
        Func<string, CancellationToken, Task<DashboardFile?>>? files = null)
        => Register(new DashboardPageRegistration(id, title, icon, description, order, render, files));

    private IDisposable Register(DashboardPageRegistration page)
    {
        var (id, title, _, _, _, render, _, _) = page;
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(render);
        if (id.Length > 64 || id.Any(ch => !(char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.')))
            throw new ArgumentException("页面 ID 只能用字母、数字、-、_ 和 .，最长 64 个字符。", nameof(id));

        if (!_pages.TryAdd(id, page)) throw new InvalidOperationException($"页面 {id} 已经注册过。");
        return lifetime.Track(new Registration(() => _pages.TryRemove(new KeyValuePair<string, DashboardPageRegistration>(id, page))));
    }

    /// <summary>按顺序排列的页面。</summary>
    public IReadOnlyList<DashboardPageRegistration> Pages =>
        _pages.Values.OrderBy(page => page.Order).ThenBy(page => page.Id, StringComparer.OrdinalIgnoreCase).ToArray();

    public DashboardPageRegistration? Find(string id) => _pages.GetValueOrDefault(id);

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
