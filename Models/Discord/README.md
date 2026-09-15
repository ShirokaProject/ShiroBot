# ShiroBot.Model.Discord

Protocol-neutral Discord contracts for ShiroBot plugins and adapters.

Reference this package when a component needs Discord-specific contracts. The official host
includes this Model assembly. Components should also declare the corresponding runtime package
requirement:

```csharp
[assembly: RequiresShiroBotPackage("shirobot.model.discord", MinimumVersion = "0.9.0")]
```
