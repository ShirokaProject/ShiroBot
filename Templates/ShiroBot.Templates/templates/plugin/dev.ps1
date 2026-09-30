$ErrorActionPreference = 'Stop'
# Windows PowerShell 5.1 downloads far slower while drawing the progress bar.
$ProgressPreference = 'SilentlyContinue'
Set-Location $PSScriptRoot

[xml]$packages = Get-Content Directory.Packages.props -Raw
$sdkVersion = @($packages.Project.ItemGroup.PackageVersion | Where-Object { $_.Include -eq 'ShiroBot.SDK' } | Select-Object -First 1).Version
if (-not $sdkVersion) { throw 'Cannot read ShiroBot.SDK version from Directory.Packages.props.' }

$architecture = if ([System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture -eq 'Arm64') { 'arm64' } else { 'x64' }
$rid = "win-$architecture"
# The host directory is not versioned: config.toml, plugins/ and adapters/ survive SDK upgrades.
$cacheDir = Join-Path $PSScriptRoot ".shirobot-dev/host/$rid"
$hostExe = Join-Path $cacheDir 'ShiroBot.exe'
$versionFile = Join-Path $cacheDir '.host-version'
$archiveName = "shirobot-host-$rid-framework-dependent.zip"
$archive = Join-Path $cacheDir $archiveName

dotnet build PluginTemplate.csproj -c Release
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$installedVersion = if (Test-Path $versionFile) { "$(Get-Content $versionFile -Raw)".Trim() } else { '' }
if (-not (Test-Path $hostExe) -or $installedVersion -ne $sdkVersion) {
    New-Item -ItemType Directory -Force $cacheDir | Out-Null
    $url = "https://github.com/ShirokaProject/ShiroBot/releases/download/v$sdkVersion/$archiveName"
    Write-Host "Downloading ShiroBot v$sdkVersion for $rid..."
    Invoke-WebRequest -Uri $url -OutFile $archive
    # The archive only contains the host executable, so the existing local data is kept.
    Expand-Archive -Path $archive -DestinationPath $cacheDir -Force
    Remove-Item $archive
    Set-Content -Path $versionFile -Value $sdkVersion -Encoding ASCII
}

$pluginDir = Join-Path $cacheDir 'plugins/PluginTemplate'
$buildDir = (Resolve-Path 'bin/Release/net10.0').Path
$manifest = Join-Path $pluginDir '.shirobot-dev-files'
New-Item -ItemType Directory -Force $pluginDir | Out-Null
# Remove files copied by the previous run that the build no longer produces. Files the
# plugin or host created there (config.toml, data, .shirobot/native, ...) are never touched.
if (Test-Path -LiteralPath $manifest) {
    foreach ($file in Get-Content -LiteralPath $manifest) {
        if (-not $file -or $file.Contains('..')) { continue }
        if (-not (Test-Path -LiteralPath (Join-Path $buildDir $file))) {
            $stale = Join-Path $pluginDir $file
            if (Test-Path -LiteralPath $stale) { Remove-Item -LiteralPath $stale -Force }
        }
    }
}
Copy-Item "$buildDir/*" $pluginDir -Recurse -Force
# config.toml is never listed, so a user-edited config is not deleted if the build stops emitting one.
Get-ChildItem -LiteralPath $buildDir -Recurse -File | Where-Object { $_.Name -ne 'config.toml' } |
    ForEach-Object { $_.FullName.Substring($buildDir.Length).TrimStart('\', '/') -replace '\\', '/' } |
    Set-Content -LiteralPath $manifest -Encoding UTF8

Write-Host "Starting ShiroBot v$sdkVersion with PluginTemplate..."
Push-Location $cacheDir
try {
    & $hostExe @args
    exit $LASTEXITCODE
} finally {
    Pop-Location
}
