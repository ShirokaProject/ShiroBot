"""Build two compatible component versions and exercise isolated host update HTTP/console flows."""
import argparse
from pathlib import Path
import subprocess
import tempfile

parser = argparse.ArgumentParser()
parser.add_argument("--dashboard-dist", type=Path, help="Optional local Dashboard dist directory")
args = parser.parse_args()
repository = Path(__file__).resolve().parents[2]
fixtures = Path(tempfile.mkdtemp(prefix="shiro-update-fixtures-"))
for name, version, instance in (("v1", 1, "first"), ("v2", 2, "first"), ("second", 1, "second")):
    subprocess.run([
        "dotnet", "build", str(repository / "Tests/UpdateProbe/ShiroBot.UpdateProbe.csproj"),
        "--disable-build-servers", "-m:1", "-p:UseSharedCompilation=false",
        f"-p:ProbeVersion={version}", f"-p:ProbeInstance={instance}", "-o", str(fixtures / name),
    ], cwd=repository, check=True)
command = [
    "dotnet", "build", str(repository / "Tests/Verification/ShiroBot.Verification.csproj"),
    "--disable-build-servers", "-m:1", "-p:UseSharedCompilation=false",
]
if args.dashboard_dist:
    command.append(f"-p:DashboardDistPath={args.dashboard_dist.resolve()}")
subprocess.run(command, cwd=repository, check=True)
subprocess.run([
    "dotnet", str(repository / "Tests/Verification/bin/Debug/net10.0/ShiroBot.Verification.dll"),
    "--update-integration", str(fixtures),
], cwd=repository, check=True)
