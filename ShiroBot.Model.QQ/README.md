# ShiroBot.Model.QQ

Protocol-neutral QQ contracts for ShiroBot plugins and adapters.

Reference this package when a component needs QQ entities, message segments, events, or optional
adapter extension interfaces. The matching Model DLL must be installed in the host `models`
directory. Components should also declare the runtime package requirement:

```csharp
[assembly: RequiresShiroBotPackage("shirobot.model.qq", MinimumVersion = "0.8.0")]
```

Public contracts follow the API evolution rules documented by ShiroBot API 0.8: existing
constructors and members remain stable, enum values are fixed, and new data is added through
optional properties or capability interfaces.
