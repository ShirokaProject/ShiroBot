using System.Reflection;
using System.Runtime.Loader;

namespace ShiroBot.Core;

/// <summary>
/// 共享程序集解析表。Library plugin 加载完成后把它的 ALC + 想共享的程序集前缀注册进来；
/// feature plugin 的 ALC 在解析时第一步就查这里，命中就直接复用 library 的程序集，
/// 这样 feature plugin import 的 Avalonia 类型和 library plugin 内部用的是同一份 Type。
/// </summary>
public sealed class SharedAssemblyResolver
{
    private readonly object _lock = new();
    private readonly List<Entry> _entries = new();

    public void Register(string[] prefixes, AssemblyLoadContext alc)
    {
        ArgumentNullException.ThrowIfNull(prefixes);
        ArgumentNullException.ThrowIfNull(alc);

        if (prefixes.Length == 0)
        {
            return;
        }

        lock (_lock)
        {
            _entries.Add(new Entry(prefixes, alc));
        }
    }

    public Assembly? TryResolve(AssemblyName name)
    {
        if (string.IsNullOrEmpty(name.Name))
        {
            return null;
        }

        Entry[] snapshot;
        lock (_lock)
        {
            snapshot = _entries.ToArray();
        }

        foreach (var entry in snapshot)
        {
            if (!Matches(name.Name, entry.Prefixes))
            {
                continue;
            }

            try
            {
                return entry.Alc.LoadFromAssemblyName(name);
            }
            catch (FileNotFoundException)
            {
                // 该 ALC 没有这个程序集，继续下一个候选。
            }
        }

        return null;
    }

    private static bool Matches(string assemblyName, string[] prefixes)
    {
        foreach (var prefix in prefixes)
        {
            if (string.IsNullOrEmpty(prefix))
            {
                continue;
            }

            if (assemblyName.Equals(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // 前缀匹配要求 prefix 是完整的命名空间段，避免 "Avalonia" 误匹配 "AvaloniaXyz" 这种第三方。
            if (assemblyName.StartsWith(prefix + ".", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private sealed record Entry(string[] Prefixes, AssemblyLoadContext Alc);
}
