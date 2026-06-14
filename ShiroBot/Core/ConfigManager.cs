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

    /// <summary>Avalonia 宿主主题：Light / Dark / Auto。插件渲染未显式指定 Theme 时仍默认 Light。</summary>
    public string AvaloniaTheme { get; set; } = "Light";

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

    public ApiHostConfig Api { get; set; } = new();
}

public class ApiHostConfig
{
    public bool Enable { get; set; } = true;

    public string ListenUrl { get; set; } = "http://127.0.0.1:7001";

    public string[] ListenUrls { get; set; } = [];

    public string? PublicBaseUrl { get; set; }

    public ApiAuthConfig Auth { get; set; } = new();
}

public class ApiAuthConfig
{
    public bool Enable { get; set; } = true;

    public string Key { get; set; } = string.Empty;
}

public class PluginRouteConfig
{
    public PluginRouteRuleConfig Default { get; set; } = new();

    public Dictionary<string, PluginRouteRuleConfig> Plugins { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public bool AllowsGroup(string pluginName, long groupId)
    {
        var plugins = Plugins;
        return !plugins.TryGetValue(pluginName, out var rule) ? Default.IsMatch(groupId) : rule.IsMatch(groupId);
    }

    /// <summary>
    /// 把 <paramref name="other"/> 的内容原子地拷贝进当前实例，使得已经引用本实例的代码无需替换引用即可看到新规则。
    /// </summary>
    public void CopyFrom(PluginRouteConfig other)
    {
        ArgumentNullException.ThrowIfNull(other);
        Default = other.Default;
        Plugins = new Dictionary<string, PluginRouteRuleConfig>(
            other.Plugins,
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
            if (TryEnsureTomlDefaults(normalizedConfigPath, toml, Activator.CreateInstance<T>(), _options, out var updatedToml))
            {
                toml = updatedToml;
            }

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

    private static bool TryEnsureTomlDefaults<T>(
        string configPath,
        string currentToml,
        T defaultConfig,
        TomlSerializerOptions options,
        out string updatedToml) where T : class
    {
        updatedToml = currentToml;
        if (string.IsNullOrWhiteSpace(currentToml)) return false;

        var defaultToml = FormatToml(TomlSerializer.Serialize(defaultConfig, options));
        var currentSections = ParseTomlSections(currentToml);
        var defaultSections = ParseTomlSections(defaultToml);
        var lines = currentToml.Replace("\r\n", "\n").Split('\n').ToList();
        var changed = false;

        foreach (var (sectionName, defaultSection) in defaultSections)
        {
            if (!currentSections.TryGetValue(sectionName, out var currentSection))
            {
                AppendMissingSection(lines, sectionName, defaultSection.Lines);
                changed = true;
                continue;
            }

            var missingLines = defaultSection.Lines
                .Where(line => TryGetTomlKey(line, out var key) && !currentSection.Keys.Contains(key))
                .ToList();
            if (missingLines.Count == 0) continue;

            var insertAt = sectionName.Length == 0
                ? FindFirstSectionIndex(lines)
                : FindSectionInsertIndex(lines, currentSection.HeaderLineIndex);
            lines.InsertRange(insertAt, missingLines);
            changed = true;
        }

        if (!changed) return false;

        updatedToml = string.Join(Environment.NewLine, lines).TrimEnd() + Environment.NewLine;
        File.WriteAllText(configPath, updatedToml);
        return true;
    }

    private static Dictionary<string, TomlSectionInfo> ParseTomlSections(string toml)
    {
        var sections = new Dictionary<string, TomlSectionInfo>(StringComparer.OrdinalIgnoreCase);
        var current = GetOrCreateSection("");
        var rawLines = toml.Replace("\r\n", "\n").Split('\n');

        for (var i = 0; i < rawLines.Length; i++)
        {
            var line = rawLines[i];
            var trimmed = line.Trim();
            if (TryParseTomlHeader(trimmed, out var sectionName, out var isArrayTable))
            {
                if (isArrayTable)
                {
                    GetOrCreateSection("").Keys.Add(sectionName);
                }

                current = GetOrCreateSection(sectionName);
                current.HeaderLineIndex = i;
                current.Lines.Add(line);
                continue;
            }

            current.Lines.Add(line);
            if (TryGetTomlKey(line, out var key))
            {
                current.Keys.Add(key);
            }
        }

        return sections;

        TomlSectionInfo GetOrCreateSection(string name)
        {
            if (!sections.TryGetValue(name, out var section))
            {
                section = new TomlSectionInfo(name);
                sections[name] = section;
            }

            return section;
        }
    }

    private static bool TryParseTomlHeader(string trimmed, out string sectionName, out bool isArrayTable)
    {
        sectionName = string.Empty;
        isArrayTable = false;

        if (!trimmed.StartsWith('[') || !trimmed.EndsWith(']')) return false;

        if (trimmed.StartsWith("[[") && trimmed.EndsWith("]]"))
        {
            sectionName = trimmed[2..^2].Trim();
            isArrayTable = true;
            return sectionName.Length > 0;
        }

        sectionName = trimmed[1..^1].Trim();
        return sectionName.Length > 0;
    }

    private static bool TryGetTomlKey(string line, out string key)
    {
        key = string.Empty;
        var trimmed = line.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#') || trimmed.StartsWith('[')) return false;

        var equalsIndex = trimmed.IndexOf('=');
        if (equalsIndex <= 0) return false;

        key = trimmed[..equalsIndex].Trim();
        return !string.IsNullOrEmpty(key);
    }

    private static int FindSectionInsertIndex(List<string> lines, int headerLineIndex)
    {
        if (headerLineIndex < 0) headerLineIndex = -1;
        for (var i = headerLineIndex + 1; i < lines.Count; i++)
        {
            var trimmed = lines[i].Trim();
            if (TryParseTomlHeader(trimmed, out _, out _)) return i;
        }

        return lines.Count;
    }

    private static int FindFirstSectionIndex(List<string> lines)
    {
        for (var i = 0; i < lines.Count; i++)
        {
            var trimmed = lines[i].Trim();
            if (TryParseTomlHeader(trimmed, out _, out _)) return i;
        }

        return lines.Count;
    }

    private static void AppendMissingSection(List<string> lines, string sectionName, List<string> sectionLines)
    {
        if (sectionName.Length == 0)
        {
            lines.InsertRange(FindFirstSectionIndex(lines), sectionLines.Where(TryGetTomlKeyLine));
            return;
        }

        while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[^1]))
        {
            lines.RemoveAt(lines.Count - 1);
        }

        if (lines.Count > 0) lines.Add(string.Empty);
        lines.AddRange(sectionLines.Where(line => !string.IsNullOrWhiteSpace(line)));
    }

    private static bool TryGetTomlKeyLine(string line) => TryGetTomlKey(line, out _);

    private sealed class TomlSectionInfo(string name)
    {
        public string Name { get; } = name;
        public int HeaderLineIndex { get; set; } = name.Length == 0 ? -1 : 0;
        public List<string> Lines { get; } = [];
        public HashSet<string> Keys { get; } = new(StringComparer.OrdinalIgnoreCase);
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
