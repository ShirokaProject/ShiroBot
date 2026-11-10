using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using ShiroBot.Configuration;
using ShiroBot.SDK.Models;

namespace ShiroBot.Hosting.Http;

internal sealed partial class HostHttpServer
{
    /// <summary>
    ///     调试/测试接口(/api/v1/debug/*),便于 AI 或脚本对适配器做端到端验证。
    ///     与主 API 共用鉴权。
    /// </summary>
    internal sealed partial class DiagnosticsEndpoints
    {
        public void MapDebug(WebApplication app, ApiHostConfig config)
        {
            var debug = app.MapGroup("/api/v1/debug");
            debug.AddEndpointFilter(async (context, next) =>
            {
                if (!IsAuthorized(context.HttpContext, config))
                    return Results.Json(new ApiError("unauthorized", "Missing or invalid API key."),
                        statusCode: StatusCodes.Status401Unauthorized);

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
                    return Results.BadRequest(new ApiError("invalid_request", "channel_id 和 text 必填。"));

                var channelType = root.TryGetProperty("channel_type", out var typeProperty)
                    ? typeProperty.GetString()
                    : "group";

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
                if (string.IsNullOrEmpty(text)) return Results.BadRequest(new ApiError("invalid_request", "text 必填。"));

                var userId = root.TryGetProperty("user_id", out var userProperty)
                    ? userProperty.GetString() ?? "10000"
                    : "10000";
                var channelId = root.TryGetProperty("channel_id", out var channelProperty)
                    ? channelProperty.GetString() ?? "10000"
                    : "10000";
                var direct = root.TryGetProperty("direct", out var directProperty) && directProperty.GetBoolean();

                var message = new MessageEvent
                {
                    Platform = botContext.Platform,
                    SelfId = null,
                    MessageId = Random.Shared.NextInt64(100000, 999999).ToString(),
                    Channel = direct
                        ? Channel.Direct(userId)
                        : new Channel(channelId, ChannelType.Group) { Name = "debug", GuildId = channelId },
                    Sender = new User(userId) { Name = "DebugUser" },
                    Member = direct ? null : new Member(new User(userId) { Name = "DebugUser" }),
                    Segments = [new TextSegment(text)],
                    Timestamp = DateTimeOffset.UtcNow
                };

                await eventDispatcher.PublishAsync(message).ConfigureAwait(false);
                return Results.Ok(new { ok = true, injected_message_id = message.MessageId });
            });
        }
    }
}