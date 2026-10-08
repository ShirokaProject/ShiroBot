#!/bin/sh
# Stable third-party dependencies go in one layer; host-owned files in another.
set -eu
publish_root=$1
layer_root=$2
test -f "$publish_root/ShiroBot.dll"
if [ -d "$layer_root" ] && [ -n "$(ls -A "$layer_root")" ]; then
    echo "Layer output must be empty: $layer_root" >&2
    exit 1
fi
mkdir -p "$layer_root/dependencies" "$layer_root/application"
cp -R "$publish_root/." "$layer_root/dependencies/"
for path in "$layer_root/dependencies"/ShiroBot* "$layer_root/dependencies"/Assets; do
    [ -e "$path" ] || continue
    mv "$path" "$layer_root/application/"
done
find "$layer_root" -type f -exec chmod 644 {} +
find "$layer_root" -type d -exec chmod 755 {} +
chmod 755 "$layer_root/application/ShiroBot"
