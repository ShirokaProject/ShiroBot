using System.Text.Json;
using System.Text.Json.Nodes;
using ShiroBot.Adapters;
using ShiroBot.Configuration;
using ShiroBot.Hosting.Context;
using ShiroBot.Hosting.Events;
using ShiroBot.Hosting.Http;
using ShiroBot.Hosting.Logging;
using ShiroBot.Hosting.Runtime;
using ShiroBot.Packages;
using ShiroBot.Plugins;
using ShiroBot.Plugins.Loading;
using ShiroBot.Plugins.Services;
using ShiroBot.SDK.Abstractions;
using ShiroBot.SDK.Models;
using ShiroBot.SDK.Plugin;

/// <summary>
/// 插件生命周期作用域、按名称调用的服务和事件总线：
/// 用 samples/PluginInterop 的两个示例插件在真实的可回收 ALC 里加载、互相调用、单独热替换提供方，
/// 最后卸载并确认两个 ALC 都能被回收。
/// </summary>
internal static class PluginInteropVerification
{
    public static async Task RunAsync()
    {
        await VerifyLifetimeScopeAsync();
        await VerifyAdapterLifetimeAsync();
        await VerifyNamedServicesAsync();
        await PluginContractVerification.RunAsync();
        VerifyEnumArrayDefaults();
        await VerifySamplePluginsAsync();
    }

    private static async Task VerifyLifetimeScopeAsync()
    {
        var order = new List<string>();
        var scope = new PluginLifetimeScope();
        var ticks = 0;
        scope.SetInterval(TimeSpan.FromMilliseconds(10), _ => { Interlocked.Increment(ref ticks); return Task.CompletedTask; });
        var timeoutFired = false;
        var cancelledTimeout = scope.SetTimeout(TimeSpan.FromMilliseconds(20), _ => { timeoutFired = true; return Task.CompletedTask; });
        cancelledTimeout.Dispose();
        var loopObservedStop = false;
        _ = scope.Run(async token =>
        {
            try { await Task.Delay(Timeout.Infinite, token); }
            catch (OperationCanceledException) { loopObservedStop = true; throw; }
        });
        scope.Defer(() => { order.Add("first"); return ValueTask.CompletedTask; });
        var removed = scope.Defer(() => { order.Add("removed"); return ValueTask.CompletedTask; });
        removed.Dispose();
        var resource = scope.Track(new Probe(order, "resource"));
        var child = scope.CreateScope();
        child.Defer(() => { order.Add("child"); return ValueTask.CompletedTask; });
        await Task.Delay(100);

        scope.Cancel();
        await scope.StopTasksAsync().WaitAsync(TimeSpan.FromSeconds(2));
        if (!loopObservedStop || ticks == 0 || timeoutFired || resource.Disposed || order.Count != 0)
            throw new InvalidOperationException("Stopping a plugin lifetime must end tasks and timers before releasing resources.");
        // OnUnload 期间仍可以登记资源。
        scope.Defer(() => { order.Add("during-unload"); return ValueTask.CompletedTask; });
        await scope.DisposeResourcesAsync().WaitAsync(TimeSpan.FromSeconds(2));
        if (!order.SequenceEqual(["child", "during-unload", "resource", "first"]))
            throw new InvalidOperationException("Plugin lifetime resources must be released in reverse order: " + string.Join(",", order));
        try { _ = scope.Run(_ => Task.CompletedTask); throw new InvalidOperationException("A stopped lifetime accepted a task."); }
        catch (ObjectDisposedException) { }

        // 子作用域可以单独释放，不影响所在作用域。
        var parent = new PluginLifetimeScope();
        var feature = parent.CreateScope();
        var featureStopped = false;
        _ = feature.Run(async token =>
        {
            try { await Task.Delay(Timeout.Infinite, token); }
            finally { featureStopped = true; }
        });
        await feature.DisposeAsync();
        if (!featureStopped || parent.Stopping.IsCancellationRequested)
            throw new InvalidOperationException("Disposing a child scope must stop only its own work.");
        await parent.DisposeResourcesAsync();
        Console.WriteLine("Plugin lifetime scope task, timer and reverse-order release verification passed.");
    }

    /// <summary>适配器每次启动拿到新的作用域；停止时先取消、再 StopAsync、再等任务、最后释放资源。</summary>
    private static async Task VerifyAdapterLifetimeAsync()
    {
        var adapter = new LifetimeAdapter();
        await AdapterManager.StartAdapterAsync(adapter);
        var first = adapter.Lifetime;
        var firstLoop = adapter.Loop!;
        var firstResource = adapter.Resource!;
        await AdapterManager.StopAdapterAsync(adapter);
        if (!adapter.StoppingCancelledBeforeStop || !firstLoop.IsCompleted || !firstResource.Disposed ||
            firstResource.DisposedBeforeStop)
            throw new InvalidOperationException("Adapter lifetimes must cancel, call StopAsync, stop tasks and then release resources.");

        await AdapterManager.StartAdapterAsync(adapter);
        if (ReferenceEquals(adapter.Lifetime, first) || adapter.Lifetime.Stopping.IsCancellationRequested)
            throw new InvalidOperationException("Every adapter start must get a fresh lifetime.");
        await AdapterManager.StopAdapterAsync(adapter);

        // 未实现 ILifetimeAwareAdapter 的旧适配器照常启动和停止。
        var legacy = new VerificationAdapter("legacy");
        await AdapterManager.StartAdapterAsync(legacy);
        await AdapterManager.StopAdapterAsync(legacy);
        Console.WriteLine("Adapter lifetime per start and stop order verification passed.");
    }

    /// <summary>枚举数组默认值按名称或数值逐个转换，不经过 JsonSerializer（组件的枚举类型会被它缓存而拉住程序集）。</summary>
    private static void VerifyEnumArrayDefaults()
    {
        var create = typeof(ConfigManager).GetMethod("CreateConfigDefaults",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.MakeGenericMethod(typeof(EnumArrayConfig));
        var config = (EnumArrayConfig)create.Invoke(null, null)!;
        if (!config.Modes.SequenceEqual([DayOfWeek.Tuesday, DayOfWeek.Sunday]) || !config.Numbers.SequenceEqual([1, 2]))
            throw new InvalidOperationException("Array config defaults must accept enum names and numbers.");
        Console.WriteLine("Config array default conversion verification passed.");
    }

    private sealed class EnumArrayConfig
    {
        [ShiroBot.SDK.Config.ConfigField("Modes.", Default = "[\"tuesday\", 0]")]
        public DayOfWeek[] Modes { get; set; } = [];

        [ShiroBot.SDK.Config.ConfigField("Numbers.", Default = "[1, 2]")]
        public int[] Numbers { get; set; } = [];
    }

    private static async Task VerifyNamedServicesAsync()
    {
        var registry = new PluginServiceRegistry();
        var providerLifetime = new PluginLifetimeScope();
        using var provider = new PluginServiceScope(registry, "provider", lifetime: providerLifetime);
        using var consumer = new PluginServiceScope(registry, "consumer");
        JsonObject? received = null;
        provider.Provide("math.add", (arguments, _) =>
        {
            received = (JsonObject)arguments!;
            return Task.FromResult<JsonNode?>(new JsonObject { ["sum"] = (int)arguments!["a"]! + (int)arguments["b"]! });
        });
        provider.Provide("math.fail", (_, _) => throw new FormatException("bad input"));

        var input = new JsonObject { ["a"] = 2, ["b"] = 3 };
        var result = await consumer.CallAsync("math.add", input);
        if ((int?)result?["sum"] != 5 || ReferenceEquals(received, input))
            throw new InvalidOperationException("Named services must return results and copy arguments between plugins.");
        try { await consumer.CallAsync("math.fail"); throw new InvalidOperationException("A failing service did not throw."); }
        catch (PluginServiceException ex) when (ex.RemoteType == typeof(FormatException).FullName && ex.InnerException is null) { }
        try { provider.Provide("math.add", (_, _) => Task.FromResult<JsonNode?>(null)); throw new InvalidOperationException("A duplicate name was accepted."); }
        catch (InvalidOperationException ex) when (ex.Message.Contains("already provided")) { }

        // 按前缀发现服务：带描述副本、按名称排序。
        var metadata = new JsonObject { ["description"] = "Add numbers" };
        provider.Provide("math.tool.mul", (_, _) => Task.FromResult<JsonNode?>(null), metadata);
        provider.Provide("math.tool.div", (_, _) => Task.FromResult<JsonNode?>(null));
        metadata["description"] = "changed";
        var found = consumer.Find("math.tool.");
        if (found is not [{ Name: "math.tool.div", ProviderId: "provider", Metadata: null }, { Name: "math.tool.mul" } mul] ||
            (string?)mul.Metadata?["description"] != "Add numbers")
            throw new InvalidOperationException("Find must list prefixed services with copied metadata.");
        mul.Metadata!["description"] = "mutated";
        if ((string?)consumer.Find("math.tool.mul")[0].Metadata?["description"] != "Add numbers")
            throw new InvalidOperationException("Find must return copies of the metadata.");

        // 提供方开始卸载：立即停止接收调用。
        providerLifetime.Cancel();
        registry.DetachInterop("provider");
        if (consumer.IsAvailable("math.add") || consumer.Find("math.").Count != 0)
            throw new InvalidOperationException("A detached provider must stop serving and stop being listed.");
        try { await consumer.CallAsync("math.add", input); throw new InvalidOperationException("A detached service was called."); }
        catch (PluginServiceUnavailableException) { }

        // 事件总线：每个订阅者拿到独立副本，处理失败不影响发布者和其他订阅者。
        var bus = new PluginEventBusScope(registry, "listener", async work => { await work(); return true; }, new PluginLifetimeScope());
        var failing = new PluginEventBusScope(registry, "failing", async work => { await work(); return true; }, new PluginLifetimeScope());
        JsonNode? first = null;
        bus.On("demo.topic", (payload, _) => { first = payload; return Task.CompletedTask; });
        failing.On("demo.topic", (_, _) => throw new InvalidOperationException("listener failed"));
        var payload = new JsonObject { ["value"] = 1 };
        var delivered = await bus.EmitAsync("demo.topic", payload);
        if (delivered != 2 || (int?)first?["value"] != 1 || ReferenceEquals(first, payload))
            throw new InvalidOperationException("The plugin event bus must deliver copies to every subscriber.");
        registry.DetachInterop("listener");
        registry.DetachInterop("failing");
        if (await bus.EmitAsync("demo.topic", payload) != 0)
            throw new InvalidOperationException("Detached subscribers must not receive events.");
        Console.WriteLine("Named plugin services and event bus verification passed.");
    }

    /// <summary>
    /// 开发者自检：把任意插件 DLL（连同所在目录的依赖）加载进隔离的插件目录，运行一段时间后卸载，
    /// 报告程序集能否被回收。用法：<c>dotnet run --project Tests/Verification -- --plugin-unload-check 插件.dll [秒数]</c>。
    /// </summary>
    public static async Task CheckPluginUnloadAsync(string dllPath, int runSeconds)
    {
        dllPath = Path.GetFullPath(dllPath);
        var root = Path.Combine(Path.GetTempPath(), "ShiroBot.UnloadCheck", Guid.NewGuid().ToString("N"));
        var pluginRoot = Path.Combine(root, "plugins");
        var target = Path.Combine(pluginRoot, Path.GetFileNameWithoutExtension(dllPath));
        Directory.CreateDirectory(target);
        foreach (var file in Directory.EnumerateFiles(Path.GetDirectoryName(dllPath)!))
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)));

        var logs = new HostLogHub();
        var botContext = new BotContext(null, [], [], new WebHostContext("http://127.0.0.1", false), logs);
        botContext.RegisterAdapter(new VerificationAdapter("qq"));
        var runtime = new HostRuntimeState(DateTimeOffset.UtcNow);
        var resolver = new SharedAssemblyResolver();
        // 与宿主启动时一样提供 SDK 和内置 Model 包，按真实环境加载第三方插件。
        resolver.Register(["ShiroBot.SDK"], System.Runtime.Loader.AssemblyLoadContext.Default);
        var models = new ModelPackageRegistry(resolver);
        models.RegisterBuiltIn(typeof(ShiroBot.Model.QQ.QGroup).Assembly);
        models.RegisterBuiltIn(typeof(ShiroBot.Model.Discord.DiscordUser).Assembly);
        models.RegisterBuiltIn(typeof(ShiroBot.Model.Telegram.TelegramUser).Assembly);
        var manager = new PluginManager(botContext, resolver, models, runtime, logs) { PluginRootPath = pluginRoot };
        var dispatcher = new HostEventDispatcher(new Lock(), botContext.ReplySubscriptions, runtime, logs);
        try
        {
            await manager.ScheduleLoadPluginByName(dispatcher, new PluginRouteConfig(), Path.Combine(target, Path.GetFileName(dllPath)));
            var id = manager.GetLoadedPluginNames().SingleOrDefault()
                ?? throw new InvalidOperationException("The plugin did not load: " + string.Join(" | ",
                    logs.GetHistory("system", 20).Select(log => log.Message)));
            Console.WriteLine($"Loaded {id}; running {runSeconds}s before unloading.");
            await Task.Delay(TimeSpan.FromSeconds(runSeconds));
            if (!await manager.ScheduleUnloadPluginByName(dispatcher, id))
                throw new InvalidOperationException($"{id} refused to unload.");
            var notice = JsonSerializer.Serialize(runtime.CreateRestartNotice("plugin"));
            if (!notice.Contains("\"recommended\":false"))
                throw new InvalidOperationException($"{id} unloaded its logic but its assembly is still referenced: " +
                    string.Join(" | ", logs.GetHistory("system", 20).Select(log => log.Message).Where(message => message.Contains("存活") || message.Contains("残留"))));
            Console.WriteLine($"Plugin unload check passed: {id} loaded, ran and released its assembly.");
        }
        finally
        {
            await manager.UnloadAllAsync(dispatcher);
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }

    /// <summary>
    /// 开发者自检：只复制适配器 DLL（不带配置，以默认配置启动），加载、运行一段时间后停止并卸载，报告程序集能否被回收。
    /// 用法：<c>dotnet run --project Tests/Verification -- --adapter-unload-check 适配器.dll [秒数]</c>。
    /// </summary>
    public static async Task CheckAdapterUnloadAsync(string dllPath, int runSeconds)
    {
        dllPath = Path.GetFullPath(dllPath);
        var root = Path.Combine(Path.GetTempPath(), "ShiroBot.UnloadCheck", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, Path.GetFileName(dllPath));
        File.Copy(dllPath, path);
        var id = ShiroBot.Adapters.Compatibility.AdapterContractProbe.ReadMetadata(path)?.Id
            ?? throw new InvalidOperationException("The DLL does not declare an adapter.");

        var logs = new HostLogHub();
        var resolver = new SharedAssemblyResolver();
        resolver.Register(["ShiroBot.SDK"], System.Runtime.Loader.AssemblyLoadContext.Default);
        var models = new ModelPackageRegistry(resolver);
        models.RegisterBuiltIn(typeof(ShiroBot.Model.QQ.QGroup).Assembly);
        models.RegisterBuiltIn(typeof(ShiroBot.Model.Discord.DiscordUser).Assembly);
        models.RegisterBuiltIn(typeof(ShiroBot.Model.Telegram.TelegramUser).Assembly);
        var runtime = new HostRuntimeState(DateTimeOffset.UtcNow);
        var context = new BotContext(null, [], [], new WebHostContext("http://127.0.0.1", false), logs);
        var dispatcher = new HostEventDispatcher(new Lock(), context.ReplySubscriptions, runtime, logs);
        var manager = new AdapterManager(root, resolver, models, context, new AdapterEventBridge(dispatcher), runtime, logs,
            _ => Task.CompletedTask);
        try
        {
            await manager.LoadByIdAsync(id, path).WaitAsync(TimeSpan.FromSeconds(60));
            Console.WriteLine($"Loaded adapter {id}; running {runSeconds}s before stopping.");
            await Task.Delay(TimeSpan.FromSeconds(runSeconds));
            // 走一遍配置热重载：比较配置指纹不能拉住适配器的程序集。
            await manager.ApplyConfigByIdAsync(id).WaitAsync(TimeSpan.FromSeconds(60));
            await manager.StopByIdAsync(id).WaitAsync(TimeSpan.FromSeconds(60));
            Console.WriteLine($"Adapter unload check passed: {id} started, stopped and released its assembly.");
        }
        catch
        {
            foreach (var log in logs.GetHistory("system", 30)) Console.WriteLine(log.Message);
            throw;
        }
        finally
        {
            await manager.StopForShutdownAsync();
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }

    private static async Task VerifySamplePluginsAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "ShiroBot.Verification", Guid.NewGuid().ToString("N"));
        var pluginRoot = Path.Combine(root, "plugins");
        Directory.CreateDirectory(pluginRoot);
        // 只复制文件，不在测试进程里引用示例插件的类型，避免它们进入默认 ALC。
        foreach (var file in new[] { "ShiroBot.Samples.Greeter.dll", "ShiroBot.Samples.GreeterClient.dll" })
            File.Copy(Path.Combine(AppContext.BaseDirectory, file), Path.Combine(pluginRoot, file));

        var adapter = new VerificationAdapter("qq");
        var logs = new HostLogHub();
        var botContext = new BotContext(null, [], [], new WebHostContext("http://127.0.0.1", false), logs);
        botContext.RegisterAdapter(adapter);
        var runtime = new HostRuntimeState(DateTimeOffset.UtcNow);
        var resolver = new SharedAssemblyResolver();
        var manager = new PluginManager(botContext, resolver, new ModelPackageRegistry(resolver), runtime, logs)
            { PluginRootPath = pluginRoot };
        var dispatcher = new HostEventDispatcher(new Lock(), botContext.ReplySubscriptions, runtime, logs);
        var routes = new PluginRouteConfig();
        var messageId = 0;

        async Task<string> GreetAsync(string name)
        {
            var client = manager.GetLoadedPluginSnapshot().Single(plugin => plugin.Name == "sample-greeter-client");
            var message = new MessageEvent
            {
                Platform = "qq", MessageId = "greet-" + Interlocked.Increment(ref messageId),
                Channel = Channel.Group("group"), Sender = new User("user"), Segments = [new TextSegment("#greet " + name)]
            };
            var before = adapter.MessageService.Messages.Count;
            using (AdapterExecutionContext.Enter("qq"))
                await client.DispatchAsync<IBotEventSubscriber>(subscriber => subscriber.OnEventAsync(message));
            return adapter.MessageService.Messages.Count == before + 1
                ? adapter.MessageService.Messages[^1]
                : throw new InvalidOperationException("The sample client did not reply exactly once.");
        }

        try
        {
            await manager.ScheduleLoadPluginByName(dispatcher, routes, "sample-greeter");
            await manager.ScheduleLoadPluginByName(dispatcher, routes, "sample-greeter-client");
            if (!manager.GetLoadedPluginNames().Order().SequenceEqual(["sample-greeter", "sample-greeter-client"]))
                throw new InvalidOperationException("The sample plugins did not load.");

            if (await GreetAsync("小白") != "你好，小白！")
                throw new InvalidOperationException("The sample client could not call the greeter service.");

            // 只卸载提供方：调用方不受影响，调用时得到“服务不可用”。
            if (!await manager.ScheduleUnloadPluginByName(dispatcher, "sample-greeter"))
                throw new InvalidOperationException("A named-service provider must unload while its caller stays loaded.");
            if (await GreetAsync("小白") != "问候服务没有加载。")
                throw new InvalidOperationException("Calling an unloaded provider must report the service as unavailable.");

            // 重新加载提供方：调用方不重新加载就能继续使用。
            await manager.ScheduleLoadPluginByName(dispatcher, routes, "sample-greeter");
            if (await GreetAsync("小红") != "你好，小红！")
                throw new InvalidOperationException("The reloaded provider did not serve the existing caller.");

            if (!await manager.ScheduleUnloadPluginByName(dispatcher, "sample-greeter-client") ||
                !await manager.ScheduleUnloadPluginByName(dispatcher, "sample-greeter"))
                throw new InvalidOperationException("The sample plugins did not unload.");
            var notice = JsonSerializer.Serialize(runtime.CreateRestartNotice("plugin"));
            if (!notice.Contains("\"recommended\":false"))
            {
                foreach (var log in logs.GetHistory("system", 50)) Console.WriteLine(log.Message);
                throw new InvalidOperationException("The sample plugins' assemblies were not released: " + notice);
            }
            Console.WriteLine("Sample plugin interop, provider hot swap and clean unload verification passed.");
        }
        finally
        {
            await manager.UnloadAllAsync(dispatcher);
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class LifetimeAdapter : ShiroBot.SDK.Core.IBotAdapter, ShiroBot.SDK.Core.ILifetimeAwareAdapter
    {
        public IPluginLifetime Lifetime { get; set; } = null!;
        public Task? Loop { get; private set; }
        public StopProbe? Resource { get; private set; }
        public bool StoppingCancelledBeforeStop { get; private set; }
        public ShiroBot.SDK.Config.IConfigContext Config { get; set; } = null!;
        public IConsoleLogger Logger { get; set; } = null!;
        public string Platform => "lifetime-adapter";
        public ShiroBot.SDK.Adapter.IMessageService Message => throw new NotSupportedException();
        public ShiroBot.SDK.Adapter.IChannelService Channel => throw new NotSupportedException();
        public ShiroBot.SDK.Adapter.IUserService User => throw new NotSupportedException();
        public ShiroBot.SDK.Adapter.IEventService Event => throw new NotSupportedException();

        public Task StartAsync()
        {
            Resource = Lifetime.Track(new StopProbe());
            Loop = Lifetime.Run(token => Task.Delay(Timeout.Infinite, token));
            return Task.CompletedTask;
        }

        public Task StopAsync()
        {
            StoppingCancelledBeforeStop = Lifetime.Stopping.IsCancellationRequested;
            Resource!.DisposedBeforeStop = Resource.Disposed;
            return Task.CompletedTask;
        }
    }

    private sealed class StopProbe : IDisposable
    {
        public bool Disposed { get; private set; }
        public bool DisposedBeforeStop { get; set; }
        public void Dispose() => Disposed = true;
    }

    private sealed class Probe(List<string> order, string name) : IDisposable
    {
        public bool Disposed { get; private set; }

        public void Dispose()
        {
            Disposed = true;
            order.Add(name);
        }
    }
}
