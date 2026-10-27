using System.Text;
using System.Runtime.Versioning;

namespace ShiroBot.TerminalUI;

public static class ConsoleOutput
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

    /// <summary>
    /// A notice floating above the prompt, like a completion menu: it stays put while logs scroll past above it and
    /// closes with Esc or the next command. For text the user should read rather than find in the log, such as an
    /// action's details. Without an interactive prompt (redirected output) it is written as plain lines instead.
    /// </summary>
    public static void Notice(string title, string? summary, string details, bool error = false)
    {
        if (!IsEnabled) return;

        lock (OutputLock)
        {
            if (Console.IsOutputRedirected || Console.IsInputRedirected)
            {
                Console.WriteLine($"{Timestamp()} {title}");
                if (!string.IsNullOrWhiteSpace(summary)) Console.WriteLine(summary);
                Console.WriteLine(details.TrimEnd());
                return;
            }

            // What the notice cannot fit goes to the log in full, so nothing is lost when it closes.
            if (Wrap(details.TrimEnd(), NoticeWidth() - 2).Count > NoticeBodyRoom(string.IsNullOrWhiteSpace(summary) ? 2 : 3))
                WriteLine($"{title}:\n{details.TrimEnd()}", ConsoleColor.Gray);

            _notice = new PendingNotice(title, summary, details, error);
            if (_isReadingInput) RedrawInputUnsafe();
        }
    }

    private sealed record PendingNotice(string Title, string? Summary, string Details, bool Error);

    private static PendingNotice? _notice;

    /// <summary>Rows the drawn notice occupies directly above the prompt row (<see cref="_inputTop"/>).</summary>
    private static int _noticeRows;

    private static readonly char RuleChar = Console.OutputEncoding.CodePage == 20127 ? '-' : '─';

    private static IReadOnlyList<(string Text, ConsoleColor Color)> BuildNoticeLinesUnsafe(PendingNotice notice)
    {
        var width = NoticeWidth();
        var textWidth = width - 2;
        var lines = new List<(string Text, ConsoleColor Color)>
        {
            // Rules above and below set it apart from the log, like a menu floating over it.
            (new string(RuleChar, width), ConsoleColor.DarkGray),
            ("  " + Truncate(notice.Title, textWidth), notice.Error ? ConsoleColor.Red : ConsoleColor.Cyan)
        };
        if (!string.IsNullOrWhiteSpace(notice.Summary))
            lines.AddRange(Wrap(notice.Summary.Trim(), textWidth).Select(line => ("  " + line, ConsoleColor.Gray)));

        // A long report shows its start and says how much is left.
        var body = Wrap(notice.Details.TrimEnd(), textWidth);
        var room = NoticeBodyRoom(lines.Count);
        var shown = body.Count > room ? room - 1 : body.Count;
        lines.AddRange(body.Take(shown).Select(line => ("  " + line, ConsoleColor.White)));
        if (shown < body.Count)
            lines.Add(($"  … 还有 {body.Count - shown} 行，已写入日志", ConsoleColor.DarkGray));

        const string hint = " Esc 关闭 ";
        lines.Add((new string(RuleChar, Math.Max(0, width - CellWidth(hint) - 2)) + hint + new string(RuleChar, 2), ConsoleColor.DarkGray));
        return lines;
    }

    // Windows consoles can have a buffer wider than the window; stay within what is visible.
    private static int NoticeWidth()
    {
        try
        {
            return Math.Max(20, Math.Min(Console.BufferWidth, Console.WindowWidth) - 1);
        }
        catch (IOException)
        {
            return 79;
        }
    }

    // Leave the screen's lower half for the log; at least a few lines however small the window.
    private static int NoticeBodyRoom(int headerLines) => Math.Clamp(SafeWindowHeight() / 2 - headerLines - 2, 3, 30);

    private static List<string> Wrap(string text, int width)
    {
        var lines = new List<string>();
        foreach (var raw in text.Replace("\r\n", "\n").Replace('\r', '\n').Replace("\t", "    ").Split('\n'))
        {
            var line = new StringBuilder();
            var used = 0;
            foreach (var ch in raw)
            {
                var cells = CellWidth(ch.ToString());
                if (used + cells > width && line.Length > 0)
                {
                    lines.Add(line.ToString());
                    line.Clear();
                    used = 0;
                }

                line.Append(ch);
                used += cells;
            }

            lines.Add(line.ToString());
        }

        return lines;
    }

    private static string Truncate(string text, int width)
    {
        var first = Wrap(text, width)[0];
        return first.Length < text.Length ? first[..^1] + "…" : first;
    }

    // CJK and other wide characters take two columns; padding by string length would misalign them.
    private static int CellWidth(string text) => new Spectre.Console.Rendering.Segment(text).CellCount();

    private static int SafeWindowHeight()
    {
        try
        {
            return Console.WindowHeight;
        }
        catch (IOException)
        {
            return 24;
        }
    }

    /// <summary>Blanks the notice and prompt rows and leaves the cursor where the notice starts.</summary>
    private static void ClearRegionUnsafe()
    {
        var width = Math.Max(1, Console.BufferWidth - 1);
        var top = Math.Max(0, _inputTop - _noticeRows);
        for (var row = top; row <= _inputTop; row++)
        {
            SetCursorPositionSafe(0, row);
            Console.Write(new string(' ', width));
        }

        SetCursorPositionSafe(0, top);
        _inputTop = top;
        _noticeRows = 0;
    }

    /// <summary>Draws the notice from the cursor row down, then moves <see cref="_inputTop"/> below it.</summary>
    private static void DrawNoticeUnsafe()
    {
        if (_notice is null) return;

        var width = NoticeWidth();
        var lines = BuildNoticeLinesUnsafe(_notice);
        var previous = Console.ForegroundColor;
        foreach (var (text, color) in lines)
        {
            Console.ForegroundColor = color;
            // Padded short of the last column, so no terminal wraps the row on its own.
            Console.WriteLine(text + new string(' ', Math.Max(0, width - CellWidth(text))));
        }

        Console.ForegroundColor = previous;
        // WriteLine may have scrolled the buffer at the bottom; the cursor row is the truth.
        _inputTop = Console.CursorTop;
        _noticeRows = lines.Count;
    }

    private static void DismissNoticeUnsafe()
    {
        if (_notice is null) return;
        _notice = null;
        RedrawInputUnsafe();
    }

    public static void Clear()
    {
        lock (OutputLock)
        {
            Console.Clear();
            _noticeRows = 0;
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
            _noticeRows = 0;
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
                        if (_notice is not null)
                        {
                            DismissNoticeUnsafe();
                        }
                        else if (!string.IsNullOrEmpty(_inlineCompletionSuffix))
                        {
                            _inlineCompletionSuffix = string.Empty;
                            RedrawInputUnsafe();
                        }

                        break;
                    case ConsoleKey.Enter:
                    {
                        var result = InputBuffer.ToString();
                        // The next command's output starts where the notice was; the notice has been read.
                        _notice = null;
                        ClearRegionUnsafe();
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
        var input = InputBuffer.ToString().TrimStart();
        if (input.Length == 0)
        {
            _inlineCompletionSuffix = string.Empty;
            return;
        }

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
            // Logs go above the notice and prompt, which are redrawn below them.
            if (_isReadingInput) ClearRegionUnsafe();

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

        ClearRegionUnsafe();
        UpdateInlineCompletion();
        DrawNoticeUnsafe();

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
