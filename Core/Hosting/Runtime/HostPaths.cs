namespace ShiroBot.Hosting.Runtime;

/// <summary>Where the host keeps its own files: default config, adapters, plugins and cache.</summary>
internal static class HostPaths
{
    internal const string RootVariable = "SHIROBOT_HOME";

    /// <summary>
    /// The executable's directory, unless <see cref="RootVariable"/> names another one. Docker runs a
    /// Dashboard-updated executable from the data volume but keeps the host files under /app.
    /// </summary>
    public static string Root { get; } = Environment.GetEnvironmentVariable(RootVariable) is { Length: > 0 } root
        ? Path.GetFullPath(root)
        : AppContext.BaseDirectory;
}
