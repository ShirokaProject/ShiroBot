#!/usr/bin/env python3
"""Create byte-reproducible, standard Deflate ZIP release assets."""
import argparse
import hashlib
import os
from pathlib import Path, PurePosixPath
import platform
import shutil
import subprocess
import tarfile
import tempfile
import urllib.request
import zipfile

VERSION = "26.06"
# Official ip7z/7zip release asset SHA-256 values; never execute an unverified download.
TOOLS = {
    ("Linux", "x86_64"): ("7z2606-linux-x64.tar.xz", "2b539cf067b2ad48e1baa5093dbaa8818b73542d1222a0ffde9dc5f2922ea62e"),
    ("Linux", "aarch64"): ("7z2606-linux-arm64.tar.xz", "edcc408aacce77da5223728f2eceb5e9f6dc713b8a9df934ad21e2a7f7560627"),
    ("Darwin", "x86_64"): ("7z2606-mac-x64.tar.xz", "3af68b124052da7fc2a246c96b0bea6a68bfee389baa788f4cead1a133445586"),
    ("Darwin", "arm64"): ("7z2606-mac.tar.xz", "b6f17920b4240940461dac8ef4009891881ac44d2a5ead625ede2ec8472185dd"),
}


def sha256(path):
    with Path(path).open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def pinned_tool(work):
    asset, expected = TOOLS[(platform.system(), platform.machine().lower())]
    cache = Path(tempfile.gettempdir()) / "shirobot-release-tools" / VERSION
    cache.mkdir(parents=True, exist_ok=True)
    archive = cache / asset
    if not archive.exists() or sha256(archive) != expected:
        url = f"https://github.com/ip7z/7zip/releases/download/{VERSION}/{asset}"
        with urllib.request.urlopen(url, timeout=90) as response:
            data = response.read(16 * 1024 * 1024 + 1)
        if hashlib.sha256(data).hexdigest() != expected:
            raise ValueError(f"7-Zip checksum mismatch: {asset}")
        with tempfile.NamedTemporaryFile(dir=cache, delete=False) as stream:
            temporary = Path(stream.name)
            stream.write(data)
        os.replace(temporary, archive)
    # Extract only the verified executable, never archive paths or symlinks.
    with tarfile.open(archive, "r:xz") as bundle:
        member = bundle.getmember("7zz")
        if not member.isfile():
            raise ValueError("7-Zip asset has no regular 7zz executable")
        tool = work / "7zz"
        with bundle.extractfile(member) as source, tool.open("wb") as target:
            shutil.copyfileobj(source, target)
    tool.chmod(0o755)
    return tool


def create_archive(root, output, selected=None, executable=()):
    root, output = Path(root).resolve(strict=True), Path(output).resolve()
    if not root.is_dir() or output.is_relative_to(root):
        raise ValueError("ZIP output must be outside the input directory")
    paths = list(root.rglob("*")) if selected is None else [root / item for item in selected]
    files = {}
    for path in paths:
        if path.is_symlink():
            raise ValueError(f"Symlinks are not release inputs: {path}")
        if not path.resolve(strict=True).is_relative_to(root):
            raise ValueError("Release input escapes its directory")
        if not path.is_file():
            continue
        name = path.relative_to(root).as_posix()
        if any(char in name for char in "\r\n\\") or PurePosixPath(name).is_absolute() or ".." in PurePosixPath(name).parts:
            raise ValueError(f"Invalid release path: {name!r}")
        files[name] = path
    if not files or not set(executable).issubset(files):
        raise ValueError("Missing release input or executable")
    output.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix="release-zip-", dir=output.parent) as temporary:
        work = Path(temporary)
        stage = work / "files"
        stage.mkdir()
        for name in sorted(files):
            dest = stage / name
            dest.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(files[name], dest)
            dest.chmod(0o755 if name in executable else 0o644)
        listing = work / "files.txt"
        listing.write_text("\n".join(sorted(files)) + "\n", encoding="utf-8")
        archive = work / "archive.zip"
        tool = pinned_tool(work)
        subprocess.run([
            str(tool), "a", "-tzip", "-mm=Deflate", "-mx=9", "-mfb=258", "-mpass=15", "-mmt=off",
            "-mtc=off", "-mta=off", "-mtm=off", "-mcu=on", "-scsUTF-8", "-bd", "-bso0", "-bsp0",
            str(archive), "@" + str(listing),
        ], cwd=stage, env={**os.environ, "TZ": "UTC", "LC_ALL": "C"}, check=True)
        with zipfile.ZipFile(archive) as result:
            if set(result.namelist()) != set(files) or result.testzip() is not None:
                raise ValueError("Release ZIP failed integrity verification")
            for info in result.infolist():
                if info.compress_type not in (zipfile.ZIP_STORED, zipfile.ZIP_DEFLATED):
                    raise ValueError("Release ZIP requires an unsupported compression method")
                if info.date_time != (1980, 1, 1, 0, 0, 0) or info.extra:
                    raise ValueError("Release ZIP contains variable metadata")
        os.replace(archive, output)
    return sha256(output)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--file", action="append", dest="selected")
    parser.add_argument("--executable", action="append", default=[])
    args = parser.parse_args()
    create_archive(args.root, args.output, args.selected, args.executable)
    print(f"Created {args.output.name} ({args.output.stat().st_size} bytes)")
