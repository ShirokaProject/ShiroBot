# PluginTemplate

Build the plugin with `dotnet build -c Release`. The generated project includes a `ping` command
that replies with `pong`; replace it with your own routes in `Plugin.cs`.

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
