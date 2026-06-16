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

    public string? GithubProxy { get; set; }

    public string HostUpdateRepository { get; set; } = "ShirokaProject/ShiroBot";

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

    public void SetConfigValue(string configPath, string keyPath, object? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyPath);

        var normalizedConfigPath = Path.GetFullPath(configPath);
        Directory.CreateDirectory(Path.GetDirectoryName(normalizedConfigPath)!);

        var toml = File.Exists(normalizedConfigPath) ? File.ReadAllText(normalizedConfigPath) : string.Empty;
        var lines = toml.Replace("\r\n", "\n").Split('\n').ToList();
        if (lines is [{ Length: 0 }]) lines.Clear();

        var pathParts = keyPath.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (pathParts.Length == 0) throw new ArgumentException("配置键路径不能为空。", nameof(keyPath));

        var key = pathParts[^1];
        var sectionName = string.Join('.', pathParts[..^1]);
        var valueLiteral = FormatTomlValue(value);

        var (sectionStart, sectionEnd) = FindOrAppendSection(lines, sectionName);
        for (var i = sectionStart; i < sectionEnd; i++)
        {
            if (!TryGetTomlKey(lines[i], out var existingKey)
                || !string.Equals(existingKey, key, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            lines[i] = ReplaceTomlValue(lines[i], valueLiteral);
            WritePatchedToml(normalizedConfigPath, lines);
            return;
        }

        lines.Insert(sectionEnd, $"{key} = {valueLiteral}");
        WritePatchedToml(normalizedConfigPath, lines);
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

    private static (int Start, int End) FindOrAppendSection(List<string> lines, string sectionName)
    {
        if (sectionName.Length == 0)
        {
            return (0, FindFirstSectionIndex(lines));
        }

        for (var i = 0; i < lines.Count; i++)
        {
            var trimmed = lines[i].Trim();
            if (!TryParseTomlHeader(trimmed, out var currentSection, out var isArrayTable)
                || isArrayTable
                || !string.Equals(currentSection, sectionName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return (i + 1, FindSectionInsertIndex(lines, i));
        }

        while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[^1]))
        {
            lines.RemoveAt(lines.Count - 1);
        }

        if (lines.Count > 0) lines.Add(string.Empty);
        lines.Add($"[{sectionName}]");
        return (lines.Count, lines.Count);
    }

    private static string ReplaceTomlValue(string line, string valueLiteral)
    {
        var equalsIndex = line.IndexOf('=');
        if (equalsIndex < 0) return line;

        var commentIndex = FindInlineCommentIndex(line, equalsIndex + 1);
        var prefix = line[..(equalsIndex + 1)].TrimEnd();
        var spacing = GetValueSpacing(line, equalsIndex + 1);
        var comment = commentIndex >= 0 ? line[commentIndex..] : string.Empty;
        var beforeCommentSpacing = commentIndex >= 0 ? GetBeforeCommentSpacing(line, commentIndex) : string.Empty;

        return prefix + spacing + valueLiteral + beforeCommentSpacing + comment;
    }

    private static int FindInlineCommentIndex(string line, int startIndex)
    {
        var inString = false;
        var quote = '\0';
        var escaped = false;

        for (var i = startIndex; i < line.Length; i++)
        {
            var c = line[i];
            if (inString)
            {
                if (quote == '"' && c == '\\' && !escaped)
                {
                    escaped = true;
                    continue;
                }

                if (c == quote && !escaped) inString = false;
                escaped = false;
                continue;
            }

            if (c is '"' or '\'')
            {
                inString = true;
                quote = c;
                continue;
            }

            if (c == '#') return i;
        }

        return -1;
    }

    private static string GetValueSpacing(string line, int valueStart)
    {
        var end = valueStart;
        while (end < line.Length && char.IsWhiteSpace(line[end])) end++;
        return end == valueStart ? " " : line[valueStart..end];
    }

    private static string GetBeforeCommentSpacing(string line, int commentIndex)
    {
        var start = commentIndex;
        while (start > 0 && char.IsWhiteSpace(line[start - 1])) start--;
        return start == commentIndex ? " " : line[start..commentIndex];
    }

    private static string FormatTomlValue(object? value)
    {
        return value switch
        {
            null => "\"\"",
            string text => QuoteTomlString(text),
            bool boolean => boolean ? "true" : "false",
            sbyte or byte or short or ushort or int or uint or long or ulong => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)!,
            float or double or decimal => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)!,
            Enum enumValue => QuoteTomlString(enumValue.ToString()),
            System.Collections.IEnumerable items when value is not string => FormatTomlArray(items),
            _ => QuoteTomlString(value.ToString() ?? string.Empty)
        };
    }

    private static string FormatTomlArray(System.Collections.IEnumerable items)
    {
        var values = items.Cast<object?>().Select(FormatTomlValue);
        return "[" + string.Join(", ", values) + "]";
    }

    private static string QuoteTomlString(string value)
    {
        return JsonSerializer.Serialize(value);
    }

    private static void WritePatchedToml(string configPath, List<string> lines)
    {
        File.WriteAllText(configPath, string.Join(Environment.NewLine, lines).TrimEnd() + Environment.NewLine);
    }
}
