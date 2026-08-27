#!/usr/bin/env bash
set -euo pipefail

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
component="${1:?缺少 .NET 组件名}"
results_root="${RUNNER_TEMP:-$repository_root/.artifacts/ci}/$component"
mkdir -p "$results_root"
cd "$repository_root"

case "$component" in
  noop)
    echo "该 PR 没有需要此 runner 执行的组件。"
    ;;
  backend)
    project="services/backend/BlazorApp.Api.Tests/BlazorApp.Api.Tests.csproj"
    node scripts/ci/backend-test-inventory.mjs
    dotnet restore "$project"
    dotnet build "$project" --configuration Release --no-restore
    dotnet test "$project" \
      --configuration Release \
      --no-build \
      --filter 'Category!=SQL&Category!=Performance&Category!=LiveE2e' \
      --logger 'trx;LogFileName=backend.trx' \
      --results-directory "$results_root"
    node scripts/ci/assert-trx-tests.mjs "$results_root/backend.trx" 'Backend tests'

    dotnet test "$project" \
      --configuration Release \
      --no-build \
      --filter 'FullyQualifiedName~Contract&Category!=SQL&Category!=LiveE2e' \
      --logger 'trx;LogFileName=backend-contract.trx' \
      --results-directory "$results_root"
    node scripts/ci/assert-trx-tests.mjs "$results_root/backend-contract.trx" 'Backend API contract'
    ;;
  pos-api)
    project="apps/pos-wpf/tests/Hbpos.Api.Tests/Hbpos.Api.Tests.csproj"
    dotnet restore "$project"
    dotnet build "$project" --configuration Release --no-restore
    dotnet test "$project" \
      --configuration Release \
      --no-build \
      --filter 'Category!=SQL&Category!=Performance&Category!=LiveE2e' \
      --logger 'trx;LogFileName=pos-api.trx' \
      --results-directory "$results_root"
    node scripts/ci/assert-trx-tests.mjs "$results_root/pos-api.trx" 'POS API tests'
    ;;
  pos-contract)
    project="apps/pos-wpf/tests/Hbpos.Api.Tests/Hbpos.Api.Tests.csproj"
    dotnet restore "$project"
    npm --prefix apps/pos-ipad ci --no-audit --no-fund
    npm --prefix apps/pos-handheld ci --no-audit --no-fund
    npm --prefix apps/pos-ipad run test:codegen
    npm --prefix apps/pos-handheld run test:codegen
    ;;
  *)
    echo "未知 .NET 组件: $component" >&2
    exit 2
    ;;
esac
