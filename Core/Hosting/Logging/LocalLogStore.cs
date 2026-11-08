using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace ShiroBot.Hosting.Logging;

/// <summary>Called under HostLogHub's lock; daily JSONL files, bounded reverse history reads.</summary>
internal sealed class LocalLogStore : IDisposable
{
    internal const long SegmentBytes = 8 * 1024 * 1024;
    private const int MaxLineBytes = 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string _directory;
    private readonly TimeProvider _clock;
    private FileStream? _stream;
    private DateTime _date;
    internal int RetentionDays { get; set; }

    internal LocalLogStore(string directory, int retentionDays, TimeProvider clock)
    {
        _directory = directory;
        _clock = clock;
        RetentionDays = retentionDays;
        Directory.CreateDirectory(directory);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        Cleanup();
    }

    internal void Append(HostLogHub.LogEntry entry)
    {
        var today = _clock.GetLocalNow().Date;
        var date = DateTime.TryParseExact(entry.Time, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var recorded)
            ? recorded.Date : today;
        if (date < today.AddDays(1 - RetentionDays)) return;
        if (_stream is null || _date != date || _stream.Length >= SegmentBytes) OpenWriter(date);
        JsonSerializer.Serialize(_stream!, entry, JsonOptions);
        _stream!.WriteByte((byte)'\n');
        _stream.Flush();
    }

    private void OpenWriter(DateTime today)
    {
        Dispose();
        Cleanup();
        var latest = Files().Where(file => file.Date == today).OrderByDescending(file => file.Name, StringComparer.Ordinal).FirstOrDefault();
        var sequence = latest.Name is null ? 0 : latest.Sequence;
        if (latest.Name is not null && new FileInfo(latest.Path).Length >= SegmentBytes) sequence++;
        var path = Path.Combine(_directory, $"shirobot-{today:yyyyMMdd}-{sequence:D6}.jsonl");
        _stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read, 4096);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        var length = _stream.Length;
        // A crash can leave an incomplete final record. Keep it on its own line so it
        // cannot corrupt the next record; reverse readers skip malformed records.
        if (length > 0)
        {
            _stream.Seek(-1, SeekOrigin.End);
            if (_stream.ReadByte() != '\n') { _stream.Seek(0, SeekOrigin.End); _stream.WriteByte((byte)'\n'); }
        }
        _stream.Seek(0, SeekOrigin.End);
        _date = today;
    }

    internal HostLogHub.LogEntry[] ReadHistory(string source, int tail)
    {
        if (tail == 0) return [];
        var result = new List<HostLogHub.LogEntry>(tail);
        var cutoff = _clock.GetLocalNow().Date.AddDays(1 - RetentionDays);
        foreach (var file in Files().Where(file => file.Date >= cutoff).OrderByDescending(file => file.Name, StringComparer.Ordinal))
        {
            using var stream = new FileStream(file.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var buffer = new byte[8192];
            var line = new List<byte>(256);
            var oversized = false;
            var position = stream.Length;
            while (position > 0)
            {
                var count = (int)Math.Min(buffer.Length, position);
                position -= count;
                stream.Position = position;
                stream.ReadExactly(buffer.AsSpan(0, count));
                for (var i = count - 1; i >= 0; i--)
                {
                    if (buffer[i] == '\n')
                    {
                        if (!oversized && TryReadLine(line, source, out var entry)) result.Add(entry!);
                        line.Clear(); oversized = false;
                        if (result.Count == tail) return Chronological(result);
                    }
                    else if (!oversized)
                    {
                        if (line.Count < MaxLineBytes) line.Add(buffer[i]);
                        else { line.Clear(); oversized = true; }
                    }
                }
            }
            if (!oversized && TryReadLine(line, source, out var first)) result.Add(first!);
            if (result.Count == tail) return Chronological(result);
        }
        return Chronological(result);
    }

    private static bool TryReadLine(List<byte> reversed, string source, out HostLogHub.LogEntry? entry)
    {
        entry = null;
        if (reversed.Count == 0) return false;
        CollectionsMarshal.AsSpan(reversed).Reverse();
        try { entry = JsonSerializer.Deserialize<HostLogHub.LogEntry>(CollectionsMarshal.AsSpan(reversed), JsonOptions); }
        catch (JsonException) { return false; }
        return entry is not null && !string.IsNullOrWhiteSpace(entry.Time) && !string.IsNullOrWhiteSpace(entry.Message) &&
            (source.Equals("all", StringComparison.OrdinalIgnoreCase) || source.Equals(entry.Source, StringComparison.OrdinalIgnoreCase));
    }

    private static HostLogHub.LogEntry[] Chronological(List<HostLogHub.LogEntry> entries)
    {
        entries.Reverse();
        return entries.ToArray();
    }

    internal void Cleanup()
    {
        var cutoff = _clock.GetLocalNow().Date.AddDays(1 - RetentionDays);
        if (_stream is not null && _date < cutoff) Dispose();
        foreach (var file in Files())
            if (file.Date < cutoff) File.Delete(file.Path);
    }

    private IEnumerable<(string Name, string Path, DateTime Date, int Sequence)> Files()
    {
        foreach (var path in Directory.EnumerateFiles(_directory, "shirobot-*.jsonl"))
        {
            var name = Path.GetFileName(path);
            if (name.Length != 30 || name[17] != '-' ||
                !DateTime.TryParseExact(name.AsSpan(9, 8), "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ||
                !int.TryParse(name.AsSpan(18, 6), NumberStyles.None, CultureInfo.InvariantCulture, out var sequence)) continue;
            yield return (name, path, date, sequence);
        }
    }

    public void Dispose()
    {
        try { _stream?.Dispose(); }
        finally { _stream = null; }
    }
}
