using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ShiroBot.Adapters;
using ShiroBot.Configuration;
using ShiroBot.Hosting.Context;
using ShiroBot.Plugins.Marketplace;
using ShiroBot.SDK.Abstractions;

namespace ShiroBot.Hosting.Http;

internal sealed partial class HostHttpServer(
    IOptions<ApiHostConfig> options,
    IEnumerable<IHostApiEndpoints> endpoints,
    WebHostContext webHostContext,
    HostHttpServer.DiagnosticsEndpoints diagnostics) : IHostedService, IAsyncDisposable
{
    private const string ApiCorsPolicyName = "ShiroBotApiCors";
    private const string DisabledPluginSuffix = ".disable";
    private const long MaxPluginUploadBytes = 100L * 1024L * 1024L;
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

    private static readonly DateTimeOffset AppStartedAt = DateTimeOffset.UtcNow;
    private static readonly PluginMarketplaceCache MarketplaceCache = PluginMarketplaceCache.Shared;
    private static readonly AdapterMarketplaceCache AdapterMarketplaceCache = AdapterMarketplaceCache.Shared;
    private static readonly TimeSpan PluginUploadTtl = TimeSpan.FromMinutes(5);

    private WebApplication? _app;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (_app is not null) return;
        var config = options.Value;
        if (!config.Enable) return;
        cancellationToken.ThrowIfCancellationRequested();

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(Program).Assembly.GetName().Name,
            ContentRootPath = AppContext.BaseDirectory
        });
        builder.Logging.ClearProviders();
        var listenUrls = GetListenUrls(config).ToArray();
        builder.WebHost.UseUrls(listenUrls);
        builder.WebHost.ConfigureKestrel(settings => settings.Limits.MaxRequestBodySize = MaxPluginUploadBytes);
        builder.Services.AddCors(cors => cors.AddPolicy(ApiCorsPolicyName,
            policy => policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));
        // These instances belong to the process, not to the nested HTTP host.
        builder.Services.AddSingleton(webHostContext);
        var app = builder.Build();
        _app = app;
        try
        {
            app.UseWebSockets();
            app.UseCors(ApiCorsPolicyName);
            if (config.EnableDashboard) MapDashboardAssets(app);
            var api = app.MapGroup("/api/v1");
            api.AddEndpointFilter(async (context, next) =>
            {
                if (!IsAuthorized(context.HttpContext, options.Value))
                    return Results.Json(new ApiError("unauthorized", "Missing or invalid API key."),
                        statusCode: StatusCodes.Status401Unauthorized);
                return await next(context).ConfigureAwait(false);
            });
            foreach (var module in endpoints) module.Map(api);
            diagnostics.MapDebug(app, config);
            app.MapFallback((HttpContext context, WebHostContext registry) => registry.HandleRequest(context));
            await app.StartAsync(cancellationToken).ConfigureAwait(false);
            BotLog.Info("宿主 API 服务已启动: " + string.Join(", ", listenUrls));
        }
        catch
        {
            await DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        var app = Interlocked.Exchange(ref _app, null);
        if (app is null) return;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(3));
        try
        {
            await app.StopAsync(deadline.Token).ConfigureAwait(false);
        }
        finally
        {
            await app.DisposeAsync().ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync() => await StopAsync(CancellationToken.None).ConfigureAwait(false);

    private static void MapDashboardAssets(WebApplication app)
    {
        const string dashboardPath = "/dashboard";
        const string resourcePrefix = "Assets.dashboard.";
        var assembly = typeof(Program).Assembly;
        var contentTypeProvider = new FileExtensionContentTypeProvider();


        app.MapGet($"{dashboardPath}/favicon.png", () => ServeDashboardFile("favicon.png"));
        app.MapGet($"{dashboardPath}/assets/{{**path}}", (string path) => ServeDashboardFile($"assets/{path}"));
        app.MapGet($"{dashboardPath}/{{**_}}", (string? _) => ServeDashboardFile("index.html"));

        return;

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
                if (!contentTypeProvider.TryGetContentType(path, out var physicalContentType))
                    physicalContentType = "application/octet-stream";
                return Results.File(physicalPath, physicalContentType, enableRangeProcessing: true);
            }

            if (!contentTypeProvider.TryGetContentType(path, out var contentType))
                contentType = "application/octet-stream";

            return Results.File(stream, contentType, enableRangeProcessing: true);
        }
    }

    private static bool IsAuthorized(HttpContext context, ApiHostConfig config)
    {
        var expected = config.Token;
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
        var authorization = context.Request.Headers.Authorization.ToString();
        if (!authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return false;

        var provided = authorization["Bearer ".Length..].Trim();
        return !string.IsNullOrWhiteSpace(config.Token) && FixedTimeEquals(provided, config.Token);
    }

    private static bool FixedTimeEquals(string provided, string expected)
    {
        if (provided.Length != expected.Length) return false;

        var diff = 0;
        for (var i = 0; i < provided.Length; i++) diff |= provided[i] ^ expected[i];

        return diff == 0;
    }

    private static IEnumerable<string> GetListenUrls(ApiHostConfig config)
    {
        return config.ListenUrls.Length > 0
            ? config.ListenUrls.Where(url => !string.IsNullOrWhiteSpace(url))
            : [ApiHostConfig.DefaultListenUrl];
    }

    // ReSharper disable NotAccessedPositionalProperty.Local
    private sealed record ApiError(
        [property: JsonPropertyName("code")] string Code,
        [property: JsonPropertyName("message")]
        string Message);

    private sealed record PluginUploadConfirmRequest(bool Replace = false, bool Enable = true);

    private sealed record AdapterUploadConfirmRequest(bool Replace = false, bool Enable = true);

    // Constructed by configuration/JSON deserialization.
    // ReSharper disable once ClassNeverInstantiated.Local
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


    private sealed record ConfigSchemaItem(
        [property: JsonPropertyName("key")] string Key,
        [property: JsonPropertyName("label")] string Label,
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("description")]
        string Description,
        [property: JsonPropertyName("placeholder")]
        string? Placeholder,
        [property: JsonPropertyName("options")]
        string[] Options,
        [property: JsonPropertyName("min")] double? Min,
        [property: JsonPropertyName("max")] double? Max,
        [property: JsonPropertyName("group")] string Group,
        [property: JsonPropertyName("group_id")]
        string GroupId,
        [property: JsonPropertyName("group_label")]
        string GroupLabel,
        [property: JsonPropertyName("group_icon")]
        string GroupIcon,
        [property: JsonPropertyName("group_description")]
        string GroupDescription,
        [property: JsonPropertyName("order")] int? Order,
        [property: JsonPropertyName("group_order")]
        int? GroupOrder,
        [property: JsonPropertyName("conditions")]
        IReadOnlyList<ConfigFieldConditionSchema> Conditions,
        [property: JsonPropertyName("default_value")]
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        object? DefaultValue,
        [property: JsonIgnore] string ValueType)
    {
        /// <summary>Fields of a <c>section</c> value; keys are relative to this item.</summary>
        [JsonPropertyName("fields")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public IReadOnlyList<ConfigSchemaItem>? Fields { get; init; }

        /// <summary>Element kind of an <c>array</c> value: boolean, integer, number, string, section or object.</summary>
        [JsonPropertyName("item_type")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? ItemType { get; init; }

        /// <summary>Fields of each element when <see cref="ItemType" /> is <c>section</c>.</summary>
        [JsonPropertyName("item_fields")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        // Read by the HTTP JSON serializer.
        // ReSharper disable once UnusedAutoPropertyAccessor.Local, UnusedAutoPropertyAccessor.Global
        public IReadOnlyList<ConfigSchemaItem>? ItemFields { get; init; }
    }

    private sealed record ConfigFieldConditionSchema(
        [property: JsonPropertyName("effect")] string Effect,
        [property: JsonPropertyName("field")] string Field,
        [property: JsonPropertyName("operator")]
        string Operator,
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
        [property: JsonPropertyName("version")]
        string Version,
        [property: JsonPropertyName("enable")] bool Enable,
        [property: JsonPropertyName("author")] string Author,
        [property: JsonPropertyName("repo")] string? Repo,
        [property: JsonPropertyName("description")]
        string Description,
        [property: JsonPropertyName("category")]
        string Category,
        [property: JsonPropertyName("status")] string Status = "disabled",
        [property: JsonPropertyName("errorMessage")]
        string? ErrorMessage = null,
        [property: JsonPropertyName("priority")]
        int? Priority = null);
    // ReSharper restore NotAccessedPositionalProperty.Local
}