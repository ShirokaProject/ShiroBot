#!/bin/sh
set -eu

mkdir -p /data/adapters /data/plugins
if [ ! -e /data/config.toml ]; then
    cp /app/config.container.toml /data/config.toml
fi

exec /app/ShiroBot --config /data/config.toml --plugin-dir /data/plugins --no-console "$@"
