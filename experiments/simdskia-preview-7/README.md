# SimdSkia renderer experiment

The experiment branch tracks ShiroBot's default branch, `master`, and now pins SimdSkia 0.1.13 with Avalonia 12.2.0. See the [0.1.13 retest and remaining issues](REPORT.md#simdskia-0113-复测). Use `RENDER_LAB_BACKEND=skia` for the Skia comparison or leave it unset for SimdSkia. Set `RENDER_LAB_FONTS=matched` to load the same embedded fixture fonts in either backend. The font subsets cover the fixed retest fixtures.

## 复跑 0.1.13 卡片测试

在实验分支的仓库根目录运行。需要仓库 `global.json` 指定的 .NET 11 SDK 和 Python 3。默认用已登录的 GitHub CLI（`gh`）下载两个指定版本的插件发布包。

```sh
python3 experiments/simdskia-preview-7/benchmark/run.py artifacts/simdskia-benchmark
```

已有发布包时，用 `--archives <目录>` 跳过 GitHub CLI 下载。目录内放 MyParser v0.6.15 的 `Shirobot.Plugin.MyParser.zip` 和 KotoSub v0.2.2 的 `Shirobot.Plugin.RssSubscriber.zip`。脚本校验 ZIP 的 SHA-256，并提取两份原始 DLL。 Windows 可用 `py -3` 代替 `python3`。[MyParser 发布包](https://github.com/ShirokaProject/Shirobot.Plugin.MyParser/releases/tag/v0.6.15) · [KotoSub 发布包](https://github.com/ShirokaProject/Shirobot.Plugin.KotoSub/releases/tag/v0.2.2)

默认在两套字体下各跑 `Skia → SimdSkia → SimdSkia → Skia`。四轮分别用独立进程，每轮覆盖四种卡片、明暗主题、192 DPI，每个用例预热 3 次，再记录 9 次耗时。计时覆盖宿主控件创建、布局、位图渲染和内存 PNG 编码。这个复跑程序直接调用实验分支宿主的渲染服务，报告中的历史耗时来自当时的独立探针。

如果要检查顺序或温度漂移，可以在新的输出目录反转顺序、在进程之间留出冷却时间，或只跑同字体组。

```sh
python3 experiments/simdskia-preview-7/benchmark/run.py artifacts/simdskia-reversed --fonts noto --first simd --between-runs 60
```

- `environment.json` 记录提交、SDK、系统和每轮进程的 UTC 起止时间，可用于对齐外部温度和频率日志。
- `default/summary.json` 和 `noto/summary.json` 汇总各用例的中位数、比值和尺寸。
- 每轮子目录里的 `samples.json` 保留全部计时以及字体信息，还保存八张原尺寸 PNG。Noto 模式校验六个真实字重及运行时解析到的字体文件哈希。

## Preview.7 archive

The instructions below document the original snapshot at `276eb57`.

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
