using System.Reflection;
using System.Runtime.InteropServices;
using Avalonia.Media.Imaging;
using ShiroBot.AvaloniaSdk;
using ShiroBot.SDK.Plugin;
using ShiroBot.Configuration;
using ShiroBot.Hosting.Context;
using ShiroBot.Integrations.Avalonia;
using ShiroBot.Update;

internal static class CoreConfigReloadVerification
{
    private const string Probe = """
        <Border xmlns="https://github.com/avaloniaui" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                Width="16" Height="16" Background="{DynamicResource ProbeBrush}">
          <Border.Resources>
            <ResourceDictionary>
              <ResourceDictionary.ThemeDictionaries>
                <ResourceDictionary x:Key="Light"><SolidColorBrush x:Key="ProbeBrush" Color="White" /></ResourceDictionary>
                <ResourceDictionary x:Key="Dark"><SolidColorBrush x:Key="ProbeBrush" Color="Black" /></ResourceDictionary>
              </ResourceDictionary.ThemeDictionaries>
            </ResourceDictionary>
          </Border.Resources>
        </Border>
        """;

    public static async Task RunAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "shirobot-config-reload-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "config.toml");
        var manager = new ConfigManager(path);
        var active = new CoreConfig { AvaloniaTheme = "Dark", Api = new ApiHostConfig { Token = "original-token" } };
        manager.SaveConfig(path, active);
        var bot = new BotContext(null, [], [], new WebHostContext("http://127.0.0.1", false));
        Updater.Initialize(() => [], (_, _) => Task.CompletedTask);
        var renderer = AvaloniaIntegration.Initialize(active.AvaloniaTheme);
        try
        {
            using var watcher = new CoreConfigWatcher(path, active, bot);
            await CheckImageAsync(RenderTheme.Auto, false);
            await CheckImageAsync(RenderTheme.Light, true);
            await CheckImageAsync(RenderTheme.Auto, false);

            var updated = await manager.LoadCoreConfig();
            updated.AvaloniaTheme = "Light";
            updated.Showid = true;
            updated.OwnerList = ["qq:owner"];
            updated.AdminList = ["qq:admin"];
            updated.GithubProxy = "https://proxy.example";
            updated.HostUpdateRepository = "example/host";
            updated.Api.Token = "updated-token";
            updated.PluginRoutes.Default = new PluginRouteRuleConfig { Mode = "blacklist", Groups = ["blocked"] };
            // Editors commonly replace rather than modify the watched file.
            var temporaryPath = Path.Combine(root, "next.toml");
            manager.SaveConfig(temporaryPath, updated);
            File.Move(temporaryPath, path, overwrite: true);
            await WaitUntilAsync(() => active.AvaloniaTheme == "Light" && active.Api.Token == "updated-token");
            await CheckImageAsync(RenderTheme.Auto, true);
            await CheckImageAsync(RenderTheme.Dark, false);
            await CheckImageAsync(RenderTheme.Auto, true);
            if (!active.Showid || bot.OwnerList.Single().UserId != "owner" || bot.AdminList.Single().UserId != "admin"
                || active.PluginRoutes.AllowsGroup("test", "blocked") || active.HostUpdateRepository != "example/host")
                throw new InvalidOperationException("Core runtime settings did not reload.");
            var applyProxy = typeof(Updater).GetMethod("ApplyGithubProxy", BindingFlags.NonPublic | BindingFlags.Static)!;
            if ((string)applyProxy.Invoke(null, ["https://github.com/example"])! != "https://proxy.example/https://github.com/example")
                throw new InvalidOperationException("GitHub proxy did not reload.");

            updated.AvaloniaTheme = "Dark";
            updated.GithubProxy = "";
            manager.SaveConfig(path, updated);
            await WaitUntilAsync(() => active.AvaloniaTheme == "Dark" && active.GithubProxy == "");
            await CheckImageAsync(RenderTheme.Auto, false);
            if ((string)applyProxy.Invoke(null, ["https://github.com/example"])! != "https://github.com/example")
                throw new InvalidOperationException("Clearing GitHub proxy did not take effect.");

            Console.WriteLine("Core file reload, Auto theme inheritance, explicit overrides, permissions, routes, API token and GitHub proxy verification passed.");
        }
        finally
        {
            AvaloniaIntegration.Shutdown();
            Directory.Delete(root, recursive: true);
        }

        async Task CheckImageAsync(RenderTheme theme, bool white)
        {
            var png = await renderer.RenderPngAsync(Probe, options: new AxamlRenderOptions(theme));
            using var bitmap = new Bitmap(new MemoryStream(png));
            var pointer = Marshal.AllocHGlobal(4);
            try
            {
                bitmap.CopyPixels(new Avalonia.PixelRect(0, 0, 1, 1), pointer, 4, 4);
                var pixel = new byte[4];
                Marshal.Copy(pointer, pixel, 0, 4);
                if (pixel.Take(3).Any(channel => white ? channel < 240 : channel > 15))
                    throw new InvalidOperationException($"Rendered theme {theme} did not match host setting; RGB={string.Join(',', pixel)}.");
            }
            finally { Marshal.FreeHGlobal(pointer); }
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline) throw new TimeoutException("Core config file change was not applied.");
            await Task.Delay(50);
        }
    }
}
