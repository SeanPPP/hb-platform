#!/usr/bin/env bash
set -euo pipefail

MODE="${1:-run}"
APP_NAME="HB Supplier Order Safari"
BUNDLE_ID="com.hotbargain.supplierorder.safari"
ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PROJECT="$ROOT_DIR/xcode/HB Supplier Order Safari/HB Supplier Order Safari.xcodeproj"
DERIVED_DATA="$ROOT_DIR/build/DerivedData"
APP_BUNDLE="$DERIVED_DATA/Build/Products/Debug-iphonesimulator/$APP_NAME.app"
SIMULATOR_ID="${HB_IOS_SIMULATOR_ID:-}"

find_booted_simulator() {
  xcrun simctl list devices booted \
    | sed -nE 's/^[[:space:]]+.*\(([0-9A-F-]{36})\)[[:space:]]+\(Booted\).*$/\1/p' \
    | head -n 1
}

if [[ -z "$SIMULATOR_ID" ]]; then
  SIMULATOR_ID="$(find_booted_simulator)"
fi
if [[ -z "$SIMULATOR_ID" ]]; then
  echo "没有已启动的 iOS Simulator；请先启动一个模拟器，或设置 HB_IOS_SIMULATOR_ID" >&2
  exit 1
fi

cd "$ROOT_DIR"
npm run build:resources
xcodebuild \
  -project "$PROJECT" \
  -scheme "$APP_NAME" \
  -configuration Debug \
  -destination "platform=iOS Simulator,id=$SIMULATOR_ID" \
  -derivedDataPath "$DERIVED_DATA" \
  CODE_SIGNING_ALLOWED=NO \
  build

install_app() {
  xcrun simctl install "$SIMULATOR_ID" "$APP_BUNDLE"
}

launch_app() {
  xcrun simctl launch "$SIMULATOR_ID" "$BUNDLE_ID"
}

wait_for_app() {
  local attempt
  for attempt in {1..20}; do
    if xcrun simctl get_app_container "$SIMULATOR_ID" "$BUNDLE_ID" app >/dev/null 2>&1; then
      return 0
    fi
    sleep 0.5
  done
  echo "等待 $APP_NAME 安装超时" >&2
  return 1
}

case "$MODE" in
  run)
    install_app
    launch_app
    ;;
  --debug|debug)
    install_app
    xcrun simctl launch --console "$SIMULATOR_ID" "$BUNDLE_ID"
    ;;
  --logs|logs)
    install_app
    launch_app
    xcrun simctl spawn "$SIMULATOR_ID" log stream --info --style compact --predicate "process == '$APP_NAME'"
    ;;
  --telemetry|telemetry)
    install_app
    launch_app
    xcrun simctl spawn "$SIMULATOR_ID" log stream --info --style compact --predicate "subsystem == '$BUNDLE_ID'"
    ;;
  --verify|verify)
    install_app
    launch_app
    wait_for_app
    ;;
  *)
    echo "usage: $0 [run|--debug|--logs|--telemetry|--verify]" >&2
    exit 2
    ;;
esac
