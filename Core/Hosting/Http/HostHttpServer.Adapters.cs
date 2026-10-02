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
using ShiroBot.Components.Updates;

namespace ShiroBot.Hosting.Http;

internal sealed partial class HostHttpServer
{
    private static void MapAdapterEndpoints(RouteGroupBuilder api, HostRuntimeState runtimeState, ConfigManager configManager, AdapterManager adapterManager, ComponentReloadCoordinator reloadCoordinator, AdapterPackageManager adapterPackages)
    {
        api.MapGet("/adapter", () => Results.Ok(adapterManager.CreateStatus()));

        api.MapGet("/adapters/{id}/config", (string id) =>
        {
            var instance = adapterPackages.GetInstance(id);
            var package = instance is null ? null : adapterPackages.Get(instance.PackageId);
            if (package is null) return Results.NotFound(new { error = "adapter_not_found", message = $"未找到 Adapter: {id}" });
            var configPath = instance!.ConfigPath;
            return Results.Ok(new
            {
                adapter_id = instance!.Id,
                config = LoadTomlObject(configPath),
                schema = GetComponentConfigSchema(package.AssemblyPath, adapterManager.GetLoadedAssembly(id)),
                apply_status = adapterManager.LoadedIds.Contains(id, StringComparer.OrdinalIgnoreCase)
                    ? "loaded"
                    : "pending_start"
            });
        });

        api.MapPatch("/adapters/{id}/config", async (string id, HttpContext context) =>
        {
            var instance = adapterPackages.GetInstance(id);
            var package = instance is null ? null : adapterPackages.Get(instance.PackageId);
            if (package is null) return Results.NotFound(new { error = "adapter_not_found", message = $"未找到 Adapter: {id}" });

            JsonDocument document;
            try
            {
                document = await JsonDocument.ParseAsync(context.Request.Body, cancellationToken: context.RequestAborted)
                    .ConfigureAwait(false);
            }
            catch (JsonException)
            {
                return Results.BadRequest(new { error = "invalid_json", message = "Adapter 配置更新内容不是有效 JSON。" });
            }

            using (document)
            {
                if (document.RootElement.ValueKind != JsonValueKind.Object ||
                    !document.RootElement.TryGetProperty("config", out var configPatch))
                    return Results.BadRequest(new { error = "invalid_request", message = "请求必须包含 config 对象。" });

                var configPath = instance!.ConfigPath;
                try
                {
                    ApplyComponentConfigPatch(configManager, configPath, configPatch,
                        GetComponentConfigSchema(package.AssemblyPath, adapterManager.GetLoadedAssembly(id)));
                    var applied = await reloadCoordinator.ExecuteAdapterMutationAsync(
                        () => adapterManager.ApplyConfigByIdAsync(id)).ConfigureAwait(false);
                    return Results.Ok(new
                    {
                        ok = true,
                        adapter_id = instance!.Id,
                        config = LoadTomlObject(configPath),
                        schema = GetComponentConfigSchema(package.AssemblyPath, adapterManager.GetLoadedAssembly(id)),
                        apply_status = applied ? "applied" :
                            adapterManager.LoadedIds.Contains(id, StringComparer.OrdinalIgnoreCase)
                                ? "legacy_saved_only"
                                : "pending_start"
                    });
                }
                catch (InvalidOperationException ex)
                {
                    return Results.BadRequest(new { error = "invalid_config", message = ex.Message });
                }
                catch (Exception ex)
                {
                    return Results.Conflict(new { error = "config_apply_failed", message = ex.Message, saved = true });
                }
            }
        });

        api.MapPost("/adapter/reload", async (HttpContext context) =>
        {
            string? assemblyPath = null;
            if (context.Request.ContentLength is > 0)
            {
                using var document = await JsonDocument.ParseAsync(context.Request.Body, cancellationToken: context.RequestAborted)
                    .ConfigureAwait(false);
                if (document.RootElement.TryGetProperty("assembly_path", out var pathElement))
                {
                    assemblyPath = pathElement.GetString();
                }
            }

            try
            {
                await reloadCoordinator.ReloadAdapterAsync(assemblyPath).ConfigureAwait(false);
                return Results.Ok(new { ok = true, adapter = adapterManager.CreateStatus() });
            }
            catch (Exception ex)
            {
                runtimeState.RecordEvent("Adapter reload failed: " + ex.Message, "error");
                return Results.Conflict(new { ok = false, error = "adapter_reload_failed", message = ex.Message });
            }
        });

        api.MapPost("/adapter/stop", async () =>
        {
            try
            {
                await reloadCoordinator.ExecuteAdapterMutationAsync(() => adapterManager.StopAsync()).ConfigureAwait(false);
                return Results.Ok(new { ok = true, adapter = adapterManager.CreateStatus() });
            }
            catch (Exception ex)
            {
                return Results.Conflict(new { ok = false, error = "adapter_stop_failed", message = ex.Message });
            }
        });

        api.MapGet("/adapters", () =>
        {
            var snapshots = adapterManager.GetSnapshot();
            var instances = adapterPackages.ListInstances();
            return Results.Ok(instances.Select(instance =>
            {
                var package = adapterPackages.Get(instance.PackageId)!;
                var snapshot = snapshots.FirstOrDefault(item => string.Equals(item.Id, instance.Id, StringComparison.OrdinalIgnoreCase));
                return new
                {
                    id = instance.Id, package_id = package.Id, name = instance.Name,
                    version = snapshot?.Version ?? package.Version, platform = snapshot?.Platform ?? package.Platform,
                    description = package.Description, assembly_path = (string?)instance.AssemblyPath,
                    config_path = (string?)instance.ConfigPath, enabled = instance.Enabled,
                    loaded = snapshot?.Loaded ?? false, error = snapshot?.Error, restartRequired = snapshot?.RestartRequired ?? false
                };
            }).Concat(snapshots.Where(item => !instances.Any(instance => string.Equals(instance.Id, item.Id, StringComparison.OrdinalIgnoreCase))).Select(item => new
            {
                id = item.Id, package_id = item.Id, name = item.Name, version = item.Version ?? string.Empty, platform = item.Platform,
                description = item.Description, assembly_path = item.AssemblyPath, config_path = (string?)null,
                enabled = false, loaded = item.Loaded, error = item.Error, restartRequired = item.RestartRequired
            })));
        });
        api.MapPost("/adapters/{packageId}/instances", async (string packageId, AdapterInstanceCreateRequest request) =>
        {
            try
            {
                InstalledAdapterInstance? instance = null;
                await reloadCoordinator.ExecuteAdapterMutationAsync(() =>
                {
                    instance = adapterPackages.CreateInstance(packageId, request.Id, request.Name);
                    return Task.CompletedTask;
                }).ConfigureAwait(false);
                return Results.Ok(new { ok = true, adapter = new { id = instance!.Id }, message = "实例已创建，请配置后启动。" });
            }
            catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
            { return Results.Conflict(new { ok = false, error = "instance_create_failed", message = ex.Message }); }
        });

        api.MapGet("/adapter-market/adapters", async (HttpContext context) =>
        {
            var installed = adapterPackages.List();
            var loaded = adapterManager.LoadedIds;
            var entries = await AdapterMarketplaceCache.GetAsync(context.RequestAborted).ConfigureAwait(false);
            foreach (var node in entries)
            {
                if (node is not JsonObject entry || entry["id"]?.GetValue<string>() is not { } id) continue;
                var package = installed.FirstOrDefault(candidate => string.Equals(candidate.Id, id, StringComparison.OrdinalIgnoreCase));
                if (package is null) continue;
                entry["installed"] = new JsonObject
                {
                    ["version"] = package.Version,
                    ["enabled"] = package.Enabled,
                    ["loaded"] = loaded.Contains(package.Id, StringComparer.OrdinalIgnoreCase)
                };
            }
            return Results.Ok(new { adapters = entries });
        });

        api.MapPost("/adapters/upload", async (HttpContext context) =>
        {
            if (!context.Request.HasFormContentType) return Results.BadRequest(new { error = "invalid_request", message = "请使用 multipart/form-data 上传 Adapter 文件。" });
            var form = await context.Request.ReadFormAsync(context.RequestAborted).ConfigureAwait(false);
            var file = form.Files.GetFile("file") ?? form.Files.FirstOrDefault();
            if (file is null || file.Length == 0) return Results.BadRequest(new { error = "missing_file", message = "未收到 Adapter 文件。" });
            if (file.Length > MaxPluginUploadBytes) return Results.BadRequest(new { error = "file_too_large", message = "Adapter 文件不能超过 100MB。" });
            var uploadId = Guid.NewGuid().ToString("N");
            var root = GetAdapterUploadRoot(uploadId);
            Directory.CreateDirectory(root);
            try
            {
                var path = Path.Combine(root, Path.GetFileName(file.FileName));
                await using (var stream = File.Create(path))
                {
                    await file.CopyToAsync(stream, context.RequestAborted).ConfigureAwait(false);
                }
                var probe = adapterPackages.Prepare(path, root);
                ScheduleAdapterUploadCleanup(root);
                return Results.Ok(CreateAdapterPreview(uploadId, probe, adapterPackages.Get(probe.Id), "upload", Path.GetFileName(path), file.Length));
            }
            catch (InvalidOperationException ex) { TryDeleteDirectory(root); return Results.BadRequest(new { error = "invalid_adapter", message = ex.Message }); }
        });

        api.MapPost("/adapters/upload/{uploadId}/confirm", async (string uploadId, HttpContext context) =>
        {
            var root = GetAdapterUploadRoot(uploadId);
            if (!Directory.Exists(root)) return Results.NotFound(new { error = "upload_not_found" });
            var request = await JsonSerializer.DeserializeAsync<AdapterUploadConfirmRequest>(context.Request.Body, JsonSerializerOptions.Web, context.RequestAborted).ConfigureAwait(false) ?? new();
            try
            {
                var source = Directory.EnumerateFiles(root, "*", SearchOption.TopDirectoryOnly).FirstOrDefault(path => Path.GetExtension(path) is ".dll" or ".zip")
                    ?? throw new InvalidOperationException("上传文件不存在。");
                var probe = adapterPackages.Prepare(source, root);
                var existing = adapterPackages.Get(probe.Id);
                if (existing is not null && !request.Replace) return Results.Conflict(new { error = "adapter_exists", message = "Adapter 已存在，请确认替换。" });
                var runningInstances = adapterPackages.ListInstances().Where(instance =>
                    string.Equals(instance.PackageId, probe.Id, StringComparison.OrdinalIgnoreCase) &&
                    adapterManager.LoadedIds.Contains(instance.Id, StringComparer.OrdinalIgnoreCase)).ToArray();
                var wasLoaded = runningInstances.Length > 0;
                async Task ActivateInstances(InstalledAdapterPackage package, bool restoring = false)
                {
                    var candidates = adapterPackages.ListInstances().Where(instance => string.Equals(instance.PackageId, package.Id, StringComparison.OrdinalIgnoreCase) &&
                        (instance.Enabled || runningInstances.Any(previous => string.Equals(previous.Id, instance.Id, StringComparison.OrdinalIgnoreCase)))).ToArray();
                    var started = new List<string>();
                    try
                    {
                        foreach (var instance in candidates)
                        {
                            await adapterManager.LoadInstanceAsync(instance, forceFreshImage: restoring).ConfigureAwait(false);
                            started.Add(instance.Id);
                        }
                    }
                    catch
                    {
                        foreach (var id in started)
                            try { await adapterManager.StopByIdAsync(id).ConfigureAwait(false); } catch (ComponentUnloadPendingException) { }
                        throw;
                    }
                }
                string? pendingReason = null;
                var result = await reloadCoordinator.ExecuteAdapterMutationAsync(async () =>
                {
                    try
                    {
                        foreach (var instance in runningInstances) await adapterManager.StopByIdAsync(instance.Id).ConfigureAwait(false);
                        return await adapterPackages.InstallAndActivateAsync(
                            probe,
                            existing?.Enabled ?? request.Enable,
                            installed => ActivateInstances(installed),
                            wasLoaded ? restored => ActivateInstances(restored, restoring: true) : null,
                            activateWhenDisabled: wasLoaded).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (existing is not null &&
                                               ex is ComponentUnloadPendingException or IOException or UnauthorizedAccessException)
                    {
                        // The running copy cannot be released (a referenced assembly, a locked file): hand the
                        // new version to the next start and keep the current one serving until then.
                        pendingReason = ex.Message;
                        adapterPackages.StageUpdate(probe, existing.Enabled);
                        foreach (var previous in runningInstances)
                        {
                            if (adapterManager.LoadedIds.Contains(previous.Id, StringComparer.OrdinalIgnoreCase)) continue;
                            try { await adapterManager.LoadInstanceAsync(previous).ConfigureAwait(false); }
                            catch (Exception reloadError) { BotLog.Warning($"重新启动当前版本 Adapter {previous.Id} 失败: {reloadError.Message}"); }
                        }
                        return null;
                    }
                }).ConfigureAwait(false);
                TryDeleteDirectory(root);
                if (result is null)
                {
                    return Results.Ok(new
                    {
                        ok = true,
                        adapter = new { id = probe.Id, enabled = request.Enable },
                        rollback = false,
                        restarted = false,
                        pending_restart = true,
                        reason = pendingReason,
                        message = $"当前版本无法热替换，新版本 {probe.Version} 已暂存，将在下次重启宿主时替换。重启前当前版本继续运行。"
                    });
                }
                var installed = result.Package;
                if (result.StartError is not null)
                {
                    // Installed but left disabled: configure it, then start it from the adapter page.
                    return Results.Ok(new
                    {
                        ok = true,
                        adapter = new { id = installed.Id, enabled = false },
                        rollback = false,
                        restarted = false,
                        started = false,
                        start_error = result.StartError,
                        message = $"Adapter 已安装，但启动失败，已保持停用。请先完成配置再启动：{result.StartError}"
                    });
                }
                return Results.Ok(new { ok = true, adapter = new { id = installed.Id, enabled = installed.Enabled }, rollback = false, restarted = existing?.Enabled ?? request.Enable, started = installed.Enabled });
            }
            catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
            { return Results.Conflict(new { error = "install_failed", message = ex.Message, rollback = ex.Message.Contains("恢复", StringComparison.Ordinal), restarted = ex.Message.Contains("恢复", StringComparison.Ordinal) }); }
        });

        api.MapDelete("/adapters/upload/{uploadId}", (string uploadId) => { TryDeleteDirectory(GetAdapterUploadRoot(uploadId)); return Results.Ok(new { ok = true }); });
        api.MapPost("/adapters/install/github", async (AdapterGitHubInstallRequest request, HttpContext context) =>
        {
            if (!TryNormalizeGitHubRepository(request.Repository, out var repository)) return Results.BadRequest(new { error = "invalid_repository" });
            string assetUrl;
            string assetName;
            string? digest = null;
            if (!string.IsNullOrWhiteSpace(request.AssetUrl))
            {
                if (!TryNormalizeSha256(request.AssetSha256, out digest) || !TryNormalizeGitHubAssetUrl(request.AssetUrl, request.AssetName, repository, out assetUrl, out assetName))
                    return Results.BadRequest(new { error = "invalid_asset", message = "assetUrl、assetName 或 assetSha256 无效。" });
            }
            else
            {
                var release = await Updater.GetLatestPluginPackageAsync(repository, request.IncludePrerelease, context.RequestAborted).ConfigureAwait(false);
                if (release is null || string.IsNullOrWhiteSpace(release.AssetDownloadUrl) || string.IsNullOrWhiteSpace(release.AssetName)) return Results.NotFound(new { error = "release_not_found" });
                assetUrl = release.AssetDownloadUrl;
                assetName = release.AssetName;
            }
            var uploadId = Guid.NewGuid().ToString("N");
            var root = GetAdapterUploadRoot(uploadId);
            Directory.CreateDirectory(root);
            try
            {
                var path = Path.Combine(root, Path.GetFileName(assetName));
                await Updater.DownloadFileAsync(assetUrl, path, context.RequestAborted, MaxPluginUploadBytes, digest).ConfigureAwait(false);
                var probe = adapterPackages.Prepare(path, root);
                ScheduleAdapterUploadCleanup(root);
                return Results.Ok(CreateAdapterPreview(uploadId, probe, adapterPackages.Get(probe.Id), "github", assetName, new FileInfo(path).Length, repository));
            }
            catch (InvalidOperationException ex) { TryDeleteDirectory(root); return Results.BadRequest(new { error = "invalid_adapter", message = ex.Message }); }
        });
        api.MapPost("/adapters/{id}/start", async (string id) =>
        {
            try
            {
                var instance = adapterPackages.GetInstance(id); if (instance is null) return Results.NotFound(new { ok = false, error = "adapter_not_found", restartRequired = false });
                await reloadCoordinator.ExecuteAdapterMutationAsync(async () => { await adapterManager.LoadInstanceAsync(instance).ConfigureAwait(false); adapterPackages.SetInstanceEnabled(instance.Id, true); }).ConfigureAwait(false);
                return Results.Ok(new { ok = true, restartRequired = false });
            }
            catch (Exception ex) { return AdapterOperationError("adapter_start_failed", ex); }
        });
        api.MapPost("/adapters/{id}/stop", async (string id) => { try { await reloadCoordinator.ExecuteAdapterMutationAsync(async () => { await adapterManager.StopByIdAsync(id).ConfigureAwait(false); adapterPackages.SetInstanceEnabled(id, false); }).ConfigureAwait(false); return Results.Ok(new { ok = true, restartRequired = false }); } catch (Exception ex) { return AdapterOperationError("adapter_stop_failed", ex); } });
        api.MapPost("/adapters/{id}/reload", async (string id) => { try { await reloadCoordinator.ReloadAdapterByIdAsync(id).ConfigureAwait(false); return Results.Ok(new { ok = true, restartRequired = false }); } catch (Exception ex) { return AdapterOperationError("adapter_reload_failed", ex); } });
        api.MapDelete("/adapters/{id}", async (string id) =>
        {
            try
            {
                var restartRequired = adapterManager.GetSnapshot().Any(item =>
                    string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase) && item.RestartRequired);
                await reloadCoordinator.ExecuteAdapterMutationAsync(async () =>
                {
                    if (adapterManager.LoadedIds.Contains(id, StringComparer.OrdinalIgnoreCase))
                    {
                        try { await adapterManager.StopByIdAsync(id).ConfigureAwait(false); }
                        catch (ComponentUnloadPendingException) { restartRequired = true; }
                    }
                    adapterPackages.DeleteInstance(id);
                    adapterManager.ForgetRemovedAdapter(id);
                }).ConfigureAwait(false);
                return Results.Ok(new
                {
                    ok = true,
                    restartRequired,
                    message = restartRequired ? "适配器文件已删除，重启宿主后将释放残留程序集。" : "适配器已删除。"
                });
            }
            catch (Exception ex) { return AdapterOperationError("adapter_delete_failed", ex); }
        });
    }

    private sealed record AdapterInstanceCreateRequest(string Id, string? Name = null);

    private static object CreateAdapterPreview(
        string uploadId,
        AdapterPackageProbe probe,
        InstalledAdapterPackage? installed,
        string sourceType,
        string fileName,
        long size,
        string? repository = null) => new
    {
        upload_id = uploadId,
        status = "parsed",
        source = new { type = sourceType, repository },
        adapter = new { id = probe.Id, name = probe.Name, version = probe.Version, platform = probe.Platform, description = probe.Description, entry_assembly = Path.GetFileName(probe.EntryAssemblyPath) },
        package = new { file_name = fileName, type = probe.Type, size },
        conflict = new { exists = installed is not null, installed_version = installed?.Version, uploaded_version = probe.Version, action = installed is null ? "install" : "replace" }
    };

    private static IResult AdapterOperationError(string error, Exception exception)
    {
        var restartRequired = exception is ComponentUnloadPendingException ||
                              exception.Message.Contains("需要重启", StringComparison.OrdinalIgnoreCase);
        return Results.Conflict(new { ok = false, error, message = exception.Message, restartRequired });
    }

}
