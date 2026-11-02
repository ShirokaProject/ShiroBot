using System.Text;
using System.Text.Json.Nodes;
using ShiroBot.Hosting.Context;
using ShiroBot.Hosting.Logging;
using ShiroBot.InteropContractProbe;
using ShiroBot.Plugins.Services;
using ShiroBot.SDK.Plugin;

/// <summary>契约生成的读写代码、Schema、一类服务和事件。</summary>
internal static class PluginContractVerification
{
    public static async Task RunAsync()
    {
        VerifyRoundTrip();
        VerifyTolerantReading();
        VerifySchema();
        await VerifyServicesAsync();
        Console.WriteLine("Plugin contract serialization, schema, families and typed calls verification passed.");
    }

    private static void VerifyRoundTrip()
    {
        var id = Guid.NewGuid();
        var at = new DateTimeOffset(2026, 11, 2, 8, 30, 0, TimeSpan.FromHours(8));
        var forecast = new Forecast("上海", 3, Mood.VerySad, 21.5, ["晴", "热"], new Dictionary<string, int> { ["rain"] = 2 },
            new Point(1.5, -2), new JsonObject { ["raw"] = true })
        {
            At = at, Id = id, Renamed = "x", Path = [new Point(0, 0), new Point(1, 1)]
        };
        var json = Encoding.UTF8.GetString(PluginJson.Serialize(forecast));
        foreach (var expected in new[]
                 {
                     "\"city\":\"上海\"", "\"days\":3", "\"mood\":\"very_sad\"", "\"temperature\":21.5", "\"tags\":[\"晴\",\"热\"]",
                     "\"counts\":{\"rain\":2}", "\"location\":{\"x\":1.5,\"y\":-2}", "\"extra\":{\"raw\":true}", "\"custom_name\":\"x\"",
                     "\"path\":[{\"x\":0,\"y\":0},{\"x\":1,\"y\":1}]"
                 })
        {
            if (!json.Contains(expected, StringComparison.Ordinal))
                throw new InvalidOperationException($"Contract JSON is missing {expected}: {json}");
        }

        var back = PluginJson.Deserialize<Forecast>(Encoding.UTF8.GetBytes(json))!;
        if (back.City != "上海" || back.Days != 3 || back.Mood != Mood.VerySad || back.Temperature != 21.5 ||
            back.Tags is not ["晴", "热"] || back.Counts?["rain"] != 2 || back.Location != new Point(1.5, -2) ||
            (bool?)back.Extra?["raw"] != true || back.At != at || back.Id != id || back.Renamed != "x" || back.Path.Count != 2)
            throw new InvalidOperationException("Contract JSON did not round-trip: " + back);
    }

    private static void VerifyTolerantReading()
    {
        // 缺少的字段取默认值，属性初始值保留；不认识的字段忽略；枚举接受不同写法和数字。
        var sparse = PluginJson.Deserialize<Forecast>("""{"city":"北京","unknown":{"a":[1,2]},"mood":"VERYSAD"}"""u8)!;
        if (sparse.City != "北京" || sparse.Days != 1 || sparse.Mood != Mood.VerySad || sparse.Temperature is not null ||
            sparse.Tags is not null || sparse.Path is not { Count: 0 })
            throw new InvalidOperationException("Missing or unknown contract fields were not handled: " + sparse);
        if (PluginJson.Deserialize<Forecast>("""{"city":"a","mood":5}"""u8)!.Mood != Mood.Confused)
            throw new InvalidOperationException("Enums must also accept their numeric value.");
        if (PluginJson.Deserialize<Forecast>("null"u8) is not null || PluginJson.Deserialize<Forecast>(ReadOnlySpan<byte>.Empty) is not null)
            throw new InvalidOperationException("JSON null and empty input must read as null.");
        if (new JsonObject { ["query"] = "猫" }.As<ToolArgs>() is not { Query: "猫", Limit: 5 })
            throw new InvalidOperationException("As<T> must read [PluginJson] types and keep parameter defaults.");
        try
        {
            PluginJson.Deserialize<Forecast>("""{"city":"a","days":"three"}"""u8);
            throw new InvalidOperationException("A wrongly typed field was accepted.");
        }
        catch (System.Text.Json.JsonException ex) when (ex.Message.Contains("days")) { }
    }

    private static void VerifySchema()
    {
        var schema = PluginJsonSchema.For<ToolArgs>();
        if ((string?)schema["type"] != "object" ||
            (string?)schema["properties"]?["query"]?["description"] != "关键词" ||
            (string?)schema["properties"]?["query"]?["type"] != "string" ||
            (int?)schema["properties"]?["limit"]?["default"] != 5 ||
            (string?)schema["properties"]?["limit"]?["type"] != "integer" ||
            schema["required"]?.ToJsonString() != "[\"query\"]")
            throw new InvalidOperationException("Unexpected tool schema: " + schema.ToJsonString());

        var forecast = PluginJsonSchema.For<Forecast>();
        if ((string?)forecast["properties"]?["mood"]?["enum"]?[1] != "very_sad" ||
            (string?)forecast["properties"]?["path"]?["items"]?["type"] != "object" ||
            (string?)forecast["properties"]?["id"]?["format"] != "uuid")
            throw new InvalidOperationException("Unexpected forecast schema: " + forecast.ToJsonString());
    }

    private static async Task VerifyServicesAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "ShiroBot.Verification", Guid.NewGuid().ToString("N"));
        var logs = new HostLogHub();
        var bot = new BotContext(null, [], [], new WebHostContext("http://127.0.0.1", false), logs);
        var registry = new PluginServiceRegistry();
        using var provider = new PluginContext(bot, "provider", Path.Combine(root, "provider"), logs, registry);
        using var other = new PluginContext(bot, "other", Path.Combine(root, "other"), logs, registry);
        using var caller = new PluginContext(bot, "caller", Path.Combine(root, "caller"), logs, registry);
        try
        {
            if (await ProbeApi.Echo.TryCallAsync(caller, new Forecast("x")) is not null)
                throw new InvalidOperationException("TryCallAsync must return null without a provider.");
            try { await ProbeApi.Echo.CallAsync(caller, new Forecast("x")); throw new InvalidOperationException("CallAsync did not throw."); }
            catch (PluginServiceUnavailableException) { }

            ProbeApi.Echo.Provide(provider, (forecast, _) => Task.FromResult(forecast with { Days = forecast.Days + 1 }),
                new JsonObject { ["description"] = "echo" });
            var echoed = await ProbeApi.Echo.CallAsync(caller, new Forecast("杭州", Tags: ["a"]));
            if (echoed is not { City: "杭州", Days: 2, Tags: ["a"] })
                throw new InvalidOperationException("Typed service call did not round-trip: " + echoed);
            if ((string?)caller.Services.Find("probe.forecast").Single().Metadata?["description"] != "echo")
                throw new InvalidOperationException("Single-service metadata was not kept.");

            // 一类服务：两个插件各提供一个，发现方按前缀找到并逐个调用。
            ProbeApi.Sources.Provide(provider, "alpha", (p, _) => Task.FromResult(p with { X = p.X + 1 }), new SourceInfo("Alpha"));
            ProbeApi.Sources.Provide(other, "beta", (p, _) => Task.FromResult(p with { Y = p.Y + 1 }),
                new SourceInfo("Beta", PluginJsonSchema.For<ToolArgs>()));
            var sources = ProbeApi.Sources.Find(caller);
            if (sources.Select(s => (s.ShortName, s.ProviderId, s.Metadata?.Title)).ToArray() is not
                [("alpha", "provider", "Alpha"), ("beta", "other", "Beta")] ||
                (string?)sources[1].Metadata?.Parameters?["properties"]?["query"]?["type"] != "string")
                throw new InvalidOperationException("Service family discovery returned unexpected entries.");
            if (await sources[0].CallAsync(caller, new Point(1, 1)) != new Point(2, 1) ||
                await sources[1].CallAsync(caller, new Point(1, 1)) != new Point(1, 2))
                throw new InvalidOperationException("Service family entries did not call their providers.");

            // 事件。
            Point? received = null;
            ProbeApi.Moved.On(other, (point, _) => { received = point; return Task.CompletedTask; });
            if (await ProbeApi.Moved.EmitAsync(caller, new Point(3, 4)) != 1 || received != new Point(3, 4))
                throw new InvalidOperationException("Typed event was not delivered.");

            // 调用描述。
            var call = ProbeApi.Echo.ToCallJson(new Forecast("x"));
            if ((string?)call["service"] != "probe.forecast" || (string?)call["arguments"]?["city"] != "x")
                throw new InvalidOperationException("ToCallJson did not describe the call: " + call.ToJsonString());

            // 提供方出错：传回异常类型和消息，TryCallAsync 不吞掉。
            using var failing = new PluginContext(bot, "failing", Path.Combine(root, "failing"), logs, registry);
            ProbeApi.Sources.Provide(failing, "broken", (_, _) => throw new FormatException("bad point"), new SourceInfo("Broken"));
            var broken = ProbeApi.Sources.Find(caller).Single(s => s.ShortName == "broken");
            try { await broken.TryCallAsync(caller, new Point(0, 0)); throw new InvalidOperationException("A failing provider was swallowed."); }
            catch (PluginServiceException ex) when (ex.RemoteMessage == "bad point") { }
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
