# PluginTemplate

Build the plugin with `dotnet build -c Release`. The generated project includes a `ping` command
that replies with `pong`; replace it with your own routes in `Plugin.cs`.

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

To try the plugin in a local ShiroBot host, run `sh dev.sh` on macOS/Linux or
`powershell -ExecutionPolicy Bypass -File dev.ps1` on Windows. The script builds the plugin,
downloads the host release matching the pinned `ShiroBot.SDK` version, installs the build output
into `.shirobot-dev/`, and starts the host. Pass host options after the script name, for example
`sh dev.sh --no-console`. The host and its local configuration stay in `.shirobot-dev/` and are
ignored by Git. When the pinned SDK version changes, only the host executable is replaced;
configuration, adapters and plugin data are kept.
Each run removes files that an earlier run copied but the build no longer produces; files the
plugin or host created in its directory are left alone. Re-run the script after changing the plugin; stop the host first.

The generated `.github/workflows/release.yml` builds the project on pushes and pull requests.
Before releasing, set the `Version` in `Plugin.cs` to the version you want, then push a matching
tag such as `v1.0.0`. GitHub Actions will publish a ZIP and the plugin DLL to GitHub Releases.
The ZIP contains the complete Release build output; install the DLL directly only when no extra files
are needed. GitHub Actions must be enabled for the repository.

## First launch troubleshooting

If Rider reports an invalid executable path, build the project in Debug first and
check that `.shirobot-dev/host/<RID>/ShiroBot` (`ShiroBot.exe` on Windows) exists.
Host downloads require a published release matching the SDK product version. A 404 for
an unreleased SDK version requires a locally built matching host, not an older release.
Copy its publish output into that host directory and write the matching SDK product
version into `.host-version` to skip downloading. Keep the host executable permission.

Open `http://127.0.0.1:7002/dashboard/` after launch. The API login token is generated
in the development host's `config.toml` under `[api].token`. Configure and connect an
adapter instance before testing chat commands. See the host documentation's plugin
quick start for the complete Run/Debug and local development version setup.
