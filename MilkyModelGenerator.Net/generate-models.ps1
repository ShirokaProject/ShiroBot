[CmdletBinding()]
param(
    [string]$Preset,
    [string]$Output,
    [string]$Namespace,
    [string]$IrUrl,
    [string]$IrSource,
    [string]$IrSha256,
    [switch]$Build
)

$ErrorActionPreference = 'Stop'

$projectRoot = $PSScriptRoot
$generatorProject = Join-Path $projectRoot 'MilkyModelGenerator.Net.csproj'

if (-not (Test-Path $generatorProject)) {
    throw "Generator project not found: $generatorProject"
}

$defaultIrUrl = 'https://unpkg.com/@saltify/milky-protocol@1.3.0-rc.1/dist/protocol.json'
$defaultIrSource = '@saltify/milky-protocol@1.3.0-rc.1/dist/protocol.json'
$defaultIrSha256 = '17a4f1da0ce44640ab73840015756227b8180ca5a503433ba4d41a3a82a13ea0'
$defaultOutput = Join-Path $projectRoot 'output\Generated'
$defaultNamespace = 'Milky.Models'

function Read-Choice {
    param(
        [string]$Prompt,
        [string[]]$Options,
        [int]$DefaultIndex = 0
    )

    for ($i = 0; $i -lt $Options.Length; $i++) {
        $marker = if ($i -eq $DefaultIndex) { '*' } else { ' ' }
        Write-Host ("[{0}] {1}. {2}" -f $marker, ($i + 1), $Options[$i])
    }

    while ($true) {
        $raw = Read-Host "$Prompt [$($DefaultIndex + 1)]"
        if ([string]::IsNullOrWhiteSpace($raw)) {
            return $Options[$DefaultIndex]
        }

        $parsed = 0
        if ([int]::TryParse($raw, [ref]$parsed)) {
            $index = $parsed - 1
            if ($index -ge 0 -and $index -lt $Options.Length) {
                return $Options[$index]
            }
        }

        Write-Host 'Invalid selection.' -ForegroundColor Yellow
    }
}

if ([string]::IsNullOrWhiteSpace($Preset)) {
    Write-Host 'Select generation target:'
    $Preset = Read-Choice -Prompt 'Preset' -Options @(
        'Default',
        'Custom'
    ) -DefaultIndex 0
}

switch ($Preset.ToLowerInvariant()) {
    'default' {
        if ([string]::IsNullOrWhiteSpace($Output)) { $Output = $defaultOutput }
        if ([string]::IsNullOrWhiteSpace($Namespace)) { $Namespace = $defaultNamespace }
        if ([string]::IsNullOrWhiteSpace($IrUrl)) { $IrUrl = $defaultIrUrl }
        if ([string]::IsNullOrWhiteSpace($IrSource)) { $IrSource = $defaultIrSource }
        if ([string]::IsNullOrWhiteSpace($IrSha256)) { $IrSha256 = $defaultIrSha256 }
    }
    'custom' {
        if ([string]::IsNullOrWhiteSpace($Output)) {
            $Output = Read-Host "Output directory [$defaultOutput]"
            if ([string]::IsNullOrWhiteSpace($Output)) { $Output = $defaultOutput }
        }

        if ([string]::IsNullOrWhiteSpace($Namespace)) {
            $Namespace = Read-Host "Root namespace [$defaultNamespace]"
            if ([string]::IsNullOrWhiteSpace($Namespace)) { $Namespace = $defaultNamespace }
        }

        if ([string]::IsNullOrWhiteSpace($IrUrl)) {
            $IrUrl = Read-Host "IR URL [$defaultIrUrl]"
            if ([string]::IsNullOrWhiteSpace($IrUrl)) { $IrUrl = $defaultIrUrl }
        }

        if ([string]::IsNullOrWhiteSpace($IrSource)) {
            $IrSource = Read-Host "IR source label [$defaultIrSource]"
            if ([string]::IsNullOrWhiteSpace($IrSource)) { $IrSource = $defaultIrSource }
        }

        if ([string]::IsNullOrWhiteSpace($IrSha256)) {
            $IrSha256 = Read-Host 'Expected IR SHA-256 (leave blank to only record the fetched hash)'
        }
    }
    default {
        throw "Unknown preset: $Preset"
    }
}

if ([System.IO.Path]::IsPathRooted($Output)) {
    $Output = [System.IO.Path]::GetFullPath($Output)
}
else {
    $Output = [System.IO.Path]::GetFullPath((Join-Path $projectRoot $Output))
}

$arguments = @(
    'run',
    '--project', $generatorProject,
    '--',
    '--output', $Output,
    '--namespace', $Namespace,
    '--ir-url', $IrUrl,
    '--ir-source', $IrSource
)

if (-not [string]::IsNullOrWhiteSpace($IrSha256)) {
    $arguments += @('--expected-sha256', $IrSha256)
}

Write-Host ''
Write-Host 'Generating models with:'
Write-Host "  Output:    $Output"
Write-Host "  Namespace: $Namespace"
Write-Host "  IR URL:    $IrUrl"
Write-Host "  IR Source: $IrSource"
Write-Host "  IR SHA256: $IrSha256"
Write-Host ''

& dotnet @arguments
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

if ($Build) {
    $projectName = Split-Path -Leaf (Split-Path -Parent $Output)
    $targetProject = Join-Path (Split-Path -Parent $Output) ($projectName + '.csproj')
    if (Test-Path $targetProject) {
        Write-Host ''
        Write-Host "Building $targetProject"
        & dotnet build $targetProject -c Debug
        exit $LASTEXITCODE
    }

    Write-Host ''
    Write-Host "Build skipped: project not found for output directory $Output" -ForegroundColor Yellow
}
