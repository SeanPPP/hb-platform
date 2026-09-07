[CmdletBinding()]
param(
    [ValidateSet('All', 'Correctness', 'Timing', 'Performance')]
    [string]$Suite = 'All',

    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [string]$ArtifactsPath
)

$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$testProject = Join-Path $repoRoot 'apps/pos-wpf/tests/Hbpos.Client.Tests/Hbpos.Client.Tests.csproj'
if ([string]::IsNullOrWhiteSpace($ArtifactsPath)) {
    $ArtifactsPath = Join-Path $repoRoot '.artifacts/wpf-card-regression'
}
$ArtifactsPath = [IO.Path]::GetFullPath($ArtifactsPath)
# 每次保留独立结果，防止旧 TRX 被误当作本轮通过证据；构建产物可复用。
$runId = '{0}-{1}' -f (Get-Date -Format 'yyyyMMdd-HHmmss'), ([Guid]::NewGuid().ToString('N').Substring(0, 8))
$resultsPath = Join-Path $ArtifactsPath "test-results/$runId"
New-Item -ItemType Directory -Path $resultsPath -Force | Out-Null

$testClasses = @(
    'CashPaymentWorkflowServiceTests'
    'CardPaymentRecoveryServiceTests'
    'SquarePaymentRecoveryService'
    'ConfiguredCardTerminalClientTests'
    'ConfiguredLinklyTerminalClientTests'
    'LinklyTerminalClientTests'
    'LinklyCloudTerminalClientTests'
    'LinklyBackendTerminalClient'
    'SquareTerminalPaymentClientTests'
    'CardRecoveryCenter'
    'CardPaymentHandoff'
    'PaymentTerminalSelectionViewModelTests'
    'PaymentViewLayoutTests'
    'PaymentViewRuntimeTests'
    'WpfViewLifecycleTests'
    'PaymentFlowIntegrationTests'
    'MainViewModelScannerTests'
    'PosTerminalCashPaymentViewModelTests'
    'LocalCardPaymentAttemptRepositoryTests'
    'LocalSquarePaymentAttemptRepositoryTests'
)
$scopeFilter = '(' + (($testClasses | ForEach-Object { "FullyQualifiedName~$_" }) -join '|') + ')'
$filters = [ordered]@{
    Correctness = "$scopeFilter&Category!=Performance&Category!=Timing"
    Timing = "$scopeFilter&Category=Timing&Category!=Performance"
    Performance = "$scopeFilter&Category=Performance"
}
$selectedSuites = if ($Suite -eq 'All') { @($filters.Keys) } else { @($Suite) }

# 先完成 restore/build，再顺序执行分组，避免构建负载干扰性能阈值。
& dotnet build $testProject --configuration $Configuration --artifacts-path $ArtifactsPath --verbosity quiet
if ($LASTEXITCODE -ne 0) {
    throw "刷卡回归测试工程构建失败，退出码：$LASTEXITCODE"
}

$summary = @()
foreach ($group in $selectedSuites) {
    Write-Host "执行刷卡回归分组：$group"
    $trxName = "$group.trx"
    & dotnet test $testProject --configuration $Configuration --artifacts-path $ArtifactsPath `
        --no-build --no-restore --filter $filters[$group] --results-directory $resultsPath `
        --logger "trx;LogFileName=$trxName" --verbosity quiet
    $testExitCode = $LASTEXITCODE
    $trxPath = Join-Path $resultsPath $trxName
    if (-not (Test-Path -LiteralPath $trxPath -PathType Leaf)) {
        $summary += [pscustomobject]@{ Suite = $group; Total = 0; Passed = 0; Failed = 0; ExitCode = $testExitCode; Valid = $false }
        continue
    }

    [xml]$trx = Get-Content -LiteralPath $trxPath -Raw
    $counters = $trx.TestRun.ResultSummary.Counters
    $total = [int]$counters.total
    $executed = [int]$counters.executed
    $passed = [int]$counters.passed
    # 零测试、跳过或缺失结果均不能被报告为该分组通过。
    $valid = $testExitCode -eq 0 -and $total -gt 0 -and $executed -eq $total -and
        $passed -eq $total -and $trx.TestRun.ResultSummary.outcome -eq 'Completed'
    foreach ($counterName in @('failed', 'error', 'timeout', 'aborted', 'inconclusive',
        'passedButRunAborted', 'notRunnable', 'notExecuted', 'disconnected', 'warning',
        'inProgress', 'pending')) {
        if ($counters.HasAttribute($counterName) -and [int]$counters.GetAttribute($counterName) -ne 0) {
            $valid = $false
        }
    }
    $summary += [pscustomobject]@{
        Suite = $group
        Total = $total
        Passed = $passed
        Failed = [int]$counters.failed
        ExitCode = $testExitCode
        Valid = $valid
    }
}

$summary | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath (Join-Path $resultsPath 'summary.json') -Encoding utf8
$summary | Format-Table -AutoSize | Out-Host
Write-Host "测试结果：$resultsPath"
if (@($summary | Where-Object { -not $_.Valid }).Count -gt 0) {
    throw '刷卡回归存在失败、跳过或未执行的分组，请检查对应 TRX。'
}
