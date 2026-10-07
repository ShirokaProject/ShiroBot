using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Text.Json;
using ShiroBot.Plugins.Loading;
using ShiroBot.Plugins.Compatibility;
using ShiroBot.Packages;
using ShiroBot.SDK.Plugin;
using ShiroBot.SDK.Core;
using ShiroBot.Model.QQ;
using ShiroBot.Model.Discord;
using ShiroBot.Model.Telegram;

internal static class PluginMigrationVerification
{
    internal sealed record Entry(string Id, string AssemblyPath);
    internal static void Run(string manifestPath)
    {
        var entries = JsonSerializer.Deserialize<Entry[]>(File.ReadAllText(manifestPath))
            ?? throw new InvalidOperationException("Missing plugin migration manifest.");
        if (entries.Length == 0) throw new InvalidOperationException("No plugins to verify.");
        foreach (var entry in entries)
        {
            var report = Load(entry);
            Console.WriteLine($"PASS {entry.Id}: host contract preflight, plugin activation, {report.TypeCount} type signatures; SDK ABI {report.SdkAbi}, QQ ABI {report.QqAbi?.ToString() ?? "unused"}");
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (int TypeCount, Version SdkAbi, Version? QqAbi) Load(Entry entry)
    {
        var path = Path.GetFullPath(entry.AssemblyPath);
        var resolver = new SharedAssemblyResolver();
        foreach (var contract in new[] { typeof(IBotPlugin).Assembly, typeof(QGroup).Assembly, typeof(DiscordUser).Assembly, typeof(TelegramUser).Assembly })
            resolver.RegisterAssembly(contract);
        resolver.Register(["Avalonia", "SkiaSharp", "HarfBuzzSharp", "MicroCom"], AssemblyLoadContext.Default);
        var registry = new ModelPackageRegistry(resolver);
        foreach (var model in new[] { typeof(QGroup).Assembly, typeof(DiscordUser).Assembly, typeof(TelegramUser).Assembly }) registry.RegisterBuiltIn(model);
        registry.ValidateDependencies(path);
        var loader = new DllLoader<IBotPlugin>(true, resolver);
        try
        {
            var plugin = loader.Load(path);
            var assembly = plugin.GetType().Assembly;
            var api = assembly.GetCustomAttribute<ShiroBotApiCompatibilityAttribute>();
            ComponentApiCompatibility.EnsureCompatible("Plugin", entry.Id, api?.MinimumVersion ?? "0.8", api?.MaximumVersion ?? "0.8");
            var types = assembly.GetTypes();
            foreach (var type in types)
            {
                foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
                {
                    _ = method.ReturnType;
                    _ = method.GetParameters();
                }
                _ = type.GetInterfaces();
                _ = type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly);
            }
            var references = assembly.GetReferencedAssemblies();
            var sdk = references.Single(x => x.Name == "ShiroBot.SDK").Version!;
            var qq = references.SingleOrDefault(x => x.Name == "ShiroBot.Model.QQ")?.Version;
            if (sdk != new Version(1, 0, 0, 0) || qq is not null && qq != new Version(1, 0, 0, 0))
                throw new InvalidOperationException($"{entry.Id} was not compiled against the new contracts.");
            // Do not invoke OnLoad: platform/API requests and background jobs remain disabled.
            return (types.Length, sdk, qq);
        }
        finally { loader.BeginUnload(); }
    }
}
