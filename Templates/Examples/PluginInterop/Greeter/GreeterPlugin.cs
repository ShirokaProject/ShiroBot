using System.ComponentModel;
using ShiroBot.SDK.Abstractions;
using ShiroBot.SDK.Core;
using ShiroBot.SDK.Plugin;

namespace ShiroBot.Samples.Greeter;

public sealed record GreetRequest([property: Description("要问候的名字")] string Name);
public sealed record GreetReply(string Text, int Count);
public sealed record Greeted(string Name, int Count);

/// <summary>
/// 对外的契约。其他插件把本插件的 DLL 放进自己的 contracts 目录，构建时就会得到 GreeterApi，
/// 运行时双方只交换 JSON，不共享程序集。
/// </summary>
[PluginContract]
public static partial class GreeterApi
{
    [PluginService("greeter.greet")]
    public static partial PluginService<GreetRequest, GreetReply> Greet { get; }

    [PluginEvent("greeter.greeted")]
    public static partial PluginEvent<Greeted> Greeted { get; }
}

/// <summary>
/// 服务提供方：提供 "greeter.greet"，每次问候后发布 "greeter.greeted"。
/// 没有 OnUnloadAsync：服务、计时器都登记在 Context 上，卸载时由宿主自动注销和停止。
/// </summary>
[BotPlugin("sample-greeter", Name = "示例：问候服务", Version = "1.0.0",
    Description = "演示声明契约、提供服务、发布事件和生命周期作用域。")]
public sealed class GreeterPlugin : PluginBase
{
    private int _greeted;

    protected override Task LoadAsync()
    {
        GreeterApi.Greet.Provide(Context, async (request, cancellationToken) =>
        {
            var name = request.Name is { Length: > 0 } given ? given : "陌生人";
            var count = Interlocked.Increment(ref _greeted);
            await GreeterApi.Greeted.EmitAsync(Context, new Greeted(name, count), cancellationToken);
            return new GreetReply($"你好，{name}！", count);
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
