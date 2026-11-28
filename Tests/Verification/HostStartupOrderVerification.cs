using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using ShiroBot.Adapters;
using ShiroBot.Configuration;
using ProbePlugin = ShiroBot.SharedContractPluginProbe.SharedContractPluginProbe;

namespace ShiroBot.Verification;

internal static class HostStartupOrderVerification
{
    public static async Task RunAsync(string? hostAssembly = null)
    {
        hostAssembly ??= typeof(ShiroBot.Program).Assembly.Location;
        await VerifyAsync(hostAssembly, occupiedPort: true);
        await VerifyAsync(hostAssembly, occupiedPort: false);
        Console.WriteLine("PASS host startup order: occupied HTTP port starts no components; HTTP ready -> sequential adapters -> plugins; clean shutdown.");
    }

    private static async Task VerifyAsync(string hostAssembly, bool occupiedPort)
    {
        var root = Path.Combine(Path.GetTempPath(), "shiro-startup-order-" + Guid.NewGuid().ToString("N"));
        var packageDirectory = Path.Combine(root, "adapters", "lifecycle-probe");
        var pluginDirectory = Path.Combine(root, "plugins");
        Directory.CreateDirectory(packageDirectory);
        Directory.CreateDirectory(pluginDirectory);
        var probeAssembly = typeof(ProbePlugin).Assembly.Location;
        File.Copy(probeAssembly, Path.Combine(packageDirectory, "Probe.dll"));
        File.Copy(probeAssembly, Path.Combine(pluginDirectory, Path.GetFileName(probeAssembly)));
        var adapterConfig = Path.Combine(packageDirectory, "config.toml");
        AdapterInstanceStore.Write(adapterConfig, [
            new PackageAdapterInstance { Id = "first", Enabled = true },
            new PackageAdapterInstance { Id = "second", Enabled = true }
        ], true);
        var beforeConfig = File.ReadAllText(adapterConfig);
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var origin = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}";
        if (!occupiedPort) listener.Stop();
        var configPath = Path.Combine(root, "config.toml");
        new ConfigManager(configPath).SaveConfig(configPath, new CoreConfig
        {
            Protocols = ["lifecycle-probe"], DisableConsoleInput = true,
            Logs = new LocalLogConfig { SaveToFile = false },
            Api = new ApiHostConfig { Enable = true, EnableDashboard = false, ListenUrls = [origin], Token = "startup-test" }
        });
        var dotnet = Path.GetFullPath(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "..", "..", "..",
            OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet"));
        var start = new ProcessStartInfo(dotnet)
        {
            WorkingDirectory = root, UseShellExecute = false,
            RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true,
            StandardOutputEncoding = new UTF8Encoding(false), StandardErrorEncoding = new UTF8Encoding(false)
        };
        start.ArgumentList.Add(hostAssembly);
        start.ArgumentList.Add("--config"); start.ArgumentList.Add(configPath);
        start.ArgumentList.Add("--no-console");
        start.Environment["SHIROBOT_HOME"] = root;
        start.Environment["SHIROBOT_LIFECYCLE_DIAGNOSTICS"] = "true";
        start.Environment.Remove("SHIROBOT_WAIT_FOR_PID");
        using var process = Process.Start(start) ?? throw new Exception("Failed to start isolated host.");
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            if (occupiedPort)
            {
                await process.WaitForExitAsync(timeout.Token);
                var text = await output + await errors;
                if (process.ExitCode != 1 || !text.Contains("程序启动失败", StringComparison.Ordinal) ||
                    text.Contains("Adapter load probe:", StringComparison.Ordinal) ||
                    text.Contains("已加载插件:", StringComparison.Ordinal) ||
                    File.ReadAllText(adapterConfig) != beforeConfig)
                    throw new Exception("Occupied HTTP port activated components before failing.");
            }
            else
            {
                using var client = new HttpClient { BaseAddress = new Uri(origin), Timeout = TimeSpan.FromSeconds(2) };
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "startup-test");
                while (true)
                {
                    timeout.Token.ThrowIfCancellationRequested();
                    if (process.HasExited) throw new Exception("Host exited during startup.");
                    try
                    {
                        using var response = await client.GetAsync("/api/v1/plugins/list", timeout.Token);
                        response.EnsureSuccessStatusCode();
                        using var plugins = JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
                        if (plugins.RootElement.GetArrayLength() == 1) break;
                    }
                    catch (HttpRequestException) { }
                    await Task.Delay(50, timeout.Token);
                }
                using var stopped = await client.PostAsync("/api/v1/system/shutdown", null, timeout.Token);
                stopped.EnsureSuccessStatusCode();
                await process.WaitForExitAsync(timeout.Token);
                var text = await output + await errors;
                var http = text.IndexOf("宿主 API 服务已启动", StringComparison.Ordinal);
                var adapters = text.IndexOf("Adapter load probe: begin:", StringComparison.Ordinal);
                var allAdapters = text.IndexOf("已加载 2 个 Adapter", StringComparison.Ordinal);
                var pluginOutput = text.IndexOf("已加载插件:", StringComparison.Ordinal);
                var firstReady = text.IndexOf("Adapter load probe: log registration:", StringComparison.Ordinal);
                var secondBegin = text.IndexOf("Adapter load probe: begin:", adapters + 1, StringComparison.Ordinal);
                var secondReady = text.IndexOf("Adapter load probe: log registration:", firstReady + 1, StringComparison.Ordinal);
                if (process.ExitCode != 0 || http < 0 || adapters <= http || allAdapters <= adapters || pluginOutput <= allAdapters ||
                    firstReady <= adapters || secondBegin <= firstReady || secondReady <= secondBegin || allAdapters <= secondReady)
                    throw new Exception("Host/adapter/plugin startup order changed.");
            }
        }
        catch (Exception error)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            throw new Exception($"Startup test occupiedPort={occupiedPort}: {error.Message}\n{await output}\n{await errors}", error);
        }
        finally
        {
            if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); }
            Directory.Delete(root, recursive: true);
        }
    }
}
