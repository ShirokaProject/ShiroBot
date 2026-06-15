using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Reflection;
using ShiroBot.Core;
using ShiroBot.Hosting.Context;
using ShiroBot.SDK.Abstractions;

namespace ShiroBot.Hosting;

internal sealed class HostHttpServer(WebApplication app) : IAsyncDisposable
{
    public static async Task<HostHttpServer?> StartAsync(ApiHostConfig config, WebHostContext webHostContext)
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
        builder.Services.AddSingleton(webHostContext);

        var app = builder.Build();

        MapDashboardAssets(app, config);
        MapApiEndpoints(app, config);

        app.MapFallback((HttpContext context, WebHostContext registry) => registry.HandleRequest(context));

        await app.StartAsync().ConfigureAwait(false);
        BotLog.Info("宿主 API 服务已启动: " + string.Join(", ", listenUrls));
        return new HostHttpServer(app);
    }

    private static void MapDashboardAssets(WebApplication app, ApiHostConfig config)
    {
        var fileProvider = new EmbeddedFileProvider(Assembly.GetExecutingAssembly(), "Assets.dashboard");

        app.UseDefaultFiles(new DefaultFilesOptions
        {
            FileProvider = fileProvider,
            RequestPath = string.Empty
        });
        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = fileProvider,
            RequestPath = string.Empty
        });
    }

    private static void MapApiEndpoints(WebApplication app, ApiHostConfig config)
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
        api.MapGet("/overview", () => Results.Ok(new
        {
            bot_version = "v1.0",
            plugins_count = 5,
            adapter = "discord",
            message_count = 1000,
            uptime = "1234567890", 
        }));
        
        //插件
        api.MapGet("/plugins/list", () => Results.Ok(new[]
        {
            new
            {
                name = "示例插件1",
                auther = "",
                version = "v1.0",
                enabled = true
            }
        }));
        
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
        
        
        
        api.MapPost("/plugins/{id}/enable", (string id) =>
        {
            //启用插件的逻辑
            return Results.Ok(new { ok = true, message = $"Plugin {id} enabled." });
        });
        
        api.MapPost("/plugins/{id}/disable", (string id) =>
        {
            //禁用插件的逻辑
            return Results.Ok(new { ok = true, message = $"Plugin {id} disabled." });
        });
        
        api.MapPost("/plugins/{id}/update", (string id) =>
        {
            //更新插件的逻辑
            return Results.Ok(new { ok = true, message = $"Plugin {id} updated." });
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
        if (!authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return false;

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

    private static readonly DateTimeOffset AppStartedAt = DateTimeOffset.UtcNow;

    private sealed record ApiError(string Code, string Message);

    public async ValueTask DisposeAsync()
    {
        await app.StopAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
        await app.DisposeAsync().ConfigureAwait(false);
    }
}
