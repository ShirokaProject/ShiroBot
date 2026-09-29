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
    /// <summary>
    /// 调试/测试接口(/api/v1/debug/*),便于 AI 或脚本对适配器做端到端验证。
    /// 与主 API 共用鉴权。
    /// </summary>
    private static void MapDebugEndpoints(
        WebApplication app,
        ApiHostConfig config,
        BotContext botContext,
        HostEventDispatcher eventDispatcher)
    {
        var debug = app.MapGroup("/api/v1/debug");
        debug.AddEndpointFilter(async (context, next) =>
        {
            if (!IsAuthorized(context.HttpContext, config))
            {
                return Results.Json(new ApiError("unauthorized", "Missing or invalid API key."), statusCode: StatusCodes.Status401Unauthorized);
            }

            return await next(context).ConfigureAwait(false);
        });

        // 适配器信息:平台 + 登录账号
        debug.MapGet("/self", async () =>
        {
            var self = await botContext.User.GetSelfAsync().ConfigureAwait(false);
            return Results.Ok(new
            {
                platform = botContext.Platform,
                id = self.Id,
                name = self.Name
            });
        });

        // 群/频道列表
        debug.MapGet("/channels", async () =>
        {
            var channels = await botContext.Channel.GetChannelsAsync().ConfigureAwait(false);
            return Results.Ok(channels.Select(channel => new
            {
                id = channel.Id,
                type = channel.Type.ToString(),
                name = channel.Name
            }));
        });

        // 发送消息:{ "channel_id": "...", "channel_type": "group|direct", "text": "..." }
        debug.MapPost("/send", async (HttpContext context) =>
        {
            using var document = await JsonDocument.ParseAsync(context.Request.Body).ConfigureAwait(false);
            var root = document.RootElement;

            var channelId = root.TryGetProperty("channel_id", out var idProperty) ? idProperty.GetString() : null;
            var text = root.TryGetProperty("text", out var textProperty) ? textProperty.GetString() : null;
            if (string.IsNullOrWhiteSpace(channelId) || string.IsNullOrEmpty(text))
            {
                return Results.BadRequest(new ApiError("invalid_request", "channel_id 和 text 必填。"));
            }

            var channelType = root.TryGetProperty("channel_type", out var typeProperty) ? typeProperty.GetString() : "group";

            try
            {
                var sent = string.Equals(channelType, "direct", StringComparison.OrdinalIgnoreCase)
                    ? await botContext.Message.SendDirectMessageAsync(channelId, text).ConfigureAwait(false)
                    : await botContext.Message.SendGroupMessageAsync(channelId, text).ConfigureAwait(false);

                return Results.Ok(new { ok = true, message_id = sent.MessageId, timestamp = sent.Timestamp });
            }
            catch (Exception ex)
            {
                return Results.Json(
                    new ApiError("send_failed", $"{ex.GetType().Name}: {ex.Message}"),
                    statusCode: StatusCodes.Status502BadGateway);
            }
        });

        // 注入模拟消息事件到插件管线:{ "channel_id": "...", "user_id": "...", "text": "...", "direct": false }
        debug.MapPost("/inject", async (HttpContext context) =>
        {
            using var document = await JsonDocument.ParseAsync(context.Request.Body).ConfigureAwait(false);
            var root = document.RootElement;

            var text = root.TryGetProperty("text", out var textProperty) ? textProperty.GetString() : null;
            if (string.IsNullOrEmpty(text))
            {
                return Results.BadRequest(new ApiError("invalid_request", "text 必填。"));
            }

            var userId = root.TryGetProperty("user_id", out var userProperty) ? userProperty.GetString() ?? "10000" : "10000";
            var channelId = root.TryGetProperty("channel_id", out var channelProperty) ? channelProperty.GetString() ?? "10000" : "10000";
            var direct = root.TryGetProperty("direct", out var directProperty) && directProperty.GetBoolean();

            var message = new SDK.Models.MessageEvent
            {
                Platform = botContext.Platform,
                SelfId = null,
                MessageId = Random.Shared.NextInt64(100000, 999999).ToString(),
                Channel = direct
                    ? SDK.Models.Channel.Direct(userId)
                    : new SDK.Models.Channel(channelId, SDK.Models.ChannelType.Group) { Name = "debug", GuildId = channelId },
                Sender = new SDK.Models.User(userId) { Name = "DebugUser" },
                Member = direct ? null : new SDK.Models.Member(new SDK.Models.User(userId) { Name = "DebugUser" }),
                Segments = [new SDK.Models.TextSegment(text)],
                Timestamp = DateTimeOffset.UtcNow
            };

            await eventDispatcher.PublishAsync(message).ConfigureAwait(false);
            return Results.Ok(new { ok = true, injected_message_id = message.MessageId });
        });
    }

}
