using ShiroBot.Console;
using ShiroBot.SDK.Plugin;

namespace ShiroBot.Hosting.Logging;

internal sealed class ConsoleLogger(string? prefix = null, HostLogHub? logHub = null) : IConsoleLogger
{
    private readonly string _prefix = string.IsNullOrWhiteSpace(prefix) ? string.Empty : prefix.Trim() + " ";
    private readonly string _source = ResolveSource(prefix);

    public bool IsEnabled
    {
        get => ConsoleOutput.IsEnabled;
        set => ConsoleOutput.IsEnabled = value;
    }

    public void Log(string message) => Write("log", message, ConsoleOutput.Log);
    public void Info(string message) => Write("info", message, ConsoleOutput.Info);
    public void Success(string message) => Write("success", message, ConsoleOutput.Success);
    public void Warning(string message) => Write("warning", message, ConsoleOutput.Warning);
    public void Error(string message) => Write("error", message, ConsoleOutput.Error);

    private void Write(string level, string message, Action<string> writeConsole)
    {
        logHub?.Record(_source, level, message);
        writeConsole(_prefix + message);
    }

    private static string ResolveSource(string? prefix)
    {
        if (string.IsNullOrWhiteSpace(prefix)) return "system";

        var text = prefix.Trim();
        if (!text.StartsWith('[') || !text.EndsWith(']')) return "system";

        var content = text[1..^1];
        var separatorIndex = content.IndexOf(':');
        if (separatorIndex < 0 || separatorIndex == content.Length - 1) return "system";

        return content[(separatorIndex + 1)..].Trim();
    }
}
