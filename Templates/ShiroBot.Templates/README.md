# ShiroBot.Templates

Install the templates:

```bash
dotnet new install ShiroBot.Templates
```

Create a plugin:

```bash
dotnet new shirobot-plugin -n MyPlugin --creator "Your Name" --allow-scripts yes
```

Create an adapter:

```bash
dotnet new shirobot-adapter -n MyAdapter --platform discord --creator "Your Name" --allow-scripts yes
```

With scripts allowed, project creation builds the component and downloads the matching
development host without starting it. Open the project in Rider and click the `ShiroBot` Run
configuration. If the IDE skips preparation, the first Run downloads the host automatically.
For breakpoints and C# Hot Reload, select `ShiroBot Debug` and start Debug with the IDE
build configuration set to Debug. This starts the managed host directly; Debug builds
prepare the host and install unmerged component DLLs with portable symbols automatically.
Release builds retain distribution packaging. New development hosts use port 7002.

Adapter projects include a solution and a separate `TestPlugin` project for checking
message reception, model mapping and replies. Development starts both components;
the adapter release package excludes the test plugin.

Use `dotnet new shirobot-plugin --help` or `dotnet new shirobot-adapter --help` for all options.

The template package injects the host product version into the generated project's
`ShiroBot.SDK` package reference when packed. Existing projects keep their pinned version
until the developer upgrades it.

Both generated projects include `.github/workflows/release.yml`. It builds on pushes and pull
requests and publishes the DLL plus a ZIP of the full Release build output when a matching `v*` tag is
pushed. Update the component's `Version` metadata before tagging.

For SDK 0.9.9, install `ShiroBot.Templates` 0.9.10. The NuGet template package 0.9.9
contains unresolved SDK version placeholders; the 0.9.10 template patch fixes them while
continuing to reference SDK 0.9.9. Template patch versions can therefore differ from SDK
versions. The next SDK release must publish a fresh template version rather than reusing
0.9.10; `publish-nuget.yml` accepts `template_version` for this purpose.
