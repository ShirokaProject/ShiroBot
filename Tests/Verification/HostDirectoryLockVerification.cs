using System.Diagnostics;
using ShiroBot.Hosting.Runtime;

internal static class HostDirectoryLockVerification
{
    internal static async Task RunAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "ShiroBot.Verification", Guid.NewGuid().ToString("N"));
        var other = Path.Combine(root, "other");
        Directory.CreateDirectory(root);
        try
        {
            using (var held = HostDirectoryLock.TryAcquire(root)
                ?? throw new InvalidOperationException("First directory lock was rejected."))
            {
                await AssertProbe(root, 73);
                await AssertProbe(other, 0);
            }
            // A persistent lock file must never prevent a subsequent process from starting.
            if (!File.Exists(Path.Combine(root, "cache", "host.lock")))
                throw new InvalidOperationException("Directory lock removed its stable lock file.");
            await AssertProbe(root, 0);

            using var child = StartProbe(root, hold: true);
            try
            {
                var ready = await child.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(20));
                if (ready != "LOCKED") throw new InvalidOperationException("Child failed to acquire directory lock.");
                await AssertProbe(root, 73);
                // Simulates abrupt termination without Dispose, including SIGKILL on Unix.
                child.Kill();
                await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));
                await AssertProbe(root, 0);
            }
            finally
            {
                if (!child.HasExited) { child.Kill(); await child.WaitForExitAsync(); }
            }
            Console.WriteLine("Host directory lock cross-process exclusion, independent directories and crash recovery verification passed.");
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static Process StartProbe(string directory, bool hold = false)
    {
        var executable = Environment.ProcessPath ?? throw new InvalidOperationException("No process path.");
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        if (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            start.ArgumentList.Add(typeof(HostDirectoryLockVerification).Assembly.Location);
        start.ArgumentList.Add("--directory-lock-probe");
        start.ArgumentList.Add(directory);
        start.ArgumentList.Add(hold ? "hold" : "once");
        return Process.Start(start) ?? throw new InvalidOperationException("Failed to start lock probe.");
    }

    private static async Task AssertProbe(string directory, int expectedExitCode)
    {
        using var probe = StartProbe(directory);
        try
        {
            await probe.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));
            if (probe.ExitCode != expectedExitCode)
                throw new InvalidOperationException($"Lock probe expected {expectedExitCode}, got {probe.ExitCode}: {await probe.StandardError.ReadToEndAsync()}");
        }
        finally
        {
            if (!probe.HasExited) { probe.Kill(); await probe.WaitForExitAsync(); }
        }
    }
}
