# AdapterTemplate

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
