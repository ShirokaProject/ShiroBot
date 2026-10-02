using System.Text.Json;
using ShiroBot.Configuration;
using Tomlyn;
using Tomlyn.Model;

namespace ShiroBot.Adapters;

internal sealed class PackageAdapterInstance
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public bool Enabled { get; set; }
    public Dictionary<string, object?> Config { get; set; } = new();
}

internal sealed class PackageAdapterConfig
{
    public List<PackageAdapterInstance>? Instances { get; set; }
}

// The package config is authoritative; mutations preserve other instances and unknown connection fields.
internal static class AdapterInstanceStore
{
    private static readonly object Gate = new();
    private static readonly TomlSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
    public static bool IsDeclared(string path) => File.Exists(path) && ReadObject(path).ContainsKey("instances");
    private static Dictionary<string, object?> Normalize(Dictionary<string, object?> values) => values.ToDictionary(pair => pair.Key, pair => NormalizeValue(pair.Value));
    private static object? NormalizeValue(object? value) => value switch
    {
        TomlTable table => table.ToDictionary(pair => pair.Key, pair => NormalizeValue(pair.Value)),
        Dictionary<string, object?> table => Normalize(table),
        TomlTableArray tables => tables.Select(item => NormalizeValue(item)).ToList(),
        TomlArray array => array.Select(NormalizeValue).ToList(),
        _ => value
    };
    public static Dictionary<string, object?> ReadObject(string path) => File.Exists(path)
        ? Normalize(TomlSerializer.Deserialize<Dictionary<string, object?>>(File.ReadAllText(path)) ?? new()) : new();
    public static List<PackageAdapterInstance> Read(string path)
    {
        var instances = File.Exists(path) ? TomlSerializer.Deserialize<PackageAdapterConfig>(File.ReadAllText(path), Options)?.Instances ?? [] : [];
        foreach (var instance in instances) instance.Config = Normalize(instance.Config);
        return instances;
    }
    public static void Write(string path, List<PackageAdapterInstance> instances, bool replaceLegacy = false)
    {
        lock (Gate)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temporary = path + ".writing-" + Guid.NewGuid().ToString("N");
            try
            {
                if (replaceLegacy) File.WriteAllText(temporary, "# Adapter instances: IDs must be unique across all adapter packages.\n");
                else if (File.Exists(path)) File.Copy(path, temporary);
                new ConfigManager(temporary).ReplaceConfigValue(temporary, "instances", instances.Select(item => (object)new Dictionary<string, object?>
                { ["id"] = item.Id, ["name"] = item.Name, ["enabled"] = item.Enabled, ["config"] = item.Config }).ToList());
                if (!File.Exists(path) || File.ReadAllText(path) != File.ReadAllText(temporary)) File.Move(temporary, path, overwrite: true);
            }
            finally { File.Delete(temporary); }
        }
    }
    public static void Mutate(string path, Action<List<PackageAdapterInstance>> mutate)
    {
        lock (Gate)
        {
            var instances = Read(path);
            mutate(instances);
            Write(path, instances);
        }
    }
    public static void Update(string path, string id, Action<PackageAdapterInstance> update) => Mutate(path, instances =>
        {
            var item = instances.SingleOrDefault(item => string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException($"未找到适配器实例 {id}。");
            update(item);
        });
    // An undeclared (implicit single-instance) config is the instance's config itself.
    public static Dictionary<string, object?> GetConfig(string path, string id) => !IsDeclared(path) ? ReadObject(path) : Read(path).Single(item => string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase)).Config;
    public static T Load<T>(string path, string id) where T : class, new()
    {
        T loaded = new();
        Patch(path, id, temporary => loaded = new ConfigManager(temporary).LoadConfig<T>(temporary, "适配器实例") ?? new());
        return loaded;
    }
    public static void Save<T>(string path, string id, T config) where T : class => Update(path, id, item =>
    {
        var values = TomlSerializer.Deserialize<Dictionary<string, object?>>(TomlSerializer.Serialize(config, Options)) ?? new();
        Merge(item.Config, Normalize(values));
    });
    private static void Merge(Dictionary<string, object?> target, Dictionary<string, object?> source)
    {
        foreach (var (key, value) in source)
            if (value is Dictionary<string, object?> nested && target.GetValueOrDefault(key) is Dictionary<string, object?> existing) Merge(existing, nested);
            else target[key] = value;
    }
    private static TomlTable ToTomlTable(Dictionary<string, object?> values)
    {
        var table = new TomlTable();
        foreach (var (key, value) in values) if (value is not null) table[key] = ToTomlValue(value)!;
        return table;
    }
    private static object? ToTomlValue(object? value)
    {
        if (value is Dictionary<string, object?> table) return ToTomlTable(table);
        if (value is System.Collections.IEnumerable array && value is not string)
        {
            var result = new TomlArray();
            foreach (var item in array) result.Add(ToTomlValue(item));
            return result;
        }
        return value;
    }
    public static void Patch(string path, string id, Action<string> patch)
    {
        lock (Gate)
        {
            if (!IsDeclared(path)) { patch(path); return; }
            var temporary = Path.Combine(Path.GetTempPath(), "shiro-instance-" + Guid.NewGuid().ToString("N") + ".toml");
            try
            {
                File.WriteAllText(temporary, TomlSerializer.Serialize(ToTomlTable(GetConfig(path, id))));
                if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                patch(temporary);
                var config = ReadObject(temporary);
                Update(path, id, item => item.Config = config);
            }
            finally { File.Delete(temporary); }
        }
    }
}
