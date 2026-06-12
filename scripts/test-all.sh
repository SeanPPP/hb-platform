#!/usr/bin/env bash
set -euo pipefail

# 第一阶段保持各项目独立验证，不引入统一 workspace。

(
  cd apps/mobile
  npm run test:ota-config
  npm run test:i18n-locales
  npm run test:public-holiday-sync
)

(
  cd apps/web
  npm run build
  npm test
)

(
  cd services/backend
  dotnet restore BlazorApp.sln
  dotnet test BlazorApp.Api.Tests/BlazorApp.Api.Tests.csproj
)

(
  cd apps/pos-wpf
  dotnet restore hbpos_win.slnx -p:EnableWindowsTargeting=true
  dotnet build hbpos_win.slnx -p:EnableWindowsTargeting=true --no-restore
  dotnet test tests/Hbpos.Api.Tests/Hbpos.Api.Tests.csproj --no-restore

  if [ "${OS:-}" = "Windows_NT" ]; then
    dotnet test tests/Hbpos.Client.Tests/Hbpos.Client.Tests.csproj --no-restore
  else
    echo "跳过 POS WPF Client 测试：非 Windows 环境缺少 Microsoft.WindowsDesktop.App runtime。"
  fi
)
