namespace ShiroBot.Components.Updates;

/// <summary>
/// The component stopped, but its collectible assembly could not be unloaded because something still
/// references it. A new version can only take its place after the host restarts.
/// </summary>
internal sealed class ComponentUnloadPendingException(string message) : InvalidOperationException(message);
