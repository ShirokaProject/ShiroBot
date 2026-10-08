# PluginTemplate

Build the plugin with `dotnet build -c Release`. The generated project includes a `ping` command
that replies with `pong`; replace it with your own routes in `Plugin.cs`.

In Rider, select `ShiroBot` in the top-right run selector
and click Run. The profiles in `Properties/launchSettings.json` call the development scripts below.
After project creation, allow the template setup script to prepare the component and download
the matching host without starting it. On the CLI, use `--allow-scripts yes`; if your IDE skips
setup, the first Run downloads the host automatically. You can also prepare it separately
with `sh dev.sh --prepare` or `powershell -ExecutionPolicy Bypass -File dev.ps1 --prepare`.
The Run button builds the component in Debug with portable symbols and no assembly merging,
then starts the prepared host. Release builds retain the distribution packaging.
For breakpoints and C# Hot Reload, choose `ShiroBot Debug` and click Debug with the IDE
build configuration set to Debug. This profile starts the managed host directly; the Debug
build prepares its component files beforehand. The script-based `ShiroBot` profile is for Run.
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
