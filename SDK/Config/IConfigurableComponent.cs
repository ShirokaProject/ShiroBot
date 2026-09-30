namespace ShiroBot.SDK.Config;

public enum ConfigApplyMode
{
    Live = 0,
    RestartComponent = 1
}

/// <summary>Host-managed lifecycle for strongly typed plugin and adapter configuration.</summary>
public interface IConfigurableComponent
{
    Type ConfigType { get; }
    object? CurrentConfig { get; }
    Task<object> InitializeConfigAsync(IConfigContext context, CancellationToken cancellationToken = default);
    Task ApplyConfigAsync(object candidate, CancellationToken cancellationToken = default);
}

/// <summary>Adapter-specific configuration lifecycle that may require a transport restart.</summary>
public interface IConfigurableAdapter : IConfigurableComponent
{
    ConfigApplyMode ApplyMode { get; }
}

/// <summary>Typed declaration used by new plugins and adapters.</summary>
public interface IConfigurableComponent<TConfig> : IConfigurableComponent
    where TConfig : class, new()
{
    TConfig CurrentConfigValue { get; }

    Type IConfigurableComponent.ConfigType => typeof(TConfig);

    object? IConfigurableComponent.CurrentConfig => CurrentConfigValue;

    Task OnConfigLoadedAsync(TConfig config, CancellationToken cancellationToken) => Task.CompletedTask;

    Task OnConfigChangedAsync(TConfig previous, TConfig current, CancellationToken cancellationToken);

    async Task<object> IConfigurableComponent.InitializeConfigAsync(IConfigContext context, CancellationToken cancellationToken)
    {
        var config = context.Load<TConfig>();
        await OnConfigLoadedAsync(config, cancellationToken).ConfigureAwait(false);
        return config;
    }

    Task IConfigurableComponent.ApplyConfigAsync(object candidate, CancellationToken cancellationToken) =>
        OnConfigChangedAsync(CurrentConfigValue, (TConfig)candidate, cancellationToken);
}
