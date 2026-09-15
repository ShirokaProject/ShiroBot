namespace ShiroBot.SDK.Plugin;

/// <summary>
/// Plugin-scoped access to services exported by other plugins.
/// Service contracts must come from a shared assembly loaded by the host.
/// </summary>
public interface IPluginServices : IServiceProvider
{
    void RegisterSingleton<TService>(TService service)
        where TService : class;

    TService? GetService<TService>()
        where TService : class;

    TService GetRequiredService<TService>()
        where TService : class;
}
