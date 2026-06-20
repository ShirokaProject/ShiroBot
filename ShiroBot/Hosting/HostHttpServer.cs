using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO.Compression;
using System.Net.WebSockets;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using ShiroBot.Core;
using ShiroBot.Hosting.Context;
using ShiroBot.SDK.Abstractions;

namespace ShiroBot.Hosting;

internal sealed class HostHttpServer(WebApplication app) : IAsyncDisposable
{
    public static async Task<HostHttpServer?> StartAsync(
        ApiHostConfig config,
        CoreConfig coreConfig,
        ConfigManager configManager,
        string configPath,
        PluginManager pluginManager,
        HostEventDispatcher eventDispatcher,
        PluginRouteConfig routePolicy,
        WebHostContext webHostContext,
        HostRuntimeState runtimeState,
        HostLogHub logHub)
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
        MapDashboardAssets(app, config);
        MapApiEndpoints(app, config, coreConfig, configManager, configPath, pluginManager, eventDispatcher, routePolicy, runtimeState, logHub);

        app.MapFallback((HttpContext context, WebHostContext registry) => registry.HandleRequest(context));

        await app.StartAsync().ConfigureAwait(false);
        BotLog.Info("宿主 API 服务已启动: " + string.Join(", ", listenUrls));
        return new HostHttpServer(app);
    }

    private static void MapDashboardAssets(WebApplication app, ApiHostConfig config)
    {
        const string dashboardPath = "/dashboard";
        const string resourcePrefix = "Assets.dashboard.";
        var assembly = Assembly.GetExecutingAssembly();
        var contentTypeProvider = new FileExtensionContentTypeProvider();

        IResult ServeDashboardFile(string path)
        {
            path = path.TrimStart('/', '\\');
            var resourcePath = path.Replace('/', '\\');
            var stream = assembly.GetManifestResourceStream(resourcePrefix + resourcePath)
                         ?? assembly.GetManifestResourceStream(resourcePrefix + path.Replace('\\', '/'));
            if (stream is null) return Results.NotFound();

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
        CoreConfig coreConfig,
        ConfigManager configManager,
        string configPath,
        PluginManager pluginManager,
        HostEventDispatcher eventDispatcher,
        PluginRouteConfig routePolicy,
        HostRuntimeState runtimeState,
        HostLogHub logHub)
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

        //配置
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
                    return Results.Ok(new { ok = true, msg = "配置更新成功" });
                }
                catch (InvalidOperationException ex)
                {
                    return Results.BadRequest(new { ok = false, msg = ex.Message });
                }
            }
        });

        //日志
        api.MapGet("/logs/sources", () => Results.Ok(logHub.GetSources()));
        api.MapGet("/logs/stream", async context =>
        {
            if (!context.WebSockets.IsWebSocketRequest)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsync("Expected WebSocket request.").ConfigureAwait(false);
                return;
            }

            var source = context.Request.Query.TryGetValue("source", out var sourceValues)
                ? sourceValues.ToString()
                : "all";
            var tail = context.Request.Query.TryGetValue("tail", out var tailValues) &&
                       int.TryParse(tailValues.ToString(), out var parsedTail)
                ? parsedTail
                : 100;

            using var webSocket = await context.WebSockets.AcceptWebSocketAsync().ConfigureAwait(false);
            await logHub.StreamAsync(webSocket, source, tail, context.RequestAborted).ConfigureAwait(false);
        });
        
        //插件
        api.MapGet("/plugins/list", () =>
        {
            var enabledPlugins = pluginManager.GetLoadedPluginSnapshot()
                .Select(plugin => new PluginListItem(
                    plugin.Name,
                    plugin.DisplayName,
                    plugin.Version,
                    true,
                    string.IsNullOrWhiteSpace(plugin.Author) ? "Unknown" : plugin.Author,
                    plugin.GithubRepo,
                    plugin.Description ?? string.Empty,
                    plugin.Category.ToString()))
                .ToArray();

            var enabledIds = enabledPlugins.Select(plugin => plugin.id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var disabledPlugins = EnumerateDisabledPlugins(pluginManager)
                .Where(plugin => !enabledIds.Contains(plugin.id));
            var listedIds = enabledIds.Concat(disabledPlugins.Select(plugin => plugin.id)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var unloadedPlugins = EnumerateUnloadedPlugins(pluginManager)
                .Where(plugin => !listedIds.Contains(plugin.id));

            return Results.Ok(enabledPlugins.Concat(disabledPlugins).Concat(unloadedPlugins).ToArray());
        });

        api.MapPost("/plugins/upload", async (HttpContext context) =>
        {
            if (!context.Request.HasFormContentType)
            {
                return Results.BadRequest(new { error = "invalid_request", message = "请使用 multipart/form-data 上传插件文件。" });
            }

            var form = await context.Request.ReadFormAsync(context.RequestAborted).ConfigureAwait(false);
            var file = form.Files.GetFile("file") ?? form.Files.FirstOrDefault();
            if (file is null || file.Length == 0)
            {
                return Results.BadRequest(new { error = "missing_file", message = "未收到插件文件。" });
            }

            if (file.Length > MaxPluginUploadBytes)
            {
                return Results.BadRequest(new { error = "file_too_large", message = "插件文件不能超过 100MB。" });
            }

            var extension = Path.GetExtension(file.FileName);
            if (!extension.Equals(".dll", StringComparison.OrdinalIgnoreCase) &&
                !extension.Equals(".zip", StringComparison.OrdinalIgnoreCase))
            {
                return Results.BadRequest(new { error = "unsupported_file", message = "只支持上传 .dll 或 .zip 插件。" });
            }

            var uploadId = Guid.NewGuid().ToString("N");
            var uploadRoot = GetPluginUploadRoot(uploadId);
            Directory.CreateDirectory(uploadRoot);

            try
            {
                var safeFileName = Path.GetFileName(file.FileName);
                var packagePath = Path.Combine(uploadRoot, safeFileName);
                await using (var stream = File.Create(packagePath))
                {
                    await file.CopyToAsync(stream, context.RequestAborted).ConfigureAwait(false);
                }

                var package = PreparePluginUploadPackage(pluginManager, uploadId, packagePath);
                var installed = FindInstalledPlugin(pluginManager, package.Info.Id);
                SchedulePluginUploadCleanup(uploadRoot);
                return Results.Ok(new
                {
                    upload_id = uploadId,
                    status = "parsed",
                    plugin = CreatePluginInfoResponse(package.Info),
                    package = new
                    {
                        file_name = safeFileName,
                        type = package.Type,
                        size = file.Length
                    },
                    conflict = new
                    {
                        exists = installed is not null,
                        installed_version = installed?.Version,
                        uploaded_version = package.Info.Version,
                        action = installed is null ? "install" : "replace"
                    }
                });
            }
            catch (InvalidOperationException ex)
            {
                TryDeleteDirectory(uploadRoot);
                return Results.BadRequest(new { error = "invalid_plugin", message = ex.Message });
            }
            catch
            {
                TryDeleteDirectory(uploadRoot);
                throw;
            }
        });

        api.MapPost("/plugins/upload/{uploadId}/confirm", async (string uploadId, HttpContext context) =>
        {
            PluginUploadConfirmRequest request;
            try
            {
                request = await JsonSerializer.DeserializeAsync<PluginUploadConfirmRequest>(
                              context.Request.Body,
                              JsonSerializerOptions.Web,
                              cancellationToken: context.RequestAborted).ConfigureAwait(false)
                          ?? new PluginUploadConfirmRequest();
            }
            catch (JsonException)
            {
                return Results.BadRequest(new { error = "invalid_json", message = "确认安装请求不是有效 JSON。" });
            }

            var uploadRoot = GetPluginUploadRoot(uploadId);
            if (!Directory.Exists(uploadRoot))
            {
                return Results.NotFound(new { error = "upload_not_found", message = "上传记录不存在或已过期。" });
            }

            try
            {
                var package = LoadPreparedPluginUploadPackage(pluginManager, uploadId);
                var installed = FindInstalledPlugin(pluginManager, package.Info.Id);
                if (installed is not null && !request.Replace)
                {
                    return Results.Conflict(new { error = "plugin_exists", message = "插件已存在，请确认替换。" });
                }

                var loaded = FindLoadedPlugin(pluginManager, package.Info.Id);
                if (loaded is not null)
                {
                    await pluginManager.ScheduleUnloadPluginByName(eventDispatcher, loaded.Name).ConfigureAwait(false);
                }

                if (installed is not null)
                {
                    DeletePluginPath(pluginManager.PluginRootPath, installed.AssemblyPath, package.Info.Id);
                }

                InstallUploadedPlugin(pluginManager.PluginRootPath, package);
                if (request.Enable)
                {
                    await pluginManager.ScheduleLoadPluginByName(eventDispatcher, routePolicy, package.Info.Id).ConfigureAwait(false);
                }

                TryDeleteDirectory(uploadRoot);
                return Results.Ok(new
                {
                    success = true,
                    plugin = new
                    {
                        id = package.Info.Id,
                        enable = request.Enable
                    }
                });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = "install_failed", message = ex.Message });
            }
        });

        api.MapDelete("/plugins/upload/{uploadId}", (string uploadId) =>
        {
            TryDeleteDirectory(GetPluginUploadRoot(uploadId));
            return Results.Ok(new { success = true });
        });

        api.MapPost("/plugins/install/github", async (HttpContext context) =>
        {
            GitHubPluginInstallRequest request;
            try
            {
                request = await JsonSerializer.DeserializeAsync<GitHubPluginInstallRequest>(
                              context.Request.Body,
                              JsonSerializerOptions.Web,
                              cancellationToken: context.RequestAborted).ConfigureAwait(false)
                          ?? new GitHubPluginInstallRequest(string.Empty);
            }
            catch (JsonException)
            {
                return Results.BadRequest(new { error = "invalid_json", message = "GitHub 插件安装请求不是有效 JSON。" });
            }

            if (!TryNormalizeGitHubRepository(request.Repository, out var repository))
            {
                return Results.BadRequest(new { error = "invalid_repository", message = "repository 必须是 owner/repo 或 GitHub 仓库 URL。" });
            }

            var packageInfo = await Updater.GetLatestPluginPackageAsync(
                repository,
                request.IncludePrerelease,
                context.RequestAborted).ConfigureAwait(false);
            if (packageInfo is null)
            {
                return Results.NotFound(new { error = "release_not_found", message = $"仓库 {repository} 没有可用 release。" });
            }

            if (string.IsNullOrWhiteSpace(packageInfo.AssetDownloadUrl) || string.IsNullOrWhiteSpace(packageInfo.AssetName))
            {
                return Results.BadRequest(new { error = "asset_not_found", message = $"仓库 {repository} 的最新 release 没有 .zip 或 .dll 插件资源。" });
            }

            var uploadId = Guid.NewGuid().ToString("N");
            var uploadRoot = GetPluginUploadRoot(uploadId);
            Directory.CreateDirectory(uploadRoot);

            try
            {
                var safeAssetName = Path.GetFileName(packageInfo.AssetName);
                var packagePath = Path.Combine(uploadRoot, safeAssetName);
                await Updater.DownloadFileAsync(packageInfo.AssetDownloadUrl, packagePath, context.RequestAborted).ConfigureAwait(false);

                var package = PreparePluginUploadPackage(pluginManager, uploadId, packagePath);
                var installed = FindInstalledPlugin(pluginManager, package.Info.Id);
                SchedulePluginUploadCleanup(uploadRoot);

                return Results.Ok(new
                {
                    upload_id = uploadId,
                    status = "parsed",
                    source = new
                    {
                        type = "github",
                        repository = packageInfo.Repository,
                        release_name = packageInfo.ReleaseName,
                        release_version = packageInfo.Version,
                        release_url = packageInfo.ReleaseUrl,
                        asset_name = packageInfo.AssetName,
                        asset_type = packageInfo.AssetType
                    },
                    plugin = CreatePluginInfoResponse(package.Info),
                    package = new
                    {
                        file_name = safeAssetName,
                        type = package.Type,
                        size = new FileInfo(packagePath).Length
                    },
                    conflict = new
                    {
                        exists = installed is not null,
                        installed_version = installed?.Version,
                        uploaded_version = package.Info.Version,
                        action = installed is null ? "install" : "replace"
                    }
                });
            }
            catch (InvalidOperationException ex)
            {
                TryDeleteDirectory(uploadRoot);
                return Results.BadRequest(new { error = "invalid_plugin", message = ex.Message });
            }
            catch
            {
                TryDeleteDirectory(uploadRoot);
                throw;
            }
        });
        
        api.MapGet("/plugins/{id}", (string id) =>
        {
            //获取插件详情的逻辑
            return Results.Ok(new
            {
                name = $"示例插件 {id}",
                version = "v1.0",
                enabled = true,
                description = "这是一个示例插件的描述信息。"
            });
        });

        api.MapGet("/plugins/{id}/config", (string id) =>
        {
            var plugin = FindPluginForConfig(pluginManager, id);
            if (plugin is null)
            {
                return Results.NotFound(new { error = "plugin_not_found", message = $"未找到插件: {id}" });
            }

            var pluginConfigPath = GetPluginConfigPath(pluginManager, plugin.AssemblyPath, plugin.Id);
            EnsureKnownPluginConfig(id, pluginConfigPath);
            return Results.Ok(new
            {
                plugin_id = plugin.Id,
                config = LoadTomlObject(pluginConfigPath),
                schema = GetPluginConfigSchema(plugin.AssemblyPath),
                routes = CreatePluginRouteResponse(routePolicy, plugin.Id)
            });
        });

        api.MapPatch("/plugins/{id}/config", async (string id, HttpContext context) =>
        {
            JsonDocument document;
            try
            {
                document = await JsonDocument.ParseAsync(context.Request.Body, cancellationToken: context.RequestAborted)
                    .ConfigureAwait(false);
            }
            catch (JsonException)
            {
                return Results.BadRequest(new { error = "invalid_json", message = "插件配置更新内容不是有效 JSON。" });
            }

            using (document)
            {
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                {
                    return Results.BadRequest(new { error = "invalid_request", message = "插件配置更新内容必须是对象。" });
                }

                var plugin = FindPluginForConfig(pluginManager, id);
                if (plugin is null)
                {
                    return Results.NotFound(new { error = "plugin_not_found", message = $"未找到插件: {id}" });
                }

                try
                {
                    var pluginConfigPath = GetPluginConfigPath(pluginManager, plugin.AssemblyPath, plugin.Id);
                    EnsureKnownPluginConfig(id, pluginConfigPath);

                    if (document.RootElement.TryGetProperty("config", out var configPatch))
                    {
                        ApplyPluginConfigPatch(configManager, pluginConfigPath, plugin.Id, configPatch);
                    }

                    if (document.RootElement.TryGetProperty("routes", out var routePatch))
                    {
                        ApplyPluginRoutePatch(configManager, configPath, routePolicy, plugin.Id, routePatch);
                    }

                    return Results.Ok(new
                    {
                        ok = true,
                        plugin_id = plugin.Id,
                        config = LoadTomlObject(pluginConfigPath),
                        routes = CreatePluginRouteResponse(routePolicy, plugin.Id)
                    });
                }
                catch (InvalidOperationException ex)
                {
                    return Results.BadRequest(new { error = "invalid_config", message = ex.Message });
                }
            }
        });
        
        
        
        api.MapPost("/plugins/{id}/enable", async (string id) =>
        {
            var gate = GetPluginOperationLock(id);
            await gate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (FindLoadedPlugin(pluginManager, id) is not null)
                {
                    return Results.Ok(new { ok = true, message = $"插件 {id} 已启用" });
                }

                RestoreDisabledPluginFile(pluginManager, id);
                if (pluginManager.ResolvePluginLoadCandidates(pluginManager.PluginRootPath, id).Count == 0)
                {
                    return Results.NotFound(new { ok = false, message = $"未找到插件文件: {id}" });
                }

                await pluginManager.ScheduleLoadPluginByName(eventDispatcher, routePolicy, id).ConfigureAwait(false);
                return Results.Ok(new { ok = true, message = $"插件 {id} 已启用" });
            }
            finally
            {
                gate.Release();
            }
        });
        
        api.MapPost("/plugins/{id}/disable", async (string id) =>
        {
            var gate = GetPluginOperationLock(id);
            await gate.WaitAsync().ConfigureAwait(false);
            try
            {
                var plugin = FindLoadedPlugin(pluginManager, id);
                var targetPath = plugin?.AssemblyPath
                                 ?? pluginManager.ResolvePluginLoadCandidates(pluginManager.PluginRootPath, id).FirstOrDefault();
                if (targetPath is null && FindDisabledPluginFile(pluginManager, id) is not null)
                {
                    return Results.Ok(new { ok = true, message = $"插件 {id} 已禁用" });
                }

                if (string.IsNullOrWhiteSpace(targetPath))
                {
                    return Results.NotFound(new { ok = false, message = $"未找到插件文件: {id}" });
                }

                if (plugin is not null)
                {
                    await pluginManager.ScheduleUnloadPluginByName(eventDispatcher, plugin.Name).ConfigureAwait(false);
                }

                try
                {
                    DisablePluginFile(targetPath);
                }
                catch (IOException ex)
                {
                    return Results.Conflict(new { ok = false, message = $"插件已卸载，但 DLL 文件仍被占用，请稍后重试: {ex.Message}" });
                }
                catch (UnauthorizedAccessException ex)
                {
                    return Results.Conflict(new { ok = false, message = $"插件已卸载，但 DLL 文件仍被占用，请稍后重试: {ex.Message}" });
                }

                return Results.Ok(new { ok = true, message = $"插件 {plugin?.Name ?? id} 已禁用" });
            }
            finally
            {
                gate.Release();
            }
        });
        
        api.MapPost("/plugins/{id}/delete", async (string id) =>
        {
            var plugin = FindLoadedPlugin(pluginManager, id);
            var targetPath = plugin?.AssemblyPath
                             ?? pluginManager.ResolvePluginLoadCandidates(pluginManager.PluginRootPath, id).FirstOrDefault()
                             ?? FindDisabledPluginFile(pluginManager, id);
            if (string.IsNullOrWhiteSpace(targetPath))
            {
                return Results.NotFound(new { ok = false, message = $"未找到插件文件: {id}" });
            }

            if (plugin is not null)
            {
                await pluginManager.ScheduleUnloadPluginByName(eventDispatcher, plugin.Name).ConfigureAwait(false);
            }

            DeletePluginPath(pluginManager.PluginRootPath, targetPath, plugin?.Name ?? id);
            return Results.Ok(new { ok = true, message = $"插件 {id} 已删除" });
        });
        
        api.MapPost("/plugins/{id}/update", async (string id, HttpContext context) =>
        {
            var plugin = FindLoadedPlugin(pluginManager, id);
            if (plugin is null)
            {
                return Results.NotFound(new { ok = false, message = $"未找到已加载插件: {id}" });
            }

            if (string.IsNullOrWhiteSpace(plugin.GithubRepo))
            {
                return Results.BadRequest(new { ok = false, message = $"插件 {plugin.Name} 未配置 GithubRepo" });
            }

            var update = await Updater.CheckGitHubReleaseAsync(
                plugin.GithubRepo,
                plugin.Version,
                cancellationToken: context.RequestAborted).ConfigureAwait(false);
            if (update is null)
            {
                return Results.Ok(new { ok = true, message = $"插件 {plugin.Name} 已是最新版本" });
            }

            if (string.IsNullOrWhiteSpace(update.AssetDownloadUrl) ||
                string.IsNullOrWhiteSpace(update.AssetName) ||
                !update.AssetName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            {
                return Results.BadRequest(new { ok = false, message = $"插件 {plugin.Name} 有新版本，但 release 中没有可用的 dll asset" });
            }

            await pluginManager.ScheduleUnloadPluginByName(eventDispatcher, plugin.Name).ConfigureAwait(false);
            await Updater.UpdatePluginAsync(plugin.Name, update.AssetDownloadUrl, plugin.AssemblyPath, context.RequestAborted).ConfigureAwait(false);
            await pluginManager.ScheduleLoadPluginByName(eventDispatcher, routePolicy, plugin.Name).ConfigureAwait(false);

            return Results.Ok(new
            {
                ok = true,
                message = $"插件 {plugin.Name} 已更新到 {update.LatestVersion}",
                current_version = update.CurrentVersion,
                latest_version = update.LatestVersion,
                release_url = update.ReleaseUrl
            });
        });      
        

        
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

        return [config.ListenUrl];
    }

    private static LoadedPluginHandle? FindLoadedPlugin(PluginManager pluginManager, string id) =>
        pluginManager.GetLoadedPluginSnapshot().FirstOrDefault(plugin =>
            string.Equals(plugin.Name, id, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(plugin.DisplayName, id, StringComparison.OrdinalIgnoreCase));

    private static void DisablePluginFile(string assemblyPath)
    {
        var disabledPath = GetDisabledPluginPath(assemblyPath);
        if (File.Exists(disabledPath)) File.Delete(disabledPath);
        File.Move(assemblyPath, disabledPath);
    }

    private static void RestoreDisabledPluginFile(PluginManager pluginManager, string id)
    {
        var disabledPath = FindDisabledPluginFile(pluginManager, id);
        if (disabledPath is null) return;

        var enabledPath = disabledPath[..^DisabledPluginSuffix.Length];
        if (File.Exists(enabledPath)) File.Delete(enabledPath);
        File.Move(disabledPath, enabledPath);
    }

    private static string? FindDisabledPluginFile(PluginManager pluginManager, string id)
    {
        var pluginRootPath = pluginManager.PluginRootPath;
        var pluginRoot = Path.GetFullPath(pluginRootPath);
        if (!Directory.Exists(pluginRoot)) return null;

        var aliases = GetPluginIdAliases(id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return Directory.EnumerateFiles(pluginRoot, "*.dll.disable", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(path =>
            {
                var info = TryProbeDisabledPluginInfo(pluginManager, path);
                var enabledFileName = Path.GetFileNameWithoutExtension(path);
                var fileName = Path.GetFileNameWithoutExtension(enabledFileName);
                var directoryName = new DirectoryInfo(Path.GetDirectoryName(path) ?? pluginRoot).Name;

                return aliases.Contains(fileName) ||
                       aliases.Contains(directoryName) ||
                       (fileName.StartsWith("ShiroBot.", StringComparison.OrdinalIgnoreCase) && aliases.Contains(fileName["ShiroBot.".Length..])) ||
                       NameMatches(info?.Id, aliases) ||
                       NameMatches(info?.Name, aliases);
            });
    }

    private static InstalledPluginInfo? FindInstalledPlugin(PluginManager pluginManager, string id)
    {
        var loaded = FindLoadedPlugin(pluginManager, id);
        if (loaded is not null)
        {
            return new InstalledPluginInfo(loaded.AssemblyPath, loaded.Version);
        }

        var candidate = pluginManager.ResolvePluginLoadCandidates(pluginManager.PluginRootPath, id).FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(candidate))
        {
            var info = pluginManager.TryProbePluginInfoFile(candidate);
            return new InstalledPluginInfo(candidate, info?.Version ?? string.Empty);
        }

        var disabled = FindDisabledPluginFile(pluginManager, id);
        if (!string.IsNullOrWhiteSpace(disabled))
        {
            var info = TryProbeDisabledPluginInfo(pluginManager, disabled);
            return new InstalledPluginInfo(disabled, info?.Version ?? string.Empty);
        }

        return null;
    }

    private static PluginConfigTarget? FindPluginForConfig(PluginManager pluginManager, string id)
    {
        var loaded = FindLoadedPlugin(pluginManager, id);
        if (loaded is not null)
        {
            return new PluginConfigTarget(loaded.Name, loaded.AssemblyPath);
        }

        var candidate = pluginManager.ResolvePluginLoadCandidates(pluginManager.PluginRootPath, id).FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(candidate))
        {
            var info = pluginManager.TryProbePluginInfoFile(candidate);
            return new PluginConfigTarget(info?.Id ?? id, candidate);
        }

        var disabled = FindDisabledPluginFile(pluginManager, id);
        if (!string.IsNullOrWhiteSpace(disabled))
        {
            var info = TryProbeDisabledPluginInfo(pluginManager, disabled);
            return new PluginConfigTarget(info?.Id ?? id, disabled);
        }

        return null;
    }

    private static string GetPluginConfigPath(PluginManager pluginManager, string assemblyPath, string pluginId)
    {
        var fullAssemblyPath = Path.GetFullPath(assemblyPath);
        if (fullAssemblyPath.EndsWith(DisabledPluginSuffix, StringComparison.OrdinalIgnoreCase))
        {
            fullAssemblyPath = fullAssemblyPath[..^DisabledPluginSuffix.Length];
        }

        var pluginRoot = Path.GetFullPath(pluginManager.PluginRootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var assemblyDirectory = Path.GetFullPath(Path.GetDirectoryName(fullAssemblyPath) ?? pluginRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var configDirectory = string.Equals(assemblyDirectory, pluginRoot, StringComparison.OrdinalIgnoreCase)
            ? Path.Combine(pluginRoot, pluginId)
            : assemblyDirectory;

        return Path.Combine(configDirectory, "config.toml");
    }

    private static void EnsureKnownPluginConfig(string pluginId, string configPath)
    {
        if (File.Exists(configPath)) return;
        if (!string.Equals(pluginId, "JmParser", StringComparison.OrdinalIgnoreCase)) return;

        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
        File.WriteAllText(configPath, string.Join(Environment.NewLine, [
            "proxy = \"\"",
            "output_mode = \"file\"",
            "delete_after_minutes = 60",
            "max_concurrency = 16",
            "send_cover = true",
            "cover_blur_radius = 12"
        ]) + Environment.NewLine);
    }

    private static IReadOnlyDictionary<string, object?> LoadTomlObject(string configPath)
    {
        if (!File.Exists(configPath)) return new Dictionary<string, object?>();

        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var rawLine in File.ReadAllLines(configPath))
        {
            var line = StripTomlInlineComment(rawLine).Trim();
            if (line.Length == 0 || line.StartsWith('[')) continue;
            var equalsIndex = line.IndexOf('=');
            if (equalsIndex <= 0) continue;

            var key = line[..equalsIndex].Trim();
            var value = line[(equalsIndex + 1)..].Trim();
            result[key] = ParseSimpleTomlValue(value);
        }

        return result;
    }

    private static string StripTomlInlineComment(string line)
    {
        var inString = false;
        var escaped = false;
        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (inString)
            {
                if (ch == '\\' && !escaped)
                {
                    escaped = true;
                    continue;
                }

                if (ch == '"' && !escaped) inString = false;
                escaped = false;
                continue;
            }

            if (ch == '"') inString = true;
            if (ch == '#') return line[..i];
        }

        return line;
    }

    private static object? ParseSimpleTomlValue(string value)
    {
        if (value.StartsWith('"') && value.EndsWith('"')) return JsonSerializer.Deserialize<string>(value) ?? string.Empty;
        if (value.Equals("true", StringComparison.OrdinalIgnoreCase)) return true;
        if (value.Equals("false", StringComparison.OrdinalIgnoreCase)) return false;
        if (value.StartsWith('[') && value.EndsWith(']'))
        {
            var body = value[1..^1].Trim();
            if (body.Length == 0) return Array.Empty<object?>();
            return body.Split(',', StringSplitOptions.TrimEntries).Select(ParseSimpleTomlValue).ToArray();
        }

        return value.Contains('.')
            ? double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var floating) ? floating : value
            : long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer) ? integer : value;
    }

    private static object[] GetPluginConfigSchema(string assemblyPath)
    {
        if (!File.Exists(assemblyPath)) return [];

        try
        {
            using var stream = File.OpenRead(assemblyPath);
            using var peReader = new PEReader(stream);
            if (!peReader.HasMetadata) return [];

            var reader = peReader.GetMetadataReader();
            TypeDefinitionHandle configTypeHandle = default;
            foreach (var typeHandle in reader.TypeDefinitions)
            {
                var type = reader.GetTypeDefinition(typeHandle);
                if (reader.GetString(type.Name).Equals("PluginConfig", StringComparison.OrdinalIgnoreCase))
                {
                    configTypeHandle = typeHandle;
                    break;
                }

                if (configTypeHandle.IsNil && type.GetProperties().Any(propertyHandle =>
                    ReadConfigFieldAttribute(reader, reader.GetPropertyDefinition(propertyHandle).GetCustomAttributes()) is not null))
                {
                    configTypeHandle = typeHandle;
                }
            }

            if (!configTypeHandle.IsNil)
            {
                return reader.GetTypeDefinition(configTypeHandle).GetProperties()
                    .Select(propertyHandle => CreateConfigSchemaItem(reader, reader.GetPropertyDefinition(propertyHandle)))
                    .Where(item => item is not null)
                    .Cast<object>()
                    .ToArray();
            }
        }
        catch (Exception ex) when (ex is BadImageFormatException or IOException or UnauthorizedAccessException)
        {
            return [];
        }

        return [];
    }

    private static ConfigSchemaItem? CreateConfigSchemaItem(MetadataReader reader, PropertyDefinition property)
    {
        var propertyName = reader.GetString(property.Name);
        if (string.IsNullOrWhiteSpace(propertyName)) return null;

        var field = ReadConfigFieldAttribute(reader, property.GetCustomAttributes());
        var key = NormalizeConfigKey(propertyName);
        var type = string.IsNullOrWhiteSpace(field?.Type)
            ? InferConfigPropertyType(reader, property)
            : field.Type!;

        return new ConfigSchemaItem(
            key,
            string.IsNullOrWhiteSpace(field?.Label) ? propertyName : field.Label!,
            type,
            field?.Description ?? string.Empty,
            field?.Placeholder,
            field?.Options ?? [],
            field is not null && !double.IsNaN(field.Min) ? field.Min : null,
            field is not null && !double.IsNaN(field.Max) ? field.Max : null);
    }

    private static ConfigFieldMetadata? ReadConfigFieldAttribute(MetadataReader reader, CustomAttributeHandleCollection attributes)
    {
        foreach (var attributeHandle in attributes)
        {
            var attribute = reader.GetCustomAttribute(attributeHandle);
            var attributeTypeName = GetCustomAttributeTypeName(reader, attribute.Constructor);
            if (!attributeTypeName.EndsWith("ConfigFieldAttribute", StringComparison.Ordinal)) continue;

            var blob = reader.GetBlobReader(attribute.Value);
            if (blob.ReadUInt16() != 1) return null;

            var description = blob.ReadSerializedString() ?? string.Empty;
            var metadata = new ConfigFieldMetadata { Description = description };
            var namedCount = blob.ReadUInt16();
            for (var i = 0; i < namedCount; i++)
            {
                _ = blob.ReadByte();
                var typeCode = blob.ReadByte();
                byte? arrayElementType = null;
                if (typeCode == SerializedTypeSzArray)
                {
                    arrayElementType = blob.ReadByte();
                }

                var memberName = blob.ReadSerializedString();
                switch (memberName)
                {
                    case nameof(ConfigFieldMetadata.Label) when typeCode == SerializedTypeString:
                        metadata.Label = blob.ReadSerializedString();
                        break;
                    case nameof(ConfigFieldMetadata.Type) when typeCode == SerializedTypeString:
                        metadata.Type = blob.ReadSerializedString();
                        break;
                    case nameof(ConfigFieldMetadata.Placeholder) when typeCode == SerializedTypeString:
                        metadata.Placeholder = blob.ReadSerializedString();
                        break;
                    case nameof(ConfigFieldMetadata.Options) when typeCode == SerializedTypeSzArray && arrayElementType == SerializedTypeString:
                        metadata.Options = ReadStringArray(ref blob);
                        break;
                    case nameof(ConfigFieldMetadata.Min) when typeCode == SerializedTypeR8:
                        metadata.Min = blob.ReadDouble();
                        break;
                    case nameof(ConfigFieldMetadata.Max) when typeCode == SerializedTypeR8:
                        metadata.Max = blob.ReadDouble();
                        break;
                    default:
                        SkipConfigAttributeValue(ref blob, typeCode, arrayElementType);
                        break;
                }
            }

            return metadata;
        }

        return null;
    }

    private static string GetCustomAttributeTypeName(MetadataReader reader, EntityHandle constructor)
    {
        return constructor.Kind switch
        {
            HandleKind.MemberReference => GetTypeName(reader, reader.GetMemberReference((MemberReferenceHandle)constructor).Parent),
            HandleKind.MethodDefinition => GetTypeName(reader, reader.GetMethodDefinition((MethodDefinitionHandle)constructor).GetDeclaringType()),
            _ => string.Empty
        };
    }

    private static string GetTypeName(MetadataReader reader, EntityHandle handle)
    {
        return handle.Kind switch
        {
            HandleKind.TypeReference => reader.GetString(reader.GetTypeReference((TypeReferenceHandle)handle).Name),
            HandleKind.TypeDefinition => reader.GetString(reader.GetTypeDefinition((TypeDefinitionHandle)handle).Name),
            _ => string.Empty
        };
    }

    private static string InferConfigPropertyType(MetadataReader reader, PropertyDefinition property)
    {
        var blob = reader.GetBlobReader(property.Signature);
        _ = blob.ReadByte();
        _ = blob.ReadCompressedInteger();
        return blob.ReadByte() switch
        {
            ElementTypeBoolean => "boolean",
            ElementTypeI1 or ElementTypeU1 or ElementTypeI2 or ElementTypeU2 or ElementTypeI4 or ElementTypeU4 or ElementTypeI8 or ElementTypeU8 or ElementTypeR4 or ElementTypeR8 => "number",
            _ => "string"
        };
    }

    private static string[] ReadStringArray(ref BlobReader blob)
    {
        var count = blob.ReadUInt32();
        if (count == uint.MaxValue) return [];

        var values = new string[count];
        for (var i = 0; i < count; i++)
        {
            values[i] = blob.ReadSerializedString() ?? string.Empty;
        }

        return values;
    }

    private static void SkipConfigAttributeValue(ref BlobReader blob, byte typeCode, byte? arrayElementType)
    {
        if (typeCode == SerializedTypeSzArray)
        {
            var count = blob.ReadUInt32();
            if (count == uint.MaxValue) return;
            for (var i = 0; i < count; i++) SkipConfigAttributeValue(ref blob, arrayElementType ?? SerializedTypeString, null);
            return;
        }

        switch (typeCode)
        {
            case SerializedTypeBoolean:
            case SerializedTypeI1:
            case SerializedTypeU1:
                blob.ReadByte();
                break;
            case SerializedTypeI2:
            case SerializedTypeU2:
                blob.ReadUInt16();
                break;
            case SerializedTypeI4:
            case SerializedTypeU4:
            case SerializedTypeR4:
                blob.ReadBytes(4);
                break;
            case SerializedTypeI8:
            case SerializedTypeU8:
            case SerializedTypeR8:
                blob.ReadBytes(8);
                break;
            case SerializedTypeString:
                blob.ReadSerializedString();
                break;
        }
    }

    private static void ApplyPluginConfigPatch(ConfigManager configManager, string pluginConfigPath, string pluginId, JsonElement patch)
    {
        if (patch.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("config 必须是对象。");
        }

        foreach (var property in patch.EnumerateObject())
        {
            var key = NormalizeConfigKey(property.Name);
            var value = ConvertJsonValue(property.Value);
            ValidatePluginConfigValue(pluginId, key, value);
            configManager.SetConfigValue(pluginConfigPath, key, value);
        }
    }

    private static void ValidatePluginConfigValue(string pluginId, string key, object? value)
    {
        if (!string.Equals(pluginId, "JmParser", StringComparison.OrdinalIgnoreCase)) return;

        switch (key)
        {
            case "proxy":
                if (value is not string) throw new InvalidOperationException("proxy 必须是字符串。");
                break;
            case "output_mode":
                var mode = value as string;
                if (mode is not ("file" or "url" or "both")) throw new InvalidOperationException("output_mode 只能是 file、url 或 both。");
                break;
            case "delete_after_minutes":
                if (!TryGetLong(value, out var deleteAfterMinutes) || deleteAfterMinutes < 0) throw new InvalidOperationException("delete_after_minutes 必须是大于等于 0 的数字。");
                break;
            case "max_concurrency":
                if (!TryGetLong(value, out var maxConcurrency) || maxConcurrency is < 1 or > 64) throw new InvalidOperationException("max_concurrency 必须在 1 到 64 之间。");
                break;
            case "send_cover":
                if (value is not bool) throw new InvalidOperationException("send_cover 必须是布尔值。");
                break;
            case "cover_blur_radius":
                if (!TryGetDouble(value, out var coverBlurRadius) || coverBlurRadius is < 0 or > 100) throw new InvalidOperationException("cover_blur_radius 必须在 0 到 100 之间。");
                break;
            default:
                throw new InvalidOperationException($"JmParser 不支持配置项: {key}");
        }
    }

    private static void ApplyPluginRoutePatch(
        ConfigManager configManager,
        string coreConfigPath,
        PluginRouteConfig routePolicy,
        string pluginId,
        JsonElement patch)
    {
        if (patch.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("routes 必须是对象。");
        }

        var current = routePolicy.Plugins.TryGetValue(pluginId, out var existing) ? existing : routePolicy.Default;
        var mode = current.Mode;
        var groups = current.Groups;

        if (TryGetString(patch, "mode", out var patchedMode))
        {
            mode = NormalizePluginRouteMode(patchedMode);
        }

        if (TryGetLongArray(patch, "groups", out var patchedGroups))
        {
            groups = patchedGroups;
        }

        var rule = new PluginRouteRuleConfig { Mode = mode, Groups = groups };
        routePolicy.Plugins[pluginId] = rule;
        configManager.SetConfigValue(coreConfigPath, $"plugin_routes.plugins.{pluginId}.mode", rule.Mode);
        configManager.SetConfigValue(coreConfigPath, $"plugin_routes.plugins.{pluginId}.groups", rule.Groups);
    }

    private static object CreatePluginRouteResponse(PluginRouteConfig routePolicy, string pluginId)
    {
        var configured = routePolicy.Plugins.TryGetValue(pluginId, out var rule);
        var effective = configured ? rule! : routePolicy.Default;
        return new
        {
            configured,
            mode = configured ? effective.Mode : "default",
            groups = configured ? effective.Groups : [],
            effective_mode = effective.Mode,
            effective_groups = effective.Groups,
            default_mode = routePolicy.Default.Mode,
            default_groups = routePolicy.Default.Groups
        };
    }

    private static string NormalizePluginRouteMode(string mode) => mode.Trim().ToLowerInvariant() switch
    {
        "whitelist" => "whitelist",
        "blacklist" => "blacklist",
        _ => throw new InvalidOperationException("routes.mode 只能是 whitelist 或 blacklist。")
    };

    private static string NormalizeConfigKey(string key)
    {
        if (key.Contains('_')) return key.Trim().ToLowerInvariant();

        var chars = new List<char>(key.Length + 4);
        foreach (var ch in key.Trim())
        {
            if (char.IsUpper(ch) && chars.Count > 0) chars.Add('_');
            chars.Add(char.ToLowerInvariant(ch));
        }

        return new string(chars.ToArray());
    }

    private static object? ConvertJsonValue(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString() ?? string.Empty,
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Number when value.TryGetInt64(out var integer) => integer,
        JsonValueKind.Number => value.GetDouble(),
        JsonValueKind.Array => value.EnumerateArray().Select(ConvertJsonValue).ToArray(),
        JsonValueKind.Null => null,
        _ => throw new InvalidOperationException("插件配置值只能是字符串、数字、布尔值、数组或 null。")
    };

    private static bool TryGetLong(object? value, out long number)
    {
        switch (value)
        {
            case long longValue:
                number = longValue;
                return true;
            case int intValue:
                number = intValue;
                return true;
            case double doubleValue when Math.Abs(doubleValue % 1) < double.Epsilon:
                number = (long)doubleValue;
                return true;
            default:
                number = 0;
                return false;
        }
    }

    private static bool TryGetDouble(object? value, out double number)
    {
        switch (value)
        {
            case double doubleValue:
                number = doubleValue;
                return true;
            case long longValue:
                number = longValue;
                return true;
            case int intValue:
                number = intValue;
                return true;
            case string text when double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed):
                number = parsed;
                return true;
            default:
                number = 0;
                return false;
        }
    }

    private static IEnumerable<PluginListItem> EnumerateDisabledPlugins(PluginManager pluginManager)
    {
        var pluginRootPath = pluginManager.PluginRootPath;
        var pluginRoot = Path.GetFullPath(pluginRootPath);
        if (!Directory.Exists(pluginRoot)) yield break;

        foreach (var path in Directory.EnumerateFiles(pluginRoot, "*.dll.disable", SearchOption.AllDirectories)
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            var info = TryProbeDisabledPluginInfo(pluginManager, path);
            if (info is not null)
            {
                yield return new PluginListItem(
                    info.Id,
                    info.Name,
                    info.Version,
                    false,
                    string.IsNullOrWhiteSpace(info.Author) ? "Unknown" : info.Author,
                    info.GithubRepo,
                    info.Description ?? string.Empty,
                    info.Category.ToString());
                continue;
            }

            var enabledFileName = Path.GetFileNameWithoutExtension(path);
            var id = Path.GetFileNameWithoutExtension(enabledFileName);
            if (id.StartsWith("ShiroBot.", StringComparison.OrdinalIgnoreCase)) id = id["ShiroBot.".Length..];
            if (string.IsNullOrWhiteSpace(id)) continue;

            yield return new PluginListItem(
                id,
                id,
                string.Empty,
                false,
                "Unknown",
                null,
                string.Empty,
                "Other");
        }
    }

    private static IEnumerable<PluginListItem> EnumerateUnloadedPlugins(PluginManager pluginManager)
    {
        var pluginRootPath = pluginManager.PluginRootPath;
        var pluginRoot = Path.GetFullPath(pluginRootPath);
        if (!Directory.Exists(pluginRoot)) yield break;

        foreach (var path in PluginManager.EnumeratePluginEntryAssemblies(pluginRoot))
        {
            var info = pluginManager.TryProbePluginInfoFile(path);
            if (info is null) continue;

            yield return new PluginListItem(
                info.Id,
                info.Name,
                info.Version,
                false,
                string.IsNullOrWhiteSpace(info.Author) ? "Unknown" : info.Author,
                info.GithubRepo,
                info.Description ?? string.Empty,
                info.Category.ToString());
        }
    }

    private static PluginProbeInfo? TryProbeDisabledPluginInfo(PluginManager pluginManager, string disabledPath)
    {
        var tempEnabledPath = disabledPath[..^DisabledPluginSuffix.Length];
        return pluginManager.TryProbePluginInfoFile(disabledPath)
               ?? pluginManager.TryProbePluginInfoFile(tempEnabledPath);
    }

    private static bool NameMatches(string? candidate, HashSet<string> aliases)
    {
        if (string.IsNullOrWhiteSpace(candidate)) return false;
        if (aliases.Contains(candidate)) return true;
        return candidate.StartsWith("ShiroBot.", StringComparison.OrdinalIgnoreCase) && aliases.Contains(candidate["ShiroBot.".Length..]);
    }

    private static string GetDisabledPluginPath(string assemblyPath) => assemblyPath + DisabledPluginSuffix;

    private static IEnumerable<string> GetPluginIdAliases(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) yield break;

        yield return id;
        const string shiroBotPrefix = "ShiroBot.";
        if (id.StartsWith(shiroBotPrefix, StringComparison.OrdinalIgnoreCase) && id.Length > shiroBotPrefix.Length)
        {
            yield return id[shiroBotPrefix.Length..];
        }
        else
        {
            yield return shiroBotPrefix + id;
        }
    }

    private static void DeletePluginPath(string pluginRootPath, string targetPath, string pluginName)
    {
        var pluginRoot = Path.GetFullPath(pluginRootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullTargetPath = Path.GetFullPath(targetPath);
        if (!fullTargetPath.StartsWith(pluginRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(Path.GetDirectoryName(fullTargetPath)?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), pluginRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("拒绝删除插件目录之外的文件");
        }

        var parentDirectory = Path.GetDirectoryName(fullTargetPath);
        if (string.IsNullOrWhiteSpace(parentDirectory))
        {
            throw new InvalidOperationException("无法解析插件文件目录");
        }

        var normalizedParent = Path.GetFullPath(parentDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(fullTargetPath);
        var parentName = new DirectoryInfo(normalizedParent).Name;
        var isPluginSubDirectory = !string.Equals(normalizedParent, pluginRoot, StringComparison.OrdinalIgnoreCase) &&
                                   (string.Equals(parentName, pluginName, StringComparison.OrdinalIgnoreCase) ||
                                    string.Equals(parentName, fileNameWithoutExtension, StringComparison.OrdinalIgnoreCase));

        if (isPluginSubDirectory)
        {
            Directory.Delete(normalizedParent, recursive: true);
            return;
        }

        File.Delete(fullTargetPath);
    }

    private static PluginUploadPackage PreparePluginUploadPackage(PluginManager pluginManager, string uploadId, string packagePath)
    {
        var extension = Path.GetExtension(packagePath);
        if (extension.Equals(".dll", StringComparison.OrdinalIgnoreCase))
        {
            var info = pluginManager.TryProbePluginInfoFile(packagePath)
                       ?? throw new InvalidOperationException("未找到 BotPluginAttribute，文件不是有效插件。");
            return new PluginUploadPackage(uploadId, Path.GetDirectoryName(packagePath)!, packagePath, packagePath, "dll", info);
        }

        if (!extension.Equals(".zip", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("只支持上传 .dll 或 .zip 插件。");
        }

        var extractRoot = Path.Combine(Path.GetDirectoryName(packagePath)!, "extract");
        Directory.CreateDirectory(extractRoot);
        ExtractZipSafely(packagePath, extractRoot);

        var pluginDlls = Directory.EnumerateFiles(extractRoot, "*.dll", SearchOption.AllDirectories)
            .Select(path => new { Path = path, Info = pluginManager.TryProbePluginInfoFile(path) })
            .Where(item => item.Info is not null)
            .ToArray();

        return pluginDlls.Length switch
        {
            0 => throw new InvalidOperationException("压缩包中未找到有效插件 DLL。"),
            > 1 => throw new InvalidOperationException("压缩包中包含多个插件入口 DLL，请一次只上传一个插件。"),
            _ => new PluginUploadPackage(uploadId, Path.GetDirectoryName(packagePath)!, packagePath, pluginDlls[0].Path, "zip", pluginDlls[0].Info!)
        };
    }

    private static PluginUploadPackage LoadPreparedPluginUploadPackage(PluginManager pluginManager, string uploadId)
    {
        var uploadRoot = GetPluginUploadRoot(uploadId);
        var packagePath = Directory.EnumerateFiles(uploadRoot, "*", SearchOption.TopDirectoryOnly)
            .FirstOrDefault(path => !Path.GetFileName(path).Equals("extract", StringComparison.OrdinalIgnoreCase));

        if (string.IsNullOrWhiteSpace(packagePath))
        {
            throw new InvalidOperationException("上传文件不存在。上传可能已过期，请重新上传。");
        }

        return PreparePluginUploadPackage(pluginManager, uploadId, packagePath);
    }

    private static void InstallUploadedPlugin(string pluginRootPath, PluginUploadPackage package)
    {
        Directory.CreateDirectory(pluginRootPath);
        if (package.Type.Equals("dll", StringComparison.OrdinalIgnoreCase))
        {
            var targetPath = Path.Combine(pluginRootPath, Path.GetFileName(package.EntryAssemblyPath));
            if (File.Exists(targetPath)) File.Delete(targetPath);
            File.Copy(package.EntryAssemblyPath, targetPath);
            return;
        }

        var sourceRoot = GetZipInstallSourceRoot(package.RootPath, package.EntryAssemblyPath);
        var targetRoot = Path.Combine(pluginRootPath, Path.GetFileNameWithoutExtension(package.EntryAssemblyPath));
        if (Directory.Exists(targetRoot)) Directory.Delete(targetRoot, recursive: true);
        CopyDirectory(sourceRoot, targetRoot);
    }

    private static string GetZipInstallSourceRoot(string uploadRoot, string entryAssemblyPath)
    {
        var extractRoot = Path.Combine(uploadRoot, "extract");
        var relativeEntry = Path.GetRelativePath(extractRoot, entryAssemblyPath);
        var firstSeparator = relativeEntry.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]);
        if (firstSeparator <= 0) return extractRoot;

        var firstSegment = relativeEntry[..firstSeparator];
        var topLevelDirectories = Directory.EnumerateDirectories(extractRoot).Select(Path.GetFileName).ToArray();
        var topLevelFiles = Directory.EnumerateFiles(extractRoot).ToArray();
        return topLevelDirectories.Length == 1 && topLevelFiles.Length == 0 &&
               string.Equals(topLevelDirectories[0], firstSegment, StringComparison.OrdinalIgnoreCase)
            ? Path.Combine(extractRoot, firstSegment)
            : extractRoot;
    }

    private static void ExtractZipSafely(string zipPath, string destinationRoot)
    {
        var normalizedDestination = Path.GetFullPath(destinationRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        using var archive = ZipFile.OpenRead(zipPath);
        foreach (var entry in archive.Entries)
        {
            var destinationPath = Path.GetFullPath(Path.Combine(normalizedDestination, entry.FullName));
            if (!destinationPath.StartsWith(normalizedDestination + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(destinationPath, normalizedDestination, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("压缩包包含非法路径。" );
            }

            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(destinationPath);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            entry.ExtractToFile(destinationPath, overwrite: true);
        }
    }

    private static void CopyDirectory(string sourceRoot, string targetRoot)
    {
        foreach (var directory in Directory.EnumerateDirectories(sourceRoot, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(targetRoot, Path.GetRelativePath(sourceRoot, directory)));
        }

        Directory.CreateDirectory(targetRoot);
        foreach (var file in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories))
        {
            var targetFile = Path.Combine(targetRoot, Path.GetRelativePath(sourceRoot, file));
            Directory.CreateDirectory(Path.GetDirectoryName(targetFile)!);
            File.Copy(file, targetFile, overwrite: true);
        }
    }

    private static string GetPluginUploadRoot(string uploadId)
    {
        if (string.IsNullOrWhiteSpace(uploadId) || uploadId.Any(ch => !char.IsAsciiLetterOrDigit(ch)))
        {
            throw new InvalidOperationException("upload_id 无效。" );
        }

        return Path.Combine(Path.GetTempPath(), "ShiroBot", "plugin_uploads", uploadId);
    }

    private static bool TryNormalizeGitHubRepository(string? input, out string repository)
    {
        repository = string.Empty;
        if (string.IsNullOrWhiteSpace(input)) return false;

        var value = input.Trim();
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            if (!uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)) return false;

            value = uri.AbsolutePath.Trim('/');
            if (value.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) value = value[..^4];
        }

        value = value.Trim('/');
        var parts = value.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 2) return false;
        if (parts.Any(part => part is "." or ".." || part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)) return false;

        repository = string.Join('/', parts);
        return true;
    }

    private static object CreatePluginInfoResponse(PluginProbeInfo info) => new
    {
        id = info.Id,
        name = info.Name,
        version = info.Version,
        author = string.IsNullOrWhiteSpace(info.Author) ? "Unknown" : info.Author,
        repo = info.GithubRepo,
        description = info.Description ?? string.Empty,
        category = info.Category.ToString()
    };

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static SemaphoreSlim GetPluginOperationLock(string id)
    {
        var key = string.IsNullOrWhiteSpace(id) ? string.Empty : id.Trim().ToUpperInvariant();
        return PluginOperationLocks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
    }

    private static void SchedulePluginUploadCleanup(string uploadRoot)
    {
        _ = Task.Run(async () =>
        {
            await Task.Delay(PluginUploadTtl).ConfigureAwait(false);
            TryDeleteDirectory(uploadRoot);
        });
    }

    private static object CreateConfigResponse(CoreConfig config) => new
    {
        protocol = config.Protocol,
        enable_log = config.EnableLog,
        disable_console_input = config.DisableConsoleInput,
        github_proxy = config.GithubProxy,
        host_update_repository = config.HostUpdateRepository,
        avalonia_theme = config.AvaloniaTheme,
        owner_list = config.OwnerList,
        admin_list = config.AdminList,
        api = new
        {
            enable = config.Api.Enable,
            listen_url = config.Api.ListenUrl,
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
        if (TryGetString(patch, "protocol", out var protocol))
        {
            configManager.SetConfigValue(configPath, "protocol", protocol);
        }

        if (TryGetBool(patch, "enable_log", out var enableLog))
        {
            ConsoleHelper.IsEnabled = enableLog;
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

        if (TryGetString(patch, "avalonia_theme", out var avaloniaTheme))
        {
#if AVALONIA
            AvaloniaIntegration.AvaloniaIntegration.SetThemeMode(avaloniaTheme);
#endif
            configManager.SetConfigValue(configPath, "avalonia_theme", avaloniaTheme);
        }

        if (TryGetLongArray(patch, "owner_list", out var ownerList))
        {
            configManager.SetConfigValue(configPath, "owner_list", ownerList);
        }

        if (TryGetLongArray(patch, "admin_list", out var adminList))
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
            currentApiConfig.ListenUrl = listenUrl;
            configManager.SetConfigValue(configPath, "api.listen_url", listenUrl);
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

    private static bool TryGetLongArray(JsonElement element, string propertyName, out long[] value)
    {
        value = [];
        if (!element.TryGetProperty(propertyName, out var property)) return false;
        if (property.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException($"{propertyName} 必须是数字数组");
        }

        value = property.EnumerateArray().Select(item =>
        {
            if (item.ValueKind != JsonValueKind.Number || !item.TryGetInt64(out var number))
            {
                throw new InvalidOperationException($"{propertyName} 必须是数字数组");
            }

            return number;
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

    private static readonly DateTimeOffset AppStartedAt = DateTimeOffset.UtcNow;
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> PluginOperationLocks = new(StringComparer.OrdinalIgnoreCase);
    private const string ApiCorsPolicyName = "ShiroBotApiCors";
    private const string DisabledPluginSuffix = ".disable";
    private const long MaxPluginUploadBytes = 100L * 1024L * 1024L;
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

    private sealed record ApiError(string Code, string Message);

    private sealed record PluginUploadConfirmRequest(bool Replace = false, bool Enable = true);

    private sealed record GitHubPluginInstallRequest(string Repository, bool IncludePrerelease = false);

    private sealed record InstalledPluginInfo(string AssemblyPath, string Version);

    private sealed record PluginConfigTarget(string Id, string AssemblyPath);

    private sealed record ConfigSchemaItem(
        string key,
        string label,
        string type,
        string description,
        string? placeholder,
        string[] options,
        double? min,
        double? max);

    private sealed class ConfigFieldMetadata
    {
        public string Description { get; init; } = string.Empty;
        public string? Label { get; set; }
        public string? Type { get; set; }
        public string[] Options { get; set; } = [];
        public double Min { get; set; } = double.NaN;
        public double Max { get; set; } = double.NaN;
        public string? Placeholder { get; set; }
    }

    private sealed record PluginUploadPackage(
        string UploadId,
        string RootPath,
        string PackagePath,
        string EntryAssemblyPath,
        string Type,
        PluginProbeInfo Info);

    private sealed record PluginListItem(
        string id,
        string name,
        string version,
        bool enable,
        string author,
        string? repo,
        string description,
        string category);

    public async ValueTask DisposeAsync()
    {
        await app.StopAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
        await app.DisposeAsync().ConfigureAwait(false);
    }
}
