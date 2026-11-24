# HostForge packages

NuGet source of `ChsBuffer.Avalonia.AppHost` from the ShiroBot fork of
[HostForge](https://github.com/greepar/HostForge/tree/shirobot). Restoring `Core/ShiroBot.csproj` downloads the
packages from the fork's release and checks their SHA-256 (`eng/HostForge.targets`); the `.nupkg` files are not
committed.

If an IDE restores before the packages are here, run `dotnet restore` once.
