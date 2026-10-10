namespace ShiroBot.SDK.Plugin;

/// <summary>A temporary directory owned and cleaned by the host; no plugin-side Dispose or deletion is needed.</summary>
public sealed record TemporaryDirectory(string Path, DateTimeOffset ExpiresAt);
