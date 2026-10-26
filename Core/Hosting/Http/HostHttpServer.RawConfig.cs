using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ShiroBot.Adapters;
using ShiroBot.Components.Reloading;
using ShiroBot.Plugins;
using Tomlyn;
using Tomlyn.Parsing;

namespace ShiroBot.Hosting.Http;

/// <summary>
/// Raw config.toml editing for the Dashboard: every key in the file, including ones the component's
/// schema does not describe. A plugin edits its whole file with comments kept; an adapter instance in a
/// shared instances file edits only its own config table, re-serialized without comments.
/// </summary>
internal sealed partial class HostHttpServer
{
    private const int MaxRawConfigLength = 1024 * 1024;

    private sealed record RawConfigUpdateRequest(string? Content, string? Revision);

    private static void MapRawConfigEndpoints(
        RouteGroupBuilder api,
        PluginManager pluginManager,
        AdapterPackageManager adapterPackages,
        AdapterManager adapterManager,
        ComponentReloadCoordinator reloadCoordinator)
    {
        api.MapGet("/plugins/{id}/config/raw", (string id) =>
        {
            var plugin = FindPluginForConfig(pluginManager, id);
            if (plugin is null) return Results.NotFound(new { error = "plugin_not_found", message = $"未找到插件: {id}" });
            var path = GetPluginConfigPath(pluginManager, plugin.AssemblyPath, plugin.Id);
            return Results.Ok(CreateRawConfigResponse(File.Exists(path) ? File.ReadAllText(path) : string.Empty));
        });

        api.MapPut("/plugins/{id}/config/raw", async (string id, HttpContext context) =>
        {
            var plugin = FindPluginForConfig(pluginManager, id);
            if (plugin is null) return Results.NotFound(new { error = "plugin_not_found", message = $"未找到插件: {id}" });
            var path = GetPluginConfigPath(pluginManager, plugin.AssemblyPath, plugin.Id);
            var (request, error) = await ReadRawConfigRequestAsync(context, File.Exists(path) ? File.ReadAllText(path) : string.Empty).ConfigureAwait(false);
            if (error is not null) return error;

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, request!.Content, context.RequestAborted).ConfigureAwait(false);
            try
            {
                var applied = await pluginManager.ApplyConfigByIdAsync(plugin.Id).ConfigureAwait(false);
                return Results.Ok(CreateRawConfigResponse(request.Content!, applied ? "applied" :
                    FindLoadedPlugin(pluginManager, plugin.Id) is null ? "pending_start" : "legacy_saved_only"));
            }
            catch (Exception ex)
            {
                return Results.Conflict(new { error = "config_apply_failed", message = ex.Message, saved = true });
            }
        });

        api.MapGet("/adapters/{id}/config/raw", (string id) =>
        {
            var instance = adapterPackages.GetInstance(id);
            if (instance is null) return Results.NotFound(new { error = "adapter_not_found", message = $"未找到 Adapter: {id}" });
            return Results.Ok(CreateRawConfigResponse(ReadAdapterRawConfig(instance)));
        });

        api.MapPut("/adapters/{id}/config/raw", async (string id, HttpContext context) =>
        {
            var instance = adapterPackages.GetInstance(id);
            if (instance is null) return Results.NotFound(new { error = "adapter_not_found", message = $"未找到 Adapter: {id}" });
            var (request, error) = await ReadRawConfigRequestAsync(context, ReadAdapterRawConfig(instance)).ConfigureAwait(false);
            if (error is not null) return error;

            // Patch hands a declared instance's own table to the writer as a temporary file.
            AdapterInstanceStore.Patch(instance.ConfigPath, id, path => File.WriteAllText(path, request!.Content));
            try
            {
                var applied = await reloadCoordinator.ExecuteAdapterStartAsync(() => adapterManager.ApplyConfigByIdAsync(id)).ConfigureAwait(false);
                return Results.Ok(CreateRawConfigResponse(ReadAdapterRawConfig(instance), applied ? "applied" :
                    adapterManager.LoadedIds.Contains(id, StringComparer.OrdinalIgnoreCase) ? "legacy_saved_only" : "pending_start"));
            }
            catch (Exception ex)
            {
                return Results.Conflict(new { error = "config_apply_failed", message = ex.Message, saved = true });
            }
        });
    }

    private static string ReadAdapterRawConfig(InstalledAdapterInstance instance) =>
        AdapterInstanceStore.IsDeclared(instance.ConfigPath)
            ? TomlSerializer.Serialize(AdapterInstanceStore.ToTomlTable(AdapterInstanceStore.GetConfig(instance.ConfigPath, instance.Id)))
            : File.Exists(instance.ConfigPath) ? File.ReadAllText(instance.ConfigPath) : string.Empty;

    private static object CreateRawConfigResponse(string content, string? applyStatus = null) => new
    {
        content,
        revision = GetRawConfigRevision(content),
        apply_status = applyStatus
    };

    internal static string GetRawConfigRevision(string content) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant();

    /// <summary>Reads and validates a raw config update; returns the error result instead when it is rejected.</summary>
    private static async Task<(RawConfigUpdateRequest? Request, IResult? Error)> ReadRawConfigRequestAsync(HttpContext context, string current)
    {
        RawConfigUpdateRequest? request;
        try
        {
            request = await JsonSerializer.DeserializeAsync<RawConfigUpdateRequest>(context.Request.Body, JsonSerializerOptions.Web, context.RequestAborted)
                .ConfigureAwait(false);
        }
        catch (JsonException)
        {
            return (null, Results.BadRequest(new { error = "invalid_json", message = "请求内容不是有效 JSON。" }));
        }

        if (request?.Content is null)
            return (null, Results.BadRequest(new { error = "invalid_request", message = "请求必须包含 content。" }));
        if (request.Content.Length > MaxRawConfigLength)
            return (null, Results.BadRequest(new { error = "config_too_large", message = "配置文件不能超过 1MB。" }));
        // Someone (an editor, the plugin or another Dashboard tab) saved after this draft was loaded.
        if (!string.IsNullOrEmpty(request.Revision) && request.Revision != GetRawConfigRevision(current))
            return (null, Results.Conflict(new { error = "config_changed", message = "配置文件已被其他地方修改，请重新加载后再编辑。", content = current, revision = GetRawConfigRevision(current) }));
        if (ValidateToml(request.Content) is { } invalid)
            return (null, Results.BadRequest(new { error = "invalid_toml", message = "TOML 格式错误: " + invalid }));
        return (request, null);
    }

    internal static string? ValidateToml(string content)
    {
        try
        {
            SyntaxParser.ParseStrict(content);
            TomlSerializer.Deserialize<Dictionary<string, object?>>(content);
            return null;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return ex.Message;
        }
    }
}
