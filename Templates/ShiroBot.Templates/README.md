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
development host without starting it. Open the project in Rider, set the IDE build
configuration to Debug, and select `ShiroBot`. The same profile supports both Run and Debug
and starts the managed host directly. Keep build-before-launch enabled so Debug builds
prepare the host and install component DLLs with portable symbols automatically.
If project creation skips preparation, the first Debug build downloads the host.
Use the Debug button for breakpoints and C# Hot Reload; no separate Debug profile is needed.
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


## NuGet release gate

Tag releases call `publish-nuget.yml` only after every host runtime build and container build
succeeds; GitHub Release publication waits for NuGet publication too. The NuGet workflow
completes the reusable host CI, the Windows Release solution build and
verification, package content checks, and eight generated consumer builds (plugin and adapter,
each using generic, QQ, Discord, and Telegram contracts) before its publish job can run.
Normal releases build consumers against the exact SDK package being released, from an isolated
local feed and package cache. A template-only patch verifies its consumers against the already
published SDK version.

Only verified artifacts are passed to publication, with SHA-256 checksums checked again after
download. Before obtaining upload credentials, the workflow checks every intended package
version on NuGet.org. Existing versions or a failed version query stop the entire publication;
it does not use `--skip-duplicate` to silently continue. Concurrent release runs are serialized.

NuGet.org does not offer an atomic transaction across SDK and template packages. Preflight
prevents known version collisions, but an upload/network failure can still leave one package
published; the workflow reports failure instead of treating a partial release as success.


To run a full pre-release test without publishing, manually dispatch `publish-nuget.yml`
with the current product `version`, leave `template_version` empty, and enable `test_only`
(the manual-run default). This runs host CI, SDK/template packaging and generated consumer
verification, and uploads test artifacts to Actions only. The publish job, NuGet login,
NuGet upload, tags and GitHub Releases are not executed.

The next coordinated host, SDK and template release is 1.0.0. All generated projects pin SDK 1.0.0 when the template package is packed for that release; no package has been published by this source change.

## Generated project layout

Source files are under `src/` (`src/Plugin.cs` or `src/Adapter.cs`). Development
scripts are under `scripts/`. The project file, central package versions, build
targets, README and `Properties/launchSettings.json` remain at the project root.
The adapter's `TestPlugin` has its own `src/Plugin.cs`. Run the development scripts
from the project root; they resolve their project directory even when invoked elsewhere.
