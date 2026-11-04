#!/bin/sh
# Exercise the real non-root entrypoint with both fresh and pre-existing data.
set -eu
image=$1
platform=$2
name="shirobot-startup-check-$$"
volume="$name-data"
cleanup() {
    docker rm -f "$name" >/dev/null 2>&1 || true
    docker volume rm "$volume" >/dev/null 2>&1 || true
}
trap cleanup EXIT
docker volume create "$volume" >/dev/null

start_and_verify() {
    docker run -d --name "$name" --platform "$platform" \
        -v "$volume:/data" "$image" >/dev/null
    ready=false
    for _attempt in $(seq 1 60); do
        # Console rendering can wrap startup messages even when the server is ready.
        # Check the real HTTP listener and bundled Dashboard without publishing a host port.
        if docker exec "$name" wget -q -T 2 -O /dev/null http://127.0.0.1:7001/dashboard/; then
            ready=true
            break
        fi
        if [ "$(docker inspect --format '{{.State.Running}}' "$name")" != true ]; then
            break
        fi
        sleep 1
    done
    if [ "$ready" != true ]; then
        docker logs "$name" 2>&1 | sed '/API 鉴权密钥:/d'
        echo "Container startup failed: $image ($platform)" >&2
        exit 1
    fi
    docker exec "$name" sh -eu -c '
        test "$(id -u)" = 1654
        test ! -L /app/cache
        test -f /app/cache/host.lock
        test -f /data/config.toml
        test -w /app/cache
        test -n "$(/app/ShiroBot --self-check)"
        test ! -e /data/host
    '
    docker stop -t 20 "$name" >/dev/null
    docker rm "$name" >/dev/null
}

start_and_verify
# Recreate the container while retaining its existing data volume.
# The marker represents user data and must survive entrypoint initialization.
docker run --rm --platform "$platform" --entrypoint sh \
    -v "$volume:/data" "$image" -eu -c '
        printf "%s" retained > /data/plugins/startup-test-data
        cp /data/config.toml /data/config.expected
        # A Dashboard update no newer than the image must be discarded on startup.
        mkdir -p /data/host
        cp /app/ShiroBot /data/host/ShiroBot
    '
start_and_verify
docker run --rm --platform "$platform" --entrypoint sh \
    -v "$volume:/data" "$image" -eu -c '
        test "$(cat /data/plugins/startup-test-data)" = retained
        cmp /data/config.toml /data/config.expected
    '
echo "Container startup and existing-volume verification passed: $platform"
