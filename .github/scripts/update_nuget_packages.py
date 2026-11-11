"""Resolve central NuGet dependencies to the latest stable versions before CI builds."""
import concurrent.futures
import json
from pathlib import Path
import re
import urllib.request
import xml.etree.ElementTree as ET


def latest_stable(package):
    url = f"https://api.nuget.org/v3-flatcontainer/{package.lower()}/index.json"
    with urllib.request.urlopen(url, timeout=60) as response:
        versions = json.load(response)["versions"]
    stable = [v for v in versions if "-" not in v]
    if not stable:
        raise RuntimeError(f"No stable version for {package}")
    return package, max(stable, key=lambda v: tuple(int(n) for n in v.split(".")))


def main():
    path = Path(__file__).resolve().parents[2] / "Directory.Packages.props"
    source = path.read_text()
    packages = [node.attrib["Include"] for node in ET.fromstring(source).iter("PackageVersion")]
    with concurrent.futures.ThreadPoolExecutor(max_workers=8) as pool:
        resolved = dict(pool.map(latest_stable, packages))
    for package, version in resolved.items():
        print(f"{package}: {version}", flush=True)
        source = re.sub(r'(<PackageVersion Include="' + re.escape(package) + r'" Version=")[^"]+("\s*/>)',
                        lambda m: m[1] + version + m[2], source)
    path.write_text(source)


if __name__ == "__main__":
    main()
