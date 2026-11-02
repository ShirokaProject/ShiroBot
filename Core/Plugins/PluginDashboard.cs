using ShiroBot.SDK.Abstractions;
using ShiroBot.SDK.Dashboard;
using ShiroBot.SDK.Plugin;

namespace ShiroBot.Plugins;

internal sealed record PluginPageFile(Stream Content, string ContentType);

internal enum PluginPageStatus { Done, NotFound, Unloading, Failed }

/// <summary>调用插件页面的结果；<see cref="Message"/> 可以直接给用户看。</summary>
internal sealed record PluginPageOutcome<T>(PluginPageStatus Status, string Message, T? Value = default);

/// <summary>
/// 在进程内调用 <see cref="IPluginDashboardProvider"/>：列出页面、读取数据、执行操作和读取文件。
/// 只接受页面上声明过的操作；插件抛出的异常变成失败结果和原因，不抛给调用方。
/// </summary>
internal static class PluginDashboard
{
    private const int MaxPageSize = 100;

    /// <summary>插件的可用页面（ID、标题和展示方式有效，同 ID 取第一个），按顺序排列；插件正在卸载时为 null。</summary>
    public static async Task<IReadOnlyList<DashboardPage>?> ListAsync(LoadedPluginHandle plugin)
    {
        if (!plugin.Supports<IPluginDashboardProvider>()) return [];
        var dispatch = await plugin.DispatchAsync<IPluginDashboardProvider, IReadOnlyList<DashboardPage>>(
            provider => Task.FromResult(provider.Pages)).ConfigureAwait(false);
        if (!dispatch.Dispatched) return null;
        return (dispatch.Result ?? [])
            .Where(page => !string.IsNullOrWhiteSpace(page.Id) && !string.IsNullOrWhiteSpace(page.Title) &&
                           page.Kind is DashboardPageKinds.Gallery or DashboardPageKinds.Table or DashboardPageKinds.List or DashboardPageKinds.Stats)
            .GroupBy(page => page.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(page => page.Order)
            .ToArray();
    }

    public static Task<PluginPageOutcome<DashboardPageData>> QueryAsync(LoadedPluginHandle plugin, string pageId, int page, int pageSize,
        IReadOnlyDictionary<string, string> filters, CancellationToken cancellationToken) =>
        RunAsync(plugin, pageId, "读取页面数据", async (provider, descriptor) =>
        {
            // 只把页面声明过的筛选项交给插件。
            var declared = (descriptor.Filters ?? [])
                .Select(filter => filter.Key)
                .Where(key => filters.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
                .ToDictionary(key => key, key => filters[key].Trim(), StringComparer.Ordinal);
            var size = descriptor.PageSize <= 0 ? 0 : Math.Clamp(pageSize <= 0 ? descriptor.PageSize : pageSize, 1, MaxPageSize);
            return await provider.QueryAsync(descriptor.Id, new DashboardQuery(Math.Max(1, page), size, declared), cancellationToken)
                .ConfigureAwait(false);
        }, cancellationToken);

    public static Task<PluginPageOutcome<PluginActionResult>> RunActionAsync(LoadedPluginHandle plugin, string pageId, string actionId,
        string itemId, CancellationToken cancellationToken) =>
        RunAsync(plugin, pageId, "执行页面操作", async (provider, descriptor) =>
        {
            var action = (descriptor.Actions ?? []).FirstOrDefault(candidate =>
                string.Equals(candidate.Id, actionId, StringComparison.OrdinalIgnoreCase));
            if (action is null) return null;
            var item = await provider.GetItemAsync(descriptor.Id, itemId, cancellationToken).ConfigureAwait(false);
            if (item is null || !string.Equals(item.Id, itemId, StringComparison.Ordinal) ||
                !(item.Actions ?? []).Contains(action.Id, StringComparer.OrdinalIgnoreCase)) return null;
            return await provider.RunActionAsync(descriptor.Id, action.Id, item.Id, cancellationToken).ConfigureAwait(false);
        }, cancellationToken);

    /// <summary>在插件调用结束前打开宿主拥有的流，文件删除或热卸载不影响正在发送的响应。</summary>
    public static Task<PluginPageOutcome<PluginPageFile>> OpenFileAsync(LoadedPluginHandle plugin, string pageId, string fileId,
        CancellationToken cancellationToken) =>
        RunAsync(plugin, pageId, "读取页面文件", async (provider, descriptor) =>
        {
            var file = await provider.OpenFileAsync(descriptor.Id, fileId, cancellationToken).ConfigureAwait(false);
            if (file is null) return null;
            if (file.Path is { } path)
            {
                try
                {
                    return new PluginPageFile(new FileStream(path, FileMode.Open, FileAccess.Read,
                        FileShare.Read | FileShare.Delete, 4096, FileOptions.Asynchronous), file.ContentType);
                }
                catch (FileNotFoundException) { return null; }
                catch (DirectoryNotFoundException) { return null; }
            }
            return file.Content is { } content
                ? new PluginPageFile(new MemoryStream(content.ToArray(), writable: false), file.ContentType)
                : null;
        }, cancellationToken);

    private static async Task<PluginPageOutcome<T>> RunAsync<T>(LoadedPluginHandle plugin, string pageId, string operation,
        Func<IPluginDashboardProvider, DashboardPage, Task<T?>> work, CancellationToken cancellationToken) where T : class
    {
        if (!plugin.Supports<IPluginDashboardProvider>())
            return new(PluginPageStatus.NotFound, $"插件 {plugin.Name} 没有页面。");

        DashboardPage? descriptor = null;
        try
        {
            var dispatch = await plugin.DispatchAsync<IPluginDashboardProvider, T?>(async provider =>
            {
                descriptor = provider.Pages.FirstOrDefault(page => string.Equals(page.Id, pageId, StringComparison.OrdinalIgnoreCase));
                return descriptor is null ? null : await work(provider, descriptor).ConfigureAwait(false);
            }).ConfigureAwait(false);

            if (!dispatch.Dispatched) return new(PluginPageStatus.Unloading, $"插件 {plugin.Name} 正在卸载。");
            if (descriptor is null) return new(PluginPageStatus.NotFound, $"未找到页面: {pageId}");
            return dispatch.Result is { } value
                ? new(PluginPageStatus.Done, "", value)
                : new(PluginPageStatus.NotFound, $"{descriptor.Title}：未找到请求的内容。");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            BotLog.Error($"{operation}失败: {plugin.Name}/{pageId} - {ex}");
            return new(PluginPageStatus.Failed, $"{operation}失败: {ex.Message}");
        }
    }
}
