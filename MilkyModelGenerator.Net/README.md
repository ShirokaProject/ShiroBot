# MilkyModelGenerator.Net

IR to C# models generator for the Milky IR schema.

## ABI compatibility

Before deleting the existing output, the generator parses positional records in `Generated` with
Roslyn. When the new IR adds fields it keeps every previously emitted constructor overload and
`Deconstruct` overload, supplying `default!` or the IR default for newly required arguments.

Generation fails with an explicit major-breaking-change error when an existing positional field is
removed, renamed, reordered, or changes type, or when a new shape collides with an old signature but
would map arguments to different properties. Existing compatibility overloads are parsed and carried
forward on later generations.

The default IR URL is locked to SHA-256
`17a4f1da0ce44640ab73840015756227b8180ca5a503433ba4d41a3a82a13ea0`. The generated
`_GeneratedInfo.cs` records source label, URL, SHA-256, Milky version and package version. Review ABI
changes before updating the lock; do not silently regenerate from a changed mutable URL.

Output layout:

- `Common`: shared structs and unions from `commonStructs`
- `System/Requests`, `System/Responses`
- `Message/Requests`, `Message/Responses`
- `Friend/Requests`, `Friend/Responses`
- `Group/Requests`, `Group/Responses`
- `File/Requests`, `File/Responses`

Default output:

```powershell
.\output\Generated
```

Default namespace:

```powershell
Milky.Models
```

Run directly:

```powershell
dotnet run --project .\MilkyModelGenerator.Net.csproj
```

Run the local compatibility fixture without downloading or replacing the current models:

```powershell
dotnet run --project .\MilkyModelGenerator.Net.csproj -- --self-test
```

Interactive wrapper:

```powershell
.\generate-models.ps1
```

Default wrapper usage:

```powershell
.\generate-models.ps1 -Preset Default
```

Custom target example:

```powershell
dotnet run --project .\MilkyModelGenerator.Net.csproj -- `
  --output C:\path\to\Generated `
  --namespace My.Models `
  --ir-url https://unpkg.com/@saltify/milky-protocol@1.3.0-rc.1/dist/protocol.json `
  --ir-source @saltify/milky-protocol@1.3.0-rc.1/dist/protocol.json `
  --expected-sha256 17a4f1da0ce44640ab73840015756227b8180ca5a503433ba4d41a3a82a13ea0
```

Equivalent wrapper usage:

```powershell
.\generate-models.ps1 -Preset Custom `
  -Output C:\path\to\Generated `
  -Namespace My.Models `
  -IrUrl https://unpkg.com/@saltify/milky-protocol@1.3.0-rc.1/dist/protocol.json `
  -IrSource @saltify/milky-protocol@1.3.0-rc.1/dist/protocol.json `
  -IrSha256 17a4f1da0ce44640ab73840015756227b8180ca5a503433ba4d41a3a82a13ea0
```
