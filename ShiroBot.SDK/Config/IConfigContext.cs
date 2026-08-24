namespace ShiroBot.SDK.Config;

public interface IConfigContext
{
    string ConfigPath { get; }
    T Load<T>() where T : class, new();
    /// <summary>
    /// Saves all known values while preserving unknown TOML keys, sections, and comments so a
    /// temporary plugin downgrade does not erase configuration introduced by a newer version.
    /// </summary>
    void Save<T>(T config) where T : class;

    /// <summary>
    /// Updates one TOML value while preserving unrelated comments and formatting when possible.
    /// Use dotted paths for nested tables, for example: api.auth.key.
    /// </summary>
    void SetValue(string keyPath, object? value);

    /// <summary>
    /// 监听配置文件变化并在变化时回调最新配置。
    /// 调用方可显式释放返回的 <see cref="IDisposable"/> 以提前停止监听；插件作用域内
    /// 创建的监听也会在插件卸载时由宿主统一释放。
    /// </summary>
    /// <param name="onChanged">配置变更后的回调，回调内的异常会被宿主吞掉并打印到日志。</param>
    /// <param name="debounceMs">连续触发的去抖时长（毫秒）。最小生效值为 50ms。</param>
    IDisposable Watch<T>(Action<T> onChanged, int debounceMs = 500) where T : class, new();
}
