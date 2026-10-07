using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.Loader;

namespace ShiroBot.Plugins.Loading;

/// <summary>读取入口和可解析私有依赖的引用元数据，不执行组件代码。</summary>
internal static class ContractReferencePreflight
{
    internal static void Validate(string entryPath, SharedAssemblyResolver shared)
    {
        var resolver = new AssemblyDependencyResolver(entryPath);
        var pending = new Queue<string>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        pending.Enqueue(Path.GetFullPath(entryPath));
        while (pending.TryDequeue(out var path))
        {
            if (!visited.Add(path)) continue;
            using var stream = File.OpenRead(path);
            using var pe = new PEReader(stream);
            if (!pe.HasMetadata) continue;
            var metadata = pe.GetMetadataReader();
            foreach (var handle in metadata.AssemblyReferences)
            {
                var reference = metadata.GetAssemblyReference(handle);
                var name = new AssemblyName
                {
                    Name = metadata.GetString(reference.Name), Version = reference.Version,
                    CultureName = reference.Culture.IsNil ? null : metadata.GetString(reference.Culture)
                };
                var key = metadata.GetBlobBytes(reference.PublicKeyOrToken);
                if ((reference.Flags & AssemblyFlags.PublicKey) != 0) name.SetPublicKey(key);
                else name.SetPublicKeyToken(key);
                if (name.Name == "ShiroBot.SDK" || name.Name.StartsWith("ShiroBot.Model.", StringComparison.Ordinal))
                {
                    try
                    {
                        var contract = shared.TryGetRegisteredAssembly(name.Name) ?? shared.TryResolve(name);
                        if (contract is null)
                            throw new InvalidOperationException($"Required contract is not registered: {name.FullName}");
                        SharedAssemblyResolver.EnsureCompatible(name, contract.GetName(), path);
                    }
                    catch (Exception ex) when (ex is InvalidOperationException or FileLoadException)
                    {
                        throw new InvalidOperationException($"Component contract preflight failed before activation: {path} references {name.FullName}. {ex.Message}", ex);
                    }
                    continue;
                }
                // Shared framework/library references are not component-private dependencies.
                var dependency = resolver.ResolveAssemblyToPath(name);
                if (dependency is null)
                {
                    var candidate = Path.Combine(Path.GetDirectoryName(path)!, name.Name + ".dll");
                    if (File.Exists(candidate)) dependency = candidate;
                }
                if (dependency is not null) pending.Enqueue(Path.GetFullPath(dependency));
            }
        }
    }
}
