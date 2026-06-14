# ShiroBot.AvaloniaSdk

Avalonia rendering SDK for ShiroBot plugins.

This package contains plugin-side abstractions for requesting Avalonia rendering from the ShiroBot host. Runtime Avalonia assemblies are provided by the host integration module (`ShiroBot.AvaloniaIntegration`) when `EnableAvalonia=true`.

## Usage

Reference this package from a ShiroBot plugin that needs Avalonia rendering, then import the extension methods:

```csharp
using ShiroBot.AvaloniaSdk;
```

Render an `.axaml` `UserControl` with a view model:

```csharp
var png = await Context.RenderControlPngAsync<MyCardView>(
    viewModel,
    new ControlRenderOptions(RenderTheme.Auto, 192));
```

Or render to a temporary `file://` URI:

```csharp
var uri = await Context.RenderControlPngToFileUriAsync<MyCardView>(
    viewModel,
    new ControlRenderOptions(RenderTheme.Light));
```

`RenderControlPngAsync<TView>` creates the control on the Avalonia UI thread and sets `DataContext` for you. This is the recommended and only supported control rendering API for plugin views.

Theme options:

```csharp
RenderTheme.Light // default
RenderTheme.Dark
RenderTheme.Auto  // switches light/dark by host time rule
```

`ControlRenderOptions` defaults to:

```csharp
new ControlRenderOptions(
    Theme: RenderTheme.Light,
    Dpi: 192);
```

The host must be built with `EnableAvalonia=true`; otherwise the extension method throws an `InvalidOperationException` explaining that Avalonia rendering is unavailable.

The consuming plugin should not ship its own Avalonia runtime copies; the host provides them from the default AssemblyLoadContext.
