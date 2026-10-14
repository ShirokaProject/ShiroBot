using ShiroBot.Configuration;

internal static class TomlFormattingVerification
{
    internal static void Run()
    {
        foreach (var newline in new[] { "\n", "\r\n" })
        {
            var newlineName = newline == "\n" ? "LF" : "CRLF";
            var root = Path.Combine(Path.GetTempPath(), "ShiroBot.TomlFormatting", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                var path = Path.Combine(root, "config.toml");
                var original = string.Join(newline,
                    "title = \"probe\"", "", "[[providers]]", "name = \"one\"", "weight = 1",
                    "", "# Labels follow", "[labels]", "x = \"1\"", "");
                File.WriteAllText(path, original);
                var manager = new ConfigManager(path);
                var first = new Dictionary<string, object?> { ["name"] = "one", ["weight"] = 1L };
                var second = new Dictionary<string, object?> { ["name"] = "two", ["weight"] = 5L };
                for (var cycle = 0; cycle < 3; cycle++)
                {
                    manager.ReplaceConfigValue(path, "providers", new[] { first, second });
                    var expanded = File.ReadAllText(path);
                    if (expanded.Split("[[providers]]").Length != 3 || !expanded.Contains("# Labels follow"))
                        throw new InvalidOperationException("Replacing TOML tables lost a provider or unrelated comment.");
                    manager.ReplaceConfigValue(path, "providers", new[] { first });
                    if (File.ReadAllText(path) != original)
                        throw new InvalidOperationException($"TOML table round trip changed formatting: newline={newlineName}, cycle={cycle + 1}.");
                }
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }
        Console.WriteLine("TOML table round trips preserve LF/CRLF formatting verification passed.");
    }
}
