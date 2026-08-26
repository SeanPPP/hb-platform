param(
  [Parameter(Mandatory = $true)]
  [string]$Component,
  [ValidateSet('pr', 'weekly')]
  [string]$Profile = 'pr',
  [ValidateSet(
    'all',
    'noop',
    'client-a-b-d-h',
    'client-c-card',
    'client-c-other',
    'client-i-k-m-n',
    'client-l-linkly',
    'client-l-other',
    'client-o-r',
    'client-s-shared',
    'client-s-other',
    'client-t-z',
    'ui')]
  [string]$Shard = 'all'
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
Set-Location $repositoryRoot

if ($Component -eq 'noop' -or $Shard -eq 'noop') {
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

function Invoke-TestProject {
  param(
    [Parameter(Mandatory = $true)]
    [string]$Project,
    [Parameter(Mandatory = $true)]
    [string]$Filter,
    [Parameter(Mandatory = $true)]
    [string]$LogFileName,
    [Parameter(Mandatory = $true)]
    [string]$Label,
    [Parameter(Mandatory = $true)]
    [string]$Destination
  )

  New-Item -ItemType Directory -Path $Destination -Force | Out-Null
  dotnet test $Project `
    --configuration Release `
    --no-build `
    --filter $Filter `
    --logger "trx;LogFileName=$LogFileName" `
    --results-directory $Destination
  Assert-TrxTests -Path (Join-Path $Destination $LogFileName) -Label $Label
}

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

$clientShardFilters = @{
  'client-a-b-d-h' = '(FullyQualifiedName~Hbpos.Client.Tests.A|FullyQualifiedName~Hbpos.Client.Tests.B|FullyQualifiedName~Hbpos.Client.Tests.D|FullyQualifiedName~Hbpos.Client.Tests.E|FullyQualifiedName~Hbpos.Client.Tests.F|FullyQualifiedName~Hbpos.Client.Tests.G|FullyQualifiedName~Hbpos.Client.Tests.H)'
  'client-c-card' = 'FullyQualifiedName~Hbpos.Client.Tests.Card'
  'client-c-other' = '(FullyQualifiedName~Hbpos.Client.Tests.C)&(FullyQualifiedName!~Hbpos.Client.Tests.Card)'
  'client-i-k-m-n' = '(FullyQualifiedName~Hbpos.Client.Tests.I|FullyQualifiedName~Hbpos.Client.Tests.J|FullyQualifiedName~Hbpos.Client.Tests.K|FullyQualifiedName~Hbpos.Client.Tests.M|FullyQualifiedName~Hbpos.Client.Tests.N)'
  'client-l-linkly' = 'FullyQualifiedName~Hbpos.Client.Tests.Linkly'
  'client-l-other' = '(FullyQualifiedName~Hbpos.Client.Tests.L)&(FullyQualifiedName!~Hbpos.Client.Tests.Linkly)'
  'client-o-r' = '(FullyQualifiedName~Hbpos.Client.Tests.O|FullyQualifiedName~Hbpos.Client.Tests.P|FullyQualifiedName~Hbpos.Client.Tests.Q|FullyQualifiedName~Hbpos.Client.Tests.R)'
  'client-s-shared' = 'FullyQualifiedName~Hbpos.Client.Tests.Shared'
  'client-s-other' = '(FullyQualifiedName~Hbpos.Client.Tests.S)&(FullyQualifiedName!~Hbpos.Client.Tests.Shared)'
  'client-t-z' = '(FullyQualifiedName~Hbpos.Client.Tests.T|FullyQualifiedName~Hbpos.Client.Tests.U|FullyQualifiedName~Hbpos.Client.Tests.V|FullyQualifiedName~Hbpos.Client.Tests.W|FullyQualifiedName~Hbpos.Client.Tests.X|FullyQualifiedName~Hbpos.Client.Tests.Y|FullyQualifiedName~Hbpos.Client.Tests.Z)'
}

if ($Shard -eq 'all' -or $clientShardFilters.ContainsKey($Shard)) {
  $clientFilter = if ($Shard -eq 'all') {
    $testFilter
  } else {
    "($testFilter)&($($clientShardFilters[$Shard]))"
  }
  $clientLogFileName = if ($Shard -eq 'all') {
    'Hbpos.Client.Tests.trx'
  } else {
    "Hbpos.Client.Tests.$Shard.trx"
  }
  $clientDestination = if ($Shard -eq 'all') {
    $resultsRoot
  } else {
    Join-Path $resultsRoot $Shard
  }
  Invoke-TestProject `
    -Project 'apps/pos-wpf/tests/Hbpos.Client.Tests/Hbpos.Client.Tests.csproj' `
    -Filter $clientFilter `
    -LogFileName $clientLogFileName `
    -Label "WPF client tests ($Shard)" `
    -Destination $clientDestination
}

if ($Shard -eq 'all' -or $Shard -eq 'ui') {
  $uiDestination = if ($Shard -eq 'all') {
    $resultsRoot
  } else {
    Join-Path $resultsRoot 'ui'
  }
  Invoke-TestProject `
    -Project 'apps/pos-wpf/tests/Hbpos.Client.UiTests/Hbpos.Client.UiTests.csproj' `
    -Filter $testFilter `
    -LogFileName 'Hbpos.Client.UiTests.trx' `
    -Label 'WPF UI tests' `
    -Destination $uiDestination
}
