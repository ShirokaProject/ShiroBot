namespace ShiroBot.SDK.Config;

public interface IConfigContext
{
    string ConfigPath { get; }
    T Load<T>() where T : class, new();
    void Save<T>(T config) where T : class;

    /// <summary>
    /// Updates one TOML value while preserving unrelated comments and formatting when possible.
    /// Use dotted paths for nested tables, for example: api.auth.key.
    /// </summary>
    void SetValue(string keyPath, object? value);

    /// <summary>
    /// 监听配置文件变化并在变化时回调最新配置。
    /// 返回的 <see cref="IDisposable"/> 必须在卸载时释放，否则会泄漏 FileSystemWatcher。
    /// </summary>
    /// <param name="onChanged">配置变更后的回调，回调内的异常会被宿主吞掉并打印到日志。</param>
    /// <param name="debounceMs">连续触发的去抖时长（毫秒）。最小生效值为 50ms。</param>
    IDisposable Watch<T>(Action<T> onChanged, int debounceMs = 500) where T : class, new();
}
