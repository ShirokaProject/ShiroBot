#!/bin/sh
set -eu

# SHIROBOT_DATA_DIR lets a host directory be mounted at the same path inside the container, so file
# paths the host hands to a protocol endpoint on that machine resolve there too. The whole host
# directory (cache and plugin temporary files included) then lives in it instead of /app.
data=${SHIROBOT_DATA_DIR:-/data}
if [ "$data" != /data ]; then
    export SHIROBOT_HOME="$data"
    export SHIROBOT_HOST_UPDATE_DIR="$data/host"
fi

mkdir -p "$data/adapters" "$data/plugins"
if [ ! -e "$data/config.toml" ]; then
    cp /app/config.container.toml "$data/config.toml"
fi

# Single-file native libraries are extracted per version; drop copies left by earlier versions.
rm -rf /app/cache/bundle

# Compares numeric x.y.z versions: succeeds when $1 is newer than $2.
version_gt() {
    a=$1
    b=$2
    while [ -n "$a$b" ]; do
        x=${a%%.*}
        y=${b%%.*}
        if [ "${x:-0}" -gt "${y:-0}" ]; then return 0; fi
        if [ "${x:-0}" -lt "${y:-0}" ]; then return 1; fi
        case $a in *.*) a=${a#*.} ;; *) a= ;; esac
        case $b in *.*) b=${b#*.} ;; *) b= ;; esac
    done
    return 1
}

# A Dashboard update installs into the data volume; use it only while it is newer than the image
# and still starts here, so pulling a newer image always wins.
host=/app/ShiroBot
update_dir=${SHIROBOT_HOST_UPDATE_DIR:-/data/host}
updated="$update_dir/ShiroBot"
if [ -e "$updated" ]; then
    image_version=$("$host" --self-check)
    if updated_version=$("$updated" --self-check 2>/dev/null) && version_gt "$updated_version" "$image_version"; then
        echo "使用面板更新的主程序 ${updated_version}（镜像版本 ${image_version}）"
        host=$updated
    else
        echo "镜像主程序 ${image_version} 已不旧于面板更新的版本，或该版本无法运行，已改用镜像版本"
        rm -rf "$update_dir"
    fi
fi

exec "$host" --config "$data/config.toml" --plugin-dir "$data/plugins" --no-console "$@"
