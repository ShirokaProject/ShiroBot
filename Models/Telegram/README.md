# ShiroBot.Model.Telegram

Protocol-neutral Telegram contracts for ShiroBot plugins and adapters.

Reference this package when a component needs Telegram-specific contracts. The official host
includes this Model assembly. Components should also declare the corresponding runtime package
requirement:

```csharp
[assembly: RequiresShiroBotPackage("shirobot.model.telegram", MinimumVersion = "0.9.0")]
```
