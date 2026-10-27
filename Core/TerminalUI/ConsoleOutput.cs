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

    // Completions matching the input, floating above the prompt as you type; Esc hides them until the input changes.
    private static List<ConsoleCommandOption> _menu = [];
    private static int _menuIndex;
    private static bool _menuClosed;
    private const int MenuRows = 8;
    private static readonly char Pointer = Console.OutputEncoding.CodePage == 65001 ? '❯' : '>';

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

    /// <summary>
    /// Rows the overlay (notice and completions) covers directly above the prompt row (<see cref="_inputTop"/>).
    /// It is drawn over log rows rather than between them, so the prompt stays where it is.
    /// </summary>
    private static int _overlayRows;

    /// <summary>Whether the overlay sits below the prompt, used when there is no room above (a nearly empty screen).</summary>
    private static bool _overlayBelow;

    /// <summary>
    /// The rows written above the prompt, newest last, exactly as drawn (wrapped here, not by the terminal).
    /// Terminals cannot be read back, so this is what restores the log an overlay covered.
    /// </summary>
    private static readonly List<(string Text, ConsoleColor Color)> ScreenRows = [];
    private const int ScreenRowLimit = 500;

    /// <summary>The terminal's own foreground (black on light themes, white on dark), unlike any fixed ConsoleColor.</summary>
    private const ConsoleColor DefaultColor = (ConsoleColor)(-1);

    /// <summary>
    /// The highlight of the selected completion and of notice titles: magenta, which no log level uses
    /// (logs are grey, cyan, green, yellow and red), so the menu never blends into the log around it.
    /// </summary>
    private const ConsoleColor AccentColor = ConsoleColor.Magenta;

    private static readonly char RuleChar = Console.OutputEncoding.CodePage == 20127 ? '-' : '─';

    private static IEnumerable<(string Text, ConsoleColor Color)[]> BuildNoticeLinesUnsafe(PendingNotice notice) =>
        BuildNoticeTextUnsafe(notice).Select(line => new[] { line });

    private static IReadOnlyList<(string Text, ConsoleColor Color)> BuildNoticeTextUnsafe(PendingNotice notice)
    {
        var width = NoticeWidth();
        var textWidth = width - 2;
        var lines = new List<(string Text, ConsoleColor Color)>
        {
            // Rules above and below set it apart from the log, like a menu floating over it.
            (new string(RuleChar, width), ConsoleColor.DarkGray),
            ("  " + Truncate(notice.Title, textWidth), notice.Error ? ConsoleColor.Red : AccentColor)
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
            var elements = System.Globalization.StringInfo.GetTextElementEnumerator(raw);
            while (elements.MoveNext())
            {
                var ch = elements.GetTextElement();
                var cells = CellWidth(ch);
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

    /// <summary>
    /// Puts back the log rows the overlay covered, from <see cref="ScreenRows"/>; the <paramref name="keep"/> rows
    /// nearest the prompt are left for a new overlay to overwrite, which avoids flicker.
    /// </summary>
    private static void RestoreOverlayUnsafe(int keep = 0)
    {
        for (var distance = keep + 1; distance <= _overlayRows; distance++)
        {
            // Nothing is ever written below the prompt, so rows there are simply blank again.
            var index = ScreenRows.Count - distance;
            if (_overlayBelow) DrawRowUnsafe(_inputTop + distance, []);
            else DrawRowUnsafe(_inputTop - distance, index >= 0 ? [ScreenRows[index]] : []);
        }

        _overlayRows = 0;
    }

    private enum OverlayPlace { Above, Below, PushDown }

    /// <summary>Above the prompt when the visible rows there fit it, else below when those fit, else push down.</summary>
    private static OverlayPlace PlaceOverlay(int rows)
    {
        var top = WindowTopSafe();
        if (_inputTop - top >= rows) return OverlayPlace.Above;
        return top + SafeWindowHeight() - 1 - _inputTop >= rows ? OverlayPlace.Below : OverlayPlace.PushDown;
    }

    // Windows scrolls a buffer taller than the window; elsewhere the window is the buffer and this is 0.
    private static int WindowTopSafe()
    {
        try
        {
            return OperatingSystem.IsWindows() ? Console.WindowTop : 0;
        }
        catch (IOException)
        {
            return 0;
        }
    }

    /// <summary>
    /// Draws the notice and completions over the rows above the prompt. Only when there are too few rows above
    /// (an almost empty screen) does the prompt move, down by blank rows, once.
    /// </summary>
    private static void DrawOverlayUnsafe(List<(string Text, ConsoleColor Color)[]> lines, OverlayPlace place)
    {
        if (lines.Count == 0) return;
        if (place == OverlayPlace.Below)
        {
            for (var i = 0; i < lines.Count; i++) DrawRowUnsafe(_inputTop + 1 + i, lines[i]);
            _overlayRows = lines.Count;
            _overlayBelow = true;
            return;
        }

        _overlayBelow = false;
        if (place == OverlayPlace.PushDown)
        {
            DrawRowUnsafe(_inputTop, []);
            SetCursorPositionSafe(0, _inputTop);
            for (var i = _inputTop; i < lines.Count; i++)
            {
                Console.WriteLine();
                AddScreenRow((string.Empty, ConsoleColor.Gray));
            }

            _inputTop = Console.CursorTop;
        }

        for (var i = 0; i < lines.Count; i++) DrawRowUnsafe(_inputTop - lines.Count + i, lines[i]);
        _overlayRows = lines.Count;
    }

    /// <summary>Notice then completions, read top to bottom; below the prompt the menu's rule comes first.</summary>
    private static List<(string Text, ConsoleColor Color)[]> BuildOverlayLinesUnsafe(bool below)
    {
        var menu = BuildMenuLinesUnsafe().ToList();
        if (below && menu.Count > 0) menu = [menu[^1], .. menu[..^1]];
        var lines = (_notice is null ? [] : BuildNoticeLinesUnsafe(_notice)).Concat(menu).ToList();
        // A blank row parts it from the log above, so a coloured log line does not read as part of the menu.
        if (!below && lines.Count > 0) lines.Insert(0, []);
        // Keep a few log rows visible however tall the overlay would be.
        var limit = Math.Max(1, SafeWindowHeight() - 3);
        return lines.Count > limit ? lines.Skip(lines.Count - limit).ToList() : lines;
    }

    /// <summary>Overwrites one row with the given segments, padded short of the last column so nothing wraps.</summary>
    private static void DrawRowUnsafe(int row, IReadOnlyList<(string Text, ConsoleColor Color)> segments)
    {
        if (row < 0) return;
        var width = NoticeWidth();
        SetCursorPositionSafe(0, row);
        var used = 0;
        foreach (var (text, color) in segments)
        {
            if (color == DefaultColor) Console.ResetColor();
            else Console.ForegroundColor = color;
            Console.Write(text);
            used += CellWidth(text);
        }

        Console.ResetColor();
        if (used < width) Console.Write(new string(' ', width - used));
    }

    private static void AddScreenRow((string Text, ConsoleColor Color) row)
    {
        ScreenRows.Add(row);
        if (ScreenRows.Count > ScreenRowLimit) ScreenRows.RemoveRange(0, ScreenRows.Count - ScreenRowLimit);
    }

    /// <summary>The matching completions, the selected one marked, then a rule with the keys.</summary>
    private static IEnumerable<(string Text, ConsoleColor Color)[]> BuildMenuLinesUnsafe()
    {
        if (_menu.Count == 0) yield break;

        var width = NoticeWidth();
        var start = Math.Clamp(_menuIndex - MenuRows / 2, 0, Math.Max(0, _menu.Count - MenuRows));
        var shown = _menu.Skip(start).Take(MenuRows).ToArray();
        var nameWidth = Math.Min(shown.Max(option => CellWidth(option.Name)), width * 2 / 5);
        var descriptionWidth = Math.Max(0, width - nameWidth - 6);

        for (var i = 0; i < shown.Length; i++)
        {
            var selected = start + i == _menuIndex;
            var name = Truncate(shown[i].Name, nameWidth);
            var head = $"{(selected ? Pointer : ' ')} {name}{new string(' ', nameWidth - CellWidth(name))}    ";
            var description = descriptionWidth > 0 ? Truncate(shown[i].Description, descriptionWidth) : string.Empty;
            yield return
            [
                (head, selected ? AccentColor : DefaultColor),
                (description, selected ? AccentColor : ConsoleColor.DarkGray)
            ];
        }

        var hidden = _menu.Count - shown.Length;
        var hint = hidden > 0 ? $" ↑↓ 选择 · 还有 {hidden} 项 · Enter/Tab 补全 " : " ↑↓ 选择 · Enter/Tab 补全 ";
        if (Pointer == '>') hint = hint.Replace("↑↓", "Up/Down");
        yield return [(new string(RuleChar, Math.Max(0, width - CellWidth(hint) - 2)) + hint + new string(RuleChar, 2), ConsoleColor.DarkGray)];
    }

    /// <summary>
    /// Puts the selected completion in the input. Tab adds a space when longer completions continue it, to go on
    /// to the arguments; Enter does not, so a second Enter runs the command.
    /// </summary>
    private static void AcceptMenuSelectionUnsafe(bool addSpace = true)
    {
        var name = _menu[_menuIndex].Name;
        var continues = addSpace && (_completionOptions ?? []).Any(option =>
            option.Name.StartsWith(name + " ", StringComparison.OrdinalIgnoreCase));
        InputBuffer.Clear().Append(continues ? name + " " : name);
        _cursorIndex = InputBuffer.Length;
        OnInputChangedUnsafe();
    }

    private static void OnInputChangedUnsafe()
    {
        // The candidates (plugins, adapters, pending updates) are gathered per edit, not per log line.
        if (_completionProvider is not null) _completionOptions = _completionProvider();
        _menuIndex = 0;
        _menuClosed = false;
        RedrawInputUnsafe();
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
            _overlayRows = 0;
            ScreenRows.Clear();
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
            _overlayRows = 0;
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
                        if (_menu.Count > 0)
                        {
                            _menuClosed = true;
                            RedrawInputUnsafe();
                        }
                        else if (_notice is not null)
                        {
                            DismissNoticeUnsafe();
                        }
                        else if (!string.IsNullOrEmpty(_inlineCompletionSuffix))
                        {
                            _inlineCompletionSuffix = string.Empty;
                            RedrawInputUnsafe();
                        }

                        break;
                    // Enter takes the highlighted completion first; once the input is that command, it runs it.
                    case ConsoleKey.Enter when _menu.Count > 0 &&
                        !string.Equals(_menu[_menuIndex].Name, InputBuffer.ToString().Trim(), StringComparison.OrdinalIgnoreCase):
                        AcceptMenuSelectionUnsafe(addSpace: false);
                        break;
                    case ConsoleKey.Enter:
                    {
                        var result = InputBuffer.ToString();
                        // The next command's output starts where the notice was; the notice has been read.
                        _notice = null;
                        _menu = [];
                        _menuClosed = false;
                        RestoreOverlayUnsafe();
                        DrawRowUnsafe(_inputTop, []);
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
                            OnInputChangedUnsafe();
                        }

                        break;
                    case ConsoleKey.Delete:
                        if (_cursorIndex < InputBuffer.Length)
                        {
                            InputBuffer.Remove(_cursorIndex, 1);
                            OnInputChangedUnsafe();
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
                    // With completions showing, the arrows choose among them; otherwise they walk the history.
                    case ConsoleKey.UpArrow when _menu.Count > 0:
                        _menuIndex = (_menuIndex - 1 + _menu.Count) % _menu.Count;
                        RedrawInputUnsafe();
                        break;
                    case ConsoleKey.DownArrow when _menu.Count > 0:
                        _menuIndex = (_menuIndex + 1) % _menu.Count;
                        RedrawInputUnsafe();
                        break;
                    case ConsoleKey.UpArrow:
                        NavigateHistory(-1);
                        break;
                    case ConsoleKey.DownArrow:
                        NavigateHistory(1);
                        break;
                    case ConsoleKey.Tab when _menu.Count > 0:
                        AcceptMenuSelectionUnsafe();
                        break;
                    case ConsoleKey.Tab:
                        ApplyCompletion();
                        break;
                    default:
                        if (!char.IsControl(key.KeyChar))
                        {
                            InputBuffer.Insert(_cursorIndex, key.KeyChar);
                            _cursorIndex++;
                            OnInputChangedUnsafe();
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
        _menuClosed = true; // a recalled command is complete; don't cover it with suggestions
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

    /// <summary>Refreshes the completion menu and the grey inline suffix (the selected completion's rest).</summary>
    private static void UpdateInlineCompletion()
    {
        _menu = [];
        _inlineCompletionSuffix = string.Empty;
        var input = InputBuffer.ToString().TrimStart();
        if (input.Length == 0) return;

        var matches = (_completionOptions ?? [])
            .Where(command => command.Name.StartsWith(input, StringComparison.OrdinalIgnoreCase))
            .OrderBy(command => command.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        // Nothing to suggest once the input is exactly the only match.
        if (matches.Count == 1 && matches[0].Name.Length == input.Length) return;
        if (matches.Count == 0) return;

        _menuIndex = Math.Clamp(_menuIndex, 0, matches.Count - 1);
        if (!_menuClosed) _menu = matches;
        var selected = matches[_menuIndex];
        if (input.Length < selected.Name.Length) _inlineCompletionSuffix = selected.Name[input.Length..];
    }

    private static void WriteLine(string message, ConsoleColor color)
    {
        if (!IsEnabled)
        {
            return;
        }

        lock (OutputLock)
        {
            // Logs go where the prompt is; the prompt and overlay are drawn again below them.
            if (_isReadingInput)
            {
                RestoreOverlayUnsafe();
                SetCursorPositionSafe(0, _inputTop);
            }

            var previous = Console.ForegroundColor;
            Console.ForegroundColor = color;
            var width = NoticeWidth();
            // Wrapped here rather than by the terminal, so each screen row is known and can be restored.
            foreach (var row in Wrap($"{Timestamp()} {message}", width))
            {
                Console.WriteLine(row + new string(' ', Math.Max(0, width - CellWidth(row))));
                AddScreenRow((row, color));
            }

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

        UpdateInlineCompletion();
        var place = PlaceOverlay(BuildOverlayLinesUnsafe(below: false).Count);
        var overlay = BuildOverlayLinesUnsafe(place == OverlayPlace.Below);
        // Rows the new overlay covers again on the same side are simply overwritten; the rest are restored.
        var sameSide = place != OverlayPlace.PushDown && _overlayBelow == (place == OverlayPlace.Below);
        RestoreOverlayUnsafe(sameSide ? Math.Min(overlay.Count, _overlayRows) : 0);
        DrawOverlayUnsafe(overlay, place);

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

        // Typed text in the terminal's own colour, the completion's rest dimmed after it, on light and dark themes.
        Console.ResetColor();
        Console.Write(renderedInput);
        if (!string.IsNullOrEmpty(visibleSuffix))
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write(visibleSuffix);
            Console.ResetColor();
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
