using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Text.Json;
using ShiroBot.Plugins;

internal static class ConfigFingerprintVerification
{
    public static void Run()
    {
        var weak = Exercise();
        for (int i = 0; weak.IsAlive && i < 20; i++)
        {
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            Thread.Sleep(20);
        }
        if (weak.IsAlive) throw new Exception("Configuration fingerprint retained collectible assembly.");
        Console.WriteLine("PASS: CollectibleConfigurationFingerprintPreservesValuesAndReleasesAssembly");
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference Exercise()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "ShiroBot.ExternalConfigModelProbe.dll");
        var context = new AssemblyLoadContext("fingerprint-probe", true);
        context.Resolving += (_, name) => AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == name.Name && !a.IsCollectible);
        var type = context.LoadFromAssemblyPath(path).GetType("ShiroBot.ExternalConfigModelProbe.Settings")!;
        var config = Activator.CreateInstance(type)!;
        type.GetProperty("Cookie")!.SetValue(config, "fixture");
        for (int i = 0; i < 12; i++)
        {
            using var doc = JsonDocument.Parse(JsonSerializer.Serialize(ConfigFingerprintValue.Create(config)));
            if (doc.RootElement.GetProperty("Cookie").GetString() != "fixture" || doc.RootElement.GetProperty("Retries").GetInt32() != 5)
                throw new Exception("Fingerprint did not preserve configuration values.");
        }
        context.Unload();
        return new WeakReference(context);
    }
}
