# ShiroBot.AvaloniaSdk

Avalonia control rendering contracts and development support for ShiroBot plugins.

## Install

```xml
<ItemGroup>
  <PackageReference Include="ShiroBot.SDK" Version="0.7.1" />
  <PackageReference Include="ShiroBot.AvaloniaSdk" Version="0.7.1" />
</ItemGroup>
```

Ordinary plugins and adapters should reference only `ShiroBot.SDK`. Add this package when a plugin
contains Avalonia controls, AXAML resources or calls the strongly typed control renderer.

## Render A Control

```csharp
using ShiroBot.AvaloniaSdk;

var png = await Context.RenderControlPngAsync<MyCardView>(
    viewModel,
    new ControlRenderOptions(RenderTheme.Auto, 192));
```

AXAML files should use the `AvaloniaResource` build action. Non-Release builds enable Avalonia
previewer support by default; set `ShiroBotAvaloniaPreviewerSupport` to `false` when a project does
not need it.

## Runtime Model

The ShiroBot host supplies `ShiroBot.AvaloniaSdk`, Avalonia, SkiaSharp, HarfBuzzSharp and MicroCom.
SDK packaging removes those shared managed and native assets from plugin output, so deploy only the
plugin assembly and its manifest-managed private native dependencies.

The assembly name, `ShiroBot.AvaloniaSdk` namespace and public rendering contracts remain compatible
with plugins compiled against version 0.6.
