#!/usr/bin/env bash
set -euo pipefail

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
component="${1:?缺少 Android 组件名}"
cd "$repository_root"

case "$component" in
  noop)
    echo "该 PR 没有需要 Android runner 执行的组件。"
    ;;
  mobile-android)
    npm --prefix apps/mobile ci --no-audit --no-fund
    (
      cd apps/mobile
      # 保留仓库内的打印机等原生定制，只同步配置并刷新本地 Expo 模块自动链接。
      EXPO_NO_TELEMETRY=1 npx expo prebuild --platform android --no-install
    )
    bash apps/mobile/android/gradlew \
      -p apps/mobile/android \
      :hb-app-installer:testDebugUnitTest \
      :app:compileDebugKotlin \
      :app:processDebugResources \
      -PreactNativeArchitectures=arm64-v8a \
      --no-daemon
    ;;
  pos-handheld-android)
    npm ci --no-audit --no-fund
    npm run prebuild:android --workspace=@hb/pos-handheld -- --clean
    (
      cd apps/pos-handheld/android
      node --print "require.resolve('@sentry/react-native/package.json')" >/dev/null
    )
    bash apps/pos-handheld/android/gradlew \
      -p apps/pos-handheld/android \
      :hb-app-installer:testDebugUnitTest \
      :hb-attendance-security:testDebugUnitTest \
      :app:assembleDebug \
      :app:lintDebug \
      -PreactNativeArchitectures=arm64-v8a \
      --no-daemon
    ;;
  *)
    echo "未知 Android 组件: $component" >&2
    exit 2
    ;;
esac
