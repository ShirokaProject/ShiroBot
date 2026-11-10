using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using ShiroBot.Adapters;
using ShiroBot.Components.Reloading;
using ShiroBot.Configuration;
using ShiroBot.Hosting.Context;
using ShiroBot.Hosting.Events;
using ShiroBot.Hosting.Logging;
using ShiroBot.Hosting.Runtime;
using ShiroBot.Packages;
using ShiroBot.Plugins;

namespace ShiroBot.Hosting.Http;

internal interface IHostApiEndpoints
{
    void Map(RouteGroupBuilder api);
}

internal static class HostHttpServices
{
    public static IServiceCollection AddHostHttpServer(this IServiceCollection services)
    {
        services.AddSingleton<IHostApiEndpoints, HostHttpServer.SystemEndpoints>();
        services.AddSingleton<IHostApiEndpoints, HostHttpServer.PluginCatalogEndpoints>();
        services.AddSingleton<IHostApiEndpoints, HostHttpServer.PluginInstallEndpoints>();
        services.AddSingleton<IHostApiEndpoints, HostHttpServer.PluginManagementEndpoints>();
        services.AddSingleton<IHostApiEndpoints, HostHttpServer.PluginPageEndpoints>();
        services.AddSingleton<IHostApiEndpoints, HostHttpServer.ComponentEndpoints>();
        services.AddSingleton<HostHttpServer.DiagnosticsEndpoints>();
        services.AddSingleton<IHostApiEndpoints>(provider =>
            provider.GetRequiredService<HostHttpServer.DiagnosticsEndpoints>());
        services.AddSingleton<IHostedService, HostHttpServer>();
        return services;
    }
}

internal sealed partial class HostHttpServer
{
    internal sealed partial class SystemEndpoints(
        IOptions<ApiHostConfig> options,
        ConfigManager configManager,
        HostRuntimeState runtimeState,
        ModelPackageRegistry modelPackages,
        HostPowerControl powerControl) : IHostApiEndpoints
    {
        public void Map(RouteGroupBuilder api)
        {
            var config = options.Value;
            //鉴权
            api.MapGet("/auth", () => Results.Ok(new { ok = true }));

            //概览
            api.MapGet("/overview", () => Results.Ok(runtimeState.CreateOverview()));
            api.MapGet("/restart-notice", (string? scope) =>
                scope is null or "plugin" or "adapter"
                    ? Results.Ok(runtimeState.CreateRestartNotice(scope))
                    : Results.BadRequest(new { error = "invalid_scope", message = "scope 必须为 plugin 或 adapter。" }));

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

            MapUpdates(api);
            MapConfiguration(api);
            api.MapGet("/status", () => Results.Ok(new
            {
                name = "ShiroBot",
                started_at = AppStartedAt,
                uptime_seconds = (long)(DateTimeOffset.UtcNow - AppStartedAt).TotalSeconds,
                api = new
                {
                    enabled = config.Enable,
                    auth_enabled = true
                }
            }));
        }
    }

    internal sealed partial class ComponentEndpoints(
        HostRuntimeState runtimeState,
        ConfigManager configManager,
        AdapterManager adapterManager,
        ComponentReloadCoordinator reloadCoordinator,
        AdapterPackageManager adapterPackages,
        PluginManager pluginManager) : IHostApiEndpoints
    {
        public void Map(RouteGroupBuilder api)
        {
            MapAdapters(api);
            MapRawConfig(api);
        }
    }

    internal sealed partial class DiagnosticsEndpoints(
        BotContext botContext,
        HostEventDispatcher eventDispatcher,
        HostLogHub logHub) : IHostApiEndpoints
    {
        public void Map(RouteGroupBuilder api)
        {
            MapLogs(api);
            MapDirectory(api);
        }
    }
}