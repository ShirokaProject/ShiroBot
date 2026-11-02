using System.Text.Json.Nodes;
using ShiroBot.SDK.Abstractions;
using ShiroBot.SDK.Core;
using ShiroBot.SDK.Models;
using ShiroBot.SDK.Plugin;

namespace ShiroBot.Samples.GreeterClient;

/// <summary>
/// 调用方：收到 "#greet 名字" 时调用 "greeter.greet"，并订阅 "greeter.greeted" 事件。
/// 只认服务名，不引用问候插件；问候插件卸载、更新或重新加载时，本插件不用跟着卸载。
/// </summary>
[BotPlugin("sample-greeter-client", Name = "示例：问候命令", Version = "1.0.0",
    Description = "演示按名称调用其他插件的服务和订阅事件。")]
public sealed class GreeterClientPlugin : PluginBase
{
    protected override void ConfigureRoutes() => AllCommands.MapPrefix("#greet", GreetAsync);

    protected override Task LoadAsync()
    {
        // 订阅随卸载自动取消。
        Context.Bus.On("greeter.greeted", (payload, _) =>
        {
            BotLog.Info($"{payload?["name"]} 收到了第 {payload?["count"]} 次问候");
            return Task.CompletedTask;
        });
        return Task.CompletedTask;
    }

    private async Task GreetAsync(MessageEvent message)
    {
        var name = message.GetPlainText().Trim()["#greet".Length..].Trim();
        string reply;
        try
        {
            var result = await Context.Services.CallAsync("greeter.greet", new JsonObject { ["name"] = name },
                Context.Lifetime.Stopping);
            reply = result?["text"]?.GetValue<string>() ?? "问候服务没有返回内容。";
        }
        catch (PluginServiceUnavailableException)
        {
            reply = "问候服务没有加载。";
        }
        await Context.Message.ReplyAsync(message, new TextSegment(reply));
    }
}
