# ShiroBot.AvaloniaSdk

Avalonia rendering contracts and development support for ShiroBot plugins.

Reference this package only when a plugin contains Avalonia controls or calls the strongly typed
control rendering APIs. Ordinary plugins and adapters should reference `ShiroBot.SDK` alone.

```xml
<ItemGroup>
  <PackageReference Include="ShiroBot.SDK" Version="0.7.0" />
  <PackageReference Include="ShiroBot.AvaloniaSdk" Version="0.7.0" />
</ItemGroup>
```

```csharp
using ShiroBot.AvaloniaSdk;

var png = await Context.RenderControlPngAsync<MyCardView>(
    viewModel,
    new ControlRenderOptions(RenderTheme.Auto, 192));
```

The ShiroBot host supplies `ShiroBot.AvaloniaSdk`, Avalonia, SkiaSharp and HarfBuzzSharp at runtime.
Deploy only the plugin assembly; do not ship private copies of those shared assemblies.

The assembly name, namespace and public rendering contracts remain compatible with 0.6 plugins.
