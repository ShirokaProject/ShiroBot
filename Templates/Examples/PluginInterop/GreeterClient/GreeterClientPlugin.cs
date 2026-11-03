using ShiroBot.SDK.Abstractions;
using ShiroBot.SDK.Core;
using ShiroBot.SDK.Models;
using ShiroBot.SDK.Plugin;
using ShiroBot.Samples.Greeter;

namespace ShiroBot.Samples.GreeterClient;

/// <summary>
/// 调用方：收到 "#greet 名字" 时调用问候服务，并订阅问候事件。
/// GreeterApi 由 SDK 从问候插件的 DLL 生成，编进本插件；问候插件卸载、更新或重新加载时，本插件不用跟着卸载。
/// </summary>
[BotPlugin("sample-greeter-client", Name = "示例：问候命令", Version = "1.0.0",
    Description = "演示引用其他插件的契约、调用服务和订阅事件。")]
public sealed class GreeterClientPlugin : PluginBase
{
    protected override void ConfigureRoutes() => AllCommands.MapPrefix("#greet", GreetAsync);

    protected override Task LoadAsync()
    {
        // 订阅随卸载自动取消。
        GreeterApi.Greeted.On(Context, (greeted, _) =>
        {
            BotLog.Info($"{greeted.Name} 收到了第 {greeted.Count} 次问候");
            return Task.CompletedTask;
        });
        return Task.CompletedTask;
    }

    private async Task GreetAsync(MessageEvent message)
    {
        var name = message.GetPlainText().Trim()["#greet".Length..].Trim();
        var reply = await GreeterApi.Greet.TryCallAsync(Context, new GreetRequest(name), Context.Lifetime.Stopping);
        await Context.Message.ReplyAsync(message, new TextSegment(reply?.Text ?? "问候服务没有加载。"));
    }
}
