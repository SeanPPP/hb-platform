#!/usr/bin/env bash
set -euo pipefail

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
results_root="${RUNNER_TEMP:-$repository_root/.artifacts/ci}/weekly-performance"
mkdir -p "$results_root"
cd "$repository_root"

project="apps/pos-wpf/tests/Hbpos.Api.Tests/Hbpos.Api.Tests.csproj"
dotnet restore "$project"
dotnet build "$project" --configuration Release --no-restore
dotnet test "$project" \
  --configuration Release \
  --no-build \
  --filter 'Category=Performance' \
  --logger 'trx;LogFileName=weekly-performance.trx' \
  --results-directory "$results_root"
node scripts/ci/assert-trx-tests.mjs "$results_root/weekly-performance.trx" 'Weekly performance tests'
