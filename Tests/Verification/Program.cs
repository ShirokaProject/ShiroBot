using System.Reflection;
using System.IO.Compression;
using System.Text.Json;
using ShiroBot.Adapters;
using ShiroBot.Adapters.Compatibility;
using ShiroBot.Configuration;
using ShiroBot.Components.Updates;
using ShiroBot.Update;
using ShiroBot.SDK.Config;
using ShiroBot.Hosting.Context;
using ShiroBot.Hosting.Events;
using ShiroBot.Hosting.Logging;
using ShiroBot.Hosting.Http;
using ShiroBot.Hosting.Runtime;
using ShiroBot.Packages;
using ShiroBot.Plugins;
using ShiroBot.Plugins.Loading;
using ShiroBot.Plugins.Services;
using ShiroBot.SDK.Adapter;
using ShiroBot.SDK.Core;
using ShiroBot.SDK.Models;
using ShiroBot.SDK.Plugin;
using ShiroBot.Model.Discord;
using ShiroBot.Model.QQ;
using ShiroBot.Model.Telegram;
using ShiroBot.Plugins.Compatibility;
using ShiroBot.SharedContractPluginProbe;

[assembly: ShiroBotApiCompatibility("0.9", "0.9")]
if (args is ["--config-reload"])
{
    await CoreConfigReloadVerification.RunAsync();
    return;
}

if (args is ["--avalonia-fonts"])
{
    await AvaloniaFontVerification.RunAsync();
    return;
}

if (args is ["--plugin-migration", var migrationManifest])
{
    PluginMigrationVerification.Run(migrationManifest);
    return;
}

if (args is ["--update-integration", var fixtureDirectory])
{
    await UpdateIntegration.RunAsync(fixtureDirectory);
    return;
}

{
    var deletionRoot = Path.Combine(Path.GetTempPath(), "ShiroBot.Verification", Guid.NewGuid().ToString("N"));
    var pluginRoot = Path.Combine(deletionRoot, "plugins");
    var installed = Path.Combine(pluginRoot, "Sample");
    Directory.CreateDirectory(installed);
    File.WriteAllText(Path.Combine(installed, "Sample.dll"), "old");
    try
    {
        // Deliberately fail a deletion, then retry from the retained marker.
        StagedComponentUpdates.StageDeletion(pluginRoot, "Sample", installed, directory: false);
        var failed = StagedComponentUpdates.ApplyStagedDeletions(pluginRoot);
        if (failed.Failed.Count != 1 || !StagedComponentUpdates.HasStagedDeletion(pluginRoot, "Sample") ||
            StagedComponentUpdates.ApplyStaged(pluginRoot).Applied.Count != 0)
            throw new InvalidOperationException("A failed deferred deletion was discarded or treated as an update.");
        StagedComponentUpdates.StageDeletion(pluginRoot, "Sample", installed, directory: true);
        var retried = StagedComponentUpdates.ApplyStagedDeletions(pluginRoot);
        if (retried.Applied.Count != 1 || Directory.Exists(installed))
            throw new InvalidOperationException("A deferred deletion could not be retried.");
        AssertThrows<InvalidOperationException>(() => StagedComponentUpdates.StageDeletion(pluginRoot, "Outside", deletionRoot, directory: true));
        AssertThrows<InvalidOperationException>(() => StagedComponentUpdates.StageDeletion(pluginRoot, "Staging", Path.Combine(pluginRoot, ".update"), directory: true));
    }
    finally { Directory.Delete(deletionRoot, recursive: true); }
    Console.WriteLine("Deferred plugin deletion retry and path validation verification passed.");
}

{
    var root = Path.Combine(Path.GetTempPath(), "ShiroBot.Verification", Guid.NewGuid().ToString("N"));
    var plugins = Path.Combine(root, "plugins");
    var work = Path.Combine(root, ".tmp", "ShiroBot.Update", Guid.NewGuid().ToString("N"), "extract");
    var temp = Path.Combine(plugins, "Sample", ".tmp");
    Directory.CreateDirectory(work);
    Directory.CreateDirectory(temp);
    File.WriteAllText(Path.Combine(work, "host"), "abandoned");
    foreach (var extension in new[] { "package", "replacement", "backup" })
        File.WriteAllText(Path.Combine(temp, $"Sample.dll.{Guid.NewGuid():N}.{extension}"), "abandoned");
    var userFile = Path.Combine(temp, "user.package");
    File.WriteAllText(userFile, "keep");
    var markers = new[]
    {
        Path.Combine(plugins, "Sample", ".shirobot-package-files"),
        Path.Combine(plugins, "Sample", ".shirobot", "native", ".complete"),
        Path.Combine(plugins, ".update", "Sample", "package.zip")
    };
    foreach (var marker in markers)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(marker)!);
        File.WriteAllText(marker, "keep");
    }
    try
    {
        StartupTempCleanup.Run(root, plugins);
        if (Directory.Exists(Path.Combine(root, ".tmp")) ||
            !Directory.EnumerateFiles(temp).SequenceEqual([userFile]) || markers.Any(path => !File.Exists(path)))
            throw new InvalidOperationException("Startup cleanup removed useful files or retained abandoned updater work.");
        File.Delete(userFile);
        StartupTempCleanup.Run(root, plugins);
        StartupTempCleanup.Run(root, plugins);
        if (Directory.Exists(temp)) throw new InvalidOperationException("Startup cleanup retained an empty temporary directory.");
    }
    finally { Directory.Delete(root, recursive: true); }
    Console.WriteLine("Startup updater temporary file cleanup verification passed.");
}

var serviceRegistry = new PluginServiceRegistry();
using var providerServices = new PluginServiceScope(serviceRegistry, "provider");
using var consumerServices = new PluginServiceScope(serviceRegistry, "consumer");
var verificationService = new VerificationService();
providerServices.RegisterSingleton<IVerificationService>(verificationService);

if (!ReferenceEquals(consumerServices.GetRequiredService<IVerificationService>(), verificationService) ||
    !serviceRegistry.GetConsumers("provider").SequenceEqual(["consumer"]))
{
    throw new InvalidOperationException("Plugin service registration or dependency tracking failed.");
}

consumerServices.Dispose();
if (serviceRegistry.GetConsumers("provider").Count != 0)
{
    throw new InvalidOperationException("Plugin service consumer cleanup failed.");
}

providerServices.Dispose();
if (serviceRegistry.GetService("consumer", typeof(IVerificationService)) is not null)
{
    throw new InvalidOperationException("Plugin service provider cleanup failed.");
}

Console.WriteLine("Plugin service registry verification passed.");

{
    var pluginRoot = Path.Combine(Path.GetTempPath(), "ShiroBot.Verification", Guid.NewGuid().ToString("N"), "plugins");
    var pluginDirectory = Path.Combine(pluginRoot, "SharedContractPluginProbe");
    Directory.CreateDirectory(pluginDirectory);
    try
    {
        var entryPath = Path.Combine(pluginDirectory, "ShiroBot.SharedContractPluginProbe.dll");
        File.Copy(typeof(SharedContractPluginProbe).Assembly.Location, entryPath);
        var entries = PluginManager.EnumeratePluginEntryAssemblies(pluginRoot).ToArray();
        if (entries.Length != 1 || !string.Equals(entries[0], entryPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Plugin entry discovery did not recognize an ID-named directory containing a prefixed DLL.");
        }
    }
    finally
    {
        Directory.Delete(Path.GetDirectoryName(pluginRoot)!, recursive: true);
    }
}
Console.WriteLine("Plugin ID directory discovery verification passed.");

{
    var pluginTomlPath = Path.Combine(Path.GetTempPath(), "ShiroBot.Verification", Guid.NewGuid().ToString("N"), "config.toml");
    Directory.CreateDirectory(Path.GetDirectoryName(pluginTomlPath)!);
    try
    {
        File.WriteAllText(pluginTomlPath, "name = \"a,b#c\"\n[network]\nport = 7021\nvalues = [1, 2]\n[[targets]]\nid = \"first\"\n");
        var config = HostHttpServer.LoadTomlObject(pluginTomlPath);
        if (config["name"] is not "a,b#c" ||
            config["network"] is not Dictionary<string, object?> network ||
            network["port"] is not 7021L ||
            network["values"] is not object?[] values || values.Length != 2 ||
            values[0] is not 1L ||
            config["targets"] is not object?[] targets || targets.Length != 1 ||
            targets[0] is not Dictionary<string, object?> target ||
            target["id"] is not "first")
        {
            throw new InvalidOperationException("Plugin config TOML model did not preserve nested tables, arrays, or string values.");
        }

        using var patch = JsonDocument.Parse("{\"network\":{\"port\":8080,\"enabled\":true}}");
        HostHttpServer.ApplyComponentConfigPatch(
            new ConfigManager(pluginTomlPath), pluginTomlPath, patch.RootElement, Array.Empty<object>());
        var updated = HostHttpServer.LoadTomlObject(pluginTomlPath);
        if (updated["network"] is not Dictionary<string, object?> updatedNetwork ||
            updatedNetwork["port"] is not 8080L || updatedNetwork["enabled"] is not true)
        {
            throw new InvalidOperationException("Plugin config API patch did not update nested TOML values.");
        }
    }
    finally
    {
        Directory.Delete(Path.GetDirectoryName(pluginTomlPath)!, recursive: true);
    }
}
Console.WriteLine("Plugin config TOML model verification passed.");
Console.WriteLine("Plugin config nested patch verification passed.");

{
    var filePath = Path.GetTempFileName();
    try
    {
        var web = new WebHostContext("http://127.0.0.1:7001", true);
        var url = new Uri(web.RegisterFile("JmParser", string.Empty, filePath, contentType: "application/pdf"));
        if (!System.Text.RegularExpressions.Regex.IsMatch(url.AbsolutePath, @"^/plugin/JmParser/[a-z0-9]{4,8}$"))
            throw new InvalidOperationException("Empty file prefix did not preserve owner isolation with a short token.");
        var legacy = new Uri(web.RegisterFile("OtherPlugin", "pdf", filePath));
        if (!legacy.AbsolutePath.StartsWith("/plugin/OtherPlugin/pdf/", StringComparison.Ordinal))
            throw new InvalidOperationException("Existing file route prefixes changed.");
        var request = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        request.Request.Method = "GET";
        request.Request.Path = url.AbsolutePath;
        if (await web.HandleRequest(request) is not Microsoft.AspNetCore.Http.HttpResults.PhysicalFileHttpResult)
            throw new InvalidOperationException("Short file URL did not resolve to the registered file.");
        web.UnregisterOwner("JmParser");
        if (await web.HandleRequest(request) is not Microsoft.AspNetCore.Http.HttpResults.NotFound)
            throw new InvalidOperationException("Short file URL survived owner unload.");
        var expired = new Uri(web.RegisterFile("JmParser", "", filePath, TimeSpan.FromTicks(1)));
        await Task.Delay(10);
        request.Request.Path = expired.AbsolutePath;
        if (await web.HandleRequest(request) is not Microsoft.AspNetCore.Http.HttpResults.NotFound)
            throw new InvalidOperationException("Short file expiration was not enforced.");
    }
    finally { File.Delete(filePath); }
    Console.WriteLine("Short owner-scoped file route, existing prefix, unload and expiration verification passed.");
}

{
    var assembly = typeof(ShiroBot.ExternalConfigPluginProbe.ProbePlugin).Assembly;
    var before = ShiroBot.ExternalConfigModelProbe.Settings.Constructions;
    var metadataSchema = ReadConfigSchema(HostHttpServer.GetComponentConfigSchema(assembly.Location));
    if (ShiroBot.ExternalConfigModelProbe.Settings.Constructions != before)
        throw new InvalidOperationException("External config metadata inspection executed a constructor.");
    var loadedSchema = ReadConfigSchema(HostHttpServer.GetComponentConfigSchema(assembly));
    foreach (var schema in new[] { metadataSchema, loadedSchema })
    {
        if (schema.Count != 2 || schema["cookie"].GetProperty("label").GetString() != "登录凭据" ||
            schema["cookie"].GetProperty("description").GetString() != "完整 Cookie，留空禁用。" ||
            schema["cookie"].GetProperty("type").GetString() != "password" ||
            schema["cookie"].GetProperty("group_id").GetString() != "cookies" ||
            schema["cookie"].GetProperty("group_label").GetString() != "Cookie")
            throw new InvalidOperationException("External config schema lost labels, descriptions, password type or groups.");
    }
    if (metadataSchema["retries"].GetProperty("default_value").GetInt32() != 0 ||
        loadedSchema["retries"].GetProperty("default_value").GetInt32() != 5)
        throw new InvalidOperationException("External config defaults did not respect the loaded/unloaded boundary.");
    Console.WriteLine("External assembly config schema verification passed.");
}

ComponentApiCompatibility.EnsureCompatible("Plugin", "legacy", "0.8", "0.8.0");
ComponentApiCompatibility.EnsureCompatible("Plugin", "current", "0.9", "0.9");
AssertThrows<InvalidOperationException>(() =>
    ComponentApiCompatibility.EnsureCompatible("Plugin", "future", "1.1", "1.1"));
AssertThrows<InvalidOperationException>(() =>
    ComponentApiCompatibility.EnsureCompatible("Plugin", "invalid", "0.9", "0.8"));
AssertThrows<InvalidOperationException>(() =>
    ComponentApiCompatibility.EnsureCompatible("Plugin", "malformed", "preview", "0.8"));
Console.WriteLine("Component API version verification passed.");

{
    var verificationAssembly = typeof(VerificationComponentConfig).Assembly;
    var constructionsBefore = VerificationComponentConfig.Constructions;
    var fileSchema = ReadConfigSchema(HostHttpServer.GetComponentConfigSchema(verificationAssembly.Location));
    if (fileSchema.ContainsKey("internal_value") || fileSchema.ContainsKey("private_value") ||
        fileSchema.ContainsKey("computed_value") || fileSchema.ContainsKey("static_value") ||
        fileSchema.ContainsKey("private_setter"))
        throw new InvalidOperationException("Config schema exposed a non-editable property.");
    var recordFields = fileSchema["record_section"].GetProperty("fields").EnumerateArray()
        .Select(field => field.GetProperty("key").GetString()).ToArray();
    if (!recordFields.SequenceEqual(["value"]))
        throw new InvalidOperationException("Record config schema exposed compiler-generated or internal properties.");
    if (VerificationComponentConfig.Constructions != constructionsBefore)
        throw new InvalidOperationException("Config schema executed code from a component assembly that is not loaded.");
    if (fileSchema["retry_count"].GetProperty("default_value").GetInt64() != 0 ||
        fileSchema["mode"].GetProperty("default_value").GetString() != "safe" ||
        fileSchema["retry_count"].GetProperty("group_id").GetString() != "network" ||
        fileSchema["retry_count"].GetProperty("group_label").GetString() != "Network" ||
        fileSchema["retry_count"].GetProperty("order").GetInt32() != 10 ||
        fileSchema["tags"].GetProperty("type").GetString() != "array")
    {
        throw new InvalidOperationException("Metadata-only config schema did not use explicit or CLR defaults.");
    }

    var loadedSchemaItems = HostHttpServer.GetComponentConfigSchema(verificationAssembly.Location, verificationAssembly);
    var loadedSchema = ReadConfigSchema(loadedSchemaItems);
    if (VerificationComponentConfig.Constructions == constructionsBefore ||
        loadedSchema["retry_count"].GetProperty("default_value").GetInt64() != 3 ||
        loadedSchema["backoff_seconds"].GetProperty("default_value").GetDouble() != 1.5 ||
        loadedSchema["mode"].GetProperty("default_value").GetString() != "safe" ||
        loadedSchema["tags"].GetProperty("default_value").EnumerateArray().Single().GetString() != "a")
    {
        throw new InvalidOperationException("Loaded config schema did not read initialized defaults.");
    }

    var condition = loadedSchema["backoff_seconds"].GetProperty("conditions").EnumerateArray().Single();
    if (condition.GetProperty("effect").GetString() != "visible" ||
        condition.GetProperty("field").GetString() != "retry_count" ||
        condition.GetProperty("operator").GetString() != "gt" ||
        condition.GetProperty("value").GetString() != "0")
    {
        throw new InvalidOperationException("Config field visibility condition was not exposed in the schema.");
    }

    // Uses in-memory metadata, so the schema also works in single-file publishes where Location is empty.
    var coreSchema = ReadConfigSchema(HostHttpServer.GetComponentConfigSchema(typeof(CoreConfig).Assembly));
    if (!coreSchema.ContainsKey("protocols") || !coreSchema.ContainsKey("owner_list") ||
        coreSchema["avalonia_theme"].GetProperty("default_value").GetString() != "Auto")
    {
        throw new InvalidOperationException("Core config schema could not be read from the loaded host assembly.");
    }
    Console.WriteLine("Component config schema verification passed.");

    var schemaRoot = Path.Combine(Path.GetTempPath(), "ShiroBot.Verification", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(schemaRoot);
    try
    {
        var componentTomlPath = Path.Combine(schemaRoot, "config.toml");
        File.WriteAllText(componentTomlPath, "retry_count = 3\nmode = \"safe\"\n");
        var componentManager = new ConfigManager(componentTomlPath);
        using (var validPatch = JsonDocument.Parse("{\"retry_count\":5,\"mode\":\"fast\",\"extra\":\"kept\"}"))
            HostHttpServer.ApplyComponentConfigPatch(componentManager, componentTomlPath, validPatch.RootElement, loadedSchemaItems);
        var patched = HostHttpServer.LoadTomlObject(componentTomlPath);
        if (patched["retry_count"] is not 5L || patched["mode"] is not "fast" || patched["extra"] is not "kept")
            throw new InvalidOperationException("Valid component config patch was not saved.");

        foreach (var invalid in new[]
                 {
                     "{\"retry_count\":11}", "{\"retry_count\":1.5}", "{\"retry_count\":\"5\"}",
                     "{\"mode\":\"turbo\"}", "{\"backoff_seconds\":true}"
                 })
        {
            using var invalidPatch = JsonDocument.Parse(invalid);
            AssertThrows<InvalidOperationException>(() => HostHttpServer.ApplyComponentConfigPatch(
                componentManager, componentTomlPath, invalidPatch.RootElement, loadedSchemaItems));
        }

        var defaultsPath = Path.Combine(schemaRoot, "defaults", "config.toml");
        var generated = new ConfigManager(defaultsPath).LoadConfig<VerificationComponentConfig>(defaultsPath, "verification")
                        ?? throw new InvalidOperationException("Component config defaults were not generated.");
        if (generated.Mode != "safe" || generated.RetryCount != 3 || !generated.Tags.SequenceEqual(["a"]))
            throw new InvalidOperationException("Explicit ConfigField defaults did not override initializers in generated config.");
    }
    finally
    {
        Directory.Delete(schemaRoot, recursive: true);
    }
    Console.WriteLine("Component config patch validation verification passed.");

    var nestedSchema = ReadConfigSchema(loadedSchemaItems);
    var network = nestedSchema["network"];
    var networkFields = network.GetProperty("fields").EnumerateArray().ToDictionary(item => item.GetProperty("key").GetString()!);
    var providers = nestedSchema["providers"];
    var recursiveChild = nestedSchema["recursive"].GetProperty("fields").EnumerateArray()
        .Single(item => item.GetProperty("key").GetString() == "child");
    if (network.GetProperty("type").GetString() != "section" ||
        networkFields["host"].GetProperty("label").GetString() != "Host" ||
        networkFields["host"].GetProperty("default_value").GetString() != "localhost" ||
        networkFields["port"].GetProperty("type").GetString() != "integer" ||
        networkFields["port"].GetProperty("default_value").GetInt64() != 8080 ||
        providers.GetProperty("type").GetString() != "array" ||
        providers.GetProperty("item_type").GetString() != "section" ||
        !providers.GetProperty("item_fields").EnumerateArray().Select(item => item.GetProperty("key").GetString()).SequenceEqual(["name", "weight"]) ||
        nestedSchema["tags"].GetProperty("item_type").GetString() != "string" ||
        nestedSchema["labels"].GetProperty("type").GetString() != "object" ||
        nestedSchema["optional_limit"].GetProperty("type").GetString() != "integer" ||
        recursiveChild.GetProperty("type").GetString() != "section" ||
        recursiveChild.GetProperty("fields").GetArrayLength() != 0)
    {
        throw new InvalidOperationException("Nested config schema did not describe sections, lists, maps or nullable values.");
    }
    Console.WriteLine("Nested component config schema verification passed.");

    var nestedRoot = Path.Combine(Path.GetTempPath(), "ShiroBot.Verification", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(nestedRoot);
    try
    {
        var nestedToml = Path.Combine(nestedRoot, "config.toml");
        const string original = """
            # Component settings
            retry_count = 3
            mode = "safe"

            [network]
            # Where to connect
            host = "a"
            port = 1

            [[providers]]
            name = "one"
            weight = 1

            # Labels follow
            [labels]
            x = "1"

            """;
        File.WriteAllText(nestedToml, original);
        var nestedManager = new ConfigManager(nestedToml);
        void Patch(string json)
        {
            using var document = JsonDocument.Parse(json);
            HostHttpServer.ApplyComponentConfigPatch(nestedManager, nestedToml, document.RootElement, loadedSchemaItems);
        }
        string CurrentConfigJson() => JsonSerializer.Serialize(HostHttpServer.LoadTomlObject(nestedToml));

        Patch(CurrentConfigJson());
        if (File.ReadAllText(nestedToml) != original)
            throw new InvalidOperationException("Saving an unchanged config rewrote the TOML file.");

        Patch("""{"network":{"host":"a","port":2}}""");
        if (File.ReadAllText(nestedToml) != original.Replace("port = 1", "port = 2"))
            throw new InvalidOperationException("A nested field edit changed more than its own line.");

        Patch("""{"providers":[{"name":"one","weight":1},{"name":"two","weight":5}]}""");
        var twoProviders = File.ReadAllText(nestedToml);
        if (twoProviders.Split("[[providers]]").Length != 3 || !twoProviders.Contains("# Labels follow") ||
            !twoProviders.Contains("# Where to connect") ||
            HostHttpServer.LoadTomlObject(nestedToml)["providers"] is not object?[] { Length: 2 })
            throw new InvalidOperationException("Appending to a list of tables did not rewrite [[providers]] in place.");

        Patch("""{"providers":[{"name":"one","weight":1}]}""");
        if (File.ReadAllText(nestedToml) != original.Replace("port = 1", "port = 2"))
            throw new InvalidOperationException("Restoring a list of tables did not reproduce the original text.");

        Patch("""{"providers":[]}""");
        var noProviders = File.ReadAllText(nestedToml);
        if (noProviders.Contains("[[providers]]") || !noProviders.Contains("providers = []") || !noProviders.Contains("# Labels follow"))
            throw new InvalidOperationException("Clearing a list of tables did not remove its [[providers]] blocks.");

        Patch("""{"labels":{"y":"2"}}""");
        if (HostHttpServer.LoadTomlObject(nestedToml)["labels"] is not Dictionary<string, object?> labels ||
            labels.ContainsKey("x") || labels["y"] is not "2")
            throw new InvalidOperationException("Replacing a free-form table kept removed keys.");

        var beforeRejected = File.ReadAllText(nestedToml);
        foreach (var invalid in new[]
                 {
                     """{"network":"oops"}""", """{"network":{"port":"2"}}""", """{"providers":["one"]}""",
                     """{"retry_count":{"value":1}}""", """{"providers":[{"name":"x"}],"retry_count":99}"""
                 })
        {
            AssertThrows<InvalidOperationException>(() => Patch(invalid));
            if (File.ReadAllText(nestedToml) != beforeRejected)
                throw new InvalidOperationException($"A rejected config patch changed the file: {invalid}");
        }
    }
    finally
    {
        Directory.Delete(nestedRoot, recursive: true);
    }
    Console.WriteLine("Nested component config patch verification passed.");
}

{
    var restartAdapter = new ConfigurableVerificationAdapter(ConfigApplyMode.RestartComponent);
    var initial = restartAdapter.CurrentConfigValue;
    var unchanged = await AdapterManager.ApplyAdapterConfigAsync(
        restartAdapter, restartAdapter, initial, new VerificationAdapterConfig { Endpoint = initial.Endpoint });
    if (!ReferenceEquals(unchanged, initial) || restartAdapter.Stops != 0 || restartAdapter.Starts != 0)
        throw new InvalidOperationException("Unchanged adapter config restarted the adapter.");

    var good = new VerificationAdapterConfig { Endpoint = "https://good.invalid/" };
    var applied = await AdapterManager.ApplyAdapterConfigAsync(restartAdapter, restartAdapter, initial, good);
    if (!ReferenceEquals(applied, good) || restartAdapter.Stops != 1 || restartAdapter.Starts != 1 ||
        restartAdapter.CurrentConfigValue.Endpoint != good.Endpoint || !restartAdapter.IsRunning)
    {
        throw new InvalidOperationException("RestartComponent adapter config was not applied with a restart.");
    }

    var bad = new VerificationAdapterConfig { Endpoint = ConfigurableVerificationAdapter.FailingEndpoint };
    await AssertThrowsAsync<InvalidOperationException>(() =>
        AdapterManager.ApplyAdapterConfigAsync(restartAdapter, restartAdapter, good, bad));
    if (restartAdapter.CurrentConfigValue.Endpoint != good.Endpoint || !restartAdapter.IsRunning)
        throw new InvalidOperationException("Failed adapter config was not rolled back to the previous running config.");

    var liveAdapter = new ConfigurableVerificationAdapter(ConfigApplyMode.Live);
    await AdapterManager.ApplyAdapterConfigAsync(liveAdapter, liveAdapter, liveAdapter.CurrentConfigValue, good);
    if (liveAdapter.Stops != 0 || liveAdapter.Starts != 0 || liveAdapter.CurrentConfigValue.Endpoint != good.Endpoint)
        throw new InvalidOperationException("Live adapter config was not applied in place.");
}
Console.WriteLine("Adapter config apply and rollback verification passed.");

{
    AssertAssemblyVersion(typeof(IBotPlugin).Assembly, "1.2.0.0");
    AssertAssemblyVersion(typeof(QGroup).Assembly, "1.0.0.0");
    AssertAssemblyVersion(typeof(DiscordUser).Assembly, "1.0.0.0");
    AssertAssemblyVersion(typeof(TelegramUser).Assembly, "1.0.0.0");

    var sharedAssemblies = new SharedAssemblyResolver();
    var modelRegistry = new ModelPackageRegistry(sharedAssemblies);
    modelRegistry.RegisterBuiltIn(typeof(DiscordUser).Assembly);
    modelRegistry.RegisterBuiltIn(typeof(QGroup).Assembly);
    modelRegistry.RegisterBuiltIn(typeof(TelegramUser).Assembly);
    if (sharedAssemblies.TryGetRegisteredAssembly("ShiroBot.Model.QQ") != typeof(QGroup).Assembly ||
        sharedAssemblies.TryGetRegisteredAssembly("ShiroBot.Model.QQ.dll") != typeof(QGroup).Assembly ||
        sharedAssemblies.TryGetRegisteredAssembly("ShiroBot.Model.Discord") != typeof(DiscordUser).Assembly ||
        sharedAssemblies.TryGetRegisteredAssembly("ShiroBot.Model.Telegram") != typeof(TelegramUser).Assembly)
    {
        throw new InvalidOperationException("Built-in Model was not available as a registered shared contract.");
    }

    var legacyQqRequest = new AssemblyName(typeof(QGroup).Assembly.FullName!)
    {
        Version = new Version(0, 8, 0, 0)
    };
    AssertThrows<InvalidOperationException>(() => sharedAssemblies.TryResolve(legacyQqRequest));

    var previousQqRequest = new AssemblyName(typeof(QGroup).Assembly.FullName!)
    {
        Version = new Version(0, 9, 0, 0)
    };
    AssertThrows<InvalidOperationException>(() => sharedAssemblies.TryResolve(previousQqRequest));
    if (sharedAssemblies.TryResolve(typeof(QGroup).Assembly.GetName()) != typeof(QGroup).Assembly)
        throw new InvalidOperationException("Matching QQ Model ABI did not resolve.");

    var futureQqRequest = new AssemblyName(typeof(QGroup).Assembly.FullName!)
    {
        Version = new Version(2, 0, 0, 0)
    };
    AssertThrows<InvalidOperationException>(() => sharedAssemblies.TryResolve(futureQqRequest));

    var builtInModels = modelRegistry.GetPackages();
    var expectedModelVersion = typeof(QGroup).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!
        .InformationalVersion.Split('+')[0];
    if (builtInModels.Count != 3 ||
        !builtInModels.All(model => model is
        {
            Source: "built_in",
            Reloadable: false,
            AssemblyPath: null
        } && model.Version == expectedModelVersion) ||
        !builtInModels.Select(model => model.Id).Order().SequenceEqual(
            ["shirobot.model.discord", "shirobot.model.qq", "shirobot.model.telegram"]))
    {
        throw new InvalidOperationException("Built-in Model package metadata is incorrect.");
    }

    Console.WriteLine("Shared contract ABI and built-in Model package verification passed.");
}

{
    var updateRoot = Path.Combine(Path.GetTempPath(), "ShiroBot.Verification", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(updateRoot);
    try
    {
        var executable = Path.Combine(updateRoot, "ShiroBot");
        var replacement = Path.Combine(updateRoot, "replacement");
        File.WriteAllText(executable, "old-host");
        File.WriteAllText(replacement, "new-host");
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(executable, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        HostSelfUpdater.ReplaceExecutable(replacement, executable);
        if (File.ReadAllText(executable) != "new-host" || File.ReadAllText(executable + ".old") != "old-host" ||
            (!OperatingSystem.IsWindows() && (File.GetUnixFileMode(executable) & UnixFileMode.UserExecute) == 0))
            throw new InvalidOperationException("Host executable replacement lost the backup or execute permission.");
        AssertThrows<IOException>(() => HostSelfUpdater.ReplaceExecutable(Path.Combine(updateRoot, "missing"), executable));
        if (File.ReadAllText(executable) != "new-host")
            throw new InvalidOperationException("A failed host replacement did not restore the original executable.");
        var emptyRepository = await HostSelfUpdater.CheckAsync("");
        if (emptyRepository.UpdateAvailable || emptyRepository.CanApply || string.IsNullOrEmpty(emptyRepository.Reason) ||
            HostSelfUpdater.BlockedReason() is null)
            throw new InvalidOperationException("Host update checks allowed an unconfigured repository or a development build to self-update.");
    }
    finally { Directory.Delete(updateRoot, recursive: true); }
    Console.WriteLine("Host executable update and rollback verification passed.");
}

{
    var attempts = 0;
    var request = new PluginUpdateRequest("Verification", "1.0.0", "2.0.0");
    var requestId = await Updater.RequestPluginUpdateAsync(request, token =>
    {
        token.ThrowIfCancellationRequested();
        if (++attempts == 1) throw new IOException("Simulated download failure");
        return Task.CompletedTask;
    });
    try { await Updater.ConfirmUpdateAsync(requestId); throw new InvalidOperationException("A failing update reported success."); }
    catch (IOException) { }
    if (!Updater.GetPendingUpdates().Any(entry => entry.Id == requestId) || !await Updater.ConfirmUpdateAsync(requestId) ||
        attempts != 2 || Updater.GetPendingUpdates().Any(entry => entry.Id == requestId))
        throw new InvalidOperationException("A failed update could not be retried or a successful request stayed pending.");

    using var requestLifetime = new CancellationTokenSource();
    var confirmToken = CancellationToken.None;
    var executionId = await Updater.RequestPluginUpdateAsync(request, token => { confirmToken = token; return Task.CompletedTask; }, requestLifetime.Token);
    requestLifetime.Cancel();
    using var confirmLifetime = new CancellationTokenSource();
    if (!await Updater.ConfirmUpdateAsync(executionId, confirmLifetime.Token) || confirmToken != confirmLifetime.Token)
        throw new InvalidOperationException("Update execution used the expired check token instead of the confirmation token.");
    Console.WriteLine("Pending update retry and cancellation verification passed.");
}

{
    const string owner = "55C88F86C7E7BF9F9F53615CDAAA493F";
    var permissions = new BotContext(new VerificationAdapter("qq"), ["qq:" + owner], ["qq:admin"], new WebHostContext("http://127.0.0.1", false));
    var root = Path.Combine(Path.GetTempPath(), "ShiroBot.OwnerAdmin", Guid.NewGuid().ToString("N"));
    try
    {
        using var context = new PluginContext(permissions, "permission-test", root, new HostLogHub(), new PluginServiceRegistry());
        IBotContext sdk = context;
        if (!permissions.IsOwner(owner) || !permissions.IsAdmin(owner) || !sdk.IsOwner(owner) || !sdk.IsAdmin(owner) ||
            !sdk.IsAdmin("admin") || sdk.IsOwner("admin") || sdk.IsAdmin("other") || sdk.AdminList.Any(entry => entry.UserId == owner))
            throw new InvalidOperationException("Owner must inherit admin permissions through SDK without duplicating the explicit admin list.");
        permissions.UpdateOwnerList(["qq:new-owner"]);
        if (sdk.IsAdmin(owner) || !sdk.IsAdmin("new-owner") || !sdk.IsAdmin("admin"))
            throw new InvalidOperationException("Owner changes did not update effective admin permissions.");
        permissions.UpdateAdminList([]);
        if (sdk.IsAdmin("admin") || !sdk.IsAdmin("new-owner"))
            throw new InvalidOperationException("Updating the admin list must not remove an owner's inherited permissions.");
        permissions.UpdateOwnerList([]);
        if (sdk.IsAdmin("new-owner")) throw new InvalidOperationException("Removed owner retained admin permissions.");
    }
    finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    permissions.RegisterAdapter(new VerificationAdapter("telegram"), "telegram-test");
    permissions.UpdateOwnerList(["qq:" + owner]);
    using (permissions.UseInstance("telegram-test"))
        if (permissions.IsOwner(owner) || permissions.IsAdmin(owner)) throw new InvalidOperationException("Permissions crossed platform instances.");
    permissions.RegisterAdapter(new VerificationAdapter("qq"), "qq-other");
    using (permissions.UseInstance("qq-other"))
        if (permissions.IsAdmin(owner)) throw new InvalidOperationException("Permissions crossed instances on the same platform.");
    permissions.UpdateOwnerList(["qq:" + owner, "qq-other:" + owner]);
    using (permissions.UseInstance("qq-other"))
        if (!permissions.IsOwner(owner)) throw new InvalidOperationException("Explicit cross-instance permission mapping did not work.");
    if (!permissions.IsOwner(new UserReference("qq", owner))) throw new InvalidOperationException("Explicit scoped identity was not recognized.");
    AssertThrows<FormatException>(() => permissions.UpdateOwnerList([owner]));
    Console.WriteLine("Owner/admin SDK inheritance and hot-reload permission verification passed.");
}

var qqAdapter = new VerificationAdapter("qq");
var discordAdapter = new VerificationAdapter("discord");
var botContext = new BotContext(null, [], [], new WebHostContext("http://127.0.0.1", false));
botContext.RegisterAdapter(qqAdapter);
botContext.RegisterAdapter(discordAdapter);

foreach (var legacyProbe in new[] { Environment.GetEnvironmentVariable("SHIROBOT_LEGACY_ABI_PROBE") ?? Path.Combine(AppContext.BaseDirectory, "fixtures/LegacyFixture.dll"), Path.Combine(AppContext.BaseDirectory, "fixtures/indirect/LegacyIndirect.dll") })
{
    var sentinel = Path.Combine(Path.GetTempPath(), "shirobot-legacy-activated-" + Guid.NewGuid().ToString("N"));
    Environment.SetEnvironmentVariable("SHIROBOT_ABI_FIXTURE_SENTINEL", sentinel);
    var loader = new DllLoader<object>(true, new SharedAssemblyResolver());
    try
    {
        try { loader.Load(legacyProbe, "LegacyFixture.Probe"); throw new Exception("Old ABI fixture was accepted."); }
        catch (InvalidOperationException error) when (error.Message.Contains("preflight") && error.Message.Contains("ShiroBot.SDK")) { }
        if (loader.Alc is not null || File.Exists(sentinel)) throw new InvalidOperationException("Legacy component activated before ABI rejection.");
        Console.WriteLine("Real legacy SDK DLL metadata preflight rejection before activation verification passed.");
    }
    finally { Environment.SetEnvironmentVariable("SHIROBOT_ABI_FIXTURE_SENTINEL", null); loader.Unload(); if (File.Exists(sentinel)) File.Delete(sentinel); }
}

{
    var adapter = new VerificationAdapter("qq");
    var context = new BotContext(adapter, [], [], new WebHostContext("http://127.0.0.1", false));
    var user = (VerificationUserService)adapter.User;
    using var cancel = new CancellationTokenSource();
    cancel.Cancel();
    try { await context.User.AcceptFriendRequestAsync("request", cancel.Token); throw new Exception("Canceled accept was executed."); }
    catch (OperationCanceledException) { }
    try { await context.User.RejectFriendRequestAsync("request", "reason", cancel.Token); throw new Exception("Canceled reject was executed."); }
    catch (OperationCanceledException) { }
    if (user.ApprovalRequests != 0 || user.LastToken != cancel.Token) throw new InvalidOperationException("Friend approval cancellation token was not forwarded.");
    using var active = new CancellationTokenSource();
    await context.User.AcceptFriendRequestAsync("request", active.Token);
    await context.User.RejectFriendRequestAsync("request", "reason", active.Token);
    if (user.ApprovalRequests != 2 || user.LastToken != active.Token) throw new InvalidOperationException("Active approval tokens were not forwarded.");
    Console.WriteLine("Friend accept/reject cancellation forwarding and no-request-on-cancel verification passed.");
}

if (Environment.GetEnvironmentVariable("SHIROBOT_QQ_ADAPTER_PROBE") is { Length: > 0 } qqProbePath)
{
    var weakContext = ProbeQQAdapterJson(qqProbePath, out var probeReferences);
    if (!DllLoader<IBotAdapter>.WaitForUnload(weakContext))
        throw new InvalidOperationException("QQ adapter token JSON deserialization pinned its collectible assembly: " +
            string.Join(",", probeReferences.Where(item => item.Value.IsAlive).Select(item => item.Key)));
    Console.WriteLine("QQ adapter JSON serialization collectible-context verification passed.");
}

{
    var root = Path.Combine(Path.GetTempPath(), "ShiroBot.AdapterLifecycle", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    var path = Path.Combine(root, "LifecycleProbe.dll");
    File.Copy(typeof(SharedContractPluginProbe).Assembly.Location, path);
    var resolver = new SharedAssemblyResolver();
    var logs = new HostLogHub();
    var runtime = new HostRuntimeState(DateTimeOffset.UtcNow);
    var context = new BotContext(null, [], [], new WebHostContext("http://127.0.0.1", false), logs);
    var dispatcher = new HostEventDispatcher(new Lock(), context.ReplySubscriptions, runtime, logs);
    var manager = new AdapterManager(root, resolver, new ModelPackageRegistry(resolver), context,
        new AdapterEventBridge(dispatcher), runtime, logs, _ => Task.CompletedTask);
    var lifecycleDumpCaptured = false;
    async Task LifecycleStep(string name, Func<Task> action)
    {
        Console.WriteLine($"Adapter lifecycle probe starting: {name}");
        try { await action().WaitAsync(TimeSpan.FromSeconds(30)); }
        catch (TimeoutException) when (OperatingSystem.IsWindows() &&
            Environment.GetEnvironmentVariable("SHIROBOT_LIFECYCLE_DIAGNOSTICS") == "true")
        {
            if (!lifecycleDumpCaptured)
            {
                lifecycleDumpCaptured = true;
                var dumpPath = Path.Combine(Path.GetTempPath(), "shirobot-adapter-lifecycle.dmp");
                await Diagnose("collect", "--process-id", Environment.ProcessId.ToString(), "--type", "Heap", "--output", dumpPath);
                await Diagnose("analyze", dumpPath, "-c", "clrstack -all", "-c", "dumpasync", "-c", "exit");
                var nativeDebugger = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                    "Windows Kits", "10", "Debuggers", "x64", "cdb.exe");
                if (File.Exists(nativeDebugger))
                {
                    var info = new System.Diagnostics.ProcessStartInfo(nativeDebugger) { UseShellExecute = false };
                    foreach (var argument in new[] { "-z", dumpPath, "-c", "~* kb; q" }) info.ArgumentList.Add(argument);
                    using var process = System.Diagnostics.Process.Start(info)!;
                    try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(45)); }
                    catch (TimeoutException) { process.Kill(entireProcessTree: true); }
                }
            }
            throw;
        }
        Console.WriteLine($"Adapter lifecycle probe finished: {name}");
    }
    static async Task Diagnose(params string[] arguments)
    {
        var info = new System.Diagnostics.ProcessStartInfo("dotnet-dump") { UseShellExecute = false };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        using var process = System.Diagnostics.Process.Start(info)!;
        try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(45)); }
        catch (TimeoutException) { process.Kill(entireProcessTree: true); }
    }
    try
    {
        await LifecycleStep("initial load", () => manager.LoadByIdAsync("lifecycle-probe", path));
        var loadedPath = manager.GetLoadedAssembly("lifecycle-probe")!.Location;
        if (string.Equals(loadedPath, path, StringComparison.OrdinalIgnoreCase) || !File.Exists(loadedPath))
            throw new InvalidOperationException("The active adapter maps the replaceable installed DLL.");

        // Simulate an editor/deployer truncating the installed image while the old version is running.
        File.WriteAllBytes(path, [0, 1, 2, 3]);
        await LifecycleStep("ReloadByIdAsync lifecycle-probe", () => manager.ReloadByIdAsync("lifecycle-probe"));
        if (!manager.IsLoaded || !context.HasAdapter || File.Exists(loadedPath))
            throw new InvalidOperationException("Adapter rollback did not recover its independent old image or clean it up.");
        File.Copy(typeof(SharedContractPluginProbe).Assembly.Location, path, overwrite: true);
        for (var iteration = 0; iteration < 3; iteration++)
        {
            await LifecycleStep("ReloadByIdAsync lifecycle-probe", () => manager.ReloadByIdAsync("lifecycle-probe"));
            if (!manager.IsLoaded || !context.HasAdapter)
                throw new InvalidOperationException("Adapter hot reload lost the active instance.");
        }
        var finalShadow = manager.GetLoadedAssembly("lifecycle-probe")!.Location;
        await LifecycleStep("StopByIdAsync lifecycle-probe", () => manager.StopByIdAsync("lifecycle-probe"));
        if (manager.IsLoaded || context.HasAdapter || manager.GetSnapshot().Any(item => item.RestartRequired) ||
            File.Exists(finalShadow))
            throw new InvalidOperationException("Adapter hot unload retained state or requires a restart.");

        var probeConfigPath = Path.Combine(root, "config.toml");
        AdapterInstanceStore.Write(probeConfigPath,
            [new PackageAdapterInstance { Id = "probe-a", Enabled = true },
             new PackageAdapterInstance { Id = "probe-b", Enabled = true }], replaceLegacy: true);
        await manager.LoadInstanceAsync(new InstalledAdapterInstance("probe-a", "lifecycle-probe", path,
            probeConfigPath, true, "First probe"));
        await manager.LoadInstanceAsync(new InstalledAdapterInstance("probe-b", "lifecycle-probe", path,
            probeConfigPath, true, "Second probe"));
        await LifecycleStep("ReloadByIdAsync probe-a", () => manager.ReloadByIdAsync("probe-a"));
        if (!manager.LoadedIds.Order().SequenceEqual(new[] { "probe-a", "probe-b" }))
            throw new InvalidOperationException("Reloading an adapter instance disrupted another instance of the same package.");
        await LifecycleStep("StopByIdAsync probe-a", () => manager.StopByIdAsync("probe-a"));
        await LifecycleStep("StopByIdAsync probe-b", () => manager.StopByIdAsync("probe-b"));
        Console.WriteLine("Collectible adapter overwrite isolation, rollback, hot reload and unload verification passed.");
    }
    catch
    {
        foreach (var log in logs.GetHistory("system", 100)) Console.WriteLine(log.Message);
        throw;
    }
    finally
    {
        await LifecycleStep("shutdown", () => manager.StopForShutdownAsync());
        Directory.Delete(root, recursive: true);
    }
}

{
    var pluginRoot = Path.Combine(Path.GetTempPath(), "ShiroBot.Verification", Guid.NewGuid().ToString("N"), "plugins");
    var configDirectory = Path.Combine(pluginRoot, "SharedContractPluginProbe");
    Directory.CreateDirectory(configDirectory);
    try
    {
        var renamedDll = Path.Combine(pluginRoot, "test.dll");
        File.Copy(typeof(SharedContractPluginProbe).Assembly.Location, renamedDll);
        var resolver = new SharedAssemblyResolver();
        var pluginManager = new PluginManager(botContext, resolver, new ModelPackageRegistry(resolver),
            new HostRuntimeState(DateTimeOffset.UtcNow), new HostLogHub()) { PluginRootPath = pluginRoot };
        var candidates = pluginManager.ResolvePluginLoadCandidates(pluginRoot, "SharedContractPluginProbe");
        if (candidates.Count != 1 || !string.Equals(candidates[0], renamedDll, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Plugin ID lookup failed when a config-only ID directory exists beside a renamed root DLL.");
        }

        var detail = HostHttpServer.FindPluginListItem(pluginManager, "sharedcontractpluginprobe");
        if (detail is null || detail.Id != "SharedContractPluginProbe" ||
            detail.Name != "Shared contract plugin probe" || detail.Version != "0.9.2" || detail.Enable ||
            HostHttpServer.FindPluginListItem(pluginManager, "missing-plugin") is not null)
        {
            throw new InvalidOperationException("Plugin detail lookup did not return installed plugin metadata or reject a missing plugin.");
        }
        var packageRoot = Path.Combine(Path.GetDirectoryName(pluginRoot)!, "package");
        Directory.CreateDirectory(packageRoot);
        var zipPath = Path.Combine(packageRoot, "plugin.zip");
        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            archive.CreateEntryFromFile(renamedDll, "payload/Probe.dll");
            using (var writer = new StreamWriter(archive.CreateEntry("payload/runtimes/native.bin").Open())) writer.Write("native-v2");
            using (var writer = new StreamWriter(archive.CreateEntry("payload/config.toml").Open())) writer.Write("seed = 1");
        }
        File.WriteAllText(Path.Combine(configDirectory, "config.toml"), "user = 42");
        File.WriteAllText(Path.Combine(configDirectory, "data.json"), "keep");
        var package = PluginUpdateService.PreparePluginUploadPackage(pluginManager, zipPath);
        var dispatcher = new HostEventDispatcher(new Lock(), botContext.ReplySubscriptions,
            new HostRuntimeState(DateTimeOffset.UtcNow), new HostLogHub());
        var replaced = await PluginUpdateService.ReplaceInstalledPluginAsync(pluginManager, dispatcher, new PluginRouteConfig(),
            package, new PluginUpdateService.InstalledPluginInfo(renamedDll, "0.9.1"), null);
        if (replaced.PendingReason is not null || replaced.EntryPath is null || File.Exists(renamedDll) ||
            !File.Exists(Path.Combine(configDirectory, "runtimes", "native.bin")) ||
            File.ReadAllText(Path.Combine(configDirectory, "config.toml")) != "user = 42" ||
            File.ReadAllText(Path.Combine(configDirectory, "data.json")) != "keep")
            throw new InvalidOperationException("The shared plugin updater did not replace the full zip or preserve user files.");
        var dllPackage = PluginUpdateService.PreparePluginUploadPackage(pluginManager, typeof(SharedContractPluginProbe).Assembly.Location);
        var nextReplacement = await PluginUpdateService.ReplaceInstalledPluginAsync(pluginManager, dispatcher, new PluginRouteConfig(),
            dllPackage, new PluginUpdateService.InstalledPluginInfo(replaced.EntryPath, "0.9.2"), null);
        if (nextReplacement.PendingReason is not null || File.Exists(replaced.EntryPath) ||
            File.Exists(Path.Combine(configDirectory, "runtimes", "native.bin")) ||
            File.ReadAllText(Path.Combine(configDirectory, "data.json")) != "keep")
            throw new InvalidOperationException("The shared plugin updater retained removed package files or deleted user data.");
        Console.WriteLine("Shared plugin zip replacement and DLL update verification passed.");
    }
    finally
    {
        Directory.Delete(Path.GetDirectoryName(pluginRoot)!, recursive: true);
    }
}
Console.WriteLine("Renamed plugin ID lookup verification passed.");
Console.WriteLine("Plugin detail lookup verification passed.");

await Task.WhenAll(
    SendInAdapterScopeAsync(qqAdapter, "qq-message"),
    SendInAdapterScopeAsync(discordAdapter, "discord-message"));

if (!qqAdapter.MessageService.Messages.SequenceEqual(["qq-message"]) ||
    !discordAdapter.MessageService.Messages.SequenceEqual(["discord-message"]))
{
    throw new InvalidOperationException("Multi-adapter event scope routed a message to the wrong adapter.");
}

Console.WriteLine("Multi-adapter message routing verification passed.");

{
    var replies = new BotContext(null, [], [], new WebHostContext("http://127.0.0.1", false));
    var milky = new VerificationAdapter("qq");
    var official = new VerificationAdapter("qq-official");
    replies.RegisterAdapter(milky);
    replies.RegisterAdapter(official);
    var messageContext = replies.CreatePluginMessageContext("reply-routing");
    MessageEvent Incoming(string platform, Channel channel) => new()
    {
        Platform = platform, MessageId = "same-message-id", Channel = channel,
        Sender = new User("sender"), Segments = []
    };
    var group = Incoming("qq-official", Channel.Group("same-channel-id"));
    var direct = Incoming("qq", Channel.Direct("same-channel-id"));
    var image = new ImageSegment("https://example.com/a.png");
    milky.MessageService.BeforeSend = () => CheckReplyScopeAsync("qq");
    official.MessageService.BeforeSend = () => CheckReplyScopeAsync("qq-official");
    async Task CheckReplyScopeAsync(string platform)
    {
        await Task.Yield();
        if (AdapterExecutionContext.Current != platform)
            throw new InvalidOperationException("Reply lost its source adapter scope across an await.");
    }

    await messageContext.ReplyAsync(group, "background-reply", image);
    using (replies.UseInstance("qq"))
    {
        await messageContext.ReplyAsync(group, new TextSegment("segments-reply"));
        await messageContext.QuoteReplyAsync(group, "quoted-text", image);
        await messageContext.QuoteReplyAsync(group, new TextSegment("quoted-segments"));
        if (replies.Platform != "qq") throw new InvalidOperationException("Reply changed the caller's adapter scope.");
    }
    await Task.WhenAll(
        messageContext.ReplyAsync(direct, "milky-direct"),
        messageContext.ReplyAsync(group, "official-group"));
    if (!official.MessageService.Messages.SequenceEqual(
            ["background-reply", "segments-reply", "quoted-text", "quoted-segments", "official-group"]) ||
        !milky.MessageService.Messages.SequenceEqual(["milky-direct"]) ||
        official.MessageService.Sent.Any(sent => sent.Channel != group.Channel) ||
        milky.MessageService.Sent.Single().Channel != direct.Channel ||
        !official.MessageService.Sent[0].Segments.SequenceEqual([new TextSegment("background-reply"), image]) ||
        !official.MessageService.Sent[2].Segments.SequenceEqual(
            [new QuoteSegment(group.MessageId), new TextSegment("quoted-text"), image]) ||
        !official.MessageService.Sent[3].Segments.SequenceEqual(
            [new QuoteSegment(group.MessageId), new TextSegment("quoted-segments")]))
        throw new InvalidOperationException("Automatic reply routing changed the target, quote or message segments.");

    try
    {
        await messageContext.ReplyAsync(group with { Platform = "missing" }, "must-not-send");
        throw new InvalidOperationException("Missing source adapter silently used the default adapter.");
    }
    catch (InvalidOperationException ex) when (ex.Message.Contains("not loaded")) { }
    official.MessageService.BeforeSend = () => Task.FromException(new IOException("send failed"));
    using (replies.UseInstance("qq"))
    {
        var failed = await messageContext.QuoteReplyAsync(group, "failed-reply");
        if (failed.IsSuccess || failed.MessageId.Length != 0 || failed.ErrorMessage is null)
            throw new InvalidOperationException("Adapter reply failure was not returned as an explicit failed send.");
        if (replies.Platform != "qq") throw new InvalidOperationException("Failed reply leaked its adapter scope.");
        await messageContext.SendGroupMessageAsync("same-channel-id", "explicit-send");
    }
    if (!milky.MessageService.Messages.SequenceEqual(["milky-direct", "explicit-send"]) ||
        official.MessageService.Messages.Count != 5 || AdapterExecutionContext.Current is not null)
        throw new InvalidOperationException("Reply routing affected later sends or sent through a missing adapter.");
    Console.WriteLine("Automatic source-platform reply and quote routing verification passed.");
}

{
    var logs = new HostLogHub();
    var adapter = new VerificationAdapter("qq-official");
    var context = new BotContext(null, [], [], new WebHostContext("http://127.0.0.1", false), logs);
    context.RegisterAdapter(adapter, "official-main");
    var messages = context.CreatePluginMessageContext("MyParser");
    adapter.MessageService.BeforeSend = () => Task.FromException(new IOException(
        "The path '/demo/base64:/PRIVATE_FILE_CONTENT' is too long."));
    var failed = await messages.SendGroupMessageAsync("group-1", new ImageSegment("base64:PRIVATE_FILE_CONTENT"));
    var error = logs.GetHistory("official-main", 10).Single();
    if (failed.IsSuccess || failed.MessageId != "" || failed.ErrorMessage is null ||
        !failed.ErrorMessage.Contains("official-main") || error.Level != "error" ||
        !error.Message.Contains("caller=MyParser") || !error.Message.Contains("IOException") ||
        error.Message.Contains("PRIVATE_FILE_CONTENT") || logs.GetHistory("MyParser", 10).Length != 0)
        throw new InvalidOperationException("Failed adapter sends were attributed to a plugin or leaked Base64 data.");

    adapter.MessageService.BeforeSend = null;
    var successful = await messages.SendGroupMessageAsync("group-1", "next send");
    if (!successful.IsSuccess || successful.MessageId != "sent" || successful.ErrorMessage is not null)
        throw new InvalidOperationException("Send failure affected a subsequent successful result.");

    adapter.MessageService.BeforeSend = () => Task.FromCanceled(new CancellationToken(canceled: true));
    try
    {
        await messages.SendGroupMessageAsync("group-1", "cancelled");
        throw new InvalidOperationException("Adapter cancellation was swallowed.");
    }
    catch (OperationCanceledException) { }
    if (logs.GetHistory("official-main", 10).Length != 1)
        throw new InvalidOperationException("Cancellation was logged as an adapter fault.");

    foreach (var prefix in new[] { "base64:", "base64://", "BASE64:/" })
    {
        var logger = new ConsoleLogger("[Plugin:MyParser]", logs);
        logger.Error("Upload failed: " + prefix + new string('A', 100000));
        var logged = logs.GetHistory("MyParser", 1).Single();
        if (!logged.Message.Contains("[Base64 内容已省略]") || logged.Message.Length > 100)
            throw new InvalidOperationException("Plugin error logging exposed a Base64 payload.");
    }

    // The default interface implementation throws synchronously, before it returns a Task.
    var unavailable = new BotContext(null, [], [], new WebHostContext("http://127.0.0.1", false), logs);
    var unsupported = await unavailable.Message.SendGroupMessageAsync("group-1", "no adapter");
    if (unsupported.IsSuccess || logs.GetHistory("none", 10).Single().Level != "error")
        throw new InvalidOperationException("Synchronous adapter errors escaped the message boundary.");
    Console.WriteLine("Adapter send error isolation, logging and cancellation verification passed.");
}

{
    var instances = new BotContext(null, [], [], new WebHostContext("http://127.0.0.1", false));
    var first = new VerificationAdapter("qq");
    var second = new VerificationAdapter("qq");
    instances.RegisterAdapter(first, "first-qq", "Main account");
    instances.RegisterAdapter(second, "second-qq");
    AssertThrows<InvalidOperationException>(() => instances.UseInstance("missing"));
    AssertThrows<InvalidOperationException>(() => instances.RegisterAdapter(new VerificationAdapter("discord"), "first-qq"));
    var directory = Path.Combine(Path.GetTempPath(), "ShiroBot.InstanceVerification", Guid.NewGuid().ToString("N"));
    using var plugin = new PluginContext(instances, "instance-routing", directory, new HostLogHub(), new PluginServiceRegistry());
    IBotContext context = plugin;
    var message = new MessageEvent
    {
        Platform = "qq", SelfId = "same-account", InstanceId = "second-qq", MessageId = "same-message",
        Channel = Channel.Direct("same-channel"), Sender = new User("sender"), Segments = []
    };
    var richRequest = new OutgoingMessage { Segments = [new MarkdownSegment("**rich**") { PlainTextFallback = "rich" }], AllowedFallbacks = MessageFallbackOptions.MarkdownAsText };
    var richTarget = new ChannelReference("second-qq", message.Channel);
    var assessment = context.Message.AssessMessage(richTarget, richRequest);
    if (!assessment.IsSupported || assessment.IsNative || first.MessageService.Sent.Count != 0 || second.MessageService.Sent.Count != 0)
        throw new InvalidOperationException("Rich assessment performed a send or lost fallback information.");
    var richResult = await context.Message.SendMessageAsync(richTarget, richRequest);
    if (richResult.Reference!.InstanceId != "second-qq" || richResult.Transformations.Single().Kind != MessageTransformationKind.MarkdownToText || second.MessageService.Messages.Single() != "rich")
        throw new InvalidOperationException("Rich send lost transformations or source routing.");
    using (context.UseInstance("first-qq"))
    {
        var reactions = context.GetAdapterExtension<IMessageReactionService>()!;
        await reactions.SetReactionAsync(message.Reference, new UnicodeReactionEmoji("👍"));
        if (first.MessageService.Reactions.Count != 0 || second.MessageService.Reactions.Single().InstanceId != "second-qq" || context.InstanceId != "first-qq")
            throw new InvalidOperationException("Cached reaction service ignored message source or leaked routing.");
        try { await reactions.SetReactionAsync(message.Reference, new PlatformReactionEmoji("1", "first-qq")); throw new InvalidOperationException("Foreign emoji accepted."); }
        catch (ArgumentException) { }
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        try { await reactions.SetReactionAsync(message.Reference, new UnicodeReactionEmoji("👍"), cancellationToken: canceled.Token); throw new InvalidOperationException("Canceled reaction executed."); }
        catch (OperationCanceledException) { }
        if (second.MessageService.Reactions.Count != 1) throw new InvalidOperationException("Rejected reactions reached adapter.");
        await context.Message.ReplyAsync(message, richRequest);
        if (second.MessageService.Sent.Last().Segments.OfType<QuoteSegment>().Single().MessageId != message.MessageId)
            throw new InvalidOperationException("Rich reply lost message reference.");
    }
    first.MessageService.Messages.Clear(); first.MessageService.Sent.Clear();
    second.MessageService.Messages.Clear(); second.MessageService.Sent.Clear();
    var interactionRoutes = new EventRouter();
    var typedClicks = 0; var platformClicks = 0;
    interactionRoutes.Map<InteractionEvent>(_ => { typedClicks++; return Task.CompletedTask; });
    interactionRoutes.MapPlatform("button", _ => { platformClicks++; return Task.CompletedTask; });
    await interactionRoutes.DispatchAsync(new InteractionEvent { Platform = "qq", SelfId = "bot", InstanceId = "second-qq", Channel = message.Channel, Kind = "button", InteractionId = "click", User = new User("user") });
    if (typedClicks != 1 || platformClicks != 1) throw new InvalidOperationException("Typed interaction lost platform event compatibility.");
    Console.WriteLine("Rich fallback assessment, send and reply routing, reaction source and cancellation verification passed.");
    async Task InInstanceAsync(string id, string text)
    {
        using var scope = context.UseInstance(id);
        await Task.Yield();
        if (context.InstanceId != id || context.Platform != "qq")
            throw new InvalidOperationException("Concurrent instance selection leaked across async calls.");
        await context.Message.SendDirectMessageAsync("same-channel", text);
    }
    await Task.WhenAll(InInstanceAsync("first-qq", "first-send"), InInstanceAsync("second-qq", "second-send"));
    var loadedInstances = context.GetAdapterInstances();
    if (loadedInstances.Select(item => item.Id).SequenceEqual(["first-qq", "second-qq"]) is false ||
        loadedInstances[0] is not { Name: "Main account", PackageId: "verification", Platform: "qq" } ||
        loadedInstances[1].Name != "second-qq")
        throw new InvalidOperationException("Plugins cannot list loaded adapter instances with their package and names.");
    using (context.UseInstance("second-qq"))
        if (context.AdapterInstance?.Id != "second-qq") throw new InvalidOperationException("The current adapter instance does not follow UseInstance.");
    await context.Message.ReplyAsync(message, "background-second-reply");
    using (context.UseInstance("first-qq"))
    {
        await context.Message.QuoteReplyAsync(message, "second-quote");
        if (context.InstanceId != "first-qq") throw new InvalidOperationException("Reply did not restore the original instance.");
    }
    try { await context.Message.ReplyAsync(message with { InstanceId = null }, "ambiguous"); throw new Exception("Ambiguous reply was sent."); }
    catch (InvalidOperationException) { }

    var firstReplies = 0;
    var secondReplies = 0;
    using (context.UseInstance("first-qq"))
        context.Message.SubscribeReply(new MessageReference(context.InstanceId!, message.Channel, "same-message"), TimeSpan.FromMinutes(1), _ => { firstReplies++; return Task.CompletedTask; });
    using (context.UseInstance("second-qq"))
        context.Message.SubscribeReply(new MessageReference(context.InstanceId!, message.Channel, "same-message"), TimeSpan.FromMinutes(1), _ => { secondReplies++; return Task.CompletedTask; });
    await instances.ReplySubscriptions.PublishAsync(message with { Channel = Channel.Group("other-group"), Segments = [new QuoteSegment("same-message")] });
    if (firstReplies != 0 || secondReplies != 0) throw new InvalidOperationException("Reply subscription crossed channels.");
    await instances.ReplySubscriptions.PublishAsync(message with { Segments = [new QuoteSegment("same-message")] });
    if (firstReplies != 0 || secondReplies != 1) throw new InvalidOperationException("Reply subscription crossed adapter instances.");

    // Query results come from adapters without InstanceId (or with forged metadata).
    second.MessageService.QueryResult = message with { InstanceId = null, Platform = "untrusted-query-platform" };
    var queried = await context.Message.GetMessageAsync(new MessageReference("second-qq", message.Channel, message.MessageId));
    if (queried is not { InstanceId: "second-qq", Platform: "qq" } || queried.Reference.InstanceId != "second-qq")
        throw new InvalidOperationException("Queried message did not receive its captured source identity.");
    await context.Message.DeleteMessageAsync(queried);
    await context.Message.ReplyAsync(queried, "queried-second-reply");
    IReadOnlyList<MessageEvent> history;
    using (context.UseInstance("second-qq")) history = await context.Message.GetHistoryMessagesAsync(message.Channel);
    var historical = history.Single();
    if (historical.Reference.InstanceId != "second-qq") throw new InvalidOperationException("History message lost its source identity.");
    await context.Message.DeleteMessageAsync(historical.Reference);
    await context.Message.ReplyAsync(historical, "history-second-reply");
    if (first.MessageService.Deleted.Count != 0 || second.MessageService.Deleted.Count != 2)
        throw new InvalidOperationException("Queried-message deletion routed to a different instance.");
    second.MessageService.QueryResult = null;
    if (await context.Message.GetMessageAsync(new MessageReference("second-qq", message.Channel, "missing")) is not null)
        throw new InvalidOperationException("Missing message query did not preserve null.");

    // The default changes during the await; the completed query must retain its original registration.
    var transient = new VerificationAdapter("qq");
    var remaining = new VerificationAdapter("qq");
    var changing = new BotContext(null, [], [], new WebHostContext("http://127.0.0.1", false));
    changing.RegisterAdapter(transient, "query-source");
    changing.RegisterAdapter(remaining, "new-default");
    transient.MessageService.QueryResult = message with { InstanceId = "forged-instance" };
    transient.MessageService.BeforeQuery = async () => { await Task.Yield(); changing.UnregisterAdapter(transient); };
    var captured = await changing.Message.GetMessageAsync(message.Channel, message.MessageId);
    if (captured?.Reference.InstanceId != "query-source" || changing.InstanceId != "new-default")
        throw new InvalidOperationException("Query source was captured after awaiting instead of before.");
    Console.WriteLine("Single/history query source stamping, reference deletion/reply and default-instance change verification passed.");

    var dispatcher = new HostEventDispatcher(new Lock(), instances.ReplySubscriptions,
        new HostRuntimeState(DateTimeOffset.UtcNow), new HostLogHub());
    var bridge = new AdapterEventBridge(dispatcher);
    MessageEvent? received = null;
    await using (var subscription = bridge.Bridge("first-qq", "qq", first.Event, async incoming =>
    {
        received = incoming;
        if (context.InstanceId != "first-qq") throw new InvalidOperationException("Event scope did not select its source instance.");
        await context.Message.ReplyAsync(incoming, "first-event-reply");
        await instances.ReplySubscriptions.PublishAsync(incoming);
    }))
    {
        await ((VerificationEventService)first.Event).RaiseAsync(message with
        {
            Platform = "forged", InstanceId = "second-qq", Segments = [new QuoteSegment("same-message")]
        });
    }
    if (received is not { InstanceId: "first-qq", Platform: "qq", SelfId: "same-account" } || firstReplies != 1 || secondReplies != 1)
        throw new InvalidOperationException("Event ingress trusted adapter-supplied identity or crossed reply subscriptions.");
    instances.UnregisterAdapter(second);
    try { await context.Message.ReplyAsync(message, "must-not-fallback"); throw new Exception("Unloaded source fell back to another instance."); }
    catch (InvalidOperationException) { }
    using (context.UseInstance("first-qq")) await context.Message.SendDirectMessageAsync("same-channel", "single-instance-send");
    if (!first.MessageService.Messages.SequenceEqual(["first-send", "first-event-reply", "single-instance-send"]) ||
        !second.MessageService.Messages.SequenceEqual(["second-send", "background-second-reply", "second-quote", "queried-second-reply", "history-second-reply"]) ||
        AdapterExecutionContext.Current is not null)
        throw new InvalidOperationException("Identical-platform/account instances routed messages incorrectly.");
    using (context.UseInstance("first-qq"))
    {
        instances.UnregisterAdapter(first);
        await AssertMissingBoundInstanceAsync();
    }
    async Task AssertMissingBoundInstanceAsync()
    {
        try { await context.Message.SendDirectMessageAsync("same-channel", "removed"); throw new Exception("Stale scope sent a message."); }
        catch (InvalidOperationException) { }
    }
    Directory.Delete(directory, recursive: true);
    Console.WriteLine("Same-platform/account instance routing, event identity and reply isolation verification passed.");
}

var explicitPlatformDirectory = Path.Combine(
    Path.GetTempPath(),
    "ShiroBot.Verification",
    Guid.NewGuid().ToString("N"));
var explicitPlatformContext = new PluginContext(
    botContext,
    "platform-selection",
    explicitPlatformDirectory,
    new HostLogHub(),
    new PluginServiceRegistry());
using (explicitPlatformContext.UseInstance("discord"))
{
    await explicitPlatformContext.Message.SendMessageAsync(
        Channel.Group("channel"),
        [new TextSegment("explicit-discord")]);
}

AssertThrows<InvalidOperationException>(() => explicitPlatformContext.UseInstance("missing"));
explicitPlatformContext.Dispose();
if (Directory.Exists(explicitPlatformDirectory)) Directory.Delete(explicitPlatformDirectory, recursive: true);
if (!discordAdapter.MessageService.Messages.Contains("explicit-discord") ||
    qqAdapter.MessageService.Messages.Contains("explicit-discord"))
{
    throw new InvalidOperationException("Explicit adapter instance selection routed to the wrong adapter.");
}

Console.WriteLine("Explicit adapter instance selection verification passed.");

var namedGroupMessage = new MessageEvent
{
    Platform = "verification",
    MessageId = "showid-test",
    Channel = Channel.Group("123") with { Name = "测试群" },
    Sender = new User("456") { Name = "用户名" },
    Member = new Member(new User("456")) { Nick = "群名片" },
    Segments = [new TextSegment("hello")]
};
if (HostEventDispatcher.Describe(namedGroupMessage) != "测试群 群名片发送: hello" ||
    HostEventDispatcher.Describe(namedGroupMessage, true) != "测试群(123) 群名片(456)发送: hello" ||
    HostEventDispatcher.Describe(namedGroupMessage with { Channel = Channel.Direct("456") }, true) != "用户名(456)发送: hello" ||
    HostEventDispatcher.Describe(namedGroupMessage with { Channel = Channel.Group("123"), Sender = new User("456"), Member = null }, true) != "123 456发送: hello")
    throw new InvalidOperationException("showid log formatting failed for group, direct, nick or missing-name messages.");
Console.WriteLine("Message log showid verification passed.");

var tempRoot = Path.Combine(Path.GetTempPath(), "ShiroBot.Verification", Guid.NewGuid().ToString("N"));
var configPath = Path.Combine(tempRoot, "config.toml");

try
{
    var adapterPackageRoot = Path.Combine(tempRoot, "adapters");
    var adapterWorkRoot = Path.Combine(tempRoot, "adapter-work");
    Directory.CreateDirectory(adapterWorkRoot);
    var adapterPackage = new AdapterPackageManager(adapterPackageRoot);
    var adapterDll = typeof(VerificationAdapter).Assembly.Location;
    var dllProbe = adapterPackage.Prepare(adapterDll, adapterWorkRoot);
    var firstInstall = adapterPackage.Install(dllProbe, enabled: true);
    if (adapterPackage.Get("verification") is not { Enabled: true } || !File.Exists(firstInstall.AssemblyPath))
        throw new InvalidOperationException("Adapter DLL install verification failed.");

    var metadataCacheRoot = Path.Combine(tempRoot, "cache", "adapters");
    var packageDirectory = Path.GetDirectoryName(firstInstall.AssemblyPath)!;
    if (File.Exists(Path.Combine(packageDirectory, "adapter.json")) || !Directory.EnumerateFiles(metadataCacheRoot, "*.json").Any())
        throw new InvalidOperationException("Adapter metadata was not stored exclusively in the host cache.");
    var originalConfig = File.ReadAllText(Path.Combine(packageDirectory, "config.toml"));
    Directory.Delete(metadataCacheRoot, recursive: true);
    if (new AdapterPackageManager(adapterPackageRoot).Get("verification") is not { Enabled: true } ||
        originalConfig != File.ReadAllText(Path.Combine(packageDirectory, "config.toml")))
        throw new InvalidOperationException("Clearing adapter metadata cache lost package identity, switches or configuration.");
    var metadataCacheFile = Directory.EnumerateFiles(metadataCacheRoot, "*.json").Single();
    File.WriteAllText(metadataCacheFile, "invalid json");
    File.WriteAllText(Path.Combine(packageDirectory, "adapter.json"), "{\"Id\":\"wrong\",\"Entry\":\"missing.dll\"}");
    if (adapterPackage.Get("verification") is null || File.Exists(Path.Combine(packageDirectory, "adapter.json")))
        throw new InvalidOperationException("Corrupt cache or obsolete package manifest prevented DLL-based discovery.");
    var nestedEntry = Path.Combine(packageDirectory, "nested", "adapter.dll");
    Directory.CreateDirectory(Path.GetDirectoryName(nestedEntry)!);
    File.Move(firstInstall.AssemblyPath, nestedEntry);
    if (adapterPackage.Get("verification")?.AssemblyPath != nestedEntry)
        throw new InvalidOperationException("Adapter cache kept a stale entry path after a DLL move.");
    var duplicateEntry = Path.Combine(packageDirectory, "duplicate.dll");
    File.Copy(nestedEntry, duplicateEntry);
    if (adapterPackage.Get("verification") is not null)
        throw new InvalidOperationException("Adapter discovery accepted ambiguous entry DLLs through stale cache.");
    File.Delete(duplicateEntry);
    Directory.Delete(metadataCacheRoot, recursive: true);
    File.WriteAllText(metadataCacheRoot, "Cache directory temporarily unavailable");
    try
    {
        if (adapterPackage.Get("verification")?.AssemblyPath != nestedEntry)
            throw new InvalidOperationException("Adapter loading required a writable metadata cache.");
    }
    finally { File.Delete(metadataCacheRoot); }
    File.Move(nestedEntry, firstInstall.AssemblyPath);
    Directory.Delete(Path.GetDirectoryName(nestedEntry)!);
    Console.WriteLine("Adapter metadata cache reconstruction, invalidation and unavailable-cache verification passed.");

    // New packages do not create implicit bots; all instances share the package config.
    if (adapterPackage.ListInstances().Count != 0) throw new InvalidOperationException("Fresh installation created a default instance.");
    var externalCorePath = Path.Combine(tempRoot, "data", "custom.toml");
    Directory.CreateDirectory(Path.GetDirectoryName(externalCorePath)!);
    File.WriteAllText(externalCorePath, "# Core settings remain unchanged\nprotocols = []\n");
    var externalPackages = new AdapterPackageManager(adapterPackageRoot, externalCorePath);
    externalPackages.CreateInstance("verification", "external-instance", null);
    externalPackages.CreateInstance("verification", "second-instance", null);
    externalPackages.SetInstanceEnabled("external-instance", true);
    var first = externalPackages.GetInstance("external-instance")!;
    var second = externalPackages.GetInstance("second-instance")!;
    if (first.ConfigPath != second.ConfigPath || !first.Enabled || second.Enabled)
        throw new InvalidOperationException("Package instances are not in one config with independent enabled flags.");
    var firstContext = ConfigContext.ForAdapter(first.ConfigPath, first.Id);
    var secondContext = ConfigContext.ForAdapter(second.ConfigPath, second.Id);
    firstContext.Save(new VerificationComponentConfig { Mode = "first account" });
    secondContext.Save(new VerificationComponentConfig { Mode = "second account" });
    if (firstContext.Load<VerificationComponentConfig>().Mode != "first account" || secondContext.Load<VerificationComponentConfig>().Mode != "second account")
        throw new InvalidOperationException("SDK configuration escaped its instance scope.");
    AssertThrows<InvalidOperationException>(() => externalPackages.CreateInstance("verification", "EXTERNAL-INSTANCE", null));
    if (first.PackageEnabled || first.Active) throw new InvalidOperationException("Empty protocols must disable packages.");
    File.WriteAllText(Path.Combine(Path.GetDirectoryName(first.ConfigPath)!, ".shirobot-adapter-state.json"), "{\"Enabled\":true}");
    if (externalPackages.GetInstance(first.Id)!.Active) throw new InvalidOperationException("Legacy package state was not ignored.");
    File.WriteAllText(externalCorePath, "# Preserve unrelated settings\nprotocols = [\"OtherAdapter\"]\nfuture_value = 42\n");
    externalPackages.SetPackageEnabled("verification", true);
    externalPackages.SetPackageEnabled("VERIFICATION", true);
    var enabledCore = File.ReadAllText(externalCorePath);
    AssertContains(enabledCore, "OtherAdapter");
    AssertContains(enabledCore, "future_value = 42");
    AssertSingle(enabledCore, "\"verification\"");
    if (!new AdapterPackageManager(adapterPackageRoot, externalCorePath).GetInstance(first.Id)!.Active)
        throw new InvalidOperationException("Restart did not restore protocols and instance switches.");
    if (!externalPackages.GetInstance(first.Id)!.Active) throw new InvalidOperationException("Protocols did not enable the instance.");
    externalPackages.SetPackageEnabled("verification", false);
    var gated = externalPackages.GetInstance(first.Id)!;
    if (gated.PackageEnabled || gated.Active || !gated.Enabled)
        throw new InvalidOperationException("The package master switch did not gate its instances without touching their own switches.");
    if (firstContext.Load<VerificationComponentConfig>().Mode != "first account" || externalPackages.ListInstances().Count != 2)
        throw new InvalidOperationException("Turning the package off disturbed instance configuration.");
    externalPackages.SetPackageEnabled("verification", true);
    if (!externalPackages.GetInstance(first.Id)!.Active) throw new InvalidOperationException("Turning the package back on did not restore its instances.");
    AssertThrows<InvalidOperationException>(() => externalPackages.UpdateInstance(first.Id, "SECOND-instance", null));
    var renamed = externalPackages.UpdateInstance(first.Id, "renamed-instance", "Main account");
    if (externalPackages.GetInstance(first.Id) is not null || renamed.Name != "Main account" || !renamed.Enabled ||
        ConfigContext.ForAdapter(renamed.ConfigPath, renamed.Id).Load<VerificationComponentConfig>().Mode != "first account")
        throw new InvalidOperationException("Renaming an instance lost its switch or configuration.");
    first = externalPackages.UpdateInstance(renamed.Id, first.Id, null);
    if (first.Name != "Main account") throw new InvalidOperationException("Renaming the ID alone changed the display name.");
    externalPackages.DeleteInstance(first.Id);
    externalPackages.DeleteInstance(second.Id);
    if (externalPackages.ListInstances().Count != 0 || externalPackages.List().Count != 1 || File.ReadAllText(externalCorePath).Contains("adapter_instances"))
        throw new InvalidOperationException("Instance mutations removed the shared DLL or wrote to core config.");

    // An older config without [[instances]] is one implicit instance and stays untouched until a second one is added.
    var packageConfigPath = first.ConfigPath;
    var declaredConfig = File.ReadAllText(packageConfigPath);
    const string legacyConfig = "# kept as-is\nmode = \"legacy account\"\n";
    File.WriteAllText(packageConfigPath, legacyConfig);
    var implicitPackages = new AdapterPackageManager(adapterPackageRoot, externalCorePath);
    var implicitInstance = implicitPackages.ListInstances().SingleOrDefault();
    if (implicitInstance is not { Implicit: true, Id: "verification" } ||
        ConfigContext.ForAdapter(implicitInstance.ConfigPath, implicitInstance.Id).Load<VerificationComponentConfig>().Mode != "legacy account")
        throw new InvalidOperationException("An undeclared config was not read as one implicit instance.");
    var loadedConfig = File.ReadAllText(packageConfigPath); // Loading fills in defaults, as for any legacy config.
    implicitPackages.SetInstanceEnabled(implicitInstance.Id, !implicitInstance.Enabled);
    implicitPackages.SetPackageEnabled("verification", false);
    implicitPackages.SetPackageEnabled("verification", true);
    if (!File.ReadAllText(packageConfigPath).Contains("enabled = true") || !loadedConfig.StartsWith(legacyConfig) || implicitPackages.GetInstance("verification")!.Enabled == implicitInstance.Enabled)
        throw new InvalidOperationException("Switches rewrote an implicit config or did not persist.");
    implicitPackages.SetInstanceEnabled(implicitInstance.Id, implicitInstance.Enabled);
    implicitPackages.CreateInstance("verification", "second-account", null);
    var converted = implicitPackages.ListInstances();
    if (converted.Count != 2 || converted.Any(item => item.Implicit) || !File.Exists(packageConfigPath + ".pre-instances.bak") ||
        AdapterInstanceStore.GetConfig(packageConfigPath, "verification")["mode"]?.ToString() != "legacy account")
        throw new InvalidOperationException("Adding a second instance did not convert the implicit config with its settings.");
    File.WriteAllText(packageConfigPath, declaredConfig);
    File.Delete(packageConfigPath + ".pre-instances.bak");

    // Instance configs are written as readable sections with UTF-8 text, not one inline table of \u escapes.
    var readablePath = Path.Combine(adapterWorkRoot, "readable-instances.toml");
    AdapterInstanceStore.Write(readablePath, [new PackageAdapterInstance
    {
        Id = "cn-bot", Name = "格瑞普的机器人 \"引号\"", Enabled = true,
        Config = new Dictionary<string, object?>
        {
            ["app_id"] = "102605036",
            ["sandbox"] = new Dictionary<string, object?> { ["enabled"] = true },
            ["routes"] = new List<object?> { new Dictionary<string, object?> { ["group"] = "群<1>" } }
        }
    }], replaceLegacy: true);
    var readableText = File.ReadAllText(readablePath);
    var readable = AdapterInstanceStore.Read(readablePath).Single();
    if (readableText.Contains("\\u") || readableText.Contains("config = {") || !readableText.Contains("格瑞普的机器人") ||
        !readableText.Contains("[instances.config]") || !readableText.Contains("[instances.config.sandbox]") ||
        !readableText.Contains("[[instances.config.routes]]") || readable.Name != "格瑞普的机器人 \"引号\"" ||
        readable.Config["app_id"]?.ToString() != "102605036" ||
        (readable.Config["sandbox"] as Dictionary<string, object?>)?["enabled"] is not true ||
        ((readable.Config["routes"] as List<object?>)?.SingleOrDefault() as Dictionary<string, object?>)?["group"]?.ToString() != "群<1>")
        throw new InvalidOperationException("Instance config was not written as readable TOML sections:\n" + readableText);

    var zipPath = Path.Combine(adapterWorkRoot, "adapter.zip");
    using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        archive.CreateEntryFromFile(adapterDll, "adapter.dll");
    var zipProbe = adapterPackage.Prepare(zipPath, adapterWorkRoot);
    var replacement = adapterPackage.Install(zipProbe, enabled: false);
    if (adapterPackage.Get("verification") is not { Enabled: false } || !File.Exists(replacement.AssemblyPath))
        throw new InvalidOperationException("Adapter ZIP replacement verification failed.");

    var installedConfig = Path.Combine(adapterPackageRoot, "verification", "config.toml");
    File.WriteAllText(installedConfig, "user_value = true");
    var packageWithDefaultConfig = Path.Combine(adapterWorkRoot, "adapter-with-config.zip");
    using (var archive = ZipFile.Open(packageWithDefaultConfig, ZipArchiveMode.Create))
    {
        archive.CreateEntryFromFile(adapterDll, "adapter.dll");
        using var writer = new StreamWriter(archive.CreateEntry("config.toml").Open());
        writer.Write("user_value = false");
    }
    var configProbe = adapterPackage.Prepare(packageWithDefaultConfig, Path.Combine(adapterWorkRoot, "config-preview"));
    adapterPackage.Install(configProbe, enabled: true);
    if (AdapterInstanceStore.GetConfig(installedConfig, "verification")["user_value"] is not true)
        throw new InvalidOperationException("Adapter replacement overwrote user config.toml.");

    AssertThrows<InvalidOperationException>(() => adapterPackage.Uninstall("."));
    if (!Directory.Exists(adapterPackageRoot))
        throw new InvalidOperationException("Adapter root was deleted by invalid uninstall ID.");

    adapterPackage.SetEnabled("verification", true);
    var restoreCalled = false;
    await AssertThrowsAsync<InvalidOperationException>(() => adapterPackage.InstallAndActivateAsync(
        zipProbe,
        enabled: true,
        _ => throw new InvalidOperationException("simulated new adapter startup failure"),
        _ =>
        {
            restoreCalled = true;
            return Task.CompletedTask;
        }));
    if (!restoreCalled || adapterPackage.Get("verification") is not { Enabled: true })
        throw new InvalidOperationException("Adapter activation failure did not restore the prior package.");

    // A fresh install that cannot start yet (e.g. missing credentials) is kept disabled so it can be configured.
    adapterPackage.Uninstall("verification");
    var freshInstall = await adapterPackage.InstallAndActivateAsync(
        zipProbe,
        enabled: true,
        installed =>
        {
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(installed.AssemblyPath)!, "config.toml"), "app_id = \"\"");
            throw new InvalidOperationException("simulated missing credentials");
        });
    if (freshInstall.StartError != "simulated missing credentials" || freshInstall.Package.Enabled ||
        adapterPackage.Get("verification") is not { Enabled: false } keptPackage ||
        !File.Exists(keptPackage.AssemblyPath) ||
        !File.Exists(Path.Combine(Path.GetDirectoryName(keptPackage.AssemblyPath)!, "config.toml")))
        throw new InvalidOperationException("A fresh adapter install that failed to start was not kept disabled for configuration.");
    if (AdapterMarketplaceCache.MarketplaceUrl != "https://raw.githubusercontent.com/ShirokaProject/awesome-shirobot/automation/refresh-marketplace/dist/adapters.v1.json")
        throw new InvalidOperationException("Adapter marketplace URL contract changed.");

    var traversalZip = Path.Combine(adapterWorkRoot, "traversal.zip");
    using (var archive = ZipFile.Open(traversalZip, ZipArchiveMode.Create))
        archive.CreateEntry("../escape.dll");
    AssertThrows<InvalidOperationException>(() => adapterPackage.Prepare(traversalZip, Path.Combine(tempRoot, "traversal-work")));
    Console.WriteLine("Adapter package ZIP, traversal, config preservation, and replacement verification passed.");

    // Staged updates: an adapter that cannot be released gets its new package at the next start.
    adapterPackage.StageUpdate(zipProbe, enabled: true);
    if (!adapterPackage.HasStagedUpdate("verification"))
        throw new InvalidOperationException("Adapter update was not staged.");
    var stagedAdapter = adapterPackage.ApplyStagedUpdates();
    if (!stagedAdapter.Applied.SequenceEqual(["verification"]) || stagedAdapter.Failed.Count != 0 ||
        adapterPackage.HasStagedUpdate("verification") ||
        adapterPackage.Get("verification") is not { Enabled: false } appliedAdapter ||
        File.ReadAllText(Path.Combine(Path.GetDirectoryName(appliedAdapter.AssemblyPath)!, "config.toml")) != "app_id = \"\"")
        throw new InvalidOperationException("Staged adapter update was not applied at startup, or replaced the user's config.toml.");
    adapterPackage.StageUpdate(zipProbe, enabled: true);
    adapterPackage.Uninstall("verification");
    if (adapterPackage.HasStagedUpdate("verification"))
        throw new InvalidOperationException("Uninstalling an adapter kept its staged update.");
    Console.WriteLine("Adapter staged update verification passed.");

    {
        var componentRoot = Path.Combine(tempRoot, "staged-components");
        var installedDir = Path.Combine(componentRoot, "Sample");
        var v1 = Path.Combine(tempRoot, "staged-v1");
        var v2 = Path.Combine(tempRoot, "staged-v2");
        foreach (var dir in new[] { installedDir, v1, Path.Combine(v2, "runtimes") }) Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(v1, "Sample.dll"), "v1");
        File.WriteAllText(Path.Combine(v1, "Old.dll"), "v1-only");
        File.WriteAllText(Path.Combine(v1, "config.toml"), "seed = 1");
        Directory.CreateDirectory(Path.Combine(v1, "cookies"));
        File.WriteAllText(Path.Combine(v1, "cookies", "bilibili.txt"), "seed-cookie");
        StagedComponentUpdates.ApplyPackage(v1, installedDir);
        if (File.ReadAllText(Path.Combine(installedDir, "config.toml")) != "seed = 1")
            throw new InvalidOperationException("A package config.toml did not seed a fresh install.");

        var cookiePath = Path.Combine(installedDir, "cookies", "bilibili.txt");
        if (File.ReadAllText(cookiePath) != "seed-cookie")
            throw new InvalidOperationException("Cookie defaults did not seed a fresh install.");
        File.WriteAllText(cookiePath, "user-cookie");
        // Simulate the ownership list written by hosts that treated credentials as package assets.
        File.AppendAllLines(Path.Combine(installedDir, StagedComponentUpdates.PackageFilesName), ["cookies/bilibili.txt"]);

        // The plugin's own state appears next to the package files.
        File.WriteAllText(Path.Combine(installedDir, "config.toml"), "seed = 2 # user edit");
        Directory.CreateDirectory(Path.Combine(installedDir, "data"));
        File.WriteAllText(Path.Combine(installedDir, "data", "history.jsonl"), "keep");

        File.WriteAllText(Path.Combine(installedDir, "notes.txt"), "user-notes");
        File.WriteAllText(Path.Combine(v2, "notes.txt"), "package-notes");
        File.WriteAllText(Path.Combine(installedDir, "user-tool.dll"), "user-dll");
        File.WriteAllText(Path.Combine(v2, "user-tool.dll"), "package-dll");
        foreach (var name in new[] { "data", "cache", "tmp", "logs" })
        {
            Directory.CreateDirectory(Path.Combine(installedDir, name));
            Directory.CreateDirectory(Path.Combine(v2, name));
            File.WriteAllText(Path.Combine(installedDir, name, "state.json"), "user-state");
            File.WriteAllText(Path.Combine(v2, name, "state.json"), "package-state");
            File.AppendAllLines(Path.Combine(installedDir, StagedComponentUpdates.PackageFilesName), [$"{name}/state.json"]);
        }
        File.WriteAllText(Path.Combine(v2, "Sample.dll"), "v2");
        File.WriteAllText(Path.Combine(v2, "runtimes", "native.so"), "v2-native");
        File.WriteAllText(Path.Combine(v2, "config.toml"), "seed = 1");
        Directory.CreateDirectory(Path.Combine(v2, "cookies"));
        File.WriteAllText(Path.Combine(v2, "cookies", "bilibili.txt"), "");
        StagedComponentUpdates.ApplyPackage(v2, installedDir);
        if (File.ReadAllText(cookiePath) != "user-cookie" ||
            File.ReadAllLines(Path.Combine(installedDir, StagedComponentUpdates.PackageFilesName)).Any(x => x.StartsWith("cookies/", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Package defaults overwrote credentials or retained credential ownership.");
        File.AppendAllLines(Path.Combine(installedDir, StagedComponentUpdates.PackageFilesName), ["cookies/bilibili.txt"]);
        if (File.ReadAllText(Path.Combine(installedDir, "Sample.dll")) != "v2" ||
            File.Exists(Path.Combine(installedDir, "Old.dll")) ||
            !File.Exists(Path.Combine(installedDir, "runtimes", "native.so")) ||
            File.ReadAllText(Path.Combine(installedDir, "config.toml")) != "seed = 2 # user edit" ||
            File.ReadAllText(Path.Combine(installedDir, "data", "history.jsonl")) != "keep")
            throw new InvalidOperationException("Package replacement touched plugin data, config, or kept a dropped file.");

        if (File.ReadAllText(Path.Combine(installedDir, "user-tool.dll")) != "user-dll" ||
            File.ReadAllText(Path.Combine(installedDir, "notes.txt")) != "user-notes" ||
            File.ReadAllLines(Path.Combine(installedDir, StagedComponentUpdates.PackageFilesName)).Contains("notes.txt"))
            throw new InvalidOperationException("Package adopted or overwrote an unowned user file.");
        foreach (var name in new[] { "data", "cache", "tmp", "logs" })
            if (File.ReadAllText(Path.Combine(installedDir, name, "state.json")) != "user-state")
                throw new InvalidOperationException("Package overwrote a reserved runtime directory.");

        // A failing replacement leaves the installed files exactly as they were.
        var v3 = Path.Combine(tempRoot, "staged-v3");
        Directory.CreateDirectory(v3);
        File.WriteAllText(Path.Combine(v3, "Sample.dll"), "v3");
        File.WriteAllText(Path.Combine(v3, "blocked"), "file");
        Directory.CreateDirectory(Path.Combine(installedDir, "blocked"));
        AssertThrows<IOException>(() => StagedComponentUpdates.ApplyPackage(v3, installedDir));
        if (File.ReadAllText(Path.Combine(installedDir, "Sample.dll")) != "v2" ||
            !File.Exists(Path.Combine(installedDir, "runtimes", "native.so")))
            throw new InvalidOperationException("A failed package replacement did not roll back.");
        Directory.Delete(Path.Combine(installedDir, "blocked"));

        // Staged at runtime, applied at the next start, staging removed.
        var staging = StagedComponentUpdates.GetStagingDirectory(componentRoot, "Sample");
        Directory.CreateDirectory(staging);
        File.WriteAllText(Path.Combine(staging, "Sample.dll"), "v4");
        StagedComponentUpdates.WriteTarget(staging, installedDir);
        if (!StagedComponentUpdates.IsStagingPath(componentRoot, Path.Combine(staging, "Sample.dll")))
            throw new InvalidOperationException("Staged package paths were not recognised.");
        // A failed startup attempt must retain its destination and old root DLL for the next restart.
        var legacyEntry = Path.Combine(componentRoot, "legacy.dll");
        File.WriteAllText(legacyEntry, "old-root-entry");
        StagedComponentUpdates.WriteLegacyEntry(staging, legacyEntry);
        File.WriteAllText(Path.Combine(staging, "blocked"), "new-file");
        Directory.CreateDirectory(Path.Combine(installedDir, "blocked"));
        var failedAttempt = StagedComponentUpdates.ApplyStaged(componentRoot);
        if (failedAttempt.Failed.Count != 1 || failedAttempt.Applied.Count != 0 ||
            File.ReadAllText(Path.Combine(installedDir, "Sample.dll")) != "v2" ||
            File.ReadAllText(legacyEntry) != "old-root-entry")
            throw new InvalidOperationException("A failed staged update changed the installed package or legacy entry.");
        Directory.Delete(Path.Combine(installedDir, "blocked"));
        var applied = StagedComponentUpdates.ApplyStaged(componentRoot);
        if (File.Exists(legacyEntry) || !applied.Applied.SequenceEqual(["Sample"]) || applied.Failed.Count != 0 ||
            File.ReadAllText(Path.Combine(installedDir, "Sample.dll")) != "v4" ||
            File.Exists(Path.Combine(installedDir, "runtimes", "native.so")) ||
            File.ReadAllText(Path.Combine(installedDir, "data", "history.jsonl")) != "keep" ||
            File.ReadAllText(Path.Combine(installedDir, "notes.txt")) != "user-notes" ||
            File.ReadAllText(cookiePath) != "user-cookie" ||
            Directory.Exists(Path.Combine(componentRoot, StagedComponentUpdates.DirectoryName)))
            throw new InvalidOperationException("Staged component update was not applied and cleaned up.");
    }
    Console.WriteLine("Staged component package replacement verification passed.");

    var pluginUpdateRoot = Path.Combine(tempRoot, "plugin-update");
    Directory.CreateDirectory(pluginUpdateRoot);
    var targetPluginName = "ShiroBot.Plugin.Example.dll";
    var expectedPluginBytes = new byte[] { 1, 2, 3, 4 };
    var pluginUpdateZip = Path.Combine(pluginUpdateRoot, "plugin.zip");
    using (var archive = ZipFile.Open(pluginUpdateZip, ZipArchiveMode.Create))
    {
        var entry = archive.CreateEntry("nested/" + targetPluginName);
        using (var output = entry.Open()) output.Write(expectedPluginBytes);
        archive.CreateEntry("nested/ShiroBot.SDK.dll");
        archive.CreateEntry("nested/ShiroBot.Model.QQ.dll");
    }
    var extractedPlugin = Path.Combine(pluginUpdateRoot, "extracted.dll");
    ShiroBot.Update.Updater.ExtractPluginEntryFromZip(pluginUpdateZip, extractedPlugin, targetPluginName);
    if (!File.ReadAllBytes(extractedPlugin).SequenceEqual(expectedPluginBytes))
        throw new InvalidOperationException("Plugin ZIP update did not extract the target entry DLL.");

    var renamedPluginZip = Path.Combine(pluginUpdateRoot, "renamed.zip");
    using (var archive = ZipFile.Open(renamedPluginZip, ZipArchiveMode.Create))
    {
        var entry = archive.CreateEntry("ShiroBot.Plugin.Example.dll");
        using var output = entry.Open();
        output.Write(expectedPluginBytes);
    }
    ShiroBot.Update.Updater.ExtractPluginEntryFromZip(renamedPluginZip, extractedPlugin, "test.dll");
    if (!File.ReadAllBytes(extractedPlugin).SequenceEqual(expectedPluginBytes))
        throw new InvalidOperationException("Plugin ZIP update did not handle a locally renamed DLL.");

    var ambiguousPluginZip = Path.Combine(pluginUpdateRoot, "ambiguous.zip");
    using (var archive = ZipFile.Open(ambiguousPluginZip, ZipArchiveMode.Create))
    {
        archive.CreateEntry("First.Plugin.dll");
        archive.CreateEntry("Second.Plugin.dll");
    }
    AssertThrows<InvalidOperationException>(() =>
        ShiroBot.Update.Updater.ExtractPluginEntryFromZip(ambiguousPluginZip, extractedPlugin, targetPluginName));
    Console.WriteLine("Plugin ZIP update extraction verification passed.");

    var eventLogHub = new HostLogHub();
    var eventDispatcher = new HostEventDispatcher(
        new Lock(),
        botContext.ReplySubscriptions,
        new HostRuntimeState(DateTimeOffset.UtcNow),
        eventLogHub);
    var directSubscriber = new DirectSubscriberPlugin();
    var routedPlugin = new RoutedPlugin();
    var directHandle = await CreatePluginHandleAsync(directSubscriber, "direct-subscriber", eventLogHub);
    var routedHandle = await CreatePluginHandleAsync(routedPlugin, "routed-plugin", eventLogHub);
    eventDispatcher.RegisterPlugin(directHandle);
    eventDispatcher.RegisterPlugin(routedHandle);
    eventDispatcher.MarkInitialPluginsReady();

    await eventDispatcher.PublishAsync(new MemberJoinedEvent
    {
        Platform = "verification",
        Channel = Channel.Group("group"),
        UserId = "user"
    });
    await eventDispatcher.PublishAsync(new MessageEvent
    {
        Platform = "verification",
        MessageId = "message",
        Channel = Channel.Group("group"),
        Sender = new User("user"),
        Segments = [new TextSegment("hello")]
    });

    if (directSubscriber.Events.Count != 2 ||
        directSubscriber.Events[0] is not MemberJoinedEvent ||
        directSubscriber.Events[1] is not MessageEvent)
    {
        throw new InvalidOperationException("Direct IBotEventSubscriber did not receive all event types exactly once.");
    }

    if (routedPlugin.BaseEventCount != 2 || routedPlugin.MemberJoinedCount != 1)
    {
        throw new InvalidOperationException("Polymorphic event routing failed or dispatched a plugin more than once.");
    }

    Console.WriteLine("Plugin event compatibility verification passed.");

    eventDispatcher.UnregisterPlugin(directHandle);
    eventDispatcher.UnregisterPlugin(routedHandle);
    var queuedEventService = new VerificationEventService();
    var adapterProbe = AdapterContractProbe.ReadMetadata(typeof(VerificationAdapter).Assembly.Location)
                       ?? throw new InvalidOperationException("Adapter metadata probe failed.");
    if (adapterProbe is not { Id: "verification", MinimumApiVersion: "0.9", MaximumApiVersion: "0.9" })
    {
        throw new InvalidOperationException("Adapter API version metadata was read incorrectly.");
    }

    var eventBridge = new AdapterEventBridge(eventDispatcher);
    var concurrentDispatchPlugin = new ConcurrentDispatchPlugin();
    var concurrentDispatchHandle = await CreatePluginHandleAsync(
        concurrentDispatchPlugin,
        "concurrent-dispatch",
        eventLogHub);
    eventDispatcher.RegisterPlugin(concurrentDispatchHandle);
    var bridgeSubscription = eventBridge.Bridge("verification", "verification", queuedEventService, _ => Task.CompletedTask);
    await queuedEventService.RaiseAsync(CreateMemberJoinedEvent("first"));
    await concurrentDispatchPlugin.FirstDispatchStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
    await queuedEventService.RaiseAsync(CreateMemberJoinedEvent("second"));
    await concurrentDispatchPlugin.SecondDispatchStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
    concurrentDispatchPlugin.ReleaseFirstDispatch.TrySetResult();
    await bridgeSubscription.DisposeAsync();

    Console.WriteLine("Adapter event concurrent dispatch verification passed.");

    var watchContext = new PluginContext(
        botContext,
        "watch-owner",
        Path.Combine(tempRoot, "watch-owner"),
        eventLogHub,
        new PluginServiceRegistry());
    var ownedWatch = (ConfigWatchSubscription)watchContext.Config.Watch<VerificationConfig>(_ => { });
    watchContext.Dispose();
    if (!ownedWatch.IsDisposed)
    {
        throw new InvalidOperationException("Plugin-owned config watcher was not disposed with its context.");
    }

    Console.WriteLine("Plugin config watcher ownership verification passed.");

    var blockedDispatchPlugin = new BlockingDispatchPlugin();
    var blockedDispatchHandle = await CreatePluginHandleAsync(
        blockedDispatchPlugin,
        "blocked-dispatch",
        eventLogHub,
        TimeSpan.FromMilliseconds(50),
        TimeSpan.FromMilliseconds(50));
    var blockedDispatch = blockedDispatchHandle.DispatchAsync<IBlockingDispatchPlugin>(plugin => plugin.DispatchAsync());
    await blockedDispatchPlugin.DispatchStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));

    var dispatchTimeoutResult = await blockedDispatchHandle.UnloadAsync().WaitAsync(TimeSpan.FromSeconds(1));
    AssertUnloadTimedOut(dispatchTimeoutResult, "active plugin dispatches");
    if (blockedDispatchHandle.Supports<IBlockingDispatchPlugin>() ||
        await blockedDispatchHandle.DispatchAsync<IBlockingDispatchPlugin>(_ => Task.CompletedTask))
    {
        throw new InvalidOperationException("Plugin accepted a new dispatch after unload started.");
    }

    blockedDispatchPlugin.ReleaseDispatch.TrySetResult();
    await blockedDispatch.WaitAsync(TimeSpan.FromSeconds(1));
    await blockedDispatchPlugin.UnloadCompleted.Task.WaitAsync(TimeSpan.FromSeconds(1));
    Console.WriteLine("Blocked plugin dispatch unload timeout verification passed.");

    var blockedUnloadPlugin = new BlockingUnloadPlugin();
    var blockedUnloadHandle = await CreatePluginHandleAsync(
        blockedUnloadPlugin,
        "blocked-unload",
        eventLogHub,
        TimeSpan.FromMilliseconds(50),
        TimeSpan.FromMilliseconds(50));
    var unloadTimeoutResult = await blockedUnloadHandle.UnloadAsync().WaitAsync(TimeSpan.FromSeconds(1));
    AssertUnloadTimedOut(unloadTimeoutResult, "plugin OnUnload");
    blockedUnloadPlugin.ReleaseUnload.TrySetResult();
    await blockedUnloadPlugin.UnloadCompleted.Task.WaitAsync(TimeSpan.FromSeconds(1));
    Console.WriteLine("Blocked plugin OnUnload timeout verification passed.");

    var configurablePlugin = new ConfigurableVerificationPlugin();
    var configurableHandle = await CreatePluginHandleAsync(configurablePlugin, "configurable", eventLogHub);
    if (!configurablePlugin.SettingsReadyBeforeLoad || configurablePlugin.CurrentSettings.RetryCount != 3)
        throw new InvalidOperationException("PluginBase<TConfig> did not load settings before LoadAsync.");
    if (!await configurableHandle.ApplyCurrentConfigAsync() || configurablePlugin.Changes.Count != 0)
        throw new InvalidOperationException("Unchanged plugin config triggered a change callback.");

    var pluginConfigPath = configurablePlugin.ConfigPath;
    var pluginConfigManager = new ConfigManager(pluginConfigPath);
    pluginConfigManager.SetConfigValue(pluginConfigPath, "retry_count", 7L);
    await configurableHandle.ApplyCurrentConfigAsync();
    if (configurablePlugin.Changes is not [(3, 7)] || configurablePlugin.CurrentSettings.RetryCount != 7)
        throw new InvalidOperationException("Explicit plugin config apply did not reach OnConfigChangedAsync.");

    // The file watcher sees the same write; the fingerprint must keep it from applying twice.
    await Task.Delay(1200);
    if (configurablePlugin.Changes.Count != 1)
        throw new InvalidOperationException("Plugin config watcher re-applied an unchanged config.");

    pluginConfigManager.SetConfigValue(pluginConfigPath, "retry_count", 9L);
    for (var attempt = 0; attempt < 50 && configurablePlugin.CurrentSettings.RetryCount != 9; attempt++)
        await Task.Delay(100);
    if (configurablePlugin.Changes is not [(3, 7), (7, 9)])
        throw new InvalidOperationException("Plugin config watcher did not hot-reload the changed config.");

    await configurableHandle.UnloadAsync().WaitAsync(TimeSpan.FromSeconds(5));
    Console.WriteLine("Plugin typed config hot reload verification passed.");

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

    var preservingPath = Path.Combine(tempRoot, "preserving", "config.toml");
    Directory.CreateDirectory(Path.GetDirectoryName(preservingPath)!);
    File.WriteAllText(preservingPath, """
        # preserved root comment
        timeout_seconds = 1 # preserved known inline comment
        output_mode = "legacy"
        future_flag = "keep" # unknown top-level

        [retry]
        # preserved nested comment
        count = 2
        future_nested = "keep" # unknown nested key

        [future_section]
        enabled = true # unknown section
        """);

    var saveContext = new PluginContext(
        botContext,
        "config-save",
        Path.GetDirectoryName(preservingPath)!,
        eventLogHub,
        new PluginServiceRegistry());
    var preservingConfig = new VerificationConfig
    {
        TimeoutSeconds = 42,
        OutputMode = "detailed",
        Retry = new VerificationRetryConfig { Count = 7 }
    };
    saveContext.Config.Save(preservingConfig);
    saveContext.Config.Save(preservingConfig);

    var preservedToml = File.ReadAllText(preservingPath);
    AssertContains(preservedToml, "timeout_seconds = 42 # preserved known inline comment");
    AssertContains(preservedToml, "output_mode = \"detailed\"");
    AssertContains(preservedToml, "count = 7");
    AssertContains(preservedToml, "future_flag = \"keep\" # unknown top-level");
    AssertContains(preservedToml, "future_nested = \"keep\" # unknown nested key");
    AssertContains(preservedToml, "[future_section]");
    AssertContains(preservedToml, "enabled = true # unknown section");
    AssertSingle(preservedToml, "# preserved root comment");
    AssertSingle(preservedToml, "# preserved nested comment");
    AssertSingle(preservedToml, "# preserved known inline comment");
    var reloadedPreservingConfig = saveContext.Config.Load<VerificationConfig>();
    if (reloadedPreservingConfig is not { TimeoutSeconds: 42, OutputMode: "detailed", Retry.Count: 7 })
    {
        throw new InvalidOperationException("Preserved plugin TOML did not deserialize with the saved values.");
    }
    saveContext.Dispose();
    Console.WriteLine("Plugin config preserving-save verification passed.");

    var patchPath = Path.Combine(tempRoot, "patch", "config.toml");
    Directory.CreateDirectory(Path.GetDirectoryName(patchPath)!);
    File.WriteAllText(patchPath,
        "# root comment\r\nmessage = \"before # value\" # inline comment\r\n" +
        "tags = [\"#one\", \"two\"] # array comment\r\n" +
        "notes = '''\r\nfirst # line\r\nsecond\r\n''' # multiline comment\r\n\r\n" +
        "[retry]\r\ncount = 2 # nested comment\r\n");
    manager.SetConfigValue(patchPath, "message", "after # value = ok");
    manager.SetConfigValue(patchPath, "tags", new[] { "x#y", "z" });
    manager.SetConfigValue(patchPath, "notes", "done");
    manager.SetConfigValue(patchPath, "new_root", 3);
    manager.SetConfigValue(patchPath, "retry.count", 5);
    manager.SetConfigValue(patchPath, "retry.enabled", true);
    manager.SetConfigValue(patchPath, "new_section.label", "created");
    var patchedToml = File.ReadAllText(patchPath);
    AssertContains(patchedToml, "message = \"after # value = ok\" # inline comment");
    AssertContains(patchedToml, "tags = [\"x#y\", \"z\"] # array comment");
    AssertContains(patchedToml, "notes = \"done\" # multiline comment");
    AssertBefore(patchedToml, "new_root = 3", "[retry]");
    AssertContains(patchedToml, "count = 5 # nested comment");
    AssertContains(patchedToml, "enabled = true");
    AssertContains(patchedToml, "[new_section]");
    AssertContains(patchedToml, "label = \"created\"");
    AssertSingle(patchedToml, "# root comment");
    if (patchedToml.Replace("\r\n", string.Empty).Contains('\n'))
    {
        throw new InvalidOperationException("Config patch changed CRLF line endings.");
    }
    _ = manager.LoadConfig<VerificationConfig>(patchPath, "verification")
        ?? throw new InvalidOperationException("Patched TOML did not deserialize.");
    var defaultMergedToml = File.ReadAllText(patchPath);
    AssertBefore(defaultMergedToml, "# Options: compact, detailed", "output_mode = \"compact\"");
    AssertContains(defaultMergedToml, "message = \"after # value = ok\" # inline comment");
    Console.WriteLine("Config syntax-tree patch verification passed.");

    var coreConfigPath = Path.Combine(tempRoot, "core", "config.toml");
    Directory.CreateDirectory(Path.GetDirectoryName(coreConfigPath)!);
    File.WriteAllText(coreConfigPath, """
        # preserved core comment
        protocols = []
        enable_log = true
        future_core_value = "keep"

        [plugin_routes.default]
        mode = "whitelist"
        groups = ["group-1"]

        [api]
        enable = true
        listen_urls = ["http://127.0.0.1:7001"]
        future_api_value = "keep"

        [future_core_section]
        value = 9
        """);
    var coreManager = new ConfigManager(coreConfigPath);
    var coreConfig = await coreManager.LoadCoreConfig();
    if (coreConfig.Protocols.Length != 0 ||
        !coreConfig.Api.ListenUrls.SequenceEqual(["http://127.0.0.1:7001"]))
    {
        throw new InvalidOperationException("Current core array settings were not loaded.");
    }
    if (!coreConfig.Api.EnableDashboard)
        throw new InvalidOperationException("Existing configs must keep Dashboard enabled by default.");
    coreConfig.Api.EnableDashboard = false;
    coreConfig.EnableLog = false;
    coreConfig.Api.ListenUrls = ["http://127.0.0.1:7999"];
    coreManager.SaveConfig(coreConfigPath, coreConfig);
    coreManager.SaveConfig(coreConfigPath, coreConfig);

    var preservedCoreToml = File.ReadAllText(coreConfigPath);
    AssertContains(preservedCoreToml, "enable_log = false");
    AssertContains(preservedCoreToml, "enable_dashboard = false");
    AssertContains(preservedCoreToml, "protocols = []");
    AssertContains(preservedCoreToml, "listen_urls = [\"http://127.0.0.1:7999\"]");
    AssertSingle(preservedCoreToml, "[plugin_routes.default]");
    AssertSingle(preservedCoreToml, "[api]");
    if (preservedCoreToml.Contains("listen_url =", StringComparison.Ordinal))
    {
        throw new InvalidOperationException("Legacy core keys remained after migration.");
    }
    AssertContains(preservedCoreToml, "future_core_value = \"keep\"");
    AssertContains(preservedCoreToml, "future_api_value = \"keep\"");
    AssertContains(preservedCoreToml, "[future_core_section]");
    AssertContains(preservedCoreToml, "value = 9");
    AssertSingle(preservedCoreToml, "# preserved core comment");
    var reloadedCoreConfig = await coreManager.LoadCoreConfig();
    if (reloadedCoreConfig.Api.EnableDashboard || reloadedCoreConfig.EnableLog ||
        !reloadedCoreConfig.Api.ListenUrls.SequenceEqual(["http://127.0.0.1:7999"]))
    {
        throw new InvalidOperationException("Preserved core TOML did not deserialize with the saved values.");
    }
    Console.WriteLine("Core config preserving-save verification passed.");

    var tempCache = Path.Combine(tempRoot, "temporary-cache");
    Directory.CreateDirectory(Path.Combine(tempCache, "adapters"));
    File.WriteAllText(Path.Combine(tempCache, "adapters", "metadata.json"), "keep");
    var time = new TemporaryFilesTestClock();
    string staleDirectory;
    using (var temporaryManager = new ShiroBot.Hosting.Files.TemporaryFileManager(tempCache, time))
    {
        var temporaryBot = new BotContext(null, [], [], new WebHostContext("http://127.0.0.1", false), temporaryFiles: temporaryManager);
        using var temporaryPlugin = new PluginContext(temporaryBot, "temp-plugin", Path.Combine(tempRoot, "temp-plugin"), new HostLogHub(), new PluginServiceRegistry());
        IBotContext temporaryContext = temporaryPlugin;
        var firstTemp = temporaryContext.CreateTempDirectory(TimeSpan.FromMinutes(1));
        var secondTemp = temporaryManager.CreateDirectory("plugin-two", TimeSpan.FromMinutes(2));
        File.WriteAllText(Path.Combine(firstTemp.Path, "download.bin"), "temporary");
        File.WriteAllText(Path.Combine(secondTemp.Path, "download.bin"), "temporary");
        if (firstTemp.Path == secondTemp.Path || firstTemp.ExpiresAt != time.GetUtcNow().AddMinutes(1) ||
            !Path.GetFullPath(firstTemp.Path).StartsWith(Path.Combine(tempCache, "plugin-temp") + Path.DirectorySeparatorChar))
            throw new InvalidOperationException("Plugin temporary paths are not unique, contained or time-bound.");
        AssertThrows<ArgumentOutOfRangeException>(() => temporaryManager.CreateDirectory("plugin/../one", TimeSpan.Zero));
        time.Advance(TimeSpan.FromSeconds(59));
        temporaryManager.CleanupExpired();
        if (!Directory.Exists(firstTemp.Path)) throw new InvalidOperationException("Temporary files expired early.");
        time.Advance(TimeSpan.FromSeconds(1));
        temporaryManager.CleanupExpired();
        if (Directory.Exists(firstTemp.Path) || !Directory.Exists(secondTemp.Path))
            throw new InvalidOperationException("Expiry cleanup touched another directory or failed to remove expired files.");
        staleDirectory = secondTemp.Path;
        temporaryPlugin.Dispose();
        AssertThrows<ObjectDisposedException>(() => temporaryContext.CreateTempDirectory(TimeSpan.FromMinutes(1)));
    }
    if (Directory.Exists(staleDirectory)) throw new InvalidOperationException("Host shutdown did not clean temporary files.");
    // Simulate a crashed previous process, which could not Dispose its manager.
    staleDirectory = Path.Combine(tempCache, "plugin-temp", "previous-process");
    Directory.CreateDirectory(staleDirectory);
    File.WriteAllText(Path.Combine(staleDirectory, "stale.bin"), "stale");
    using (var restarted = new ShiroBot.Hosting.Files.TemporaryFileManager(tempCache, time))
    {
        if (Directory.Exists(staleDirectory) || File.ReadAllText(Path.Combine(tempCache, "adapters", "metadata.json")) != "keep")
            throw new InvalidOperationException("Startup must clean only temporary cache leftovers.");
    }
    if (!OperatingSystem.IsWindows())
    {
        var linkedCache = Path.Combine(tempRoot, "linked-cache");
        var persistent = Path.Combine(tempRoot, "persistent-files");
        Directory.CreateDirectory(linkedCache);
        Directory.CreateDirectory(persistent);
        File.WriteAllText(Path.Combine(persistent, "keep.txt"), "keep");
        var link = Path.Combine(linkedCache, "plugin-temp");
        Directory.CreateSymbolicLink(link, persistent);
        AssertThrows<IOException>(() => new ShiroBot.Hosting.Files.TemporaryFileManager(linkedCache, time));
        if (File.ReadAllText(Path.Combine(persistent, "keep.txt")) != "keep")
            throw new InvalidOperationException("Startup followed a temporary-root symlink into persistent files.");
        Directory.Delete(link);
    }
    Console.WriteLine("Host-managed temporary directory isolation, expiry, shutdown and crash recovery verification passed.");

    var publicUrlsPath = Path.Combine(tempRoot, "public-urls.toml");
    File.WriteAllText(publicUrlsPath, "[api]\npublic_base_url = [\"https://primary.example.com\"]\ntoken = \"test-token\"\n");
    var publicUrlsManager = new ConfigManager(publicUrlsPath);
    var publicUrlsConfig = await publicUrlsManager.LoadCoreConfig();
    if (!publicUrlsConfig.Api.PublicBaseUrl.SequenceEqual(["https://primary.example.com"]) ||
        publicUrlsConfig.Api.Token != "test-token")
        throw new InvalidOperationException("Public URL array or flattened API token failed to load.");
    publicUrlsConfig.Api.PublicBaseUrl = ["https://primary.example.com", "https://backup.example.com"];
    publicUrlsManager.SaveConfig(publicUrlsPath, publicUrlsConfig);
    publicUrlsConfig = await publicUrlsManager.LoadCoreConfig();
    if (publicUrlsConfig.Api.PublicBaseUrl.Length != 2 || publicUrlsConfig.Api.GetPrimaryBaseUrl() != "https://primary.example.com")
        throw new InvalidOperationException("Multiple public URLs did not round-trip or select their primary address.");
    var authorize = typeof(HostHttpServer).GetMethod("IsAuthorized", BindingFlags.Static | BindingFlags.NonPublic)!;
    var anonymous = new Microsoft.AspNetCore.Http.DefaultHttpContext();
    if ((bool)authorize.Invoke(null, [anonymous, publicUrlsConfig.Api])!)
        throw new InvalidOperationException("Anonymous API access must always be denied.");
    anonymous.Request.Headers.Authorization = "Bearer test-token";
    if (!(bool)authorize.Invoke(null, [anonymous, publicUrlsConfig.Api])!)
        throw new InvalidOperationException("Flattened API token was not accepted.");
    publicUrlsConfig.Api.Token = "";
    if ((bool)authorize.Invoke(null, [anonymous, publicUrlsConfig.Api])!)
        throw new InvalidOperationException("An empty configured token must never disable authentication.");
    publicUrlsConfig.Api.PublicBaseUrl = [];
    publicUrlsConfig.Api.ListenUrls = ["http://127.0.0.1:8001"];
    if (publicUrlsConfig.Api.GetPrimaryBaseUrl() != "http://127.0.0.1:8001")
        throw new InvalidOperationException("Empty public URLs did not fall back to a listener.");
    Console.WriteLine("Mandatory flat API token and multiple public base URLs verification passed.");

    var newCorePath = Path.Combine(tempRoot, "new-core", "config.toml");
    await new ConfigManager(newCorePath).LoadCoreConfig();
    var newCoreToml = File.ReadAllText(newCorePath);
    AssertContains(newCoreToml, "protocols = []");
    AssertContains(newCoreToml, "enable_dashboard = true");
    AssertBefore(newCoreToml, "[api]", "[plugin_routes.default]");
    AssertContains(newCoreToml, "listen_urls = [\"http://127.0.0.1:7001\"]");
    if (newCoreToml.Contains("protocol =", StringComparison.Ordinal) ||
        newCoreToml.Contains("listen_url =", StringComparison.Ordinal))
    {
        throw new InvalidOperationException("New core TOML contains legacy single-value keys.");
    }

    var invalidPublicUrlsPath = Path.Combine(tempRoot, "invalid-public-urls.toml");
    File.WriteAllText(invalidPublicUrlsPath, "[api]\npublic_base_url = \"https://legacy.example.com\"\n");
    try
    {
        await new ConfigManager(invalidPublicUrlsPath).LoadCoreConfig();
        throw new InvalidOperationException("Old scalar public URLs must not be accepted.");
    }
    catch (Exception error) when (error.Message.StartsWith("加载配置时出错:", StringComparison.Ordinal)) { }

    async Task<LoadedPluginHandle> CreatePluginHandleAsync(
        IBotPlugin plugin,
        string id,
        HostLogHub logHub,
        TimeSpan? activeDispatchDrainTimeout = null,
        TimeSpan? pluginOnUnloadTimeout = null)
    {
        var pluginDirectory = Path.Combine(tempRoot, id);
        var context = new PluginContext(botContext, id, pluginDirectory, logHub, new PluginServiceRegistry());
        await plugin.OnLoad(context);
        return new LoadedPluginHandle(
            plugin,
            context,
            new DllLoader<IBotPlugin>(),
            typeof(Program).Assembly.Location,
            new PluginProbeInfo(
                plugin.GetType().FullName!,
                id,
                id,
                "1.0.0",
                null,
                null,
                PluginCategory.Other,
                null,
                false,
                [],
                []),
            logHub,
            activeDispatchDrainTimeout: activeDispatchDrainTimeout,
            pluginOnUnloadTimeout: pluginOnUnloadTimeout);
    }
}
finally
{
    if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true);
}

static Dictionary<string, JsonElement> ReadConfigSchema(object[] schema) =>
    JsonSerializer.SerializeToElement(schema).EnumerateArray()
        .ToDictionary(item => item.GetProperty("key").GetString()!, item => item, StringComparer.Ordinal);

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
    if (text.Split(value).Length - 1 != 1)
    {
        throw new InvalidOperationException($"Expected exactly one '{value}' comment.\n{text}");
    }
}

static void AssertContains(string text, string value)
{
    if (!text.Contains(value, StringComparison.Ordinal))
    {
        throw new InvalidOperationException($"Expected '{value}'.\n{text}");
    }
}

static void AssertThrows<TException>(Action action) where TException : Exception
{
    try
    {
        action();
    }
    catch (TException)
    {
        return;
    }

    throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
}

static async Task AssertThrowsAsync<TException>(Func<Task> action) where TException : Exception
{
    try
    {
        await action();
    }
    catch (TException)
    {
        return;
    }

    throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
}

static void AssertAssemblyVersion(Assembly assembly, string expectedVersion)
{
    var actualVersion = assembly.GetName().Version?.ToString();
    if (!string.Equals(actualVersion, expectedVersion, StringComparison.Ordinal))
    {
        throw new InvalidOperationException(
            $"Unexpected shared contract ABI for {assembly.GetName().Name}: expected {expectedVersion}, got {actualVersion}.");
    }
}

static void AssertUnloadTimedOut(PluginUnloadResult result, string expectedPhase)
{
    if (result.Unloaded ||
        result.AssemblyLoadContextWeakReference is not null ||
        result.Error is not TimeoutException timeoutException ||
        !timeoutException.Message.Contains(expectedPhase, StringComparison.Ordinal) ||
        !timeoutException.Message.Contains("restarted", StringComparison.Ordinal))
    {
        throw new InvalidOperationException($"Unload did not report the expected restart-required {expectedPhase} timeout.");
    }
}

static MemberJoinedEvent CreateMemberJoinedEvent(string userId) => new()
{
    Platform = "verification",
    Channel = Channel.Group("group"),
    UserId = userId
};

async Task SendInAdapterScopeAsync(VerificationAdapter adapter, string text)
{
    using var _ = AdapterExecutionContext.Enter(adapter.Platform);
    await botContext.Message.SendMessageAsync(Channel.Group("channel"), [new TextSegment(text)]);
}

[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
static WeakReference? ProbeQQAdapterJson(string assemblyPath, out Dictionary<string, WeakReference> references)
{
    var loader = new DllLoader<IBotAdapter>(true, new SharedAssemblyResolver());
    var adapter = loader.Load(Path.GetFullPath(assemblyPath));
    var assembly = adapter.GetType().Assembly;
    var config = Activator.CreateInstance(assembly.GetType("ShiroBot.Adapter.QQPlatform.QQPlatformConfig")!)!;
    config.GetType().GetProperty("AppId")!.SetValue(config, "probe");
    config.GetType().GetProperty("AppSecret")!.SetValue(config, "probe");
    using var http = new HttpClient(new QQTokenProbeHandler());
    var providerType = assembly.GetType("ShiroBot.Adapter.QQPlatform.Protocol.QQTokenProvider")!;
    var provider = Activator.CreateInstance(providerType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
        null, [http, config], null)!;
    var result = (Task<string>)providerType.GetMethod("GetAsync")!.Invoke(provider, [false, CancellationToken.None])!;
    if (result.GetAwaiter().GetResult() != "probe-token") throw new InvalidOperationException("QQ token probe failed.");
    var apiType = assembly.GetType("ShiroBot.Adapter.QQPlatform.Protocol.QQOpenApiClient")!;
    var api = Activator.CreateInstance(apiType, [http, config, provider, null])!;
    ((Task)apiType.GetMethod("GetCurrentUserAsync")!.Invoke(api, [CancellationToken.None])!).GetAwaiter().GetResult();
    var messagesType = assembly.GetType("ShiroBot.Adapter.QQPlatform.AdapterImpl.QQMessageService")!;
    var messages = (IMessageService)Activator.CreateInstance(messagesType, [api, null, null])!;
    var sent = messages.SendMessageAsync(Channel.Direct("probe-user"),
        [new ImageSegment("https://example.org/probe.png")]).GetAwaiter().GetResult();
    if (sent.MessageId != "probe-sent") throw new InvalidOperationException("QQ media serialization probe failed.");
    // Exercise new group contracts and nested keyboard metadata before checking collection.
    var groupType = assembly.GetType("ShiroBot.Adapter.QQPlatform.AdapterImpl.QQOfficialGroupService")!;
    var groupApi = (IQGroupApi)Activator.CreateInstance(groupType, [api])!;
    adapter.GetType().GetField("_officialGroups", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(adapter, groupApi);
    if (!ReferenceEquals(groupApi, adapter.GetExtension<IQGroupApi>()))
        throw new InvalidOperationException("QQ group extension discovery failed.");
    var page = groupApi.GetJoinRequestsAsync("probe-group").GetAwaiter().GetResult();
    if (page.Requests.Count != 1 || page.Requests[0].RequestId != "probe-request")
        throw new InvalidOperationException("QQ group response mapping failed.");
    groupApi.SetMemberMutesAsync("probe-group", [new QMemberMute { UserId = "probe-member", Duration = TimeSpan.Zero }])
        .GetAwaiter().GetResult();
    var officialType = assembly.GetType("ShiroBot.Adapter.QQPlatform.AdapterImpl.QQOfficialMessageService")!;
    var official = (IQOfficialMessageApi)Activator.CreateInstance(officialType, [api, messages, null, null])!;
    official.SendMarkdownAsync(new QOfficialMessageTarget(QOfficialMessageScene.Group, "probe-group"),
        new QCustomMarkdown("probe") { ForceVerifyImageResource = true },
        new QInlineKeyboard([new QKeyboardRow([new QKeyboardButton
        {
            Id = "probe", GroupId = "probe-group",
            RenderData = new QKeyboardRenderData("Probe", "Done", QKeyboardButtonStyle.Red),
            Action = new QKeyboardAction
            {
                Type = QKeyboardActionType.Callback, Data = "probe", UnsupportTips = "upgrade",
                Permission = new QKeyboardPermission { Type = QKeyboardPermissionType.Everyone },
                Modal = new QKeyboardModal("Confirm", "Yes", "No")
            }
        }])])).GetAwaiter().GetResult();
    references = new()
    {
        ["adapter"] = new(adapter), ["config"] = new(config), ["provider"] = new(provider),
        ["token-task"] = new(result), ["http"] = new(http),
        ["options"] = new(assembly.GetType("ShiroBot.Adapter.QQPlatform.Protocol.QQJson")?.GetProperty("Options")?.GetValue(null))
    };
    return loader.BeginUnload();
}

internal sealed class QQTokenProbeHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent(request.RequestUri!.AbsolutePath switch
            {
                "/app/getAppAccessToken" => """{"access_token":"probe-token","expires_in":7200}""",
                "/users/@me" => """{"id":"probe-bot","username":"Probe"}""",
                var path when path.EndsWith("/files") => """{"file_info":"probe-file"}""",
                var path when path.EndsWith("/join_request_list") => """{"list":[{"member_openid":"probe-member","join_request_id":"probe-request"}]}""",
                var path when path.EndsWith("/restrict_chat_setting") => "{}",
                _ => """{"id":"probe-sent"}"""
            }, System.Text.Encoding.UTF8, "application/json")
        });
}

internal sealed class VerificationConfig
{
    [ConfigField("Timeout in seconds", Label = "Request timeout", Min = 1, Max = 120)]
    public int TimeoutSeconds { get; set; } = 15;

    [ConfigField("Output format", Options = ["compact", "detailed"], Placeholder = "compact")]
    public string OutputMode { get; set; } = "compact";

    public VerificationRetryConfig Retry { get; set; } = new();
}

internal sealed class VerificationRetryConfig
{
    public int Count { get; set; } = 3;
}

[ConfigModel]
internal sealed class VerificationComponentConfig
{
    public static int Constructions;

    public VerificationComponentConfig() => Interlocked.Increment(ref Constructions);

    [ConfigField("Retry count", Min = 0, Max = 10, Group = "network", GroupLabel = "Network", GroupOrder = 10, Order = 10)]
    public int RetryCount { get; set; } = 3;

    [ConfigField("Mode", Options = ["fast", "safe"], Default = "safe")]
    public string Mode { get; set; } = "fast";

    [ConfigVisibleWhen("RetryCount", ConfigConditionOperator.GreaterThan, "0")]
    [ConfigField("Backoff seconds")]
    public double BackoffSeconds { get; set; } = 1.5;

    public string[] Tags { get; set; } = ["a"];

    public VerificationNetworkSection Network { get; set; } = new();

    public List<VerificationProvider> Providers { get; set; } = [];

    public Dictionary<string, string> Labels { get; set; } = new() { ["env"] = "test" };

    public int? OptionalLimit { get; set; }

    internal string InternalValue { get; set; } = "internal";
    private string PrivateValue { get; set; } = "private";
    public string ComputedValue => "computed";
    public static string StaticValue { get; set; } = "static";
    public string PrivateSetter { get; private set; } = "private setter";
    public VerificationRecordSection RecordSection { get; set; } = new();

    public VerificationRecursiveSection Recursive { get; set; } = new();
}

internal sealed record VerificationRecordSection
{
    public string Value { get; init; } = "record";
    internal string InternalValue { get; init; } = "internal";
}

internal sealed class VerificationNetworkSection
{
    [ConfigField("Host name", Label = "Host")]
    public string Host { get; set; } = "localhost";

    public int Port { get; set; } = 8080;
}

internal sealed class VerificationProvider
{
    public string Name { get; set; } = string.Empty;

    public int Weight { get; set; } = 1;
}

internal sealed class VerificationRecursiveSection
{
    public string Label { get; set; } = "root";

    public VerificationRecursiveSection? Child { get; set; }
}

internal sealed class ConfigurableVerificationPlugin : PluginBase<VerificationComponentConfig>
{
    public bool SettingsReadyBeforeLoad { get; private set; }
    public VerificationComponentConfig CurrentSettings => Settings;
    public string ConfigPath => Context.Config.ConfigPath;
    public List<(int Previous, int Current)> Changes { get; } = [];

    protected override Task LoadAsync()
    {
        SettingsReadyBeforeLoad = Settings.RetryCount == 3;
        return Task.CompletedTask;
    }

    protected override Task OnConfigChangedAsync(
        VerificationComponentConfig previous,
        VerificationComponentConfig current,
        CancellationToken cancellationToken)
    {
        lock (Changes) Changes.Add((previous.RetryCount, current.RetryCount));
        return Task.CompletedTask;
    }
}

internal sealed class VerificationAdapterConfig
{
    public string Endpoint { get; set; } = "https://initial.invalid/";
}

internal sealed class ConfigurableVerificationAdapter(ConfigApplyMode applyMode)
    : IBotAdapter, IConfigurableAdapter, IConfigurableComponent<VerificationAdapterConfig>
{
    public const string FailingEndpoint = "https://fail.invalid/";

    public int Starts { get; private set; }
    public int Stops { get; private set; }
    public bool IsRunning { get; private set; } = true;
    public VerificationAdapterConfig CurrentConfigValue { get; private set; } = new();
    public ConfigApplyMode ApplyMode { get; } = applyMode;
    public string Platform => "configurable-verification";
    public IMessageService Message { get; } = new VerificationMessageService();
    public IChannelService Channel { get; } = new VerificationChannelService();
    public IUserService User { get; } = new VerificationUserService();
    public IEventService Event { get; } = new VerificationEventService();
    public IConfigContext Config { get; set; } = null!;
    public IConsoleLogger Logger { get; set; } = null!;
    public TService? GetExtension<TService>() where TService : class => this as TService;

    public Task StartAsync()
    {
        Starts++;
        if (CurrentConfigValue.Endpoint == FailingEndpoint)
            throw new InvalidOperationException("Verification adapter rejected its endpoint.");
        IsRunning = true;
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        Stops++;
        IsRunning = false;
        return Task.CompletedTask;
    }

    public Task OnConfigChangedAsync(
        VerificationAdapterConfig previous,
        VerificationAdapterConfig current,
        CancellationToken cancellationToken)
    {
        CurrentConfigValue = current;
        return Task.CompletedTask;
    }
}

internal interface IVerificationService;

internal sealed class VerificationService : IVerificationService;

internal sealed class DirectSubscriberPlugin : IBotPlugin, IBotEventSubscriber
{
    public string Name => nameof(DirectSubscriberPlugin);
    public List<BotEvent> Events { get; } = [];
    public Task OnLoad(IBotContext context) => Task.CompletedTask;
    public Task OnUnload() => Task.CompletedTask;

    public Task OnEventAsync(BotEvent e)
    {
        Events.Add(e);
        return Task.CompletedTask;
    }
}

internal sealed class RoutedPlugin : PluginBase
{
    public int BaseEventCount { get; private set; }
    public int MemberJoinedCount { get; private set; }

    protected override void ConfigureRoutes()
    {
        Events.Map<BotEvent>(_ =>
        {
            BaseEventCount++;
            return Task.CompletedTask;
        });
        Events.Map<MemberJoinedEvent>(_ =>
        {
            MemberJoinedCount++;
            return Task.CompletedTask;
        });
    }
}

internal interface IBlockingDispatchPlugin
{
    Task DispatchAsync();
}

internal sealed class BlockingDispatchPlugin : IBotPlugin, IBlockingDispatchPlugin
{
    public TaskCompletionSource DispatchStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource ReleaseDispatch { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource UnloadCompleted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public string Name => nameof(BlockingDispatchPlugin);
    public Task OnLoad(IBotContext context) => Task.CompletedTask;

    public Task OnUnload()
    {
        UnloadCompleted.TrySetResult();
        return Task.CompletedTask;
    }

    public async Task DispatchAsync()
    {
        DispatchStarted.TrySetResult();
        await ReleaseDispatch.Task;
    }
}

internal sealed class BlockingUnloadPlugin : IBotPlugin
{
    public TaskCompletionSource ReleaseUnload { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource UnloadCompleted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public string Name => nameof(BlockingUnloadPlugin);
    public Task OnLoad(IBotContext context) => Task.CompletedTask;

    public async Task OnUnload()
    {
        await ReleaseUnload.Task;
        UnloadCompleted.TrySetResult();
    }
}

internal sealed class ConcurrentDispatchPlugin : IBotPlugin, IBotEventSubscriber
{
    public TaskCompletionSource FirstDispatchStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource SecondDispatchStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource ReleaseFirstDispatch { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public string Name => nameof(ConcurrentDispatchPlugin);
    public Task OnLoad(IBotContext context) => Task.CompletedTask;
    public Task OnUnload() => Task.CompletedTask;

    public async Task OnEventAsync(BotEvent e)
    {
        if (e is not MemberJoinedEvent memberJoined)
        {
            return;
        }

        if (memberJoined.UserId == "first")
        {
            FirstDispatchStarted.TrySetResult();
            await ReleaseFirstDispatch.Task;
        }
        else if (memberJoined.UserId == "second")
        {
            SecondDispatchStarted.TrySetResult();
        }
    }
}

[BotAdapter("verification")]
internal sealed class VerificationAdapter(string platform) : IBotAdapter
{
    public VerificationAdapter() : this("verification")
    {
    }

    public VerificationMessageService MessageService { get; } = new();
    public string Platform { get; } = platform;
    public IMessageService Message => MessageService;
    public IChannelService Channel { get; } = new VerificationChannelService();
    public IUserService User { get; } = new VerificationUserService();
    public IEventService Event { get; } = new VerificationEventService();
    public IConfigContext Config { get; set; } = null!;
    public IConsoleLogger Logger { get; set; } = null!;
    public TService? GetExtension<TService>() where TService : class => this as TService ?? MessageService as TService;
    public Task StartAsync() => Task.CompletedTask;
    public Task StopAsync() => Task.CompletedTask;
}

internal sealed class VerificationMessageService : IMessageService, IMessageReactionService
{
    public MessageCapabilities GetMessageCapabilities(Channel channel) => new() { NativeFeatures = MessageFeatures.Text | MessageFeatures.Quote };
    public List<MessageReference> Reactions { get; } = [];
    public ReactionCapabilities GetReactionCapabilities(Channel channel) => ReactionCapabilities.Unicode | ReactionCapabilities.PlatformEmoji;
    public Task SetReactionAsync(MessageReference message, ReactionEmoji emoji, bool isAdd = true, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Reactions.Add(message);
        return Task.CompletedTask;
    }
    public List<string> Messages { get; } = [];
    public List<(Channel Channel, IReadOnlyList<MessageSegment> Segments)> Sent { get; } = [];
    public Func<Task>? BeforeSend { get; set; }
    public Func<Task>? BeforeQuery { get; set; }
    public MessageEvent? QueryResult { get; set; }
    public List<(Channel Channel, string MessageId)> Deleted { get; } = [];
    public Task DeleteMessageAsync(Channel channel, string messageId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Deleted.Add((channel, messageId));
        return Task.CompletedTask;
    }
    public async Task<MessageEvent?> GetMessageAsync(Channel channel, string messageId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (BeforeQuery is { } beforeQuery) await beforeQuery();
        return QueryResult;
    }
    public async Task<IReadOnlyList<MessageEvent>> GetHistoryMessagesAsync(Channel channel, string? beforeMessageId = null, int limit = 20, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (BeforeQuery is { } beforeQuery) await beforeQuery();
        return QueryResult is { } result ? [result] : [];
    }
    public async Task<SentMessage> SendMessageAsync(Channel channel, IReadOnlyList<MessageSegment> segments, CancellationToken cancellationToken = default)
    {
        if (BeforeSend is { } beforeSend) await beforeSend();
        lock (Messages)
        {
            Messages.Add(string.Concat(segments.OfType<TextSegment>().Select(segment => segment.Text)));
            Sent.Add((channel, segments.ToArray()));
        }
        return new SentMessage("sent");
    }
}

internal sealed class VerificationChannelService : IChannelService;
internal sealed class VerificationUserService : IUserService
{
    public int ApprovalRequests { get; private set; }
    public CancellationToken LastToken { get; private set; }
    public Task AcceptFriendRequestAsync(string token, CancellationToken cancellationToken = default) => Approve(cancellationToken);
    public Task RejectFriendRequestAsync(string token, string? reason = null, CancellationToken cancellationToken = default) => Approve(cancellationToken);
    private Task Approve(CancellationToken token)
    {
        LastToken = token;
        token.ThrowIfCancellationRequested();
        ApprovalRequests++;
        return Task.CompletedTask;
    }
}
internal sealed class VerificationEventService : IEventService
{
    public event Func<BotEvent, Task>? EventReceived;

    public async Task RaiseAsync(BotEvent botEvent)
    {
        if (EventReceived is null) return;

        foreach (var handler in EventReceived.GetInvocationList().Cast<Func<BotEvent, Task>>())
        {
            await handler(botEvent);
        }
    }
}

internal sealed class TemporaryFilesTestClock : TimeProvider
{
    private DateTimeOffset _now = new(2026, 10, 10, 0, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => _now;
    public void Advance(TimeSpan duration) => _now += duration;
}
