using System.Reflection;
using System.Runtime.Loader;
using ShiroBot.SDK.Core;

namespace ShiroBot.Core;

/// <summary>从 models 目录加载平台契约，并在加载 Adapter/Plugin 前验证其依赖。</summary>
internal sealed class ModelPackageRegistry(SharedAssemblyResolver sharedAssemblies)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, Package> _packages = new(StringComparer.OrdinalIgnoreCase);
    private string? _modelRoot;

    public void LoadFromDirectory(string modelRoot)
    {
        _modelRoot = Path.GetFullPath(modelRoot);
        if (!Directory.Exists(modelRoot))
        {
            Directory.CreateDirectory(modelRoot);
            return;
        }

        foreach (var dllPath in Directory.EnumerateFiles(modelRoot, "*.dll", SearchOption.AllDirectories)
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            var packageMetadata = PackageDependencyProbe.ReadPackage(dllPath);
            if (packageMetadata is null)
            {
                // Ordinary managed DLLs beneath a Model package directory are private dependencies,
                // not additional package entry points.
                continue;
            }

            if (packageMetadata.Kind != ShiroBotPackageKind.Model)
            {
                throw new InvalidOperationException(
                    $"models 目录中的 ShiroBot 包必须为 Model 类别: {dllPath}");
            }

            var loadContext = new ModelAssemblyLoadContext(dllPath, sharedAssemblies);
            var assembly = loadContext.LoadFromAssemblyPath(Path.GetFullPath(dllPath));
            var metadata = assembly.GetCustomAttribute<ShiroBotPackageAttribute>();
            if (metadata is null || metadata.Kind != ShiroBotPackageKind.Model)
            {
                throw new InvalidOperationException(
                    $"无法加载 Model 包元数据: {dllPath}");
            }

            sharedAssemblies.RegisterAssembly(assembly);
            if (!_packages.TryAdd(metadata.Id, new Package(metadata, assembly, loadContext, Path.GetFullPath(dllPath))))
            {
                sharedAssemblies.UnregisterAssembly(assembly);
                loadContext.Unload();
                throw new InvalidOperationException($"重复安装的 Model 包: {metadata.Id}");
            }
        }
    }

    public IReadOnlyList<ModelPackageInfo> GetPackages() =>
        _packages.Values
            .Select(package => new ModelPackageInfo(
                package.Metadata.Id,
                package.Metadata.Version,
                package.Assembly.GetName().Name ?? string.Empty,
                package.AssemblyPath))
            .OrderBy(package => package.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public async Task ReloadAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var root = _modelRoot ?? throw new InvalidOperationException("Model 目录尚未初始化。");
            var unloadReferences = BeginUnloadAll();
            await Task.Delay(100).ConfigureAwait(false);
            foreach (var reference in unloadReferences)
            {
                if (!DllLoader<object>.WaitForUnload(reference))
                {
                    throw new InvalidOperationException("Model 程序集仍被 Adapter 或 Plugin 引用，无法热重载。");
                }
            }

            LoadFromDirectory(root);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task InstallAsync(string stagedAssemblyPath)
    {
        var metadata = PackageDependencyProbe.ReadPackage(stagedAssemblyPath)
            ?? throw new InvalidOperationException("DLL 未声明 ShiroBotPackageAttribute。");
        if (metadata.Kind != ShiroBotPackageKind.Model)
        {
            throw new InvalidOperationException("上传的 DLL 不是 Model 包。");
        }

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var root = _modelRoot ?? throw new InvalidOperationException("Model 目录尚未初始化。");
            var assemblyName = AssemblyName.GetAssemblyName(stagedAssemblyPath).Name
                ?? throw new InvalidOperationException("无法读取 Model 程序集名称。");
            var targetPath = GetInstalledAssemblyPath(metadata.Id) ?? Path.Combine(root, assemblyName + ".dll");
            var backupPath = targetPath + ".bak";
            var unloadReferences = BeginUnloadAll();
            await Task.Delay(100).ConfigureAwait(false);
            foreach (var reference in unloadReferences)
            {
                if (!DllLoader<object>.WaitForUnload(reference))
                {
                    LoadFromDirectory(root);
                    throw new InvalidOperationException("Model 程序集仍被引用，无法安装更新。");
                }
            }

            if (File.Exists(backupPath)) File.Delete(backupPath);
            if (File.Exists(targetPath)) File.Move(targetPath, backupPath);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
                File.Copy(stagedAssemblyPath, targetPath, overwrite: true);
                LoadFromDirectory(root);
                if (File.Exists(backupPath)) File.Delete(backupPath);
            }
            catch
            {
                if (File.Exists(targetPath)) File.Delete(targetPath);
                if (File.Exists(backupPath)) File.Move(backupPath, targetPath);
                LoadFromDirectory(root);
                throw;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private IReadOnlyList<WeakReference> BeginUnloadAll()
    {
        var unloadReferences = new List<WeakReference>();
        foreach (var package in _packages.Values)
        {
            sharedAssemblies.UnregisterAssembly(package.Assembly);
            package.LoadContext.Unload();
            unloadReferences.Add(new WeakReference(package.LoadContext));
        }

        _packages.Clear();
        return unloadReferences;
    }

    private string? GetInstalledAssemblyPath(string packageId) =>
        _packages.Values
            .Where(package => string.Equals(package.Metadata.Id, packageId, StringComparison.OrdinalIgnoreCase))
            .Select(package => package.AssemblyPath)
            .FirstOrDefault();

    public void ValidateDependencies(string assemblyPath)
    {
        foreach (var requirement in PackageDependencyProbe.ReadRequirements(assemblyPath))
        {
            if (!_packages.TryGetValue(requirement.PackageId, out var package))
            {
                throw new InvalidOperationException(
                    $"无法加载 {Path.GetFileName(assemblyPath)}：缺少 Model 包 {requirement.PackageId}。请将其安装到 models 目录。");
            }

            if (string.IsNullOrWhiteSpace(requirement.MinimumVersion))
            {
                continue;
            }

            if (!Version.TryParse(requirement.MinimumVersion, out var minimumVersion))
            {
                throw new InvalidOperationException(
                    $"无法加载 {Path.GetFileName(assemblyPath)}：Model 依赖 {requirement.PackageId} " +
                    $"声明了无效最低版本 '{requirement.MinimumVersion}'。");
            }

            if (!Version.TryParse(package.Metadata.Version, out var installedVersion))
            {
                throw new InvalidOperationException(
                    $"无法加载 {Path.GetFileName(assemblyPath)}：已安装 Model 包 {requirement.PackageId} " +
                    $"声明了无效版本 '{package.Metadata.Version}'。");
            }

            if (installedVersion < minimumVersion)
            {
                throw new InvalidOperationException(
                    $"无法加载 {Path.GetFileName(assemblyPath)}：需要 {requirement.PackageId} >= {minimumVersion}，" +
                    $"当前安装版本为 {installedVersion}。");
            }
        }
    }

    public bool ContainsAssembly(string assemblyName) =>
        _packages.Values.Any(package => string.Equals(
            package.Assembly.GetName().Name, assemblyName, StringComparison.OrdinalIgnoreCase));

    internal sealed record ModelPackageInfo(string Id, string Version, string AssemblyName, string AssemblyPath);

    private sealed record Package(
        ShiroBotPackageAttribute Metadata,
        Assembly Assembly,
        ModelAssemblyLoadContext LoadContext,
        string AssemblyPath);

    private sealed class ModelAssemblyLoadContext(string assemblyPath, SharedAssemblyResolver shared)
        : AssemblyLoadContext($"model:{Path.GetFileNameWithoutExtension(assemblyPath)}", isCollectible: true)
    {
        private readonly AssemblyDependencyResolver _resolver = new(assemblyPath);

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            if (shared.TryResolve(assemblyName) is { } sharedAssembly) return sharedAssembly;
            var path = _resolver.ResolveAssemblyToPath(assemblyName);
            return path is null ? null : LoadFromAssemblyPath(path);
        }
    }
}
