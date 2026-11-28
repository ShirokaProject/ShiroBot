# ShiroBot.Templates

`dotnet new` templates for [ShiroBot](https://github.com/ShirokaProject/ShiroBot) plugins and adapters.

## Install

```bash
dotnet new install ShiroBot.Templates
```

## Create a project

```bash
# Plugin
dotnet new shirobot-plugin -n MyPlugin --creator "Your Name" --allow-scripts yes

# Adapter
dotnet new shirobot-adapter -n MyAdapter --platform discord --creator "Your Name" --allow-scripts yes
```

Run `dotnet new shirobot-plugin --help` or `dotnet new shirobot-adapter --help` for all options.

## What you get

- `src/Plugin.cs` or `src/Adapter.cs`, a `ShiroBot.SDK` reference pinned to the template's host version,
  and development scripts under `scripts/`.
- A `ShiroBot` launch profile: Run and Debug (breakpoints, C# Hot Reload) start a local development host on
  port 7002 with your component installed. With `--allow-scripts yes` the host is downloaded when the project
  is created; otherwise the first Debug build downloads it.
- Adapter projects also include a `TestPlugin` for checking message reception and replies during
  development. It is not part of the release package.
- `.github/workflows/release.yml`: builds on every push and pull request, and publishes the DLL plus a ZIP
  of the Release output when you push a `v*` tag. Update the component's `Version` before tagging.

## Documentation

- [Create a plugin](https://docs.shiroka.org/plugin/)
- [Create an adapter](https://docs.shiroka.org/adapter/)
