namespace ShiroBot.SDK.Plugin;

public sealed record PluginActionDescriptor(
    string Id,
    string Label,
    string? Description = null,
    string Tone = "default",
    bool RequiresConfirmation = false,
    string? ConfirmationText = null);

public sealed record PluginActionResult(bool Ok, string Message, bool Refresh = false)
{
    /// <summary>
    /// Longer text the user should read, e.g. a report or generated values. When set, the Dashboard shows it in a
    /// centred dialog and the console in a centred panel; without it, Message stays a brief notice.
    /// </summary>
    public string? Details { get; init; }

    /// <summary>Heading of the Details dialog; defaults to the action's label.</summary>
    public string? Title { get; init; }
}

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
