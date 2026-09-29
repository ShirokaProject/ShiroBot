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
    private static void MapPluginInstallEndpoints(RouteGroupBuilder api, PluginManager pluginManager, HostEventDispatcher eventDispatcher, PluginRouteConfig routePolicy)
    {
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

                var package = PreparePluginUploadPackage(pluginManager, packagePath);
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
                var pluginDirectory = GetPluginInstallDirectory(pluginManager.PluginRootPath, package.Info.Id);
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

                string? preservedConfigPath = null;
                if (installed is not null)
                {
                    var installedConfigPath = GetPluginConfigPath(pluginManager, installed.AssemblyPath, package.Info.Id);
                    if (File.Exists(installedConfigPath))
                    {
                        preservedConfigPath = Path.Combine(uploadRoot, "preserved-config.toml");
                        File.Copy(installedConfigPath, preservedConfigPath, overwrite: true);
                    }

                    var installedDirectory = Path.GetFullPath(Path.GetDirectoryName(installed.AssemblyPath)!);
                    if (package.Type.Equals("dll", StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(installedDirectory, Path.GetFullPath(pluginDirectory), StringComparison.OrdinalIgnoreCase))
                    {
                        // A single-file plugin may keep its configuration beside the DLL.
                        // Replacing it must not remove the whole plugin directory.
                        pluginManager.SuppressWatcherPath(installed.AssemblyPath);
                        File.Delete(installed.AssemblyPath);
                    }
                    else
                    {
                        DeletePluginPath(pluginManager.PluginRootPath, installed.AssemblyPath, package.Info.Id);
                    }
                }

                var installedAssemblyPath = InstallUploadedPlugin(pluginManager, package);
                if (preservedConfigPath is not null)
                {
                    var configPath = GetPluginConfigPath(pluginManager, installedAssemblyPath, package.Info.Id);
                    Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
                    File.Copy(preservedConfigPath, configPath, overwrite: true);
                }
                if (request.Enable)
                {
                    await pluginManager.ScheduleLoadPluginByName(eventDispatcher, routePolicy, package.Info.Id).ConfigureAwait(false);
                }
                else
                {
                    pluginManager.SuppressWatcherPath(installedAssemblyPath);
                    DisablePluginFile(installedAssemblyPath);
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

            var hasRepository = TryNormalizeGitHubRepository(request.Repository, out var repository);
            var hasAssetUrl = !string.IsNullOrWhiteSpace(request.AssetUrl);
            if (hasAssetUrl && !hasRepository)
            {
                return Results.BadRequest(new { error = "missing_repository", message = "使用 assetUrl 安装时必须提供对应的 GitHub repository。" });
            }
            if (!hasAssetUrl && !hasRepository)
            {
                return Results.BadRequest(new { error = "invalid_repository", message = "repository 必须是 owner/repo 或 GitHub 仓库 URL。" });
            }

            string assetDownloadUrl;
            string assetName;
            string? releaseName = null;
            string? releaseVersion = null;
            string? releaseUrl = null;

            if (hasAssetUrl)
            {
                if (!TryNormalizeSha256(request.AssetSha256, out var assetSha256))
                {
                    return Results.BadRequest(new { error = "invalid_asset_digest", message = "assetSha256 必须是 sha256: 后跟 64 位十六进制摘要。" });
                }
                if (!TryNormalizeGitHubAssetUrl(
                        request.AssetUrl,
                        request.AssetName,
                        hasRepository ? repository : null,
                        out assetDownloadUrl,
                        out assetName))
                {
                    return Results.BadRequest(new { error = "invalid_asset_url", message = "assetUrl 必须属于 repository 对应的 GitHub Release，且是 .zip 或 .dll HTTPS URL。" });
                }

                request = request with { AssetSha256 = assetSha256 };
            }
            else
            {
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

                assetDownloadUrl = packageInfo.AssetDownloadUrl;
                assetName = packageInfo.AssetName;
                repository = packageInfo.Repository;
                releaseName = packageInfo.ReleaseName;
                releaseVersion = packageInfo.Version;
                releaseUrl = packageInfo.ReleaseUrl;
            }

            var uploadId = Guid.NewGuid().ToString("N");
            var uploadRoot = GetPluginUploadRoot(uploadId);
            Directory.CreateDirectory(uploadRoot);

            try
            {
                var safeAssetName = Path.GetFileName(assetName);
                var packagePath = Path.Combine(uploadRoot, safeAssetName);
                await Updater.DownloadFileAsync(
                    assetDownloadUrl,
                    packagePath,
                    context.RequestAborted,
                    MaxPluginUploadBytes,
                    hasAssetUrl ? request.AssetSha256 : null).ConfigureAwait(false);

                var package = PreparePluginUploadPackage(pluginManager, packagePath);
                var installed = FindInstalledPlugin(pluginManager, package.Info.Id);
                SchedulePluginUploadCleanup(uploadRoot);

                return Results.Ok(new
                {
                    upload_id = uploadId,
                    status = "parsed",
                    source = new
                    {
                        type = "github",
                        repository = hasRepository ? repository : null,
                        release_name = releaseName,
                        release_version = releaseVersion,
                        release_url = releaseUrl,
                        asset_name = assetName,
                        asset_type = Path.GetExtension(assetName).TrimStart('.').ToLowerInvariant()
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
    }

}
