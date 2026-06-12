#!/usr/bin/env bash
set -euo pipefail

# 统一查看各子项目的关键入口，方便迁移后快速巡检。
for path in apps/mobile apps/web apps/pos-wpf services/backend; do
  echo "== ${path} =="
  if [ -d "${path}" ]; then
    find "${path}" -maxdepth 2 \( -name package.json -o -name '*.sln' -o -name '*.slnx' -o -name '*.csproj' \) | sort
  else
    echo "missing"
  fi
  echo
done

