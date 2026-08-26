#!/usr/bin/env bash
set -euo pipefail

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
component="${1:?缺少 macOS 组件名}"
derived_root="${RUNNER_TEMP:-$repository_root/.artifacts/ci}/$component-derived"
cd "$repository_root"

if [[ "$component" == "noop" ]]; then
  echo "该 PR 没有需要 macOS runner 执行的组件。"
  exit 0
fi

required_xcode_version="26.5"
fixed_xcode_path="/Applications/Xcode_26.5.app/Contents/Developer"
selected_xcode_path=""

is_valid_xcode_path() {
  local candidate="$1"
  [[ -n "$candidate" && -d "$candidate" && -x "$candidate/usr/bin/xcodebuild" ]]
}

# 只通过进程环境选择 Xcode，避免改动 runner 的全局 xcode-select 状态。
if is_valid_xcode_path "$fixed_xcode_path"; then
  selected_xcode_path="$fixed_xcode_path"
elif is_valid_xcode_path "${DEVELOPER_DIR:-}"; then
  selected_xcode_path="$DEVELOPER_DIR"
else
  xcode_select_path=""
  if xcode_select_path="$(xcode-select -p 2>/dev/null)" && is_valid_xcode_path "$xcode_select_path"; then
    selected_xcode_path="$xcode_select_path"
  fi
fi

if [[ -z "$selected_xcode_path" ]]; then
  echo "未找到有效 Xcode 开发目录（依次检查固定 26.5 路径、DEVELOPER_DIR、xcode-select -p）。" >&2
  exit 1
fi

export DEVELOPER_DIR="$selected_xcode_path"
echo "使用 Xcode 开发目录: $DEVELOPER_DIR"

if ! xcode_version_output="$(xcodebuild -version 2>&1)"; then
  echo "无法读取 Xcode 版本。" >&2
  printf '%s\n' "$xcode_version_output" >&2
  exit 1
fi
printf '%s\n' "$xcode_version_output"

xcode_version_line=""
while IFS= read -r line; do
  if [[ "$line" == Xcode\ * ]]; then
    xcode_version_line="$line"
    break
  fi
done <<< "$xcode_version_output"

if [[ "$xcode_version_line" != "Xcode $required_xcode_version" ]]; then
  echo "要求 Xcode ${required_xcode_version}，实际版本: ${xcode_version_line:-未识别}" >&2
  exit 1
fi

case "$component" in
  pos-ipad-native)
    npm --prefix apps/pos-ipad ci --no-audit --no-fund
    npm --prefix apps/pos-ipad run prebuild:ios -- --clean
    node scripts/ci/test-inventory.mjs --app pos-ipad --run native
    xcodebuild -workspace apps/pos-ipad/ios/HBPOS.xcworkspace -list
    xcodebuild \
      -workspace apps/pos-ipad/ios/HBPOS.xcworkspace \
      -scheme HBPOS \
      -configuration Debug \
      -destination 'generic/platform=iOS Simulator' \
      -derivedDataPath "$derived_root" \
      CODE_SIGNING_ALLOWED=NO \
      build
    ;;
  pos-handheld-native)
    npm --prefix apps/pos-handheld ci --no-audit --no-fund
    npm --prefix apps/pos-handheld run prebuild:ios -- --clean
    node scripts/ci/test-inventory.mjs --app pos-handheld --run native
    xcodebuild -workspace apps/pos-handheld/ios/HBPOSMobile.xcworkspace -list
    xcodebuild \
      -workspace apps/pos-handheld/ios/HBPOSMobile.xcworkspace \
      -scheme HBPOSMobile \
      -configuration Debug \
      -destination 'generic/platform=iOS Simulator' \
      -derivedDataPath "$derived_root" \
      CODE_SIGNING_ALLOWED=NO \
      build
    ;;
  supplier-safari)
    npm --prefix apps/supplier-order-extension ci --no-audit --no-fund
    npm --prefix apps/supplier-order-safari-extension test
    git diff --exit-code -- \
      'apps/supplier-order-safari-extension/xcode/HB Supplier Order Safari/HB Supplier Order Safari Extension/Resources' \
      'apps/supplier-order-safari-extension/xcode/HB Supplier Order Safari/HB Supplier Order Safari.xcodeproj/project.pbxproj'
    npm --prefix apps/supplier-order-safari-extension run build:xcode
    ;;
  *)
    echo "未知 macOS 组件: $component" >&2
    exit 2
    ;;
esac
