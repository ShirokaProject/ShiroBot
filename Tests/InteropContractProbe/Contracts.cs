using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using ShiroBot.SDK.Dashboard;
using ShiroBot.SDK.Plugin;

namespace ShiroBot.InteropContractProbe;

public enum Mood
{
    Happy,
    VerySad,
    Confused = 5
}

public sealed record Forecast(
    [property: Description("城市名")] string City,
    [property: Description("天数")] int Days = 1,
    Mood Mood = Mood.Happy,
    double? Temperature = null,
    IReadOnlyList<string>? Tags = null,
    Dictionary<string, int>? Counts = null,
    Point? Location = null,
    JsonObject? Extra = null)
{
    public DateTimeOffset At { get; init; }
    public Guid Id { get; init; }
    [JsonPropertyName("custom_name")] public string? Renamed { get; init; }
    public List<Point> Path { get; init; } = [];
}

public sealed record Point(double X, double Y);

public sealed record SourceInfo(string Title, JsonObject? Parameters = null);

[PluginJson]
public sealed record ToolArgs([property: Description("关键词")] string Query, int Limit = 5);

[PluginContract]
public static partial class ProbeApi
{
    [PluginService("probe.forecast")] public static partial PluginService<Forecast, Forecast> Echo { get; }

    [PluginService("probe.source.*")]
    public static partial PluginServiceFamily<Point, Point, SourceInfo> Sources { get; }

    [PluginEvent("probe.moved")] public static partial PluginEvent<Point> Moved { get; }
}

[PluginJson]
public sealed record DashboardRule(
    [property: Description("名称")]
    [property: MaxLength(10)]
    string Name,
    [property: UiField(Kind = UiFieldKind.Group)]
    string Group,
    [property: Range(typeof(int), "0", "3600")]
    int Cooldown = 45,
    bool Enabled = true);