"""Validate all release packages and their consumers before enabling NuGet uploads."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import tempfile
import urllib.error
import urllib.request
import xml.etree.ElementTree as ET
import zipfile

PACKAGE_DIRECTORY = Path("artifacts/nuget")
CHECKSUMS = "validated-checksums.json"


def release_packages(sdk_version, template_version, template_only):
    versions = [("ShiroBot.Templates", template_version)]
    if not template_only:
        versions.insert(0, ("ShiroBot.SDK", sdk_version))
    for _, version in versions:
        if not re.fullmatch(r"\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?", version):
            raise RuntimeError("Release versions must use MAJOR.MINOR.PATCH with an optional prerelease suffix.")
    return versions


def validate_packages(directory, packages, sdk_version):
    expected = {f"{name}.{version}.nupkg" for name, version in packages}
    actual = {path.name for path in directory.glob("*.nupkg")}
    if actual != expected:
        raise RuntimeError(f"Package set differs: expected {sorted(expected)}, found {sorted(actual)}")
    for name, version in packages:
        with zipfile.ZipFile(directory / f"{name}.{version}.nupkg") as archive:
            nuspec = [entry for entry in archive.namelist() if entry.endswith(".nuspec")]
            if len(nuspec) != 1:
                raise RuntimeError(f"Invalid package metadata: {name}")
            metadata = ET.fromstring(archive.read(nuspec[0]))
            values = {element.tag.rsplit("}", 1)[-1]: element.text for element in metadata.iter()}
            if values.get("id") != name or values.get("version") != version:
                raise RuntimeError(f"Incorrect identity or version: {name}")
            if name == "ShiroBot.SDK":
                for entry in ["lib/net10.0/ShiroBot.SDK.dll", *[f"lib/net10.0/ShiroBot.Model.{kind}.dll" for kind in ("QQ", "Discord", "Telegram")], "tools/ILRepack.Lib.MSBuild.Task.dll", "buildTransitive/ShiroBot.SDK.props", "buildTransitive/ShiroBot.SDK.targets"]:
                    if entry not in archive.namelist():
                        raise RuntimeError(f"Missing SDK asset: {entry}")
            else:
                for kind in ("plugin", "adapter"):
                    prefix = f"content/templates/{kind}/"
                    props = archive.read(prefix + "Directory.Packages.props").decode("utf-8-sig")
                    if f'Include="ShiroBot.SDK" Version="{sdk_version}"' not in props:
                        raise RuntimeError(f"Incorrect SDK reference in {kind} template")
                    workflow = archive.read(prefix + ".github/workflows/release.yml").decode("utf-8-sig")
                    if "-x '*.[pP][dD][bB]'" not in workflow:
                        raise RuntimeError(f"{kind} release workflow must exclude PDB files")
    return {file: hashlib.sha256((directory / file).read_bytes()).hexdigest() for file in sorted(expected)}


def run_dotnet(arguments, cwd=None, allowed_codes=(0,)):
    result = subprocess.run(["dotnet", *arguments], cwd=cwd, check=False)
    if result.returncode not in allowed_codes:
        # Never include command arguments: nuget push arguments contain the short-lived API key.
        raise RuntimeError(f"dotnet {arguments[0]} failed with exit code {result.returncode}")


def verify_consumers(directory, sdk_version, template_version, template_only):
    package = (directory / f"ShiroBot.Templates.{template_version}.nupkg").resolve()
    with tempfile.TemporaryDirectory(prefix="shirobot-nuget-consumers-") as temporary:
        root = Path(temporary)
        hive = root / "hive"
        run_dotnet(["new", "install", str(package), "--force", "--debug:custom-hive", str(hive)])
        config = ET.Element("configuration")
        sources = ET.SubElement(config, "packageSources")
        ET.SubElement(sources, "clear")
        ET.SubElement(sources, "add", key="nuget.org", value="https://api.nuget.org/v3/index.json")
        mapping = ET.SubElement(config, "packageSourceMapping")
        ET.SubElement(mapping, "clear")
        public = ET.SubElement(mapping, "packageSource", key="nuget.org")
        ET.SubElement(public, "package", pattern="*")
        if not template_only:
            ET.SubElement(sources, "add", key="release", value=str(directory.resolve()))
            local = ET.SubElement(mapping, "packageSource", key="release")
            ET.SubElement(local, "package", pattern="ShiroBot.SDK")
        config_path = root / "NuGet.config"
        ET.ElementTree(config).write(config_path, encoding="utf-8", xml_declaration=True)
        for kind in ("plugin", "adapter"):
            for platform in ("generic", "qq", "discord", "telegram"):
                name = f"Smoke{kind.capitalize()}{platform.capitalize()}"
                output = root / name
                run_dotnet(["new", f"shirobot-{kind}", "-n", name, "-o", str(output), "--platform", platform, "--allow-scripts", "no", "--debug:custom-hive", str(hive)], allowed_codes=(0, 105))
                props = (output / "Directory.Packages.props").read_text(encoding="utf-8-sig")
                if f'Include="ShiroBot.SDK" Version="{sdk_version}"' not in props:
                    raise RuntimeError(f"Incorrect generated SDK reference: {name}")
                if not (output / "Properties/launchSettings.json").is_file():
                    raise RuntimeError(f"Missing launch settings: {name}")
                project = output / f"{name}.csproj"
                run_dotnet(["restore", str(project), "--configfile", str(config_path), "--packages", str(root / "packages"), "--no-http-cache", "-p:Configuration=Release"])
                published = root / f"{name}-publish"
                run_dotnet(["publish", str(project), "-c", "Release", "--no-restore", "-o", str(published), "-p:CopyPluginToHost=false", "-p:CopyAdapterToHost=false"])
                dlls = sorted(path.name for path in published.rglob("*.dll"))
                if dlls != [f"{name}.dll"]:
                    raise RuntimeError(f"Generated {name} did not publish as one merged DLL: {dlls}")
                if platform == "generic" and not template_only:
                    # Verify SDK cleanup itself, independently of template shared-assembly defaults.
                    sdk_probe = root / f"{name}-sdk-probe"
                    run_dotnet(["publish", str(project), "-c", "Release", "--no-restore", "-o", str(sdk_probe), "-p:CopyPluginToHost=false", "-p:CopyAdapterToHost=false", "-p:ShiroBotPluginSharedAssemblies="])
                    if sorted(path.name for path in sdk_probe.rglob("*.dll")) != [f"{name}.dll"]:
                        raise RuntimeError(f"SDK left undeclared host Models in {name} publish output")
        print("All eight generated plugin/adapter projects compiled with their selected SDK and platform Models.")


def ensure_unpublished(packages):
    existing = []
    for name, version in packages:
        url = f"https://api.nuget.org/v3-flatcontainer/{name.lower()}/index.json"
        try:
            with urllib.request.urlopen(url, timeout=30) as response:
                versions = json.load(response)["versions"]
        except urllib.error.HTTPError as error:
            if error.code == 404:
                versions = []
            else:
                raise RuntimeError(f"Could not check NuGet versions for {name}: HTTP {error.code}") from None
        if version.lower() in {value.lower() for value in versions}:
            existing.append(f"{name} {version}")
    if existing:
        raise RuntimeError("No packages uploaded; versions already exist: " + ", ".join(existing))


def main():
    parser = argparse.ArgumentParser()
    modes = parser.add_mutually_exclusive_group()
    modes.add_argument("--preflight", action="store_true")
    modes.add_argument("--push", action="store_true")
    args = parser.parse_args()
    sdk_version = os.environ["SDK_VERSION"]
    template_version = os.environ["TEMPLATE_VERSION"]
    template_only = os.environ.get("TEMPLATE_ONLY", "false").lower() == "true"
    packages = release_packages(sdk_version, template_version, template_only)
    checksums = validate_packages(PACKAGE_DIRECTORY, packages, sdk_version)
    marker = PACKAGE_DIRECTORY / CHECKSUMS
    if args.preflight or args.push:
        if json.loads(marker.read_text()) != checksums:
            raise RuntimeError("Packages differ from the artifacts that passed consumer verification.")
        ensure_unpublished(packages)
        if args.push:
            api_key = os.environ["NUGET_API_KEY"]
            for name, version in packages:
                run_dotnet(["nuget", "push", str(PACKAGE_DIRECTORY / f"{name}.{version}.nupkg"), "--api-key", api_key, "--source", "https://api.nuget.org/v3/index.json"])
    else:
        marker.unlink(missing_ok=True)
        verify_consumers(PACKAGE_DIRECTORY, sdk_version, template_version, template_only)
        marker.write_text(json.dumps(checksums, indent=2) + "\n")
        print("Packages verified and checksums recorded; nothing has been uploaded.")


if __name__ == "__main__":
    main()
