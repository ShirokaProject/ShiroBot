using ShiroBot.SDK.Plugin;

namespace ShiroBot.SDK.Abstractions;

public static class BotLog
{
    private static readonly AsyncLocal<IConsoleLogger?> CurrentLogger = new();
    private static IConsoleLogger _defaultLogger = new DefaultLogger();

    private static IConsoleLogger Logger => CurrentLogger.Value ?? _defaultLogger;

    public static void SetDefault(IConsoleLogger logger)
    {
        _defaultLogger = logger;
    }

    public static IDisposable BeginScope(IConsoleLogger? logger)
    {
        var previous = CurrentLogger.Value;
        CurrentLogger.Value = logger;
        return new Scope(() => CurrentLogger.Value = previous);
    }
    
    public static async Task RunScoped(IConsoleLogger? logger, Func<Task> action)
    {
        using var _ = BeginScope(logger);
        await action();
    }

    public static void Log(string message) => Logger.Log(message);
    public static void Info(string message) => Logger.Info(message);
    public static void Success(string message) => Logger.Success(message);
    public static void Warning(string message) => Logger.Warning(message);
    public static void Error(string message) => Logger.Error(message);

    private sealed class Scope(Action onDispose) : IDisposable
    {
        public void Dispose() => onDispose();
    }

    private sealed class DefaultLogger : IConsoleLogger
    {
        public bool IsEnabled { get; set; } = true;

        void IConsoleLogger.Log(string message) { }
        void IConsoleLogger.Info(string message) { }
        void IConsoleLogger.Success(string message) { }
        void IConsoleLogger.Warning(string message) { }
        void IConsoleLogger.Error(string message) { }
    }
}
