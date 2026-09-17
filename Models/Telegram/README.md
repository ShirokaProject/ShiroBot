# ShiroBot.Model.Telegram

Protocol-neutral Telegram contracts for ShiroBot plugins and adapters.

Telegram contracts are distributed by the `ShiroBot.SDK` package and built into the official host.
A component that uses these contracts should declare the runtime package requirement:

```csharp
[assembly: RequiresShiroBotPackage("shirobot.model.telegram", MinimumVersion = "0.9.2")]
```
