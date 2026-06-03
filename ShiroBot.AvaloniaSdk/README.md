# ShiroBot.AvaloniaSdk

Avalonia rendering SDK for ShiroBot plugins.

This package contains plugin-side abstractions for requesting Avalonia rendering from the ShiroBot host. Runtime Avalonia assemblies are provided by the host integration module (`ShiroBot.AvaloniaIntegration`) when `EnableAvalonia=true`.

## Usage

Reference this package from a ShiroBot plugin that needs Avalonia rendering, then use `IBotContext.Render` and `AsAvalonia()` to access the renderer.

```csharp
var avalonia = Context.Render?.AsAvalonia();
if (avalonia is null)
{
    await Context.Message.ReplyAsync(message, "Host Avalonia rendering is not enabled.");
    return;
}
```

The consuming plugin should not ship its own Avalonia runtime copies; the host provides them from the default AssemblyLoadContext.
