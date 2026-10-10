namespace ShiroBot.SDK.Plugin;

/// <summary>Host-managed temporary directories scoped to the current plugin.</summary>
public interface ITemporaryFileContext
{
    /// <summary>
    /// Creates a unique directory under the host cache. Retention starts at creation and must be positive.
    /// The host deletes the entire directory after expiry, with periodic cleanup and retries for locked files.
    /// All directories left by a previous process are removed at the next startup, regardless of expiry.
    /// Do not use this directory for persistent data or continue writing after <see cref="TemporaryDirectory.ExpiresAt"/>.
    /// </summary>
    TemporaryDirectory CreateDirectory(TimeSpan retention);
}

/// <summary>A temporary directory owned and cleaned by the host; no plugin-side Dispose or deletion is needed.</summary>
public sealed record TemporaryDirectory(string Path, DateTimeOffset ExpiresAt);
