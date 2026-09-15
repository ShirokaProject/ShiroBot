# NuGet Central Package Management Conversion

## 1. Conversion overview

The `Shirobot.slnx` scope was converted to NuGet Central Package Management (CPM).

- Projects in solution scope: 7
- Projects with direct NuGet package references: 2
- Unique centrally managed package IDs: 10
- Generated project template scopes: 2 (`shirobot-plugin` and `shirobot-adapter`)
- Skipped projects: none
- `packages.config` projects: none
- `VersionOverride` entries: none
- Removed MSBuild version properties: none

The repository-wide source of truth is `Directory.Packages.props`. Package versions were removed
from `Core/ShiroBot.csproj` and `SDK/ShiroBot.SDK.csproj`, while existing reference metadata such as
`PrivateAssets` and `GeneratePathProperty` was preserved.

Each generated project template includes its own `Directory.Packages.props`. This is intentional:
projects created by `dotnet new` outside this repository cannot inherit the repository-level file.

## 2. Version conflict resolutions

No project-to-project version conflicts existed in the baseline. Every package used a single version
across all projects that referenced it, so no `VersionOverride` was required.

The current central version file contains package upgrades relative to the captured pre-conversion
baseline. These are recorded in the comparison below rather than being hidden as part of the CPM
mechanical conversion.

## 3. Package comparison: baseline vs. result

### Changes

| Package | Affected projects | Baseline | Result | Status |
|---|---|---:|---:|---|
| Avalonia | Core, SDK | 12.1.0 | 12.1.2 | Central version upgraded |
| Avalonia.HarfBuzz | SDK | 12.1.0 | 12.1.1 | Central version upgraded |
| Avalonia.Headless | Core | 12.1.0 | 12.1.2 | Central version upgraded |
| Avalonia.Markup.Xaml.Loader | Core | 12.1.0 | 12.1.2 | Central version upgraded |
| Avalonia.Skia | Core, SDK | 12.1.0 | 12.1.2 | Central version upgraded |
| Avalonia.Themes.Fluent | Core | 12.1.0 | 12.1.1 | Central version upgraded |
| ILRepack.Lib.MSBuild.Task | SDK | 2.0.45 | 2.0.46 | Central version upgraded |
| System.CommandLine | Core | 3.0.0-preview.2.26159.112 | 3.0.0-preview.6.26359.118 | Preview build upgraded |

No direct package was added or removed.

### Unchanged

| Package | Affected projects | Baseline | Result |
|---|---|---:|---:|
| Microsoft.NET.ILLink.Tasks | Core (SDK auto-reference) | 10.0.9 | 10.0.9 |
| Spectre.Console | Core | 0.57.2 | 0.57.2 |
| Tomlyn | Core | 2.10.1 | 2.10.1 |

Projects without direct NuGet references remain unchanged:

- `Models/Discord/ShiroBot.Model.Discord.csproj`
- `Models/QQ/ShiroBot.Model.QQ.csproj`
- `Models/Telegram/ShiroBot.Model.Telegram.csproj`
- `Templates/ShiroBot.Templates/ShiroBot.Templates.csproj`
- `Tests/Verification/ShiroBot.Verification.csproj`

## 4. Risk assessment

**Moderate risk.** CPM itself is configured correctly and the clean restore/build succeeds, but the
result is not version-neutral because eight direct package versions changed from the baseline.

The Avalonia and ILRepack changes are patch-level upgrades. `System.CommandLine` remains on the same
3.0.0 preview line but moves from preview 2 to preview 6, so command-line parsing behavior deserves
specific smoke testing. No `VersionOverride` weakens central governance, and no unexpected package
addition or removal was detected.

The post-conversion Release build completed with zero warnings and zero errors. The ShiroBot
verification executable also completed successfully.

## 5. Follow-up items

1. Review Avalonia 12.1.1 and 12.1.2 release notes before merging the version upgrades.
2. Review System.CommandLine preview 2 through preview 6 changes and smoke-test `--config`,
   `--adapter`, `--plugin-dir`, and `--no-console`.
3. Keep package upgrades in `Directory.Packages.props`; do not reintroduce per-project `Version`
   attributes.
4. Run the full release publish matrix before a tagged release, including all configured runtime
   identifiers.
5. Update the template-local ShiroBot package versions when the SDK and Model packages move beyond
   0.8.0.

No known vulnerable packages were reported by `dotnet package list --vulnerable
--include-transitive` against NuGet.org at the time of this conversion.

## 6. Artifacts and how to use them

- `baseline.binlog`: MSBuild binary log captured before CPM conversion.
- `after-cpm.binlog`: clean MSBuild binary log captured after CPM conversion and final version selection.
- `baseline-packages.json`: compact machine-readable baseline of resolved direct package versions.
- `after-cpm-packages.json`: compact machine-readable final resolved direct package versions.
- `convert-to-cpm.md`: this review report, suitable for a pull request description or team record.

Open the `.binlog` files in MSBuild Structured Log Viewer to compare restore and build evaluation.
Diff the two JSON files to review package resolution independently of project-file formatting.
