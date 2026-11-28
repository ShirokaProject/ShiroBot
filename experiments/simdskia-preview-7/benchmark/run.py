#!/usr/bin/env python3
"""Run the headless ShiroBot card benchmark in separate, sequential processes."""

import argparse
import hashlib
import json
import os
import platform
import subprocess
import time
import zipfile
from datetime import datetime, timezone
from pathlib import Path
from statistics import median

HERE = Path(__file__).resolve().parent
REPO = HERE.parents[2]
FIXTURES = (
    (
        "ShirokaProject/Shirobot.Plugin.MyParser",
        "v0.6.15",
        "Shirobot.Plugin.MyParser.zip",
        "7af59cdf169955e0ecbcca60ea2f5fcac69d59012611d5bc38dbd4c073389115",
    ),
    (
        "ShirokaProject/Shirobot.Plugin.KotoSub",
        "v0.2.2",
        "Shirobot.Plugin.RssSubscriber.zip",
        "9101a28ede06aeae1c8c665e39e1f470283b2fd923f1ca4f81667517d3461c58",
    ),
    (
        "greepar/ShiroBot.Plugin.GithubView",
        "v1.1.1",
        "ShiroBot.Plugin.GithubView.dll",
        "410bf0ed25c17163e8f47972341733e0572090d5ba4a7b0e78c21f57545c36e1",
    ),
    (
        "greepar/ShiroBot.Plugin.JmParser",
        "v1.3.4",
        "ShiroBot.JmParser.dll",
        "838b76f77d9de4dd5e8f401961bd4a47330875eace9bde852452840ba3e437f6",
    ),
)


def utc_now():
    return datetime.now(timezone.utc).isoformat(timespec="seconds")


def write_json(path, data):
    path.write_text(json.dumps(data, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")


def prepare_fixtures(output, archives):
    dll_dir = output / "fixtures"
    dll_dir.mkdir()
    if archives is None:
        archives = output / "downloads"
        archives.mkdir()

    for repo, tag, filename, expected_hash in FIXTURES:
        archive = archives / filename
        if not archive.is_file() and archives == output / "downloads":
            subprocess.run(
                ["gh", "release", "download", tag, "--repo", repo,
                 "--pattern", filename, "--dir", str(archives)],
                check=True,
            )
        actual_hash = hashlib.sha256(archive.read_bytes()).hexdigest()
        if actual_hash != expected_hash:
            raise ValueError(f"{filename}: expected SHA-256 {expected_hash}, got {actual_hash}")
        if archive.suffix == ".zip":
            with zipfile.ZipFile(archive) as bundle:
                dll = f"{archive.stem}.dll"
                member, = [item for item in bundle.namelist() if Path(item).name == dll]
                (dll_dir / dll).write_bytes(bundle.read(member))
        else:
            # The v1.3.4 release asset is renamed after ILRepack. Match the assembly identity for .NET.
            assembly = "ShiroBot.JmParser.merged.dll" if filename == "ShiroBot.JmParser.dll" else filename
            (dll_dir / assembly).write_bytes(archive.read_bytes())
    return dll_dir


def summarize(runs):
    backend_cards = {"skia": {}, "simd": {}}
    for backend, report in runs:
        for card in report["cards"]:
            backend_cards[backend].setdefault(card["sample"], []).append(card)

    rows = []
    for sample, skia_runs in backend_cards["skia"].items():
        simd_runs = backend_cards["simd"][sample]
        skia_ms = median(ms for run in skia_runs for ms in run["times"])
        simd_ms = median(ms for run in simd_runs for ms in run["times"])
        rows.append({
            "sample": sample,
            "skia_ms": skia_ms,
            "simd_ms": simd_ms,
            "ratio": simd_ms / skia_ms,
            "skia_sizes": [[run["width"], run["height"]] for run in skia_runs],
            "simd_sizes": [[run["width"], run["height"]] for run in simd_runs],
        })
        print(f"{sample:23} {skia_ms:8.1f} → {simd_ms:8.1f} ms ({simd_ms / skia_ms:.2f}×)", flush=True)
    return rows


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("output", type=Path, help="new directory for PNGs and timing data")
    parser.add_argument("--archives", type=Path, help="directory with the original plugin release assets")
    parser.add_argument("--fonts", choices=["both", "default", "noto"], default="both")
    parser.add_argument("--first", choices=["skia", "simd"], default="skia")
    parser.add_argument("--warmups", type=int, default=3)
    parser.add_argument("--samples", type=int, default=9)
    parser.add_argument("--between-runs", type=int, default=0, metavar="SECONDS")
    args = parser.parse_args()
    if args.warmups < 0 or args.samples < 1 or args.between_runs < 0:
        parser.error("require warmups >= 0, samples >= 1 and between-runs >= 0")

    output = args.output.resolve()
    output.mkdir(parents=True)
    fixtures = prepare_fixtures(output, args.archives.resolve() if args.archives else None)
    subprocess.run(
        ["dotnet", "build", str(HERE / "CardBenchmark.csproj"), "-c", "Release",
         "--output", str(output / "bin"), f"-p:FixtureDirectory={fixtures}"],
        cwd=REPO, check=True,
    )

    other = "simd" if args.first == "skia" else "skia"
    sequence = [args.first, other, other, args.first]
    modes = ["default", "noto"] if args.fonts == "both" else [args.fonts]
    metadata = {
        "commit": subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=REPO, text=True).strip(),
        "dotnet_sdk": subprocess.check_output(["dotnet", "--version"], cwd=REPO, text=True).strip(),
        "platform": platform.platform(),
        "architecture": platform.machine(),
        "processor": platform.processor(),
        "dpi": 192,
        "warmups": args.warmups,
        "samples": args.samples,
        "between_runs_seconds": args.between_runs,
        "sequence": sequence,
        "runs": [],
    }
    write_json(output / "environment.json", metadata)
    expected_faces = {
        weight: hashlib.sha256((REPO / "Core" / "LabFonts" / f"NotoSansSC-{weight}.ttf").read_bytes()).hexdigest()
        for weight in (400, 500, 600, 700, 800, 900)
    }
    for mode in modes:
        runs = []
        for index, backend in enumerate(sequence, start=1):
            if metadata["runs"] and args.between_runs:
                time.sleep(args.between_runs)
            env = os.environ.copy()
            env.pop("SHIROBOT_DEFAULT_FONT_FAMILY", None)
            env.pop("SHIROBOT_EMOJI_FONT_FAMILY", None)
            env["RENDER_LAB_BACKEND"] = backend
            env["RENDER_LAB_FONTS"] = "matched" if mode == "noto" else "default"

            run_dir = output / mode / f"{index:02d}-{backend}"
            print(f"\n{mode} · {index}/{len(sequence)} · {backend}", flush=True)
            run = {"mode": mode, "backend": backend, "directory": run_dir.relative_to(output).as_posix(),
                   "started_utc": utc_now()}
            metadata["runs"].append(run)
            write_json(output / "environment.json", metadata)
            subprocess.run(
                ["dotnet", str(output / "bin" / "CardBenchmark.dll"), str(run_dir),
                 str(args.warmups), str(args.samples)],
                cwd=REPO, env=env, check=True,
            )
            run["finished_utc"] = utc_now()
            write_json(output / "environment.json", metadata)
            report = json.loads((run_dir / "samples.json").read_text(encoding="utf-8"))
            if mode == "noto":
                actual_faces = {face["weight"]: face["sha256"].lower() for face in report["fonts"]["faces"]}
                if actual_faces != expected_faces:
                    raise ValueError(f"{backend} did not select the expected Noto font files")
            runs.append((backend, report))

        print(f"\n{mode} totals", flush=True)
        write_json(output / mode / "summary.json", summarize(runs))


if __name__ == "__main__":
    main()
