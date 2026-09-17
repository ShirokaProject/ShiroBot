# ShiroBot.Model.QQ

Protocol-neutral QQ contracts for ShiroBot plugins and adapters.

QQ contracts are distributed by the `ShiroBot.SDK` package and built into the official host. A
component that uses these contracts should declare the runtime package requirement:

```csharp
[assembly: RequiresShiroBotPackage("shirobot.model.qq", MinimumVersion = "0.9.2")]
```

Public contracts follow the API evolution rules documented by ShiroBot API 0.9: existing
constructors and members remain stable, enum values are fixed, and new data is added through
optional properties or capability interfaces.
