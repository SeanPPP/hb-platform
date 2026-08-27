#!/usr/bin/env bash
set -euo pipefail

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
component="${1:?缺少 Android 组件名}"
cd "$repository_root"

if [[ "$component" == "noop" ]]; then
  echo "该 PR 没有需要 Android runner 执行的组件。"
  exit 0
fi
if [[ "$component" != "pos-handheld-android" ]]; then
  echo "未知 Android 组件: $component" >&2
  exit 2
fi

npm --prefix apps/pos-handheld ci --no-audit --no-fund
npm --prefix apps/pos-handheld run prebuild:android -- --clean
bash apps/pos-handheld/android/gradlew \
  -p apps/pos-handheld/android \
  :hb-app-installer:testDebugUnitTest \
  :hb-attendance-security:testDebugUnitTest \
  :app:assembleDebug \
  :app:lintDebug \
  -PreactNativeArchitectures=arm64-v8a \
  --no-daemon
