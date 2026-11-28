# ShiroBot.SDK

Contracts and helpers for building [ShiroBot](https://github.com/ShirokaProject/ShiroBot) plugins and
adapters. The package also contains the built-in QQ, Discord and Telegram Model contracts and the Avalonia
rendering references, so one package reference is all a component needs.

The host supplies these assemblies at runtime; component outputs do not ship them.

## Getting started

Create a project from the templates (recommended):

```bash
dotnet new install ShiroBot.Templates
dotnet new shirobot-plugin -n MyPlugin
```

Or reference the package directly:

```bash
dotnet add package ShiroBot.SDK
```

A minimal plugin:

```csharp
using ShiroBot.SDK.Core;
using ShiroBot.SDK.Plugin;

[BotPlugin("HelloPlugin", Name = "HelloPlugin", Version = "1.0.0")]
public sealed class HelloPlugin : PluginBase
{
    protected override void ConfigureRoutes()
    {
        AllCommands.MapExact("ping", message => Context.Message.ReplyAsync(message, "pong"));
    }
}
```

## Version and ABI

Package versions follow the host version. Each shared assembly keeps its own ABI version:

| ShiroBot.SDK (NuGet) | SDK ABI | QQ Model ABI | Discord Model ABI | Telegram Model ABI |
| --- | --- | --- | --- | --- |
| 1.0.5 | 1.5.0.0 | 1.2.0.0 | 1.1.0.0 | 1.1.0.0 |
| 1.0.4 | 1.5.0.0 | 1.1.0.0 | 1.1.0.0 | 1.1.0.0 |
| 1.0.3 | 1.4.0.0 | 1.0.0.0 | 1.0.0.0 | 1.0.0.0 |
| 1.0.2 | 1.4.0.0 | 1.0.0.0 | 1.0.0.0 | 1.0.0.0 |

A component loads when, for every SDK or Model assembly it references, the ABI major version matches the
host's and the host's version is not lower. The host checks the actual DLL references when loading.

## Documentation

- [Create a plugin](https://docs.shiroka.org/plugin/)
- [Messages and events](https://docs.shiroka.org/plugin/routes-events)
- [Message segments](https://docs.shiroka.org/plugin/segments)
- [Config and context](https://docs.shiroka.org/plugin/context-config)
- [Calling other plugins](https://docs.shiroka.org/plugin/interop)
- [Dashboard pages](https://docs.shiroka.org/plugin/dashboard-pages)
- [Single-DLL packaging and native dependencies](https://docs.shiroka.org/plugin/packaging-native)
- [Platform Models: QQ, Discord, Telegram](https://docs.shiroka.org/plugin/models)
- [Create an adapter](https://docs.shiroka.org/adapter/)
- [ABI compatibility](https://docs.shiroka.org/plugin/api-compatibility)
- [Changelog](https://docs.shiroka.org/changelog/)
