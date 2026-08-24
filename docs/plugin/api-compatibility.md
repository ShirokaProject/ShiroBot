# Plugin and Adapter API Compatibility

ShiroBot API `0.8` is the current compatibility baseline for plugins and adapters built against the
current SDK. Existing components that do not declare an API range are treated as supporting `0.8`,
so they continue to load without recompilation.

Components may declare their supported range in metadata:

```csharp
[assembly: ShiroBotApiCompatibility("0.8", "0.8")]

[BotPlugin("example")]
public sealed class ExamplePlugin : PluginBase;
```

Adapters use the same standalone compatibility attribute. Keeping the compatibility declaration
separate lets older hosts ignore metadata they do not understand. The new host reads it without
loading component code and rejects incompatible ranges before creating the component load context.

## Evolution rules

- Do not remove, rename, or change the signature of an existing public member within one API version.
- Do not add an abstract member to an existing public interface. Add a default implementation or a
  new capability interface instead.
- Give public enum members explicit numeric values. Append new members; never reorder existing ones.
- Do not change positional record constructor parameters or their order. Add optional `init`
  properties when a contract must grow.
- Do not add `required` properties to an existing public type.
- Deprecate public members with `ObsoleteAttribute` for at least one major API-version transition.
- SDK and contracts loaded into the Default ALC require a host restart. Model packages use a
  coordinated collectible ALC and may be reloaded only after dependent adapters and plugins stop.
  Private component implementation assemblies support normal hot reload.
- Change `ShiroBotApi.CurrentVersion` only for a deliberate compatibility transition and update component
  ranges explicitly.

`ShiroBot.SDK` keeps a stable `AssemblyVersion` for its current compatibility line. NuGet package
and file versions may increase independently for compatible releases.

## Multi-adapter background work

Event handlers automatically use the adapter that produced the event. Timers, dashboard actions,
and other background work must select a platform explicitly when more than one adapter is loaded:

```csharp
using (Context.UsePlatform("discord"))
{
    await Context.Message.SendMessageAsync(channel, segments);
}
```

The scope flows through asynchronous calls and restores the previous platform when disposed.
