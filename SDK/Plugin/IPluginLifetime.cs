namespace ShiroBot.SDK.Plugin;

/// <summary>
/// 插件的生命周期作用域：在这里登记的后台任务、计时器和资源由宿主在插件卸载时自动停止和释放，
/// 插件不需要在 OnUnloadAsync 里逐个清理。
/// 卸载顺序：<see cref="Stopping"/> 取消 → 等待正在处理的事件 → 等待后台任务和计时器结束 →
/// 插件 OnUnloadAsync → 按登记的相反顺序释放资源和执行清理回调。
/// </summary>
public interface IPluginLifetime
{
    /// <summary>插件开始卸载（或作用域被释放）时取消；可直接传给 HttpClient、Task.Delay 等。</summary>
    CancellationToken Stopping { get; }

    /// <summary>
    /// 登记一个资源，卸载时按登记的相反顺序释放；同时实现 <see cref="IAsyncDisposable"/> 时优先异步释放。
    /// 返回原对象，便于 <c>var http = Context.Lifetime.Track(new HttpClient());</c>。
    /// </summary>
    T Track<T>(T resource) where T : class;

    /// <summary>登记一个清理回调，卸载时执行；返回的对象 Dispose 后取消登记（不执行回调）。</summary>
    IDisposable Defer(Func<ValueTask> cleanup);

    /// <summary>
    /// 启动后台任务；卸载时取消传入的 token 并等待任务结束，任务不再需要单独保存和等待。
    /// 未处理的异常记录到插件日志，取消引起的异常忽略。
    /// </summary>
    Task Run(Func<CancellationToken, Task> work);

    /// <summary>每隔 <paramref name="period"/> 执行一次，上一次执行结束后才开始计时；返回的对象 Dispose 后停止。</summary>
    IDisposable SetInterval(TimeSpan period, Func<CancellationToken, Task> tick);

    /// <summary>延迟 <paramref name="delay"/> 后执行一次；返回的对象 Dispose 后取消。</summary>
    IDisposable SetTimeout(TimeSpan delay, Func<CancellationToken, Task> callback);

    /// <summary>
    /// 创建子作用域：随所在作用域一起停止，也可以单独释放，适合随配置开关重建的功能。
    /// 开始停止后不再接受新子作用域，调用会抛出 ObjectDisposedException。
    /// </summary>
    IPluginScope CreateScope();
}

/// <summary>可单独释放的子作用域；释放时按与插件卸载相同的顺序停止任务并释放资源。</summary>
public interface IPluginScope : IPluginLifetime, IAsyncDisposable;
