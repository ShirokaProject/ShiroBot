using ShiroBot.SDK.Plugin;

namespace ShiroBot.SDK.Core;

/// <summary>
/// 适配器可选实现：宿主在每次 <see cref="IBotAdapter.StartAsync"/> 之前注入一个新的生命周期作用域，
/// 在启动期间登记的接收循环、心跳计时器、连接等由宿主在停止时统一收尾，包括配置触发的重启。
/// 停止顺序：<see cref="IPluginLifetime.Stopping"/> 取消 → 适配器的 StopAsync → 等待后台任务和计时器结束 →
/// 按登记的相反顺序释放资源。未实现此接口的适配器不受影响。
/// </summary>
public interface ILifetimeAwareAdapter
{
    /// <summary>当前这次启动的作用域，由宿主在 StartAsync 之前设置。</summary>
    IPluginLifetime Lifetime { get; set; }
}
