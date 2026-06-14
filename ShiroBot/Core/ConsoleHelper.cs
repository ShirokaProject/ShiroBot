using System.Text;
using System.Runtime.Versioning;

namespace ShiroBot.Core;

public static class ConsoleHelper
{
    public sealed record ConsoleCommandOption(string Name, string Description);

    private static readonly Lock OutputLock = new();
    private static readonly StringBuilder InputBuffer = new();
    private static readonly List<string> History = [];

    private static IReadOnlyList<ConsoleCommandOption>? _completionOptions;
    private static Func<IReadOnlyList<ConsoleCommandOption>>? _completionProvider;
    private static bool _isReadingInput;
    private static string? _activePrompt;
    private static int _cursorIndex;
    private static int _historyIndex;
    private static string _historyDraft = string.Empty;
    private static int _inputTop;
    private static string _inlineCompletionSuffix = string.Empty;

    public static bool IsEnabled { get; set; } = true;

    public static void Log(string message) => WriteLine(message, ConsoleColor.Gray);

    public static void Info(string message) => WriteLine(message, ConsoleColor.Cyan);

    public static void Success(string message) => WriteLine(message, ConsoleColor.Green);

    public static void Warning(string message) => WriteLine(message, ConsoleColor.Yellow);

    public static void Error(string message) => WriteLine(message, ConsoleColor.Red);

    public static void Clear()
    {
        lock (OutputLock)
        {
            Console.Clear();
            if (_isReadingInput)
            {
                _inputTop = Console.CursorTop;
                RedrawInputUnsafe();
            }
        }
    }

    public static string ReadPrompt(string prompt, IReadOnlyList<ConsoleCommandOption>? completions = null)
    {
        return ReadPrompt(prompt, () => completions ?? []);
    }

    public static string ReadPrompt(string prompt, Func<IReadOnlyList<ConsoleCommandOption>>? completions)
    {
        lock (OutputLock)
        {
            _completionProvider = completions;
            _completionOptions = completions?.Invoke();
            _activePrompt = prompt;
            _isReadingInput = true;
            InputBuffer.Clear();
            _cursorIndex = 0;
            _historyIndex = History.Count;
            _historyDraft = string.Empty;
            _inputTop = Console.CursorTop;
            _inlineCompletionSuffix = string.Empty;
            RedrawInputUnsafe();
        }

        while (true)
        {
            ConsoleKeyInfo key;
            try
            {
                // Workaround for Linux/Mac: Console.ReadKey blocks stdout
                if (!OperatingSystem.IsWindows())
                {
                    while (!Console.KeyAvailable)
                    {
                        Thread.Sleep(10);
                    }
                }
                
                key = Console.ReadKey(intercept: true);
            }
            catch
            {
                lock (OutputLock)
                {
                    _isReadingInput = false;
                    _activePrompt = null;
                    _completionProvider = null;
                    InputBuffer.Clear();
                    _cursorIndex = 0;
                    _inlineCompletionSuffix = string.Empty;
                }

                return ReadPromptFallback(prompt, _completionOptions);
            }

            lock (OutputLock)
            {
                switch (key.Key)
                {
                    case ConsoleKey.PageUp:
                        if (OperatingSystem.IsWindows())
                        {
                            ScrollViewportUnsafe(-1);
                        }
                        break;
                    case ConsoleKey.PageDown:
                        if (OperatingSystem.IsWindows())
                        {
                            ScrollViewportUnsafe(1);
                        }
                        break;
                    case ConsoleKey.Escape:
                        if (!string.IsNullOrEmpty(_inlineCompletionSuffix))
                        {
                            _inlineCompletionSuffix = string.Empty;
                            RedrawInputUnsafe();
                        }

                        break;
                    case ConsoleKey.Enter:
                    {
                        var result = InputBuffer.ToString();
                        ClearOverlayUnsafe();
                        SetCursorPositionSafe(0, _inputTop);
                        _isReadingInput = false;
                        _activePrompt = null;
                        _completionProvider = null;

                        if (!string.IsNullOrWhiteSpace(result) &&
                            (History.Count == 0 || !string.Equals(History[^1], result, StringComparison.Ordinal)))
                        {
                            History.Add(result);
                        }

                        InputBuffer.Clear();
                        _cursorIndex = 0;
                        _inlineCompletionSuffix = string.Empty;
                        return result;
                    }
                    case ConsoleKey.Backspace:
                        if (_cursorIndex > 0)
                        {
                            InputBuffer.Remove(_cursorIndex - 1, 1);
                            _cursorIndex--;
                            RedrawInputUnsafe();
                        }

                        break;
                    case ConsoleKey.Delete:
                        if (_cursorIndex < InputBuffer.Length)
                        {
                            InputBuffer.Remove(_cursorIndex, 1);
                            RedrawInputUnsafe();
                        }

                        break;
                    case ConsoleKey.LeftArrow:
                        if (_cursorIndex > 0)
                        {
                            _cursorIndex--;
                            PositionCursorUnsafe();
                        }

                        break;
                    case ConsoleKey.RightArrow:
                        if (_cursorIndex < InputBuffer.Length)
                        {
                            _cursorIndex++;
                            PositionCursorUnsafe();
                        }

                        break;
                    case ConsoleKey.Home:
                        _cursorIndex = 0;
                        PositionCursorUnsafe();
                        break;
                    case ConsoleKey.End:
                        _cursorIndex = InputBuffer.Length;
                        PositionCursorUnsafe();
                        break;
                    case ConsoleKey.UpArrow:
                        NavigateHistory(-1);
                        break;
                    case ConsoleKey.DownArrow:
                        NavigateHistory(1);
                        break;
                    case ConsoleKey.Tab:
                        ApplyCompletion();
                        break;
                    default:
                        if (!char.IsControl(key.KeyChar))
                        {
                            InputBuffer.Insert(_cursorIndex, key.KeyChar);
                            _cursorIndex++;
                            RedrawInputUnsafe();
                        }

                        break;
                }
            }
        }
    }

    private static string ReadPromptFallback(string prompt, IReadOnlyList<ConsoleCommandOption>? completions)
    {
        lock (OutputLock)
        {
            _completionOptions = completions;
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.Write(prompt);
            Console.ResetColor();
        }

        var input = Console.ReadLine() ?? string.Empty;

        if (!string.IsNullOrWhiteSpace(input) &&
            (History.Count == 0 || !string.Equals(History[^1], input, StringComparison.Ordinal)))
        {
            History.Add(input);
        }

        _historyIndex = History.Count;
        _historyDraft = string.Empty;
        return input;
    }

    private static void NavigateHistory(int direction)
    {
        if (History.Count == 0)
        {
            return;
        }

        if (_historyIndex == History.Count)
        {
            _historyDraft = InputBuffer.ToString();
        }

        _historyIndex = Math.Clamp(_historyIndex + direction, 0, History.Count);
        InputBuffer.Clear();

        InputBuffer.Append(_historyIndex == History.Count ? _historyDraft : History[_historyIndex]);

        _cursorIndex = InputBuffer.Length;
        RedrawInputUnsafe();
    }

    private static void ApplyCompletion()
    {
        UpdateInlineCompletion();
        if (string.IsNullOrEmpty(_inlineCompletionSuffix))
        {
            return;
        }

        InputBuffer.Append(_inlineCompletionSuffix);
        _cursorIndex = InputBuffer.Length;
        _inlineCompletionSuffix = string.Empty;
        RedrawInputUnsafe();
    }

    private static void UpdateInlineCompletion()
    {
        if (_completionProvider is not null)
        {
            _completionOptions = _completionProvider();
        }

        var completions = _completionOptions;
        if (completions is null || completions.Count == 0)
        {
            _inlineCompletionSuffix = string.Empty;
            return;
        }

        var input = InputBuffer.ToString().TrimStart();
        if (input.Length == 0)
        {
            _inlineCompletionSuffix = string.Empty;
            return;
        }

        var query = input.ToLowerInvariant();
        ConsoleCommandOption? match;

        if (query.Length == 0)
        {
            match = completions.FirstOrDefault(command =>
                        string.Equals(command.Name, "help", StringComparison.OrdinalIgnoreCase)) ??
                    completions
                        .OrderBy(command => command.Name, StringComparer.OrdinalIgnoreCase)
                        .FirstOrDefault();
        }
        else
        {
            match = completions
                .Where(command => command.Name.StartsWith(query, StringComparison.OrdinalIgnoreCase))
                .OrderBy(command => command.Name, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
        }

        if (match is null || input.Length >= match.Name.Length)
        {
            _inlineCompletionSuffix = string.Empty;
            return;
        }

        _inlineCompletionSuffix = match.Name[input.Length..];
    }

    private static void WriteLine(string message, ConsoleColor color)
    {
        if (!IsEnabled)
        {
            return;
        }

        lock (OutputLock)
        {
            if (_isReadingInput)
            {
                ClearOverlayUnsafe();
                SetCursorPositionSafe(0, _inputTop);
            }

            var previous = Console.ForegroundColor;
            Console.ForegroundColor = color;
            Console.WriteLine($"{Timestamp()} {message}");
            Console.ForegroundColor = previous;

            if (_isReadingInput)
            {
                _inputTop = Console.CursorTop;
                RedrawInputUnsafe();
            }
        }
    }

    private static void RedrawInputUnsafe()
    {
        if (!_isReadingInput || string.IsNullOrEmpty(_activePrompt))
        {
            return;
        }

        ClearOverlayUnsafe();
        UpdateInlineCompletion();

        SetCursorPositionSafe(0, _inputTop);
        RenderInputLineUnsafe();
        PositionCursorUnsafe();
    }

    private static string GetRenderedInputUnsafe()
    {
        var content = _activePrompt + InputBuffer;
        var width = Math.Max(1, Console.BufferWidth);
        if (content.Length <= width - 1)
        {
            return content;
        }

        var visibleWidth = Math.Max(1, width - 1);
        var promptLength = _activePrompt!.Length;
        var windowStart = Math.Max(0, _cursorIndex - Math.Max(0, visibleWidth - promptLength - 1));
        var visibleInput = InputBuffer.ToString();
        visibleInput = windowStart < visibleInput.Length ? visibleInput[windowStart..] : string.Empty;

        var maxInputLength = Math.Max(0, visibleWidth - promptLength);
        if (visibleInput.Length > maxInputLength)
        {
            visibleInput = visibleInput[..maxInputLength];
        }

        return _activePrompt + visibleInput;
    }

    private static void RenderInputLineUnsafe()
    {
        var width = Math.Max(1, Console.BufferWidth);
        var renderedInput = GetRenderedInputUnsafe();
        var visibleSuffix = _inlineCompletionSuffix;

        if (renderedInput.Length + visibleSuffix.Length > width - 1)
        {
            visibleSuffix = visibleSuffix[..Math.Max(0, width - 1 - renderedInput.Length)];
        }

        Console.ForegroundColor = ConsoleColor.Gray;
        Console.Write(renderedInput);
        if (!string.IsNullOrEmpty(visibleSuffix))
        {
            var previous = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write(visibleSuffix);
            Console.ForegroundColor = previous;
        }

        var written = renderedInput.Length + visibleSuffix.Length;
        if (written < width)
        {
            Console.Write(new string(' ', width - written));
        }
    }

    private static void ClearOverlayUnsafe()
    {
        var width = Math.Max(1, Console.BufferWidth);
        SetCursorPositionSafe(0, _inputTop);
        Console.Write(new string(' ', width));
        SetCursorPositionSafe(0, _inputTop);
    }

    private static void PositionCursorUnsafe()
    {
        if (string.IsNullOrEmpty(_activePrompt))
        {
            return;
        }

        var width = Math.Max(1, Console.BufferWidth);
        var promptLength = _activePrompt.Length;
        var visibleWidth = Math.Max(1, width - 1);
        var windowStart = Math.Max(0, _cursorIndex - Math.Max(0, visibleWidth - promptLength - 1));
        var visibleCursor = promptLength + (_cursorIndex - windowStart);
        var left = Math.Clamp(visibleCursor, 0, Math.Max(0, width - 1));
        SetCursorPositionSafe(left, _inputTop);
    }

    [SupportedOSPlatform("windows")]
    private static void ScrollViewportUnsafe(int delta)
    {
        try
        {
            var maxTop = Math.Max(0, Console.BufferHeight - Console.WindowHeight);
            Console.WindowTop = Math.Clamp(Console.WindowTop + delta, 0, maxTop);
        }
        catch
        {
            // ignore viewport issues on unsupported terminals
        }
    }

    private static string Timestamp() => $"[{DateTime.Now:HH:mm:ss}]";

    private static void SetCursorPositionSafe(int left, int top)
    {
        var safeLeft = Math.Clamp(left, 0, Math.Max(0, Console.BufferWidth - 1));
        var safeTop = Math.Clamp(top, 0, Math.Max(0, Console.BufferHeight - 1));
        Console.SetCursorPosition(safeLeft, safeTop);
    }
}
