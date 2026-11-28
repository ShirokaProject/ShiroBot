using System.Reflection;
using System.Text.Json;

namespace ShiroBot.Plugins.Loading;

/// <summary>
///     System.Text.Json 的反射序列化会把访问过的类型记在进程级缓存里（成员访问器委托、各 options 的类型元数据）。
///     访问器缓存只在下一次有新类型被序列化时才淘汰旧条目，所以组件用反射序列化过自己的类型后，
///     只要宿主之后没有再触发淘汰，这些条目就一直拉着它的可回收 ALC，卸载永远等不到。
///     这里调用运行时给热重载准备的同一个清理入口；缓存会在下次序列化时按需重建。
/// </summary>
internal static class SerializerCacheReset
{
    private static readonly MethodInfo? ClearCache = typeof(JsonSerializer).Assembly
        .GetType("System.Text.Json.JsonSerializerOptionsUpdateHandler")
        ?.GetMethod("ClearCache", BindingFlags.Public | BindingFlags.Static, [typeof(Type[])]);

    /// <summary>运行时没有这个入口时返回 false，卸载按原来的方式继续等待。</summary>
    public static bool Clear()
    {
        if (ClearCache is null) return false;
        try
        {
            ClearCache.Invoke(null, [null]);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
