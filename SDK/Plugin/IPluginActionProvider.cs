namespace ShiroBot.SDK.Plugin;

public sealed record PluginActionDescriptor(
    string Id,
    string Label,
    string? Description = null,
    string Tone = "default",
    bool RequiresConfirmation = false,
    string? ConfirmationText = null);

public sealed record PluginActionResult(bool Ok, string Message, bool Refresh = false);

/// <summary>
/// Optional plugin capability surfaced both in the authenticated host dashboard
/// and as host console commands, so an action is declared once and reachable
/// from either front end.
/// </summary>
public interface IPluginActionProvider
{
    IReadOnlyList<PluginActionDescriptor> Actions { get; }

    Task<PluginActionResult> ExecuteActionAsync(
        string actionId,
        CancellationToken cancellationToken = default);
}
