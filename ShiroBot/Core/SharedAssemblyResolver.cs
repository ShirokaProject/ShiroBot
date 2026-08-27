using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.Loader;

namespace ShiroBot.Core;

/// <summary>
/// 共享程序集解析表。宿主程序集前缀和动态插件契约都在这里注册；
/// 插件 ALC 在解析时优先查询该表，确保所有插件看到同一个共享 Type。
/// </summary>
public sealed class SharedAssemblyResolver
{
    private readonly Lock _lock = new();
    private readonly List<Entry> _entries = new();
    private readonly Dictionary<string, Assembly> _assemblies = new(StringComparer.OrdinalIgnoreCase);

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
            if (_assemblies.TryGetValue(name.Name, out var exactAssembly))
            {
                EnsureCompatible(name, exactAssembly.GetName(), GetAssemblyOrigin(exactAssembly));
                return exactAssembly;
            }

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
                if (TryGetLoadedAssembly(entry.Alc, name) is { } loadedAssembly)
                {
                    return loadedAssembly;
                }

                return entry.Alc.LoadFromAssemblyName(name);
            }
            catch (FileNotFoundException)
            {
                if (TryLoadFromDefaultBaseDirectory(entry.Alc, name) is { } assembly)
                {
                    return assembly;
                }

                // 该 ALC 没有这个程序集，继续下一个候选。
            }
        }

        return null;
    }

    public Assembly? TryGetRegisteredAssembly(string assemblyName)
    {
        if (string.IsNullOrWhiteSpace(assemblyName))
        {
            return null;
        }

        var simpleName = assemblyName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
            ? assemblyName[..^4]
            : assemblyName;
        lock (_lock)
        {
            if (_assemblies.TryGetValue(simpleName, out var registered))
            {
                return registered;
            }
        }

        return AssemblyLoadContext.Default.Assemblies.FirstOrDefault(assembly =>
            string.Equals(assembly.GetName().Name, simpleName, StringComparison.OrdinalIgnoreCase));
    }

    public Assembly RegisterDefaultAssembly(string assemblyPath)
    {
        var fullPath = Path.GetFullPath(assemblyPath);
        var requestedName = AssemblyName.GetAssemblyName(fullPath);
        if (string.IsNullOrWhiteSpace(requestedName.Name))
        {
            throw new InvalidOperationException($"Assembly has no simple name: {fullPath}");
        }

        lock (_lock)
        {
            if (_assemblies.TryGetValue(requestedName.Name, out var registered))
            {
                EnsureCompatible(requestedName, registered.GetName(), fullPath);
                EnsureSameModuleOrRestart(registered, fullPath);
                return registered;
            }

            var loaded = AssemblyLoadContext.Default.Assemblies.FirstOrDefault(assembly =>
                string.Equals(assembly.GetName().Name, requestedName.Name, StringComparison.OrdinalIgnoreCase));
            if (loaded is not null)
            {
                EnsureCompatible(requestedName, loaded.GetName(), fullPath);
                EnsureSameModuleOrRestart(loaded, fullPath);
                _assemblies.Add(requestedName.Name, loaded);
                return loaded;
            }

            var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(fullPath);
            _assemblies.Add(requestedName.Name, assembly);
            return assembly;
        }
    }

    public void RegisterAssembly(Assembly assembly)
    {
        var name = assembly.GetName();
        if (string.IsNullOrWhiteSpace(name.Name))
        {
            throw new InvalidOperationException("Shared assembly has no simple name.");
        }

        lock (_lock)
        {
            if (_assemblies.TryGetValue(name.Name, out var registered))
            {
                EnsureCompatible(name, registered.GetName(), GetAssemblyOrigin(assembly));
                return;
            }

            _assemblies.Add(name.Name, assembly);
        }
    }

    public void UnregisterAssembly(Assembly assembly)
    {
        var name = assembly.GetName().Name;
        if (string.IsNullOrWhiteSpace(name)) return;

        lock (_lock)
        {
            if (_assemblies.TryGetValue(name, out var registered) && ReferenceEquals(registered, assembly))
            {
                _assemblies.Remove(name);
            }
        }
    }

    private static void EnsureCompatible(AssemblyName requested, AssemblyName loaded, string requestedPath)
    {
        if (AssemblyName.ReferenceMatchesDefinition(requested, loaded) && requested.Version == loaded.Version)
        {
            return;
        }

        throw new InvalidOperationException(
            $"Shared assembly conflict for {requested.Name}: {loaded.FullName} is already loaded, " +
            $"but {requested.FullName} was requested from {requestedPath}.");
    }

    private static void EnsureSameModuleOrRestart(Assembly loaded, string requestedPath)
    {
        var requestedModuleVersionId = ReadModuleVersionId(requestedPath);
        if (requestedModuleVersionId == loaded.ManifestModule.ModuleVersionId)
        {
            return;
        }

        throw new SharedAssemblyRestartRequiredException(
            $"Shared assembly {loaded.GetName().Name} changed on disk, but its previous version is " +
            "already loaded into the non-collectible Default ALC. Restart ShiroBot to apply the update.");
    }

    private static Guid ReadModuleVersionId(string assemblyPath)
    {
        using var stream = File.OpenRead(assemblyPath);
        using var peReader = new PEReader(stream);
        if (!peReader.HasMetadata)
        {
            throw new BadImageFormatException("Shared assembly has no managed metadata.", assemblyPath);
        }

        var metadata = peReader.GetMetadataReader();
        return metadata.GetGuid(metadata.GetModuleDefinition().Mvid);
    }

    private static string GetAssemblyOrigin(Assembly assembly)
    {
        // The source path is only diagnostic text and is unavailable in single-file deployments.
        return assembly.GetName().Name + ".dll";
    }

    private static Assembly? TryGetLoadedAssembly(AssemblyLoadContext alc, AssemblyName name)
    {
        if (string.IsNullOrWhiteSpace(name.Name))
        {
            return null;
        }

        return alc.Assemblies.FirstOrDefault(assembly =>
            string.Equals(assembly.GetName().Name, name.Name, StringComparison.OrdinalIgnoreCase));
    }

    private static Assembly? TryLoadFromDefaultBaseDirectory(AssemblyLoadContext alc, AssemblyName name)
    {
        if (alc != AssemblyLoadContext.Default || string.IsNullOrWhiteSpace(name.Name))
        {
            return null;
        }

        var assemblyPath = Path.Combine(AppContext.BaseDirectory, name.Name + ".dll");
        if (!File.Exists(assemblyPath))
        {
            return null;
        }

        try
        {
            return alc.LoadFromAssemblyPath(assemblyPath);
        }
        catch (FileLoadException)
        {
            return alc.Assemblies.FirstOrDefault(assembly =>
                string.Equals(assembly.GetName().Name, name.Name, StringComparison.OrdinalIgnoreCase));
        }
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

internal sealed class SharedAssemblyRestartRequiredException(string message) : InvalidOperationException(message);
