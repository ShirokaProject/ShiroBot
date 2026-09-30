# Plugin and Adapter API Compatibility

ShiroBot API `0.9.1` is the current compatibility level. It adds host-managed component configuration (`IConfigurableComponent`, `PluginBase<TConfig>`) on top of `0.9`. Existing components that do not declare API
metadata are treated as requiring `0.8`, so newer hosts continue to load them without recompilation.

Components may declare their supported range in metadata:

```csharp
[assembly: ShiroBotApiCompatibility("0.9.1", "0.9.1")]

[BotPlugin("example")]
public sealed class ExamplePlugin : PluginBase;
```

Adapters use the same standalone compatibility attribute. `MinimumVersion` is the hard requirement;
`MaximumVersion` records the newest host API tested by the component. A newer host may load a
component beyond that tested version because host APIs evolve additively. An older host rejects a
component whose minimum version is newer before creating the component load context.

## Evolution rules

- Do not remove, rename, or change the signature of an existing public member within one API version.
- Do not add an abstract member to an existing public interface. Add a default implementation or a
  new capability interface instead.
- Give public enum members explicit numeric values. Append new members; never reorder existing ones.
- Do not change positional record constructor parameters or their order. Add optional `init`
  properties when a contract must grow.
- Do not add `required` properties to an existing public type.
- Deprecate public members with `ObsoleteAttribute` for at least one major API-version transition.
- SDK and built-in Model contracts loaded into the Default ALC require a host restart. Private
  component implementation assemblies support normal hot reload.
- Change `ShiroBotApi.CurrentVersion` only for a deliberate compatibility transition and update component
  ranges explicitly.

Shared SDK and Model assembly versions represent the minimum host ABI required by a component.
The host may satisfy references to the same or an older ABI, so a newer host continues to load old
plugins. A host never satisfies a reference to a newer ABI, so a plugin compiled against newly added
contracts correctly requires that host version or later. ABI changes must remain additive: do not
remove or change existing public contracts merely because `AssemblyVersion` increases.

## Version map

The NuGet package version follows the host release. ABI versions only increase when that assembly's
public contracts change, so one ABI can span several SDK releases. A plugin built against an SDK
package loads on any host whose ABI for every referenced assembly is the same or newer.

| ShiroBot.SDK (NuGet) | ShiroBot API | SDK ABI | QQ Model ABI | Discord Model ABI | Telegram Model ABI |
| --- | --- | --- | --- | --- | --- |
| 0.9.5 | 0.9.1 | 0.9.2.0 | 0.9.2.0 | 0.9.0.0 | 0.9.0.0 |
| 0.9.4 | 0.9.1 | 0.9.2.0 | 0.9.2.0 | 0.9.0.0 | 0.9.0.0 |
| 0.9.3 | 0.9 | 0.9.1.0 | 0.9.1.0 | 0.9.0.0 | 0.9.0.0 |
| 0.9.2 | 0.9 | 0.9.0.0 | 0.9.0.0 | 0.9.0.0 | 0.9.0.0 |
| 0.9.1 | 0.9 | 0.9.0.0 | 0.9.0.0 | 0.9.0.0 | 0.9.0.0 |

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
