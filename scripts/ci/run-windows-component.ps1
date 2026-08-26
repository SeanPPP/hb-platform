param(
  [Parameter(Mandatory = $true)]
  [string]$Component,
  [ValidateSet('pr', 'weekly')]
  [string]$Profile = 'pr'
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
Set-Location $repositoryRoot

if ($Component -eq 'noop') {
  Write-Host '该 PR 没有需要 Windows runner 执行的组件。'
  exit 0
}
if ($Component -ne 'pos-wpf') {
  throw "未知 Windows 组件：$Component"
}

$resultsRoot = if ($env:RUNNER_TEMP) {
  Join-Path $env:RUNNER_TEMP 'pos-wpf'
} else {
  Join-Path $repositoryRoot '.artifacts\ci\pos-wpf'
}
New-Item -ItemType Directory -Path $resultsRoot -Force | Out-Null

function Assert-TrxTests {
  param(
    [Parameter(Mandatory = $true)]
    [string]$Path,
    [Parameter(Mandatory = $true)]
    [string]$Label
  )

  if (-not (Test-Path -LiteralPath $Path)) {
    throw "$Label 缺少 TRX：$Path"
  }
  [xml]$trx = Get-Content -LiteralPath $Path -Raw
  $counters = $trx.SelectSingleNode("//*[local-name()='Counters']")
  if ($null -eq $counters) {
    throw "$Label TRX 缺少 Counters"
  }
  $requiredCounterNames = @('total', 'executed', 'passed', 'failed', 'error', 'timeout', 'aborted')
  $counterValues = @{}
  foreach ($counterName in $requiredCounterNames) {
    if (-not $counters.HasAttribute($counterName)) {
      throw "$Label TRX 缺少关键计数：$counterName"
    }

    $count = 0
    if (-not [int]::TryParse($counters.GetAttribute($counterName), [ref]$count)) {
      throw "$Label TRX 计数无效：$counterName"
    }
    $counterValues[$counterName] = $count
  }
  if ($counterValues.total -lt 1) {
    throw "$Label 执行测试数为 0，拒绝假绿"
  }
  if ($counterValues.total -ne $counterValues.executed -or $counterValues.total -ne $counterValues.passed) {
    throw "$Label TRX 计数不一致：total=$($counterValues.total), executed=$($counterValues.executed), passed=$($counterValues.passed)"
  }
  foreach ($counterName in @('failed', 'error', 'timeout', 'aborted', 'inconclusive', 'passedButRunAborted', 'notRunnable', 'notExecuted', 'disconnected', 'warning', 'completed', 'inProgress', 'pending')) {
    if ($counters.HasAttribute($counterName)) {
      $count = 0
      if (-not [int]::TryParse($counters.GetAttribute($counterName), [ref]$count)) {
        throw "$Label TRX 计数无效：$counterName"
      }
      if ($count -ne 0) {
        throw "$Label 存在非成功结果：$counterName=$count"
      }
    }
  }
  Write-Host "$Label TRX 验证通过：executed=$($counterValues.executed), passed=$($counterValues.passed)"
}

$testFilter = if ($Profile -eq 'weekly') {
  $env:HBPOS_RUN_PERF_TESTS = '1'
  'Category!=LiveE2e'
} else {
  'Category!=Performance&Category!=LiveE2e'
}

dotnet restore apps/pos-wpf/hbpos_win.slnx
dotnet build apps/pos-wpf/hbpos_win.slnx --configuration Release --no-restore

dotnet test apps/pos-wpf/tests/Hbpos.Client.Tests/Hbpos.Client.Tests.csproj `
  --configuration Release `
  --no-build `
  --filter $testFilter `
  --logger 'trx;LogFileName=Hbpos.Client.Tests.trx' `
  --results-directory $resultsRoot
Assert-TrxTests -Path (Join-Path $resultsRoot 'Hbpos.Client.Tests.trx') -Label 'WPF client tests'

dotnet test apps/pos-wpf/tests/Hbpos.Client.UiTests/Hbpos.Client.UiTests.csproj `
  --configuration Release `
  --no-build `
  --filter $testFilter `
  --logger 'trx;LogFileName=Hbpos.Client.UiTests.trx' `
  --results-directory $resultsRoot
Assert-TrxTests -Path (Join-Path $resultsRoot 'Hbpos.Client.UiTests.trx') -Label 'WPF UI tests'
