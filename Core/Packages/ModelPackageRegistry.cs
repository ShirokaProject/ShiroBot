using System.Reflection;
using ShiroBot.SDK.Core;
using ShiroBot.Plugins.Loading;

namespace ShiroBot.Packages;

/// <summary>注册宿主内置平台契约，并在加载 Adapter/Plugin 前验证其依赖。</summary>
internal sealed class ModelPackageRegistry(SharedAssemblyResolver sharedAssemblies)
{
    private readonly Dictionary<string, Package> _packages = new(StringComparer.OrdinalIgnoreCase);

    public void RegisterBuiltIn(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        var metadata = assembly.GetCustomAttribute<ShiroBotPackageAttribute>();
        if (metadata is null || metadata.Kind != ShiroBotPackageKind.Model)
        {
            throw new InvalidOperationException(
                $"Built-in Model assembly must declare {nameof(ShiroBotPackageAttribute)} with kind Model: " +
                assembly.GetName().Name);
        }

        if (_packages.ContainsKey(metadata.Id))
        {
            throw new InvalidOperationException($"Duplicate built-in Model package: {metadata.Id}");
        }

        sharedAssemblies.RegisterAssembly(assembly);
        _packages.Add(metadata.Id, new Package(metadata, assembly));
    }

    public IReadOnlyList<ModelPackageInfo> GetPackages() =>
        _packages.Values
            .Select(package => new ModelPackageInfo(
                package.Metadata.Id,
                package.Metadata.Version,
                package.Assembly.GetName().Name ?? string.Empty,
                null,
                "built_in",
                false))
            .OrderBy(package => package.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public void ValidateDependencies(string assemblyPath)
    {
        foreach (var requirement in PackageDependencyProbe.ReadRequirements(assemblyPath))
        {
            if (!_packages.TryGetValue(requirement.PackageId, out var package))
            {
                throw new InvalidOperationException(
                    $"无法加载 {Path.GetFileName(assemblyPath)}：宿主未内置 Model 包 {requirement.PackageId}。" +
                    "第三方扩展请实现为标准 Plugin，不支持运行时加载额外 Model 包。");
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

    internal sealed record ModelPackageInfo(
        string Id,
        string Version,
        string AssemblyName,
        string? AssemblyPath,
        string Source,
        bool Reloadable);

    private sealed record Package(ShiroBotPackageAttribute Metadata, Assembly Assembly);
}
