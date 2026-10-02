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
                    ApplyConfigPatch(document.RootElement, config, configManager, configPath);
                    return Results.Ok(new
                    {
                        ok = true,
                        msg = "配置更新成功",
                        schema = GetComponentConfigSchema(typeof(CoreConfig).Assembly)
                    });
                }
                catch (InvalidOperationException ex)
                {
                    return Results.BadRequest(new { ok = false, msg = ex.Message });
                }
            }
        });
    }

    private static object CreateConfigResponse(CoreConfig config) => new
    {
        schema = GetComponentConfigSchema(typeof(CoreConfig).Assembly),
        protocol = config.Protocols.FirstOrDefault() ?? string.Empty,
        protocols = config.Protocols,
        enable_log = config.EnableLog,
        showid = config.Showid,
        disable_console_input = config.DisableConsoleInput,
        github_proxy = config.GithubProxy,
        host_update_repository = config.HostUpdateRepository,
        avalonia_theme = config.AvaloniaTheme,
        owner_list = config.OwnerList,
        admin_list = config.AdminList,
        api = new
        {
            enable = config.Api.Enable,
            listen_url = config.Api.ListenUrls.FirstOrDefault() ?? ApiHostConfig.DefaultListenUrl,
            listen_urls = config.Api.ListenUrls,
            public_base_url = config.Api.PublicBaseUrl,
            auth_enable = config.Api.Auth.Enable,
            token = config.Api.Auth.Key
        }
    };

    private static void ApplyConfigPatch(
        JsonElement patch,
        ApiHostConfig currentApiConfig,
        ConfigManager configManager,
        string configPath)
    {
        // Validated before anything is written, so a rejected value leaves the file untouched.
        var hasShowid = TryGetBool(patch, "showid", out var showid);
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

        // Legacy single value: an empty one means "no extra adapter", not a list holding "".
        if (TryGetString(patch, "protocol", out var protocol))
        {
            configManager.SetConfigValue(configPath, "protocols",
                string.IsNullOrWhiteSpace(protocol) ? Array.Empty<string>() : [protocol.Trim()]);
        }

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

        if (TryGetIdArray(patch, "owner_list", out var ownerList))
        {
            configManager.SetConfigValue(configPath, "owner_list", ownerList);
        }

        if (TryGetIdArray(patch, "admin_list", out var adminList))
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

        if (TryGetString(apiPatch, "listen_url", out var listenUrl))
        {
            currentApiConfig.ListenUrls = [listenUrl];
            configManager.SetConfigValue(configPath, "api.listen_urls", currentApiConfig.ListenUrls);
        }

        if (TryGetStringArray(apiPatch, "listen_urls", out var listenUrls))
        {
            currentApiConfig.ListenUrls = listenUrls;
            configManager.SetConfigValue(configPath, "api.listen_urls", listenUrls);
        }

        if (TryGetNullableString(apiPatch, "public_base_url", out var publicBaseUrl))
        {
            currentApiConfig.PublicBaseUrl = publicBaseUrl;
            configManager.SetConfigValue(configPath, "api.public_base_url", publicBaseUrl ?? string.Empty);
        }

        if (TryGetBool(apiPatch, "auth_enable", out var authEnable))
        {
            currentApiConfig.Auth.Enable = authEnable;
            configManager.SetConfigValue(configPath, "api.auth.enable", authEnable);
        }

        if (TryGetString(apiPatch, "token", out var token))
        {
            currentApiConfig.Auth.Key = token;
            configManager.SetConfigValue(configPath, "api.auth.key", token);
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
