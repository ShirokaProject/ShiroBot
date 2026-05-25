using System.Text.Json;
using ShiroBot.SDK.Abstractions;
using Tomlyn;

namespace ShiroBot.Core;

//总配置类
public class CoreConfig
{
    public string Protocol { get; set; } = string.Empty;

    public bool EnableLog { get; set; } = true;

    public bool DisableConsoleInput { get; set; } = false;

    public long[] OwnerList { get; set; } = [];

    public long[] AdminList { get; set; } = [];

    public PluginRouteConfig PluginRoutes { get; set; } = new()
    {
        Default = new PluginRouteRuleConfig
        {
            Mode = "blacklist",
            Groups = []
        }
    };
}

public class PluginRouteConfig
{
    public PluginRouteRuleConfig Default { get; set; } = new();

    public Dictionary<string, PluginRouteRuleConfig> Plugins { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public bool AllowsGroup(string pluginName, long groupId)
    {
        var plugins = Plugins;
        if (plugins is null || !plugins.TryGetValue(pluginName, out var rule))
        {
            return Default.IsMatch(groupId);
        }

        return rule.IsMatch(groupId);
    }

    /// <summary>
    /// 把 <paramref name="other"/> 的内容原子地拷贝进当前实例，使得已经引用本实例的代码无需替换引用即可看到新规则。
    /// </summary>
    public void CopyFrom(PluginRouteConfig other)
    {
        ArgumentNullException.ThrowIfNull(other);
        Default = other.Default ?? new PluginRouteRuleConfig();
        Plugins = new Dictionary<string, PluginRouteRuleConfig>(
            other.Plugins ?? new Dictionary<string, PluginRouteRuleConfig>(),
            StringComparer.OrdinalIgnoreCase);
    }
}

public class PluginRouteRuleConfig
{
    public string Mode { get; init; } = "whitelist";

    public long[] Groups { get; init; } = [];

    public bool IsMatch(long groupId)
    {
        var contains = Groups.Contains(groupId);
        return NormalizeMode(Mode) switch
        {
            "blacklist" => !contains,
            _ => contains
        };
    }

    private static string NormalizeMode(string? mode)
    {
        return string.IsNullOrEmpty(mode) ? "whitelist" : mode.ToLowerInvariant();
    }
}

public class ConfigManager(string? coreConfigPath = null)
{
    private readonly string _coreConfigPath = string.IsNullOrWhiteSpace(coreConfigPath)
        ? Path.Combine(AppContext.BaseDirectory, "config.toml")
        : Path.GetFullPath(coreConfigPath);

    private readonly TomlSerializerOptions _options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
        IndentSize = 4,
        MaxDepth = 64,
        DefaultIgnoreCondition = TomlIgnoreCondition.WhenWritingNull,
    };
    
    public async Task<CoreConfig> LoadCoreConfig()
    {
        try
        {
            if (!File.Exists(_coreConfigPath)) return await CreateDefaultConfig();
            var tomlString = await File.ReadAllTextAsync(_coreConfigPath);
            if (string.IsNullOrWhiteSpace(tomlString)) return await CreateDefaultConfig();
            var config = TomlSerializer.Deserialize<CoreConfig>(tomlString, _options);
            return config ?? await CreateDefaultConfig();
        }
        catch (Exception ex)
        {
            throw new Exception($"加载配置时出错:{ex.Message}");
        }
        
        //创建默认配置保存的方法
        async Task<CoreConfig> CreateDefaultConfig()
        {
            BotLog.Info("未找到配置文件，正在创建默认配置...");
            var defaultConfig = new CoreConfig();
            var tomlString = FormatToml(TomlSerializer.Serialize(defaultConfig, _options));
            Directory.CreateDirectory(Path.GetDirectoryName(_coreConfigPath)!);
            await File.WriteAllTextAsync(_coreConfigPath, tomlString);
            return defaultConfig;
        }
    }

    public T? LoadPluginConfig<T>(string pluginDirectory) where T : class, new()
    {
        return LoadScopedConfig<T>(pluginDirectory, "插件目录");
    }

    public T? LoadAdapterConfig<T>(string adapterDirectory) where T : class, new()
    {
        return LoadScopedConfig<T>(adapterDirectory, "适配器目录");
    }

    public T? LoadConfig<T>(string configPath, string scopeName) where T : class, new()
    {
        try
        {
            var normalizedConfigPath = Path.GetFullPath(configPath);
            var directory = Path.GetDirectoryName(normalizedConfigPath)
                            ?? throw new InvalidOperationException($"无法解析配置文件目录: {normalizedConfigPath}");

            Directory.CreateDirectory(directory);
            if (!File.Exists(normalizedConfigPath))
            {
                var newConfig = Activator.CreateInstance<T>();
                ConsoleHelper.Warning($"未找到{scopeName}配置文件 {normalizedConfigPath}，已生成默认配置，请前往配置。");
                SaveToml(normalizedConfigPath, newConfig, _options);
                return newConfig;
            }

            var toml = File.ReadAllText(normalizedConfigPath);
            var config = TomlSerializer.Deserialize<T>(toml, _options);
            return config;
        }
        catch (Exception ex)
        {
            throw new Exception($"加载{scopeName}配置时出错: {configPath} - {ex.Message}", ex);
        }
    }

    public void SavePluginConfig<T>(string pluginDirectory, T config) where T : class
    {
        SaveScopedConfig(pluginDirectory, config);
    }

    public void SaveAdapterConfig<T>(string adapterDirectory, T config) where T : class
    {
        SaveScopedConfig(adapterDirectory, config);
    }

    public T? LoadScopedConfig<T>(string directory, string scopeName) where T : class, new()
    {
        return LoadConfig<T>(Path.Combine(directory, "config.toml"), scopeName);
    }

    public void SaveScopedConfig<T>(string directory, T config) where T : class
    {
        SaveConfig(Path.Combine(directory, "config.toml"), config);
    }

    public void SaveConfig<T>(string configPath, T config) where T : class
    {
        SaveToml(configPath, config, _options);
    }

    private static void SaveToml<T>(string configPath, T config, TomlSerializerOptions options) where T : class
    {
        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
        var tomlString = FormatToml(TomlSerializer.Serialize(config, options));
        File.WriteAllText(configPath, tomlString);
    }

    private static string FormatToml(string toml)
    {
        var lines = toml.Replace("\r\n", "\n").Split('\n');
        var builder = new System.Text.StringBuilder();
        
        foreach (var line in lines)
        {
            var trimmed = line.TrimStart();
            var isTableHeader = trimmed.StartsWith('[') && trimmed.EndsWith(']');

            if (isTableHeader && builder.Length > 0)
            {
                var current = builder.ToString();
                if (!current.EndsWith("\n\n", StringComparison.Ordinal))
                {
                    builder.AppendLine();
                }
            }

            builder.AppendLine(line);
        }

        return builder.ToString();
    }
}
