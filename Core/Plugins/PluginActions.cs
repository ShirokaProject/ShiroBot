using ShiroBot.SDK.Abstractions;
using ShiroBot.SDK.Plugin;

namespace ShiroBot.Plugins;

internal enum PluginActionStatus
{
    Done,
    ActionNotFound,
    Unloading,
    Failed
}

/// <summary>What running an action came to; <see cref="Message" /> is always fit to show the user.</summary>
internal sealed record PluginActionOutcome(
    PluginActionStatus Status,
    string Message,
    PluginActionDescriptor? Action = null,
    PluginActionResult? Result = null);

/// <summary>
///     Lists and runs <see cref="IPluginActionProvider" /> actions for both the Dashboard and the console, so they find
///     plugins, filter actions and report failures the same way. A throwing plugin becomes a failed outcome with its
///     reason, never an exception for the caller.
/// </summary>
internal static class PluginActions
{
    /// <summary>A loaded plugin by ID or display name.</summary>
    public static LoadedPluginHandle? FindPlugin(PluginManager pluginManager, string id)
    {
        return pluginManager.GetLoadedPluginSnapshot().FirstOrDefault(plugin =>
            string.Equals(plugin.Name, id, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(plugin.DisplayName, id, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The plugin's usable actions (ID and label set, first of each ID); null while it unloads.</summary>
    public static async Task<IReadOnlyList<PluginActionDescriptor>?> ListAsync(LoadedPluginHandle plugin)
    {
        if (!plugin.Supports<IPluginActionProvider>()) return [];
        var dispatch = await plugin
            .DispatchAsync<IPluginActionProvider, IReadOnlyList<PluginActionDescriptor>>(provider =>
                Task.FromResult(provider.Actions)).ConfigureAwait(false);
        if (!dispatch.Dispatched) return null;
        return
        [
            .. (dispatch.Result ?? [])
            .Where(action => !string.IsNullOrWhiteSpace(action.Id) && !string.IsNullOrWhiteSpace(action.Label))
            .GroupBy(action => action.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
        ];
    }

    /// <summary>Every loaded plugin's actions; a plugin whose list fails is logged and left out.</summary>
    public static async Task<IReadOnlyList<(string PluginId, PluginActionDescriptor Action)>> ListAllAsync(
        PluginManager pluginManager)
    {
        var collected = new List<(string PluginId, PluginActionDescriptor Action)>();
        foreach (var plugin in pluginManager.GetLoadedPluginSnapshot()
                     .OrderBy(plugin => plugin.Name, StringComparer.OrdinalIgnoreCase))
            try
            {
                collected.AddRange((await ListAsync(plugin).ConfigureAwait(false) ?? [])
                    .Select(action => (plugin.Name, action)));
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                BotLog.Error($"读取插件操作列表失败: {plugin.Name} - {ex.Message}");
            }

        return collected;
    }

    public static async Task<PluginActionOutcome> ExecuteAsync(
        LoadedPluginHandle plugin,
        string actionId,
        CancellationToken cancellationToken = default)
    {
        if (!plugin.Supports<IPluginActionProvider>())
            return new PluginActionOutcome(PluginActionStatus.ActionNotFound, $"插件 {plugin.Name} 未提供操作。");

        PluginActionDescriptor? descriptor = null;
        try
        {
            var dispatch = await plugin.DispatchAsync<IPluginActionProvider, PluginActionResult?>(async provider =>
            {
                descriptor = provider.Actions.FirstOrDefault(action =>
                    string.Equals(action.Id, actionId, StringComparison.OrdinalIgnoreCase));
                return descriptor is null
                    ? null
                    : await provider.ExecuteActionAsync(descriptor.Id, cancellationToken).ConfigureAwait(false);
            }).ConfigureAwait(false);

            if (!dispatch.Dispatched)
                return new PluginActionOutcome(PluginActionStatus.Unloading, $"插件 {plugin.Name} 正在卸载。");
            if (descriptor is null)
                return new PluginActionOutcome(PluginActionStatus.ActionNotFound, $"未找到插件操作: {actionId}");
            return dispatch.Result is not { } result
                ? new PluginActionOutcome(PluginActionStatus.Failed, $"插件操作 {descriptor.Label} 没有返回结果。", descriptor)
                : new PluginActionOutcome(PluginActionStatus.Done, result.Message, descriptor, result);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            BotLog.Error($"执行插件操作失败: {plugin.Name}/{actionId} - {ex}");
            return new PluginActionOutcome(PluginActionStatus.Failed, $"插件操作执行失败: {ex.Message}", descriptor);
        }
    }
}