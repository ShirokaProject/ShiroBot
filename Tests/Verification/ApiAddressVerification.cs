using System.Reflection;
using System.Text.Json;
using ShiroBot.Configuration;
using ShiroBot.Hosting.Http;

namespace ShiroBot.Verification;

/// <summary>API listen / public addresses are checked before they are saved, so a typo cannot stop the host.</summary>
internal static class ApiAddressVerification
{
    public static void Run()
    {
        foreach (var url in (string[])
                 [
                     "http://127.0.0.1:7001", "http://0.0.0.0:7001", "https://localhost:443/", "http://*:7001",
                     "http://+:80", "http://[::]:7001", "http://bot.example.com"
                 ])
            if (HostHttpServer.NormalizeUrls([url], "监听地址", true).Single() != url)
                throw new Exception($"Rejected valid listen address {url}");

        foreach (var url in (string[])
                 [
                     "asdnjasnj", "http", "127.0.0.1:7001", "ftp://127.0.0.1:21", "http://127.0.0.1:0",
                     "http://127.0.0.1:70000", "http://127.0.0.1:7001/base", "http://",
                     "http://bad host:7001"
                 ])
            Expect.Throws(() => HostHttpServer.NormalizeUrls([url], "监听地址", true), url);

        if (!HostHttpServer.NormalizeUrls(["https://bot.example.com/shiro", " http://a.example.com "], "外部基础 URL", false)
                .SequenceEqual(["https://bot.example.com/shiro", "http://a.example.com"]))
            throw new Exception("Public base URLs were not kept or trimmed");
        foreach (var url in (string[]) ["kjdkjds", "http", "https://bot.example.com/?a=1", "mailto:a@b.c"])
            Expect.Throws(() => HostHttpServer.NormalizeUrls([url], "外部基础 URL", false), url);
        if (HostHttpServer.NormalizeUrls(["", "  ", "http://127.0.0.1:7001", "http://127.0.0.1:7001"], "监听地址", true)
                .Length != 1)
            throw new Exception("Blank or repeated addresses were kept");

        // A rejected address leaves the whole file untouched, including settings sent alongside it
        var root = Directory.CreateTempSubdirectory("shirobot-api-addresses-").FullName;
        try
        {
            var path = Path.Combine(root, "config.toml");
            var manager = new ConfigManager(path);
            manager.SaveConfig(path, new CoreConfig());
            var before = File.ReadAllText(path);
            var error = Expect.Throws(() => Patch(manager, path,
                "{\"showid\":true,\"api\":{\"listen_urls\":[\"http://0.0.0.0:7002\",\"asdnjasnj\"]}}"), "patch");
            if (!error.Contains("asdnjasnj", StringComparison.Ordinal))
                throw new Exception("The error did not name the bad address: " + error);
            if (File.ReadAllText(path) != before) throw new Exception("A rejected address still changed the file");

            Patch(manager, path, "{\"api\":{\"listen_urls\":[\" http://0.0.0.0:7002 \",\"\"],\"public_base_url\":[]}}");
            var saved = File.ReadAllText(path);
            if (!saved.Contains("listen_urls = [\"http://0.0.0.0:7002\"]", StringComparison.Ordinal))
                throw new Exception("A valid address was not saved trimmed: " + saved);
        }
        finally
        {
            Directory.Delete(root, true);
        }

        Console.WriteLine("API address validation verification passed.");
    }

    private static void Patch(ConfigManager manager, string path, string json)
    {
        using var document = JsonDocument.Parse(json);
        typeof(HostHttpServer).GetMethod("ApplyConfigPatch", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, [document.RootElement, new ApiHostConfig { Token = "fixture" }, manager, path]);
    }

    private static class Expect
    {
        public static string Throws(Action action, string what)
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                var inner = exception is TargetInvocationException { InnerException: { } cause } ? cause : exception;
                if (inner is InvalidOperationException) return inner.Message;
                throw;
            }

            throw new Exception($"Accepted invalid address {what}");
        }
    }
}
