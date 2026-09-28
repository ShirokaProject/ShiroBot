# ShiroBot.Templates

Install the templates:

```bash
dotnet new install ShiroBot.Templates
```

Create a plugin:

```bash
dotnet new shirobot-plugin -n MyPlugin --creator "Your Name"
```

Create an adapter:

```bash
dotnet new shirobot-adapter -n MyAdapter --platform discord --creator "Your Name"
```

Use `dotnet new shirobot-plugin --help` or `dotnet new shirobot-adapter --help` for all options.

Both generated projects include `.github/workflows/release.yml`. It builds on pushes and pull
requests and publishes the DLL plus a ZIP of the full Release build output when a matching `v*` tag is
pushed. Update the component's `Version` metadata before tagging.
