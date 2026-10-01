namespace ShiroBot.Hosting.Context;

internal static class AdapterExecutionContext
{
    private static readonly AsyncLocal<string?> CurrentAdapterId = new();

    public static string? Current => CurrentAdapterId.Value;

    public static IDisposable Enter(string adapterId)
    {
        var previous = CurrentAdapterId.Value;
        CurrentAdapterId.Value = adapterId;
        return new Scope(previous);
    }

    private sealed class Scope(string? previous) : IDisposable
    {
        private string? _previous = previous;
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            CurrentAdapterId.Value = _previous;
            _previous = null;
        }
    }
}
