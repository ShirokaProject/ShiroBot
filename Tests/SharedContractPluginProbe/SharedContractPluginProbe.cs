using ShiroBot.SDK.Core;
using ShiroBot.SDK.Adapter;
using ShiroBot.SDK.Models;
using ShiroBot.SDK.Plugin;
using ShiroBot.Model.QQ;

[assembly: ShiroBotApiCompatibility("0.8", "0.8")]

namespace ShiroBot.SharedContractPluginProbe;

[BotPlugin(
    "SharedContractPluginProbe",
    Name = "Shared contract plugin probe",
    Version = "0.9.2",
    SharedAssemblies = "ShiroBot.Model.QQ",
    Priority = 5)]
public sealed class SharedContractPluginProbe : PluginBase
{
    public override string Name => "SharedContractPluginProbe";
    public static Type SharedQqContractType => typeof(QGroup);
    public int Loads { get; private set; }
    public int Unloads { get; private set; }
    public int EventsReceived { get; private set; }
    private IBotAdapter? _cachedAdapter;
    public bool HasCachedAdapter => _cachedAdapter is not null;
    public void CacheAdapter() => _cachedAdapter = Context.GetAdapterExtension<IBotAdapter>();
    public void ClearAdapter() => _cachedAdapter = null;
    protected override Task LoadAsync() { Loads++; return Task.CompletedTask; }
    protected override Task OnUnloadAsync() { Unloads++; ClearAdapter(); return Task.CompletedTask; }
    protected override void ConfigureRoutes() => Events.Map<MemberJoinedEvent>(_ =>
    {
        EventsReceived++;
        return Task.CompletedTask;
    });
}
