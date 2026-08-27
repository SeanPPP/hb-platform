#!/usr/bin/env bash
set -euo pipefail

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
results_root="${RUNNER_TEMP:-$repository_root/.artifacts/ci}/weekly-sql"
mkdir -p "$results_root"
cd "$repository_root"

required_variables=(
  LOCAL_PURCHASE_DASHBOARD_SQLSERVER_TEST_CONNECTION
  PREORDER_SQLSERVER_TEST_CONNECTION
  SET_CHILD_PURCHASE_PRICE_SQLSERVER_TEST_CONNECTION
  STORE_PRICE_TRANSFER_SQLSERVER_TEST_CONNECTION
  DEVICE_ACTIVATION_SQLSERVER_TEST_CONNECTION
  CI_SQL_PASSWORD
)
for variable in "${required_variables[@]}"; do
  if [[ -z "${!variable:-}" ]]; then
    echo "Weekly SQL 缺少环境变量: $variable" >&2
    exit 1
  fi
done

mapfile -t sqlserver_containers < <(
  docker ps --filter 'publish=1433' --format '{{.ID}}'
)
if [[ "${#sqlserver_containers[@]}" -ne 1 ]]; then
  echo "Weekly SQL 必须且只能发现一个发布 1433 端口的专用容器。" >&2
  exit 1
fi
CI_SQLSERVER_CONTAINER_ID="${sqlserver_containers[0]}"

if docker exec "$CI_SQLSERVER_CONTAINER_ID" test -x /opt/mssql-tools18/bin/sqlcmd; then
  sqlcmd=/opt/mssql-tools18/bin/sqlcmd
else
  sqlcmd=/opt/mssql-tools/bin/sqlcmd
fi

ready=false
for _attempt in $(seq 1 60); do
  if docker exec "$CI_SQLSERVER_CONTAINER_ID" "$sqlcmd" \
    -C -S localhost -U sa -P "$CI_SQL_PASSWORD" -Q 'SELECT 1' >/dev/null 2>&1; then
    ready=true
    break
  fi
  sleep 2
done
if [[ "$ready" != true ]]; then
  echo "Weekly SQL Server 在 120 秒内未就绪。" >&2
  exit 1
fi

preflight_database="HbCiPreflight_${GITHUB_RUN_ID:-local}_${GITHUB_RUN_ATTEMPT:-1}"
docker exec "$CI_SQLSERVER_CONTAINER_ID" "$sqlcmd" \
  -C -S localhost -U sa -P "$CI_SQL_PASSWORD" \
  -Q "CREATE DATABASE [$preflight_database]; DROP DATABASE [$preflight_database];"

project="services/backend/BlazorApp.Api.Tests/BlazorApp.Api.Tests.csproj"
dotnet restore "$project"
dotnet build "$project" --configuration Release --no-restore
dotnet test "$project" \
  --configuration Release \
  --no-build \
  --filter 'Category=SQL' \
  --logger 'trx;LogFileName=weekly-sql.trx' \
  --results-directory "$results_root"
node scripts/ci/assert-trx-tests.mjs "$results_root/weekly-sql.trx" 'Weekly SQL tests'

activation_project="apps/pos-wpf/tests/Hbpos.Api.Tests/Hbpos.Api.Tests.csproj"
dotnet restore "$activation_project"
dotnet build "$activation_project" --configuration Release --no-restore
dotnet test "$activation_project" \
  --configuration Release \
  --no-build \
  --filter 'Category=SQL' \
  --logger 'trx;LogFileName=weekly-device-activation-sql.trx' \
  --results-directory "$results_root"
node scripts/ci/assert-trx-tests.mjs \
  "$results_root/weekly-device-activation-sql.trx" \
  'Weekly device activation SQL tests'
