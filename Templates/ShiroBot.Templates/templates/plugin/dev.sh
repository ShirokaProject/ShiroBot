#!/usr/bin/env sh
set -eu

cd "$(dirname "$0")"

sdk_version=$(sed -n 's/.*PackageVersion Include="ShiroBot.SDK" Version="\([^"]*\)".*/\1/p' Directory.Packages.props)
if [ -z "$sdk_version" ]; then
  echo 'Cannot read ShiroBot.SDK version from Directory.Packages.props.' >&2
  exit 1
fi

case "$(uname -s)" in
  Darwin) platform=osx ;;
  Linux)
    platform=linux
    if [ -f /etc/alpine-release ]; then platform=linux-musl; fi
    ;;
  *) echo 'Unsupported operating system. On Windows, use dev.ps1.' >&2; exit 1 ;;
esac

case "$(uname -m)" in
  x86_64|amd64) architecture=x64 ;;
  arm64|aarch64) architecture=arm64 ;;
  *) echo 'Unsupported CPU architecture.' >&2; exit 1 ;;
esac

rid="$platform-$architecture"
# The host directory is not versioned: config.toml, plugins/ and adapters/ survive SDK upgrades.
cache_dir=".shirobot-dev/host/$rid"
host_exe="$cache_dir/ShiroBot"
version_file="$cache_dir/.host-version"
archive="$cache_dir/shirobot-host-$rid-framework-dependent.zip"

dotnet build PluginTemplate.csproj -c Release

installed_version=$(cat "$version_file" 2>/dev/null || true)
if [ ! -f "$host_exe" ] || [ "$installed_version" != "$sdk_version" ]; then
  mkdir -p "$cache_dir"
  url="https://github.com/ShirokaProject/ShiroBot/releases/download/v$sdk_version/$(basename "$archive")"
  echo "Downloading ShiroBot v$sdk_version for $rid..."
  curl --fail --location --retry 2 "$url" --output "$archive"
  # The archive only contains the host executable, so the existing local data is kept.
  unzip -q -o "$archive" -d "$cache_dir"
  rm -f "$archive"
  chmod +x "$host_exe"
  printf '%s\n' "$sdk_version" > "$version_file"
fi

plugin_dir="$cache_dir/plugins/PluginTemplate"
build_dir="bin/Release/net10.0"
manifest="$plugin_dir/.shirobot-dev-files"
mkdir -p "$plugin_dir"
# Remove files copied by the previous run that the build no longer produces. Files the
# plugin or host created there (config.toml, data, .shirobot/native, ...) are never touched.
if [ -f "$manifest" ]; then
  while IFS= read -r file; do
    case "$file" in ""|*..*) continue ;; esac
    [ -e "$build_dir/$file" ] || rm -f "$plugin_dir/$file"
  done < "$manifest"
fi
cp -R "$build_dir/." "$plugin_dir/"
# config.toml is never listed, so a user-edited config is not deleted if the build stops emitting one.
(cd "$build_dir" && find . -type f ! -name config.toml | sed 's#^\./##') > "$manifest"

echo "Starting ShiroBot v$sdk_version with PluginTemplate..."
cd "$cache_dir"
exec ./ShiroBot "$@"
