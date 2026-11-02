using System.Text.Json.Nodes;
using ShiroBot.SDK.Abstractions;
using ShiroBot.SDK.Core;
using ShiroBot.SDK.Plugin;

namespace ShiroBot.Samples.Greeter;

/// <summary>
/// 服务提供方：按名称提供 "greeter.greet" 服务，每次问候后在事件总线上发布 "greeter.greeted"。
/// 没有 OnUnloadAsync：服务、计时器都登记在 Context 上，卸载时由宿主自动注销和停止。
/// </summary>
[BotPlugin("sample-greeter", Name = "示例：问候服务", Version = "1.0.0",
    Description = "演示按名称提供服务、发布事件和生命周期作用域。")]
public sealed class GreeterPlugin : PluginBase
{
    private int _greeted;

    protected override Task LoadAsync()
    {
        // 参数和返回值都是 JSON：调用方不需要引用本插件或任何共享程序集。
        Context.Services.Provide("greeter.greet", async (arguments, cancellationToken) =>
        {
            var name = arguments?["name"]?.GetValue<string>() is { Length: > 0 } given ? given : "陌生人";
            var count = Interlocked.Increment(ref _greeted);
            await Context.Bus.EmitAsync("greeter.greeted",
                new JsonObject { ["name"] = name, ["count"] = count }, cancellationToken);
            return new JsonObject { ["text"] = $"你好，{name}！", ["count"] = count };
        });

        // 卸载时自动停止，不需要自己保存和取消。
        Context.Lifetime.SetInterval(TimeSpan.FromMinutes(10), _ =>
        {
            BotLog.Info($"累计问候 {_greeted} 次");
            return Task.CompletedTask;
        });
        return Task.CompletedTask;
    }
}
