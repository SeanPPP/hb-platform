#!/usr/bin/env bash
set -euo pipefail

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
component="${1:?缺少 Node 组件名}"
cd "$repository_root"

case "$component" in
  noop)
    echo "该 PR 没有需要此 runner 执行的组件。"
    ;;
  web)
    npm --prefix apps/web ci --no-audit --no-fund
    npm --prefix apps/web run typecheck
    npm --prefix apps/web run test:ci
    VITE_CENTER_LOG_KEY=ci-test-only \
      VITE_CENTER_LOG_PROJECT=hbweb_rv \
      VITE_CENTER_LOG_ENVIRONMENT=Production \
      VITE_CENTER_LOG_SERVICE_NAME=hbweb_rv-web \
      npm --prefix apps/web run build:ci -- --manifest
    npm --prefix apps/web run verify:bundle
    ;;
  mobile)
    npm --prefix apps/mobile ci --no-audit --no-fund
    npm --prefix apps/mobile run typecheck
    npm --prefix apps/mobile run lint
    npm --prefix apps/mobile run test:ci
    EXPO_NO_TELEMETRY=1 npm --prefix apps/mobile run build:ci
    ;;
  pos-ipad)
    npm ci --no-audit --no-fund
    npm run test:pos-shared-ci
    npm run typecheck --workspace=@hb/pos-ipad
    npm run lint --workspace=@hb/pos-ipad
    npm run test:ci --workspace=@hb/pos-ipad
    ;;
  pos-handheld)
    npm ci --no-audit --no-fund
    npm run typecheck --workspace=@hb/pos-handheld
    npm run lint --workspace=@hb/pos-handheld
    npm run test:ci --workspace=@hb/pos-handheld
    ;;
  supplier-extension)
    npm --prefix apps/supplier-order-extension ci --no-audit --no-fund
    npm --prefix apps/supplier-order-extension test
    npm --prefix apps/supplier-order-extension run build
    node scripts/ci/extension-manifest.mjs
    ;;
  antpos-web)
    npm --prefix apps/antpos-web run check
    npm --prefix apps/antpos-web run build
    ;;
  *)
    echo "未知 Node 组件: $component" >&2
    exit 2
    ;;
esac
