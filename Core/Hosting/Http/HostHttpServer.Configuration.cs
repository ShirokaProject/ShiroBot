using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO.Compression;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using ShiroBot.Configuration;
using ShiroBot.Hosting.Events;
using ShiroBot.Hosting.Context;
using ShiroBot.Adapters;
using ShiroBot.Components.Reloading;
using ShiroBot.Hosting.Logging;
using ShiroBot.Hosting.Runtime;
using ShiroBot.Packages;
using ShiroBot.Plugins;
using ShiroBot.Plugins.Loading;
using ShiroBot.Plugins.Marketplace;
using ShiroBot.Update;
using ShiroBot.Console;
using ShiroBot.Integrations.Avalonia;
using ShiroBot.SDK.Abstractions;
using ShiroBot.SDK.Plugin;

namespace ShiroBot.Hosting.Http;

internal sealed partial class HostHttpServer
{
    private static void MapConfigurationEndpoints(RouteGroupBuilder api, ApiHostConfig config, ConfigManager configManager, string configPath)
    {
        api.MapGet("/config", async () => Results.Ok(CreateConfigResponse(await configManager.LoadCoreConfig().ConfigureAwait(false))));
        api.MapPatch("/config", async (HttpContext context) =>
        {
            JsonDocument document;
            try
            {
                document = await JsonDocument.ParseAsync(context.Request.Body, cancellationToken: context.RequestAborted)
                    .ConfigureAwait(false);
            }
            catch (JsonException)
            {
                return Results.BadRequest(new { ok = false, msg = "配置格式不是有效 JSON" });
            }

            using (document)
            {
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                {
                    return Results.BadRequest(new { ok = false, msg = "配置更新内容必须是对象" });
                }

                try
                {
                    if (!document.RootElement.TryGetProperty("config", out var configPatch) ||
                        configPatch.ValueKind != JsonValueKind.Object)
                    {
                        return Results.BadRequest(new { ok = false, msg = "请求必须包含 config 对象。" });
                    }

                    ApplyConfigPatch(configPatch, config, configManager, configPath);
                    return Results.Ok(new
                    {
                        ok = true,
                        msg = "配置更新成功",
                        schema = GetCoreConfigSchema()
                    });
                }
                catch (InvalidOperationException ex)
                {
                    return Results.BadRequest(new { ok = false, msg = ex.Message });
                }
            }
        });
    }

    private static readonly JsonSerializerOptions CoreConfigJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static object[] GetCoreConfigSchema() => GetComponentConfigSchema(typeof(CoreConfig).Assembly)
        .OfType<ConfigSchemaItem>()
        .Where(item => item.Key is not "protocols" and not "plugin_routes")
        .Cast<object>()
        .ToArray();

    private static object CreateConfigResponse(CoreConfig config) => new
    {
        schema = GetCoreConfigSchema(),
        config = JsonSerializer.SerializeToElement(config, CoreConfigJsonOptions)
    };

    private static void ApplyConfigPatch(
        JsonElement patch,
        ApiHostConfig currentApiConfig,
        ConfigManager configManager,
        string configPath)
    {
        if (patch.TryGetProperty("api", out var requestedApi) && requestedApi.ValueKind == JsonValueKind.Object &&
            TryGetString(requestedApi, "token", out var requestedToken) && string.IsNullOrWhiteSpace(requestedToken))
            throw new InvalidOperationException("运行中的 API 令牌不能设为空；可在控制台执行 api token 生成新令牌。");
        // Validated before anything is written, so a rejected value leaves the file untouched.
        var hasShowid = TryGetBool(patch, "showid", out var showid);
        var hasOwners = TryGetIdArray(patch, "owner_list", out var ownerList);
        var hasAdmins = TryGetIdArray(patch, "admin_list", out var adminList);
        try
        {
            if (hasOwners) foreach (var entry in ownerList) ShiroBot.SDK.Models.UserReference.Parse(entry);
            if (hasAdmins) foreach (var entry in adminList) ShiroBot.SDK.Models.UserReference.Parse(entry);
        }
        catch (Exception error) when (error is FormatException or ArgumentException)
        {
            throw new InvalidOperationException("权限列表必须使用 instanceId:userId，不能填写裸用户 ID。", error);
        }
        // Auto switches by the clock (dark 18:00–06:00); older dashboards sent "System" for it.
        string? avaloniaTheme = TryGetString(patch, "avalonia_theme", out var requestedTheme)
            ? requestedTheme.Trim().ToLowerInvariant() switch
            {
                "light" => "Light",
                "dark" => "Dark",
                "auto" or "system" => "Auto",
                _ => throw new InvalidOperationException("avalonia_theme 只能是 Light、Dark 或 Auto。")
            }
            : null;

        if (hasShowid) configManager.SetConfigValue(configPath, "showid", showid);

        if (TryGetStringArray(patch, "protocols", out var protocols))
        {
            configManager.SetConfigValue(configPath, "protocols",
                protocols.Select(value => value.Trim()).Where(value => value.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
        }

        if (TryGetBool(patch, "enable_log", out var enableLog))
        {
            ConsoleOutput.IsEnabled = enableLog;
            configManager.SetConfigValue(configPath, "enable_log", enableLog);
        }

        if (TryGetBool(patch, "disable_console_input", out var disableConsoleInput))
        {
            configManager.SetConfigValue(configPath, "disable_console_input", disableConsoleInput);
        }

        if (TryGetNullableString(patch, "github_proxy", out var githubProxy))
        {
            configManager.SetConfigValue(configPath, "github_proxy", githubProxy ?? string.Empty);
        }

        if (TryGetString(patch, "host_update_repository", out var hostUpdateRepository))
        {
            configManager.SetConfigValue(configPath, "host_update_repository", hostUpdateRepository);
        }

        if (avaloniaTheme is not null)
        {
            AvaloniaIntegration.SetThemeMode(avaloniaTheme);
            configManager.SetConfigValue(configPath, "avalonia_theme", avaloniaTheme);
        }

        if (hasOwners)
        {
            configManager.SetConfigValue(configPath, "owner_list", ownerList);
        }

        if (hasAdmins)
        {
            configManager.SetConfigValue(configPath, "admin_list", adminList);
        }

        if (!patch.TryGetProperty("api", out var apiPatch)) return;
        if (apiPatch.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("api 配置必须是对象");
        }

        if (TryGetBool(apiPatch, "enable", out var apiEnable))
        {
            currentApiConfig.Enable = apiEnable;
            configManager.SetConfigValue(configPath, "api.enable", apiEnable);
        }

        if (TryGetBool(apiPatch, "enable_dashboard", out var enableDashboard))
        {
            currentApiConfig.EnableDashboard = enableDashboard;
            configManager.SetConfigValue(configPath, "api.enable_dashboard", enableDashboard);
        }

        if (TryGetStringArray(apiPatch, "listen_urls", out var listenUrls))
        {
            currentApiConfig.ListenUrls = listenUrls;
            configManager.SetConfigValue(configPath, "api.listen_urls", listenUrls);
        }

        if (TryGetStringArray(apiPatch, "public_base_url", out var publicBaseUrl))
        {
            currentApiConfig.PublicBaseUrl = publicBaseUrl;
            configManager.SetConfigValue(configPath, "api.public_base_url", publicBaseUrl);
        }

        if (TryGetString(apiPatch, "token", out var token))
        {
            currentApiConfig.Token = token;
            configManager.SetConfigValue(configPath, "api.token", token);
        }
    }

    private static bool TryGetString(JsonElement element, string propertyName, out string value)
    {
        value = string.Empty;
        if (!element.TryGetProperty(propertyName, out var property)) return false;
        if (property.ValueKind != JsonValueKind.String)
        {
            throw new InvalidOperationException($"{propertyName} 必须是字符串");
        }

        value = property.GetString() ?? string.Empty;
        return true;
    }

    private static bool TryGetNullableString(JsonElement element, string propertyName, out string? value)
    {
        value = null;
        if (!element.TryGetProperty(propertyName, out var property)) return false;
        if (property.ValueKind == JsonValueKind.Null) return true;
        if (property.ValueKind != JsonValueKind.String)
        {
            throw new InvalidOperationException($"{propertyName} 必须是字符串或 null");
        }

        value = property.GetString();
        return true;
    }

    private static bool TryGetBool(JsonElement element, string propertyName, out bool value)
    {
        value = false;
        if (!element.TryGetProperty(propertyName, out var property)) return false;
        if (property.ValueKind is not JsonValueKind.True and not JsonValueKind.False)
        {
            throw new InvalidOperationException($"{propertyName} 必须是布尔值");
        }

        value = property.GetBoolean();
        return true;
    }

    /// <summary>解析平台 ID 数组：同时接受字符串和数字元素（数字会转为字符串）。</summary>
    private static bool TryGetIdArray(JsonElement element, string propertyName, out string[] value)
    {
        value = [];
        if (!element.TryGetProperty(propertyName, out var property)) return false;
        if (property.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException($"{propertyName} 必须是字符串或数字数组");
        }

        value = property.EnumerateArray().Select(item => item.ValueKind switch
        {
            JsonValueKind.String => item.GetString() ?? string.Empty,
            JsonValueKind.Number => item.GetRawText(),
            _ => throw new InvalidOperationException($"{propertyName} 必须是字符串或数字数组")
        }).ToArray();
        return true;
    }

    private static bool TryGetStringArray(JsonElement element, string propertyName, out string[] value)
    {
        value = [];
        if (!element.TryGetProperty(propertyName, out var property)) return false;
        if (property.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException($"{propertyName} 必须是字符串数组");
        }

        value = property.EnumerateArray().Select(item =>
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                throw new InvalidOperationException($"{propertyName} 必须是字符串数组");
            }

            return item.GetString() ?? string.Empty;
        }).ToArray();
        return true;
    }

}
