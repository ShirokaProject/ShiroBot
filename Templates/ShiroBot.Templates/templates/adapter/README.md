# AdapterTemplate

Build the adapter with `dotnet build -c Release`. Implement platform connectivity in `Adapter.cs`,
map platform payloads to the common SDK models, and publish events through `AdapterEventService`.

The generated `.github/workflows/release.yml` builds the project on pushes and pull requests.
Before releasing, set the `Version` in `Adapter.cs` to the version you want, then push a matching
tag such as `v1.0.0`. GitHub Actions will publish a ZIP and the adapter DLL to GitHub Releases.
The ZIP contains the complete Release build output; install the DLL directly only when no extra files
are needed. GitHub Actions must be enabled for the repository.
