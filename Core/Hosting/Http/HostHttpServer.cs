using Microsoft.AspNetCore.Builder;
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

internal sealed partial class HostHttpServer(WebApplication app) : IAsyncDisposable
{
    public static async Task<HostHttpServer?> StartAsync(
        ApiHostConfig config,
        ConfigManager configManager,
        string configPath,
        PluginManager pluginManager,
        HostEventDispatcher eventDispatcher,
        PluginRouteConfig routePolicy,
        WebHostContext webHostContext,
        HostRuntimeState runtimeState,
        HostLogHub logHub,
        BotContext botContext,
        ModelPackageRegistry modelPackages,
        AdapterManager adapterManager,
        ComponentReloadCoordinator reloadCoordinator,
        AdapterPackageManager adapterPackages,
        HostPowerControl powerControl)
    {
        if (!config.Enable) return null;

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(Program).Assembly.GetName().Name,
            ContentRootPath = AppContext.BaseDirectory
        });

        builder.Logging.ClearProviders();
        var listenUrls = GetListenUrls(config).ToArray();
        builder.WebHost.UseUrls(listenUrls);
        builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = MaxPluginUploadBytes);
        builder.Services.AddCors(options =>
        {
            options.AddPolicy(ApiCorsPolicyName, policy => policy
                .AllowAnyOrigin()
                .AllowAnyHeader()
                .AllowAnyMethod());
        });
        builder.Services.AddSingleton(webHostContext);
        builder.Services.AddSingleton(runtimeState);
        builder.Services.AddSingleton(logHub);

        var app = builder.Build();

        app.UseWebSockets();
        app.UseCors(ApiCorsPolicyName);
        if (config.EnableDashboard) MapDashboardAssets(app);
        MapApiEndpoints(
            app, config, configManager, configPath, pluginManager, eventDispatcher,
            routePolicy, runtimeState, logHub, modelPackages, adapterManager, reloadCoordinator, adapterPackages, powerControl);
        MapDebugEndpoints(app, config, botContext, eventDispatcher);

        app.MapFallback((HttpContext context, WebHostContext registry) => registry.HandleRequest(context));

        await app.StartAsync().ConfigureAwait(false);
        BotLog.Info("宿主 API 服务已启动: " + string.Join(", ", listenUrls));
        return new HostHttpServer(app);
    }

    private static void MapDashboardAssets(WebApplication app)
    {
        const string dashboardPath = "/dashboard";
        const string resourcePrefix = "Assets.dashboard.";
        var assembly = typeof(Program).Assembly;
        var contentTypeProvider = new FileExtensionContentTypeProvider();

        IResult ServeDashboardFile(string path)
        {
            path = path.TrimStart('/', '\\');
            var resourcePath = path.Replace('/', '\\');
            var physicalPath = Path.Combine(
                AppContext.BaseDirectory,
                "Assets",
                "dashboard",
                path.Replace('/', Path.DirectorySeparatorChar));
            var stream = assembly.GetManifestResourceStream(resourcePrefix + resourcePath)
                         ?? assembly.GetManifestResourceStream(resourcePrefix + path.Replace('\\', '/'));
            if (stream is null)
            {
                if (!File.Exists(physicalPath)) return Results.NotFound();
                if (!contentTypeProvider.TryGetContentType(path, out var physicalContentType)) physicalContentType = "application/octet-stream";
                return Results.File(physicalPath, physicalContentType, enableRangeProcessing: true);
            }

            if (!contentTypeProvider.TryGetContentType(path, out var contentType))
            {
                contentType = "application/octet-stream";
            }

            return Results.File(stream, contentType, enableRangeProcessing: true);
        }

        IResult ServeIndex() => ServeDashboardFile("index.html");

        app.MapGet($"{dashboardPath}/favicon.png", () => ServeDashboardFile("favicon.png"));
        app.MapGet($"{dashboardPath}/assets/{{**path}}", (string path) => ServeDashboardFile($"assets/{path}"));
        app.MapGet($"{dashboardPath}/{{**path}}", ServeIndex);
    }

    private static void MapApiEndpoints(
        WebApplication app,
        ApiHostConfig config,
        ConfigManager configManager,
        string configPath,
        PluginManager pluginManager,
        HostEventDispatcher eventDispatcher,
        PluginRouteConfig routePolicy,
        HostRuntimeState runtimeState,
        HostLogHub logHub,
        ModelPackageRegistry modelPackages,
        AdapterManager adapterManager,
        ComponentReloadCoordinator reloadCoordinator,
        AdapterPackageManager adapterPackages,
        HostPowerControl powerControl)
    {
        var api = app.MapGroup("/api/v1");
        api.AddEndpointFilter(async (context, next) =>
        {
            if (!IsAuthorized(context.HttpContext, config))
            {
                return Results.Json(new ApiError("unauthorized", "Missing or invalid API key."), statusCode: StatusCodes.Status401Unauthorized);
            }

            return await next(context).ConfigureAwait(false);
        });

        //鉴权
        api.MapGet("/auth", () => Results.Ok(new { ok = true }));

        //概览
        api.MapGet("/overview", () => Results.Ok(runtimeState.CreateOverview()));

        // 重启 / 关机
        api.MapPost("/system/{action}", (string action) =>
        {
            var result = action.ToLowerInvariant() switch
            {
                "restart" => powerControl.Restart(),
                "shutdown" => powerControl.Shutdown(),
                _ => null
            };
            if (result is null) return Results.NotFound(new { ok = false, message = $"不支持的操作: {action}" });
            return result.Ok
                ? Results.Ok(new { ok = true, message = result.Message })
                : Results.Conflict(new { ok = false, message = result.Message });
        });

        api.MapGet("/models/list", () => Results.Ok(modelPackages.GetPackages().Select(model => new
        {
            id = model.Id,
            version = model.Version,
            assembly = model.AssemblyName,
            path = model.AssemblyPath,
            source = model.Source,
            reloadable = model.Reloadable
        })));

        MapHostUpdateEndpoints(api, configManager, powerControl);

        MapAdapterEndpoints(api, runtimeState, configManager, adapterManager, reloadCoordinator, adapterPackages);

        MapConfigurationEndpoints(api, config, configManager, configPath);

        MapLogEndpoints(api, logHub);

        MapPluginCatalogEndpoints(api, pluginManager);
        MapPluginInstallEndpoints(api, pluginManager, eventDispatcher, routePolicy);
        MapPluginManagementEndpoints(api, config, configManager, configPath, pluginManager, eventDispatcher, routePolicy);

        api.MapGet("/status", () => Results.Ok(new
        {
            name = "ShiroBot",
            started_at = AppStartedAt,
            uptime_seconds = (long)(DateTimeOffset.UtcNow - AppStartedAt).TotalSeconds,
            api = new
            {
                enabled = config.Enable,
                auth_enabled = config.Auth.Enable
            }
        }));
    }

    private static bool IsAuthorized(HttpContext context, ApiHostConfig config)
    {
        if (!config.Auth.Enable) return true;

        var expected = config.Auth.Key;
        if (string.IsNullOrWhiteSpace(expected)) return false;

        var authorization = context.Request.Headers.Authorization.ToString();
        if (!authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            var queryKey = context.Request.Query.TryGetValue("access_token", out var accessToken)
                ? accessToken.ToString()
                : context.Request.Query.TryGetValue("api_key", out var apiKey)
                    ? apiKey.ToString()
                    : context.Request.Query.TryGetValue("token", out var token)
                        ? token.ToString()
                        : string.Empty;

            return !string.IsNullOrWhiteSpace(queryKey) && FixedTimeEquals(queryKey, expected);
        }

        var provided = authorization["Bearer ".Length..].Trim();

        return FixedTimeEquals(provided, expected);
    }

    private static bool IsBearerAuthorized(HttpContext context, ApiHostConfig config)
    {
        if (!config.Auth.Enable) return true;

        var authorization = context.Request.Headers.Authorization.ToString();
        if (!authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return false;

        var provided = authorization["Bearer ".Length..].Trim();
        return !string.IsNullOrWhiteSpace(config.Auth.Key) && FixedTimeEquals(provided, config.Auth.Key);
    }

    private static bool FixedTimeEquals(string provided, string expected)
    {
        if (provided.Length != expected.Length) return false;

        var diff = 0;
        for (var i = 0; i < provided.Length; i++)
        {
            diff |= provided[i] ^ expected[i];
        }

        return diff == 0;
    }

    private static IEnumerable<string> GetListenUrls(ApiHostConfig config)
    {
        if (config.ListenUrls.Length > 0)
        {
            return config.ListenUrls.Where(url => !string.IsNullOrWhiteSpace(url));
        }

        return [ApiHostConfig.DefaultListenUrl];
    }

    private static readonly DateTimeOffset AppStartedAt = DateTimeOffset.UtcNow;
    private static readonly PluginMarketplaceCache MarketplaceCache = new();
    private static readonly AdapterMarketplaceCache AdapterMarketplaceCache = new();
    private const string ApiCorsPolicyName = "ShiroBotApiCors";
    private const string DisabledPluginSuffix = ".disable";
    private const long MaxPluginUploadBytes = 100L * 1024L * 1024L;
    private const long MaxPluginExtractedBytes = 500L * 1024L * 1024L;
    private const int MaxPluginArchiveEntries = 4096;
    private static readonly TimeSpan PluginUploadTtl = TimeSpan.FromMinutes(5);
    private const byte SerializedTypeBoolean = 0x02;
    private const byte SerializedTypeI1 = 0x04;
    private const byte SerializedTypeU1 = 0x05;
    private const byte SerializedTypeI2 = 0x06;
    private const byte SerializedTypeU2 = 0x07;
    private const byte SerializedTypeI4 = 0x08;
    private const byte SerializedTypeU4 = 0x09;
    private const byte SerializedTypeI8 = 0x0A;
    private const byte SerializedTypeU8 = 0x0B;
    private const byte SerializedTypeR4 = 0x0C;
    private const byte SerializedTypeR8 = 0x0D;
    private const byte SerializedTypeString = 0x0E;
    private const byte SerializedTypeSzArray = 0x1D;
    private const byte SerializedTypeObject = 0x51;
    private const byte SerializedTypeEnum = 0x55;
    private const byte ElementTypeBoolean = 0x02;
    private const byte ElementTypeI1 = 0x04;
    private const byte ElementTypeU1 = 0x05;
    private const byte ElementTypeI2 = 0x06;
    private const byte ElementTypeU2 = 0x07;
    private const byte ElementTypeI4 = 0x08;
    private const byte ElementTypeU4 = 0x09;
    private const byte ElementTypeI8 = 0x0A;
    private const byte ElementTypeU8 = 0x0B;
    private const byte ElementTypeR4 = 0x0C;
    private const byte ElementTypeR8 = 0x0D;
    private const byte ElementTypeValueType = 0x11;
    private const byte ElementTypeSzArray = 0x1D;

    // ReSharper disable NotAccessedPositionalProperty.Local
    private sealed record ApiError(
        [property: JsonPropertyName("code")] string Code,
        [property: JsonPropertyName("message")] string Message);

    private sealed record PluginUploadConfirmRequest(bool Replace = false, bool Enable = true);
    private sealed record AdapterUploadConfirmRequest(bool Replace = false, bool Enable = true);
    private sealed record AdapterGitHubInstallRequest(
        string Repository = "",
        bool IncludePrerelease = false,
        string? AssetUrl = null,
        string? AssetName = null,
        string? AssetSha256 = null);

    private sealed record GitHubPluginInstallRequest(
        string Repository = "",
        bool IncludePrerelease = false,
        string? AssetUrl = null,
        string? AssetName = null,
        string? AssetSha256 = null);


    private sealed record PluginConfigTarget(string Id, string AssemblyPath);

    private sealed record PluginActionExecution(bool Found, PluginActionResult? Result);

    private sealed record ConfigSchemaItem(
        [property: JsonPropertyName("key")] string Key,
        [property: JsonPropertyName("label")] string Label,
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("description")] string Description,
        [property: JsonPropertyName("placeholder")] string? Placeholder,
        [property: JsonPropertyName("options")] string[] Options,
        [property: JsonPropertyName("min")] double? Min,
        [property: JsonPropertyName("max")] double? Max,
        [property: JsonPropertyName("group")] string Group,
        [property: JsonPropertyName("group_id")] string GroupId,
        [property: JsonPropertyName("group_label")] string GroupLabel,
        [property: JsonPropertyName("group_icon")] string GroupIcon,
        [property: JsonPropertyName("group_description")] string GroupDescription,
        [property: JsonPropertyName("order")] int? Order,
        [property: JsonPropertyName("group_order")] int? GroupOrder,
        [property: JsonPropertyName("conditions")] IReadOnlyList<ConfigFieldConditionSchema> Conditions,
        [property: JsonPropertyName("default_value"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] object? DefaultValue,
        [property: JsonIgnore] string ValueType)
    {
        /// <summary>Fields of a <c>section</c> value; keys are relative to this item.</summary>
        [JsonPropertyName("fields"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public IReadOnlyList<ConfigSchemaItem>? Fields { get; init; }

        /// <summary>Element kind of an <c>array</c> value: boolean, integer, number, string, section or object.</summary>
        [JsonPropertyName("item_type"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? ItemType { get; init; }

        /// <summary>Fields of each element when <see cref="ItemType"/> is <c>section</c>.</summary>
        [JsonPropertyName("item_fields"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public IReadOnlyList<ConfigSchemaItem>? ItemFields { get; init; }
    }

    private sealed record ConfigFieldConditionSchema(
        [property: JsonPropertyName("effect")] string Effect,
        [property: JsonPropertyName("field")] string Field,
        [property: JsonPropertyName("operator")] string Operator,
        [property: JsonPropertyName("value")] string Value);

    private sealed class ConfigFieldMetadata
    {
        public string Description { get; init; } = string.Empty;
        public string? Label { get; set; }
        public string? Type { get; set; }
        public string[] Options { get; set; } = [];
        public double Min { get; set; } = double.NaN;
        public double Max { get; set; } = double.NaN;
        public string? Placeholder { get; set; }
        public string? Group { get; set; }
        public string? GroupLabel { get; set; }
        public string? GroupIcon { get; set; }
        public string? GroupDescription { get; set; }
        public int? Order { get; set; }
        public int? GroupOrder { get; set; }
        public object? Default { get; set; }
    }

    internal sealed record PluginListItem(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("version")] string Version,
        [property: JsonPropertyName("enable")] bool Enable,
        [property: JsonPropertyName("author")] string Author,
        [property: JsonPropertyName("repo")] string? Repo,
        [property: JsonPropertyName("description")] string Description,
        [property: JsonPropertyName("category")] string Category,
        [property: JsonPropertyName("status")] string Status = "disabled",
        [property: JsonPropertyName("errorMessage")] string? ErrorMessage = null);
    // ReSharper restore NotAccessedPositionalProperty.Local

    public async ValueTask DisposeAsync()
    {
        try
        {
            await app.StopAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
        }
        finally
        {
            await app.DisposeAsync().ConfigureAwait(false);
        }
    }
}
