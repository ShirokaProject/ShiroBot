#!/usr/bin/env bash
set -euo pipefail

tag="${CI_COMMIT_TAG:?release tag is required}"
base_url="https://git.oeo.one/justme/ShiroBot/releases/download/${tag}"
previous_tag="$(git describe --tags --abbrev=0 "${tag}^" 2>/dev/null || true)"
range="$tag"
if [ -n "$previous_tag" ]; then
  range="${previous_tag}..${tag}"
fi

features=()
fixes=()
improvements=()
while IFS= read -r subject; do
  case "$subject" in
    ""|"Release ShiroBot "*|"Prepare ShiroBot "*) continue ;;
    feat:*|feat\(*) features+=("$subject") ;;
    fix:*|fix\(*) fixes+=("$subject") ;;
    *) improvements+=("$subject") ;;
  esac
done < <(git log --no-merges --reverse --format=%s "$range")

write_changes() {
  local heading="$1"
  shift
  if [ "$#" -eq 0 ]; then return; fi
  echo "#### ${heading}"
  echo
  for subject in "$@"; do echo "- ${subject}"; done
  echo
}

{
  echo "## ${tag}"
  echo
  echo "### 更新日志"
  echo
  write_changes "新功能" "${features[@]}"
  write_changes "修复问题" "${fixes[@]}"
  write_changes "其他改进" "${improvements[@]}"
  if [ "${#features[@]}" -eq 0 ] && [ "${#fixes[@]}" -eq 0 ] && [ "${#improvements[@]}" -eq 0 ]; then
    echo "- 本次发布没有单独记录的代码改动。"
    echo
  fi
  echo "## 下载地址"
  echo
  echo "#### Linux x64 自带运行时（推荐，不需要预装 .NET）"
  echo
  echo "- [ShiroBot.gz](${base_url}/ShiroBot.gz)"
  echo "- [SHA256 校验文件](${base_url}/ShiroBot.sha256)"
  echo
  echo "#### OCI 镜像"
  echo
  echo "- `registry.oeo.one/justme/shirobot:${tag}`"
  echo "- Linux 二进制制品：`registry.oeo.one/justme/shirobot-linux-x64:${tag}`"
} > RELEASE_NOTES.md
