#!/usr/bin/env bash
set -euo pipefail

# 各应用继续使用独立 lockfile；这里只统一任务编排。
repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repository_root"

# 本地全量编排与 weekly 一致，包含对机器负载敏感的 POS 微基准；调用方仍可显式关闭。
export HBPOS_RUN_PERF_TESTS="${HBPOS_RUN_PERF_TESTS:-1}"

run_lane() {
  local lane="$1"
  local reason="$2"
  shift 2
  printf 'executed lane=%s reason=%s\n' "$lane" "$reason"
  "$@"
}

skip_lane() {
  local lane="$1"
  local reason="$2"
  printf 'skipped lane=%s reason=%s\n' "$lane" "$reason"
}

for component in web mobile pos-ipad pos-handheld supplier-extension antpos-web; do
  run_lane "node:$component" "所有平台执行 Node 组件门禁" \
    ./scripts/ci/run-node-component.sh "$component"
done

for component in backend pos-api pos-contract; do
  run_lane "dotnet:$component" "所有平台执行 .NET 组件门禁" \
    ./scripts/ci/run-dotnet-component.sh "$component"
done

macos_components=(pos-ipad-native pos-handheld-native supplier-safari)

if [[ "${OS:-}" == "Windows_NT" ]]; then
  run_lane "windows:pos-wpf" "当前平台为 Windows" \
    pwsh -File ./scripts/ci/run-windows-component.ps1 -Component pos-wpf
  skip_lane "android:pos-handheld-android" "Android 原生门禁仅在 Linux 执行"
  for component in "${macos_components[@]}"; do
    skip_lane "macos:$component" "Apple 原生门禁仅在 macOS 执行"
  done
elif [[ "$(uname -s)" == "Darwin" ]]; then
  skip_lane "windows:pos-wpf" "WPF 门禁仅在 Windows 执行"
  skip_lane "android:pos-handheld-android" "Android 原生门禁仅在 Linux 执行"
  for component in "${macos_components[@]}"; do
    run_lane "macos:$component" "当前平台为 macOS" \
      ./scripts/ci/run-macos-component.sh "$component"
  done
else
  skip_lane "windows:pos-wpf" "WPF 门禁仅在 Windows 执行"
  for component in "${macos_components[@]}"; do
    skip_lane "macos:$component" "Apple 原生门禁仅在 macOS 执行"
  done
  run_lane "android:pos-handheld-android" "当前平台执行 Linux 原生门禁" \
    ./scripts/ci/run-android-component.sh pos-handheld-android
fi
