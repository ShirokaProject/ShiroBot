# PluginTemplate

Build the plugin with `dotnet build -c Release`. The generated project includes a `ping` command
that replies with `pong`; replace it with your own routes in `Plugin.cs`.

The generated `.github/workflows/release.yml` builds the project on pushes and pull requests.
Before releasing, set the `Version` in `Plugin.cs` to the version you want, then push a matching
tag such as `v1.0.0`. GitHub Actions will publish a ZIP and the plugin DLL to GitHub Releases.
The ZIP contains the complete Release build output; install the DLL directly only when no extra files
are needed. GitHub Actions must be enabled for the repository.
