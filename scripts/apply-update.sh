#!/usr/bin/env bash
# scripts/apply-update.sh
# 用法: apply-update.sh <pid> <entry-assembly-path>
#
# 工作流：
#   1) 等待主进程 <pid> 退出
#   2) 把 update/ 下的 asset 解压/复制到运行目录
#   3) 重启 dotnet <entry-assembly-path>
#
# 仅处理 update/pending-update.toml 描述的目标，未找到 manifest 时直接退出。

set -euo pipefail

if [[ $# -lt 2 ]]; then
    echo "[apply-update] 用法: $0 <pid> <entry-assembly-path>" >&2
    exit 64
fi

PID="$1"
ENTRY_ASSEMBLY="$2"
APP_DIR="$(cd -- "$(dirname -- "$ENTRY_ASSEMBLY")" && pwd)"
UPDATE_DIR="$APP_DIR/update"
MANIFEST="$UPDATE_DIR/pending-update.toml"

if [[ ! -f "$MANIFEST" ]]; then
    echo "[apply-update] 未找到 $MANIFEST，跳过升级。" >&2
    exit 1
fi

ASSET="$(awk -F= '/^asset/ {gsub(/[ "\t]/, "", $2); print $2}' "$MANIFEST" | head -1)"
if [[ -z "$ASSET" ]]; then
    echo "[apply-update] manifest 缺少 asset 字段。" >&2
    exit 1
fi

ASSET_PATH="$UPDATE_DIR/$ASSET"
if [[ ! -f "$ASSET_PATH" ]]; then
    echo "[apply-update] 缺少待安装文件: $ASSET_PATH" >&2
    exit 1
fi

echo "[apply-update] 等待主进程 $PID 退出..."
for _ in $(seq 1 60); do
    if ! kill -0 "$PID" >/dev/null 2>&1; then
        break
    fi
    sleep 1
done

if kill -0 "$PID" >/dev/null 2>&1; then
    echo "[apply-update] 主进程仍在运行，发送 SIGTERM。"
    kill "$PID" >/dev/null 2>&1 || true
    sleep 3
fi

echo "[apply-update] 部署 $ASSET ..."
case "$ASSET" in
    *.zip)
        unzip -oq "$ASSET_PATH" -d "$APP_DIR"
        ;;
    *.tar.gz | *.tgz)
        tar -xzf "$ASSET_PATH" -C "$APP_DIR"
        ;;
    *.dll | *)
        cp -f "$ASSET_PATH" "$APP_DIR/"
        ;;
esac

# 升级完成，清理 manifest，保留 asset 以便排查
rm -f "$MANIFEST"

echo "[apply-update] 重启 ShiroBot ..."
cd "$APP_DIR"
nohup dotnet "$ENTRY_ASSEMBLY" >/dev/null 2>&1 &
disown || true
echo "[apply-update] 完成。"
