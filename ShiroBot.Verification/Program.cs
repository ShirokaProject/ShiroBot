using ShiroBot.Core;
using ShiroBot.Model.Common;
using ShiroBot.SDK.Config;

if (!EventMetadataRegistry.TryGetEventType("group_disband", out var groupDisbandType) ||
    groupDisbandType != typeof(GroupDisbandEvent) ||
    !EventMetadataRegistry.TryGetEventType("message_receive", out var messageReceiveType) ||
    messageReceiveType != typeof(IncomingMessage) ||
    !EventMetadataRegistry.TryGetIncomingMessageType("temp", out var tempMessageType) ||
    tempMessageType != typeof(TempIncomingMessage))
{
    throw new InvalidOperationException("Generated Milky event discriminator registry is incomplete.");
}

var tempRoot = Path.Combine(Path.GetTempPath(), "ShiroBot.Verification", Guid.NewGuid().ToString("N"));
var configPath = Path.Combine(tempRoot, "config.toml");

try
{
    var manager = new ConfigManager(configPath);
    var config = manager.LoadConfig<VerificationConfig>(configPath, "verification")
                 ?? throw new InvalidOperationException("Default TOML generation failed.");
    var generatedToml = File.ReadAllText(configPath);
    AssertBefore(generatedToml, "# Request timeout", "timeout_seconds = 15");
    AssertBefore(generatedToml, "# Options: compact, detailed", "output_mode = \"compact\"");

    manager.SaveConfig(configPath, config);
    manager.SaveConfig(configPath, config);

    var toml = File.ReadAllText(configPath);
    AssertBefore(toml, "# Request timeout", "timeout_seconds = 15");
    AssertBefore(toml, "# Timeout in seconds", "timeout_seconds = 15");
    AssertBefore(toml, "# Range: 1..120", "timeout_seconds = 15");
    AssertBefore(toml, "# Options: compact, detailed", "output_mode = \"compact\"");
    AssertBefore(toml, "# Placeholder: compact", "output_mode = \"compact\"");
    AssertSingle(toml, "# Request timeout");
    AssertSingle(toml, "# Options: compact, detailed");

    _ = manager.LoadConfig<VerificationConfig>(configPath, "verification")
        ?? throw new InvalidOperationException("Generated TOML did not deserialize.");
    Console.WriteLine("Config comment verification passed.");
}
finally
{
    if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true);
}

static void AssertBefore(string text, string comment, string key)
{
    var commentIndex = text.IndexOf(comment, StringComparison.Ordinal);
    var keyIndex = text.IndexOf(key, StringComparison.Ordinal);
    if (commentIndex < 0 || keyIndex < 0 || commentIndex > keyIndex)
    {
        throw new InvalidOperationException($"Expected '{comment}' above '{key}'.\n{text}");
    }
}

static void AssertSingle(string text, string value)
{
    if (text.Split(value, StringSplitOptions.None).Length - 1 != 1)
    {
        throw new InvalidOperationException($"Expected exactly one '{value}' comment.\n{text}");
    }
}

internal sealed class VerificationConfig
{
    [ConfigField("Timeout in seconds", Label = "Request timeout", Min = 1, Max = 120)]
    public int TimeoutSeconds { get; set; } = 15;

    [ConfigField("Output format", Options = ["compact", "detailed"], Placeholder = "compact")]
    public string OutputMode { get; set; } = "compact";
}
