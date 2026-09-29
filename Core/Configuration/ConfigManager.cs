using System.Text.Json;
using System.Text.RegularExpressions;
using ShiroBot.SDK.Abstractions;
using ShiroBot.SDK.Config;
using ShiroBot.Console;
using Tomlyn;
using Tomlyn.Parsing;
using Tomlyn.Syntax;

namespace ShiroBot.Configuration;

//总配置类
public class CoreConfig
{
    /// <summary>并行加载的 Adapter 名称或 DLL 路径。</summary>
    public string[] Protocols { get; set; } = [];

    public bool EnableLog { get; set; } = true;

    public bool DisableConsoleInput { get; set; } = false;

    public string? GithubProxy { get; set; }

    public string HostUpdateRepository { get; set; } = "ShirokaProject/ShiroBot";

    /// <summary>Avalonia 宿主主题：Light / Dark / Auto。插件渲染未显式指定 Theme 时仍默认 Light。</summary>
    public string AvaloniaTheme { get; set; } = "Light";

    public string[] OwnerList { get; set; } = [];

    public string[] AdminList { get; set; } = [];

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
    public const string DefaultListenUrl = "http://127.0.0.1:7001";

    public bool Enable { get; set; } = true;

    public string[] ListenUrls { get; set; } = [DefaultListenUrl];

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

    public bool AllowsGroup(string pluginName, string groupId)
    {
        var plugins = Plugins;
        return !plugins.TryGetValue(pluginName, out var rule) ||
               string.Equals(rule.Mode, "default", StringComparison.OrdinalIgnoreCase)
            ? Default.IsMatch(groupId)
            : rule.IsMatch(groupId);
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

    public string[] Groups { get; init; } = [];

    public bool IsMatch(string groupId)
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
            var normalizedToml = NormalizeLegacyCoreConfig(tomlString);
            if (normalizedToml != tomlString)
            {
                await File.WriteAllTextAsync(_coreConfigPath, normalizedToml);
                tomlString = normalizedToml;
            }
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
            var tomlString = SerializeToml(defaultConfig, _options);
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
                ConsoleOutput.Warning($"未找到{scopeName}配置文件 {normalizedConfigPath}，已生成默认配置，请前往配置。");
                SaveToml(normalizedConfigPath, newConfig, _options);
                return newConfig;
            }

            var toml = File.ReadAllText(normalizedConfigPath);
            var defaults = SerializeToml(Activator.CreateInstance<T>(), _options);
            var updatedToml = MergeToml(toml, defaults, overwriteExisting: false);
            if (updatedToml != toml)
            {
                File.WriteAllText(normalizedConfigPath, updatedToml);
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

        var pathParts = keyPath.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (pathParts.Length == 0) throw new ArgumentException("配置键路径不能为空。", nameof(keyPath));

        var key = pathParts[^1];
        var sectionName = string.Join('.', pathParts[..^1]);
        var valueLiteral = FormatTomlValue(value);
        var patch = (sectionName.Length == 0 ? string.Empty : $"[{sectionName}]\n") + $"{key} = {valueLiteral}\n";
        var current = File.Exists(normalizedConfigPath) ? File.ReadAllText(normalizedConfigPath) : string.Empty;
        var updated = MergeToml(current, patch, overwriteExisting: true);
        if (updated != current) File.WriteAllText(normalizedConfigPath, updated);
    }

    private static void SaveToml<T>(string configPath, T config, TomlSerializerOptions options) where T : class
    {
        var normalizedConfigPath = Path.GetFullPath(configPath);
        Directory.CreateDirectory(Path.GetDirectoryName(normalizedConfigPath)!);
        var tomlString = SerializeToml(config, options);
        if (!File.Exists(normalizedConfigPath) || new FileInfo(normalizedConfigPath).Length == 0)
        {
            File.WriteAllText(normalizedConfigPath, tomlString);
            return;
        }

        var original = File.ReadAllText(normalizedConfigPath);
        var current = config is CoreConfig ? NormalizeLegacyCoreConfig(original) : original;
        var updated = MergeToml(current, tomlString, overwriteExisting: true);
        if (updated != original) File.WriteAllText(normalizedConfigPath, updated);
    }

    private static string NormalizeLegacyCoreConfig(string toml)
    {
        DocumentSyntax document;
        try
        {
            document = SyntaxParser.ParseStrict(toml);
        }
        catch
        {
            var repaired = RepairGeneratedDuplicateRouteDefaults(toml);
            if (repaired == toml) throw;
            toml = repaired;
            document = SyntaxParser.ParseStrict(toml);
        }
        var edits = new List<TomlEdit>();
        Migrate(document.KeyValues, "protocol", "protocols");
        foreach (var table in document.Tables)
        {
            if (string.Equals(table.Name?.ToString().Trim(), "api", StringComparison.OrdinalIgnoreCase))
                Migrate(table.Items, "listen_url", "listen_urls");
        }

        var normalized = toml;
        foreach (var edit in edits.OrderByDescending(edit => edit.Offset))
            normalized = normalized.Remove(edit.Offset, edit.Length).Insert(edit.Offset, edit.Text);

        if (edits.Count > 0) SyntaxParser.ParseStrict(normalized);
        return normalized;

        void Migrate(SyntaxList<KeyValueSyntax> items, string oldKey, string newKey)
        {
            var legacy = items.FirstOrDefault(item =>
                string.Equals(item.Key?.ToString().Trim(), oldKey, StringComparison.OrdinalIgnoreCase));
            if (legacy is null) return;
            var legacyValue = legacy.Value!.Span;
            var legacyLiteral = toml.Substring(legacyValue.Offset, legacyValue.Length);
            var emptyLegacyValue = legacyLiteral.Trim() is "\"\"" or "''";
            var arrayLiteral = emptyLegacyValue ? "[]" : "[" + legacyLiteral + "]";

            var canonical = items.FirstOrDefault(item =>
                string.Equals(item.Key?.ToString().Trim(), newKey, StringComparison.OrdinalIgnoreCase));
            if (canonical is not null)
            {
                var canonicalValue = canonical.Value!.Span;
                var literal = toml.Substring(canonicalValue.Offset, canonicalValue.Length).Trim();
                if (!emptyLegacyValue && literal.StartsWith('[') && literal.EndsWith(']') &&
                    string.IsNullOrWhiteSpace(literal[1..^1]))
                {
                    edits.Add(new TomlEdit(canonicalValue.Offset, canonicalValue.Length,
                        arrayLiteral, edits.Count));
                }

                var lineStart = toml.LastIndexOf('\n', Math.Max(0, legacy.Key!.Span.Offset - 1)) + 1;
                var lineEnd = legacy.EndOfLineToken is { } eol
                    ? eol.Span.Offset + eol.Span.Length
                    : legacy.Span.Offset + legacy.Span.Length;
                edits.Add(new TomlEdit(lineStart, lineEnd - lineStart, string.Empty, edits.Count));
                return;
            }

            var keySpan = legacy.Key!.Span;
            edits.Add(new TomlEdit(keySpan.Offset, keySpan.Length, newKey, edits.Count));
            edits.Add(new TomlEdit(legacyValue.Offset, legacyValue.Length, arrayLiteral, edits.Count));
        }
    }

    private static string RepairGeneratedDuplicateRouteDefaults(string toml)
    {
        var headers = Regex.Matches(toml,
            @"(?m)^[ \t]*\[[^\r\n]+\][ \t]*(?:\r?\n|$)");
        var routeHeaders = headers.Cast<Match>()
            .Where(match => match.Value.Trim() == "[plugin_routes.default]")
            .ToArray();
        if (routeHeaders.Length != 2) return toml;

        var first = routeHeaders[0];
        var next = headers.Cast<Match>().FirstOrDefault(match => match.Index > first.Index);
        if (next is null) return toml;
        var section = toml[(first.Index + first.Length)..next.Index];
        var values = section.Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#'))
            .ToArray();
        if (!values.Contains("mode = \"blacklist\"") || !values.Contains("groups = []") ||
            values.Any(line => line is not ("mode = \"blacklist\"" or "groups = []" or "listen_urls = []")))
            return toml;

        return toml.Remove(first.Index, next.Index - first.Index);
    }

    private static string MergeToml(string current, string incoming, bool overwriteExisting)
    {
        if (string.IsNullOrWhiteSpace(current))
        {
            SyntaxParser.ParseStrict(incoming);
            return incoming;
        }

        var original = SyntaxParser.ParseStrict(current);
        var replacement = SyntaxParser.ParseStrict(incoming);
        var newline = current.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var edits = new List<TomlEdit>();
        var matchedTables = new HashSet<TableSyntaxBase>();
        MergeSection(original.KeyValues, replacement.KeyValues, original.Tables.ChildrenCount > 0
            ? original.Tables.GetChild(0)!.Span.Offset
            : current.Length);

        foreach (var newTable in replacement.Tables)
        {
            var oldTable = original.Tables.FirstOrDefault(table =>
                !matchedTables.Contains(table) &&
                table.GetType() == newTable.GetType() &&
                string.Equals(table.Name!.ToString().Trim(), newTable.Name!.ToString().Trim(), StringComparison.OrdinalIgnoreCase));

            if (oldTable is null)
            {
                var sectionText = newTable.ToString().TrimStart('\r', '\n');
                var separator = current.Length == 0 || current.EndsWith("\n\n", StringComparison.Ordinal)
                    ? string.Empty
                    : current.EndsWith('\n') ? newline : newline + newline;
                edits.Add(new TomlEdit(current.Length, 0, separator + ConvertNewlines(sectionText, newline), edits.Count));
                continue;
            }

            matchedTables.Add(oldTable);
            var insertion = oldTable.Items.ChildrenCount > 0
                ? EndOfLine(oldTable.Items.GetChild(oldTable.Items.ChildrenCount - 1)!)
                : EndOfTableHeader(oldTable);
            MergeSection(oldTable.Items, newTable.Items, insertion);
        }

        if (edits.Count == 0) return current;

        // Apply source-span edits from the end, so earlier offsets remain valid.
        var updated = current;
        foreach (var edit in edits.OrderByDescending(edit => edit.Offset).ThenByDescending(edit => edit.Order))
        {
            updated = updated.Remove(edit.Offset, edit.Length).Insert(edit.Offset, edit.Text);
        }

        SyntaxParser.ParseStrict(updated);
        return updated;

        void MergeSection(SyntaxList<KeyValueSyntax> oldItems, SyntaxList<KeyValueSyntax> newItems, int insertion)
        {
            var additions = new System.Text.StringBuilder();
            foreach (var newItem in newItems)
            {
                var oldItem = oldItems.FirstOrDefault(item =>
                    string.Equals(item.Key!.ToString().Trim(), newItem.Key!.ToString().Trim(), StringComparison.OrdinalIgnoreCase));
                if (oldItem is null)
                {
                    additions.Append(ConvertNewlines(newItem.ToString(), newline));
                }
                else if (overwriteExisting)
                {
                    var oldValue = oldItem.Value!.Span;
                    var newValue = newItem.Value!.Span;
                    var literal = incoming.Substring(newValue.Offset, newValue.Length);
                    if (current.AsSpan(oldValue.Offset, oldValue.Length).SequenceEqual(literal)) continue;
                    edits.Add(new TomlEdit(oldValue.Offset, oldValue.Length, ConvertNewlines(literal, newline), edits.Count));
                }
            }

            if (additions.Length > 0)
            {
                var prefix = insertion > 0 && current[insertion - 1] != '\n' ? newline : string.Empty;
                edits.Add(new TomlEdit(insertion, 0, prefix + additions.ToString(), edits.Count));
            }
        }

        int EndOfLine(KeyValueSyntax item) => item.EndOfLineToken is { } eol
            ? eol.Span.Offset + eol.Span.Length
            : item.Span.Offset + item.Span.Length;

        int EndOfTableHeader(TableSyntaxBase table) => table.EndOfLineToken is { } eol
            ? eol.Span.Offset + eol.Span.Length
            : table.CloseBracket!.Span.Offset + table.CloseBracket.Span.Length;
    }

    private static string ConvertNewlines(string text, string newline) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\n", newline, StringComparison.Ordinal);

    private sealed record TomlEdit(int Offset, int Length, string Text, int Order);

    private static string SerializeToml<T>(T config, TomlSerializerOptions options) where T : class
    {
        var toml = FormatToml(TomlSerializer.Serialize(config, options));
        var comments = typeof(T).GetProperties()
            .Select(property => new
            {
                Key = JsonNamingPolicy.SnakeCaseLower.ConvertName(property.Name),
                Field = property.GetCustomAttributes(typeof(ConfigFieldAttribute), inherit: true)
                    .OfType<ConfigFieldAttribute>()
                    .FirstOrDefault()
            })
            .Where(item => item.Field is not null)
            .ToDictionary(
                item => item.Key,
                item => CreateConfigCommentLines(item.Field!),
                StringComparer.OrdinalIgnoreCase);

        if (comments.Count == 0) return toml;

        var lines = toml.Replace("\r\n", "\n").Split('\n');
        var result = new List<string>(lines.Length + comments.Count * 2);
        var inTopLevel = true;
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (TryParseTomlHeader(trimmed, out _, out _))
            {
                inTopLevel = false;
            }

            if (inTopLevel && TryGetTomlKey(line, out var key) && comments.TryGetValue(key, out var commentLines))
            {
                result.AddRange(commentLines);
            }

            result.Add(line);
        }

        return string.Join(Environment.NewLine, result).TrimEnd() + Environment.NewLine;
    }

    private static IReadOnlyList<string> CreateConfigCommentLines(ConfigFieldAttribute field)
    {
        var comments = new List<string>();
        AddComment(field.Label);
        AddComment(field.Description);

        if (field.Options.Length > 0)
        {
            AddComment("Options: " + string.Join(", ", field.Options));
        }

        if (!double.IsNaN(field.Min) || !double.IsNaN(field.Max))
        {
            var min = double.IsNaN(field.Min) ? "-inf" : field.Min.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var max = double.IsNaN(field.Max) ? "+inf" : field.Max.ToString(System.Globalization.CultureInfo.InvariantCulture);
            AddComment($"Range: {min}..{max}");
        }

        if (!string.IsNullOrWhiteSpace(field.Placeholder))
        {
            AddComment("Placeholder: " + field.Placeholder);
        }

        return comments;

        void AddComment(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;

            foreach (var line in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
            {
                var normalized = line.Trim();
                if (normalized.Length == 0) continue;
                var comment = "# " + normalized;
                if (!comments.Contains(comment, StringComparer.Ordinal)) comments.Add(comment);
            }
        }
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

}
