using System.Diagnostics;
using System.Reflection;
using System.Text.RegularExpressions;
using ShiroBot.Terminal;

internal static class ConsoleOutputVerification
{
    private static readonly string LongLine = "中文😀 Podman redirected log " + new string('x', 300);

    public static void Probe()
    {
        if (!Console.IsOutputRedirected) throw new Exception("Probe requires redirected stdout.");
        ConsoleOutput.Info(LongLine);
        ConsoleOutput.Warning("multiline first\n多行 second");
        ConsoleOutput.IsEnabled = false;
        ConsoleOutput.Error("disabled output must not appear");
        ConsoleOutput.IsEnabled = true;
        ConsoleOutput.Notice("notice", "summary", "details");
        var rows = (System.Collections.ICollection)typeof(ConsoleOutput)
            .GetField("ScreenRows", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        if (rows.Count != 0) throw new Exception("Redirected logs retained terminal screen rows.");
    }

    public static async Task RunAsync()
    {
        var executable = Environment.ProcessPath ?? throw new Exception("Missing process path.");
        var start = new ProcessStartInfo(executable)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false
        };
        if (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
        start.ArgumentList.Add("--console-output-probe");
        // A bogus terminal width must have no effect on output consumed by container log drivers.
        start.Environment["COLUMNS"] = "20";
        using var process = Process.Start(start)!;
        process.StandardInput.Close();
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await process.WaitForExitAsync(timeout.Token);
        var output = await outputTask;
        var error = await errorTask;
        if (process.ExitCode != 0) throw new Exception($"Redirected console probe failed: {error}");
        var normalized = Regex.Replace(output.Replace("\r\n", "\n"), @"\[\d{2}:\d{2}:\d{2}\] ", "");
        var expected = LongLine + "\nmultiline first\n多行 second\nnotice\nsummary\ndetails\n";
        if (normalized != expected || error.Length != 0)
            throw new Exception($"Redirected output was wrapped, padded, escaped or changed: {output}");
        Console.WriteLine("PASS redirected console: complete UTF-8 lines, original multiline text, no padding/ANSI/screen history, disabled output respected.");
    }
}
