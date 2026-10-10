# AdapterTemplate

Open `AdapterTemplate.slnx` in Rider to work on the adapter and `TestPlugin` together.
The development scripts install both components into the same host. The test plugin uses
only generic SDK models and provides `ping` (replies `pong`), `adapter-info` (shows the
mapped message fields), and `adapter-echo <text>` (sends the text back). Implement the
adapter's receive/send paths, then send these commands from the target platform.
`TestPlugin` is a separate project and is excluded from the adapter Release package.

In Rider, select `ShiroBot` in the top-right run selector and set the IDE build
configuration to Debug. Use the same profile for Run or Debug: it starts the managed
host directly, so the Debug button can attach the .NET debugger for plugin/adapter
breakpoints and C# Hot Reload. Keep the configuration's build-before-launch step enabled.
Debug builds automatically prepare the host and install component DLLs with portable
symbols and no assembly merging before launch. Release builds retain distribution packaging.
After project creation, allow the template setup script to prepare the component and download
the matching host without starting it. On the CLI, use `--allow-scripts yes`; if your IDE skips
setup, the first Debug build prepares the host automatically. You can also prepare it separately
with `sh dev.sh --prepare` or `powershell -ExecutionPolicy Bypass -File dev.ps1 --prepare`.
New development hosts use port 7002; existing local configuration is preserved.

Build the adapter with `dotnet build -c Release`. Implement platform connectivity in `Adapter.cs`,
map platform payloads to the common SDK models, and publish events through `AdapterEventService`.

To try the adapter in a local ShiroBot host, run `sh dev.sh` on macOS/Linux or
`powershell -ExecutionPolicy Bypass -File dev.ps1` on Windows. The script builds the adapter,
downloads the host release matching the pinned `ShiroBot.SDK` version, installs the build output
into `.shirobot-dev/` as an enabled adapter, and starts the host. The adapter's `config.toml` is
created beside it and can also be edited from the Dashboard. Pass host options after the script name,
for example `sh dev.sh --no-console`. The host and its local configuration stay in `.shirobot-dev/`
and are ignored by Git. When the pinned SDK version changes, only the host executable is replaced;
configuration, adapters and plugin data are kept.
Each run removes files that an earlier run copied but the build no longer produces; files the
adapter or host created in its directory are left alone. Re-run the script after changing the adapter; stop the host first.

The generated `.github/workflows/release.yml` builds the project on pushes and pull requests.
Before releasing, set the `Version` in `Adapter.cs` to the version you want, then push a matching
tag such as `v1.0.0`. GitHub Actions will publish a ZIP and the adapter DLL to GitHub Releases.
The ZIP contains the complete Release build output; install the DLL directly only when no extra files
are needed. GitHub Actions must be enabled for the repository.
