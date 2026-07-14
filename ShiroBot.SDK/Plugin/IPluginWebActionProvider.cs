namespace ShiroBot.SDK.Plugin;

public sealed record PluginWebActionDescriptor(
    string Id,
    string Label,
    string? Description = null,
    string Tone = "default",
    bool RequiresConfirmation = false,
    string? ConfirmationText = null);

public sealed record PluginWebActionResult(bool Ok, string Message, bool Refresh = false);

/// <summary>
/// Optional plugin capability exposed through the authenticated host dashboard API.
/// </summary>
public interface IPluginWebActionProvider
{
    IReadOnlyList<PluginWebActionDescriptor> WebActions { get; }

    Task<PluginWebActionResult> ExecuteWebActionAsync(
        string actionId,
        CancellationToken cancellationToken = default);
}
