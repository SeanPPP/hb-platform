#!/bin/bash
# KFC 补货信号例行任务入口：以仓库 origin/main 上的版本为准。
# 先把脚本、模板和本文件同步到本机任务目录，再运行分析；参数原样传给 analyze_kfc.py。
# 拉取失败（离线、别的会话正在 fetch 等）时沿用本机上一份，并在 stderr 里给出 SYNC_WARN。
# 注意：bash 3.2 下变量紧跟中文会解析错，变量一律写成 ${var}。
set -u

DIR="$(cd "$(dirname "$0")" && pwd)"
REPO="${KFC_REPORT_REPO:-/Users/sean/DEV/hb-platform}"
SRC="scripts/ops/kfc-uncle-bills"

if git -C "${REPO}" fetch -q origin main 2>/dev/null; then
  for f in analyze_kfc.py report_template.html run.sh; do
    tmp="${DIR}/.${f}.sync"
    if git -C "${REPO}" show "origin/main:${SRC}/${f}" > "${tmp}" 2>/dev/null && [ -s "${tmp}" ]; then
      # 用 rename 原子替换：正在执行的 run.sh 仍读旧文件，新版本下次生效
      mv "${tmp}" "${DIR}/${f}"
    else
      rm -f "${tmp}"
      echo "SYNC_WARN=无法从 origin/main 读取 ${f}，沿用本机版本" >&2
    fi
  done
  chmod +x "${DIR}/run.sh" 2>/dev/null || true
else
  echo "SYNC_WARN=git fetch 失败，沿用本机脚本" >&2
fi

exec python3 "${DIR}/analyze_kfc.py" "$@"
