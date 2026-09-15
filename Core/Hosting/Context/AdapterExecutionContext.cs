namespace ShiroBot.Hosting.Context;

internal static class AdapterExecutionContext
{
    private static readonly AsyncLocal<string?> CurrentPlatform = new();

    public static string? Current => CurrentPlatform.Value;

    public static IDisposable Enter(string platform)
    {
        var previous = CurrentPlatform.Value;
        CurrentPlatform.Value = platform;
        return new Scope(previous);
    }

    private sealed class Scope(string? previous) : IDisposable
    {
        private string? _previous = previous;
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            CurrentPlatform.Value = _previous;
            _previous = null;
        }
    }
}
