namespace ShiroBot.SDK.Dashboard;

/// <summary>Built-in renderer kinds understood by the authenticated host dashboard.</summary>
public static class DashboardTabKinds
{
    public const string Gallery = "gallery";
    public const string Table = "table";
    public const string List = "list";
    public const string Stats = "stats";
    public const string Link = "link";
    public const string Iframe = "iframe";
}

/// <summary>Where a tab gets its data, or the page it opens for link/iframe tabs.</summary>
public sealed record DashboardTabSource(string Path, string Method = "GET");

/// <summary>Semantic field names for gallery and list items.</summary>
public sealed record DashboardTabFields(
    string? Image = null,
    string? Title = null,
    string? Description = null,
    string? Tags = null,
    string? Meta = null);

/// <summary>One table column; values are read from an item's <c>values</c> object.</summary>
public sealed record DashboardTabColumn(string Key, string Label, string Type = "text");

public sealed record DashboardTabFilterOption(string Value, string Label);

/// <summary>A query parameter the dashboard renders as a filter control.</summary>
public sealed record DashboardTabFilter(
    string Key,
    string Label,
    string Type = "text",
    IReadOnlyList<DashboardTabFilterOption>? Options = null);

/// <summary>Pagination parameters appended to the tab's data request.</summary>
public sealed record DashboardTabPagination(
    string PageParameter = "page",
    string PageSizeParameter = "page_size",
    int PageSize = 24);

/// <summary>An action rendered for every item; <c>{id}</c> in the path is replaced by the item ID.</summary>
public sealed record DashboardTabAction(
    string Id,
    string Label,
    string Path,
    string Method = "POST",
    string? Confirm = null,
    string Tone = "default");

/// <summary>
/// A plugin-owned dashboard tab. The dashboard only understands the common renderers and never
/// injects plugin HTML or JavaScript into its own page.
/// </summary>
public sealed record DashboardTabDescriptor(
    string Id,
    string Label,
    string Kind,
    string? Icon = null,
    string? Description = null,
    int Order = 100,
    DashboardTabSource? Source = null,
    DashboardTabFields? Fields = null,
    IReadOnlyList<DashboardTabColumn>? Columns = null,
    IReadOnlyList<DashboardTabFilter>? Filters = null,
    DashboardTabPagination? Pagination = null,
    IReadOnlyList<DashboardTabAction>? Actions = null)
{
    /// <summary>Additive contract version; unknown renderers are shown as unsupported.</summary>
    public int ContractVersion { get; init; } = 1;
}

/// <summary>
/// Optional plugin capability: declares dashboard tabs backed by common renderers.
/// Implement this on the plugin class; the host reads it while building plugin details.
/// </summary>
public interface IPluginDashboardTabProvider
{
    IReadOnlyList<DashboardTabDescriptor> Tabs { get; }
}
