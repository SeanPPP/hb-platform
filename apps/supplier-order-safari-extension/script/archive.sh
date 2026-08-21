#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT_DIR"

xcode_args=(
  -project "xcode/HB Supplier Order Safari/HB Supplier Order Safari.xcodeproj"
  -scheme "HB Supplier Order Safari"
  -configuration Release
  -destination "generic/platform=iOS"
  -archivePath "build/HB Supplier Order.xcarchive"
  -allowProvisioningUpdates
)

# Team ID 仅通过当前进程注入，禁止写入工程、脚本或发布元数据。
if [[ -n "${HB_APPLE_DEVELOPMENT_TEAM:-}" ]]; then
  xcode_args+=("DEVELOPMENT_TEAM=${HB_APPLE_DEVELOPMENT_TEAM}")
fi
xcode_args+=(archive)

xcodebuild "${xcode_args[@]}"
node verify-archive.mjs
