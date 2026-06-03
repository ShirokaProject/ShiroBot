using System.Reflection;
using System.Runtime.Loader;

namespace ShiroBot.Core;

/// <summary>
/// 通用 DLL 加载器。原来只支持可回收 ALC 加载首个 T 实例；现在扩展为：
/// 1. 可选 collectible / non-collectible；
/// 2. 可注入 <see cref="SharedAssemblyResolver"/>，让 feature plugin 在解析共享前缀时
///    回退到 library plugin 的 ALC，从而保证类型一致；
/// 3. 暴露 <see cref="Alc"/> 供宿主把 library plugin 的 ALC 注册成共享。
/// </summary>
public class DllLoader<T>
    where T : class
{
    private readonly bool _collectible;
    private readonly SharedAssemblyResolver? _shared;
    private PluginAssemblyLoadContext? _alc;
    private WeakReference? _alcWeakReference;

    public DllLoader()
        : this(collectible: true, shared: null)
    {
    }

    public DllLoader(bool collectible, SharedAssemblyResolver? shared)
    {
        _collectible = collectible;
        _shared = shared;
    }

    /// <summary>
    /// 当前 ALC 实例。<see cref="Load"/> 之前为 null。
    /// </summary>
    public AssemblyLoadContext? Alc => _alc;

    public T Load(string dllPath)
    {
        _alc = new PluginAssemblyLoadContext(dllPath, _collectible, _shared);
        _alcWeakReference = new WeakReference(_alc);

        var assembly = _alc.LoadFromAssemblyPath(dllPath);

        var candidateTypes = assembly.GetTypes()
            .Where(t =>
                typeof(T).IsAssignableFrom(t) &&
                t is { IsAbstract: false, IsInterface: false })
            .ToList();

        if (candidateTypes.Count == 0)
            throw new Exception($"No {typeof(T).Name} found in DLL");

        var primaryType = candidateTypes
            .FirstOrDefault() ?? candidateTypes.First();

        return Activator.CreateInstance(primaryType) as T
               ?? throw new InvalidOperationException("Failed to create dllInstance");
    }

    public WeakReference? BeginUnload()
    {
        if (_alc is { IsCollectible: true })
        {
            _alc.Unload();
        }
        _alc = null;

        return _alcWeakReference;
    }

    public static bool WaitForUnload(WeakReference? alcWeakReference, int maxAttempts = 50, int delayMs = 100)
    {
        if (alcWeakReference is null)
        {
            return true;
        }

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            if (!alcWeakReference.IsAlive)
            {
                return true;
            }

            Thread.Sleep(delayMs);
        }

        return !alcWeakReference.IsAlive;
    }


    //Dispose
    public void Unload()
    {
        if (_alc is { IsCollectible: true })
        {
            _alc.Unload();
        }
        _alc = null;
    }
}

/// <summary>
/// 插件 ALC：
/// 1. 解析 Avalonia 这类共享程序集时优先走 library plugin 的 ALC，保证类型同一性；
/// 2. 否则使用 <see cref="AssemblyDependencyResolver"/> 按 plugin 自己的 deps.json 解析（让 plugin 能加载到自己的依赖，例如 Avalonia 全家桶）；
/// 3. 仍然找不到时返回 null，fallback 到 Default ALC（共用 SDK / Model 等）。
/// </summary>
internal sealed class PluginAssemblyLoadContext : AssemblyLoadContext
{
    private readonly SharedAssemblyResolver? _shared;
    private readonly AssemblyDependencyResolver _depsResolver;

    public PluginAssemblyLoadContext(string dllPath, bool isCollectible, SharedAssemblyResolver? shared)
        : base(dllPath, isCollectible)
    {
        _shared = shared;
        _depsResolver = new AssemblyDependencyResolver(dllPath);
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        if (_shared?.TryResolve(assemblyName) is { } sharedAsm)
        {
            return sharedAsm;
        }

        var resolvedPath = _depsResolver.ResolveAssemblyToPath(assemblyName);
        if (!string.IsNullOrEmpty(resolvedPath))
        {
            return LoadFromAssemblyPath(resolvedPath);
        }

        return null;
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        var resolvedPath = _depsResolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        if (!string.IsNullOrEmpty(resolvedPath))
        {
            return LoadUnmanagedDllFromPath(resolvedPath);
        }

        return IntPtr.Zero;
    }
}
