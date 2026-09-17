# ShiroBot.Model.Discord

Protocol-neutral Discord contracts for ShiroBot plugins and adapters.

Discord contracts are distributed by the `ShiroBot.SDK` package and built into the official host.
A component that uses these contracts should declare the runtime package requirement:

```csharp
[assembly: RequiresShiroBotPackage("shirobot.model.discord", MinimumVersion = "0.9.2")]
```
