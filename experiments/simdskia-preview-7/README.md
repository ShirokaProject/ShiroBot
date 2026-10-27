# SimdSkia preview.7 experiment (2026-10-27)

This branch preserves an incomplete renderer migration for future testing. It is not a production replacement. The baseline is ShiroBot d2233a7; the main branch continues to use native Avalonia.Skia.

## Integration

- Avalonia packages are pinned to 12.1.4; SimdSkia.Avalonia.Desktop is pinned to 0.1.0-preview.7.
- The host keeps its headless window platform and replaces its drawing/text services with UseSimdSkia(). Avalonia.Skia remains available because released MyParser code directly calls SkiaSharp.
- RENDER_LAB_BACKEND=skia selects the original renderer for comparison; the default on this experimental branch is SimdSkia.
- RENDER_LAB_FONTS=matched enables Core/LabFonts' embedded Noto Sans SC subsets, including actual bold weights. These subsets only support the fixed test corpus; they are not suitable for production deployment. Without this override, existing cards fail when the backend tries to synthesize missing bold/italic faces.
- Program.BuildAvaloniaApp and the headless bootstrap use the same experimental backend switch.

## Findings and validation

All 11 plugins were loaded in the compatibility environment. Actual released card controls were tested using DebugAdapter and a test probe: 21 controls, light/dark themes, 42 cases per environment. Native Avalonia 12.1.2 and 12.1.4 produced pixel-identical output in all 42 cases.

Default SimdSkia failed 42/42 card cases. With the native Skia dependency retained and matching static font faces supplied, all 42 produced PNGs. Two RSS Git cards changed height by 2 px. Color emoji remains unsupported: explicitly registering Apple Color Emoji fails with `Color/bitmap/SVG font rendering requires its own color glyph pipeline.`

On one Apple M4/macOS arm64 machine with Debug host builds, the median per-case warmed rendering time ratio was about 2.15x native; some complex cards were 20-29x slower. These are small local samples, not cross-platform benchmarks. Actual RSS and GitHubView message flows also returned card images through DebugAdapter.

See REPORT.md for methods/limits, comparison.json for measurements, comparison-sheet.png for examples, and downloads-manifest.json for release hashes. File paths in the original report refer to the full local experiment directory, which remains at `/Users/geruipu/riderprojects/ShiroBot.SimdSkia.Lab` with the original PNGs, heatmaps, interactive report, source snapshots, fixtures, adapter instrumentation and replay scripts. Generated runtimes/downloaded binaries were not committed.

## Build

```sh
dotnet build Core/ShiroBot.csproj -c Debug
# Use a fresh isolated config/data directory when testing this experimental host.
# The original renderer remains selectable for the same experiment build:
RENDER_LAB_BACKEND=skia dotnet run --project Core/ShiroBot.csproj
```

No tags or release workflows are created by preserving this branch.
