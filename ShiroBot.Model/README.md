# ShiroBot.Model

Shared Milky protocol models used by ShiroBot plugins, adapters and the host.

## Install

Most projects receive this package transitively from `ShiroBot.SDK`. Reference it directly only when
building model-only tooling:

```xml
<PackageReference Include="ShiroBot.Model" Version="1.3.0-rc2" />
```

Models are grouped into these namespaces:

- `ShiroBot.Model.Common`
- `ShiroBot.Model.System.Requests` and `Responses`
- `ShiroBot.Model.Message.Requests` and `Responses`
- `ShiroBot.Model.Friend.Requests` and `Responses`
- `ShiroBot.Model.Group.Requests` and `Responses`
- `ShiroBot.Model.File.Requests` and `Responses`

## Compatibility

Version 1.3.0-rc2 is generated from the locked `@saltify/milky-protocol@1.3.0-rc.1` IR. Additive fields
preserve previously emitted constructors and `Deconstruct` overloads so existing plugin binaries can
continue loading. Removed, renamed, reordered or type-changed fields are rejected by the generator and
require a deliberate major model release.

`EventMetadataRegistry` includes generated Milky `event_type` and `message_scene` discriminator maps.
Adapters should use it instead of maintaining handwritten event type dictionaries.

## Regeneration

`Generated/` may be overwritten; hand-written extensions belong in `Manual/`. From the repository
root, run:

```bash
dotnet run --project ./MilkyModelGenerator.Net/MilkyModelGenerator.Net.csproj -- --self-test
dotnet run --project ./MilkyModelGenerator.Net/MilkyModelGenerator.Net.csproj -- \
  --output ./ShiroBot.Model/Generated \
  --namespace ShiroBot.Model \
  --expected-sha256 17a4f1da0ce44640ab73840015756227b8180ca5a503433ba4d41a3a82a13ea0
```
