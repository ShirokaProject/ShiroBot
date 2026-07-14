# ShiroBot.Model

`Generated/` contains code generated from Milky IR and may be overwritten.

The generator reads the current positional-record ABI before overwriting this directory. Added IR
fields preserve old constructor and `Deconstruct` overloads; removed, renamed, reordered or changed
fields fail generation and require a deliberate major model release. `_GeneratedInfo.cs` records the
locked IR URL, SHA-256 and Milky versions.

`Manual/` contains hand-written models and extensions and will not be touched by the generator.

`Manual/` mirrors the generated layout:

- `Manual/Common`
- `Manual/System/Requests`, `Manual/System/Responses`
- `Manual/Message/Requests`, `Manual/Message/Responses`
- `Manual/Friend/Requests`, `Manual/Friend/Responses`
- `Manual/Group/Requests`, `Manual/Group/Responses`
- `Manual/File/Requests`, `Manual/File/Responses`

To regenerate:

```powershell
dotnet run --project .\MilkyModelGenerator.Net\MilkyModelGenerator.Net.csproj -- `
  --output .\ShiroBot.Model\Generated `
  --namespace ShiroBot.Model `
  --expected-sha256 45ad273eae90347596ecf6f1cd0c9ad59b70a8835779e640664b35858b1a531b
```

```bash
dotnet run --project ./MilkyModelGenerator.Net/MilkyModelGenerator.Net.csproj -- \
  --output ./ShiroBot.Model/Generated \
  --namespace ShiroBot.Model \
  --expected-sha256 45ad273eae90347596ecf6f1cd0c9ad59b70a8835779e640664b35858b1a531b
```

Run `dotnet run --project ./MilkyModelGenerator.Net/MilkyModelGenerator.Net.csproj -- --self-test`
before regeneration to validate the compatibility fixture without touching `Generated`.
