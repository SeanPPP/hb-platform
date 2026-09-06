param(
    [string]$Configuration = "Release",
    [string]$OutputDirectory = "",
    [string]$WpfOutputDirectory = ""
)

$ErrorActionPreference = "Stop"
$scriptDirectory = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectDirectory = Split-Path -Parent $scriptDirectory
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $projectDirectory "artifacts\remote-maintenance"
}

if (Test-Path -LiteralPath $OutputDirectory) {
    if ((Get-ChildItem -LiteralPath $OutputDirectory -Force | Measure-Object).Count -gt 0) {
        throw "输出目录必须为空：$OutputDirectory"
    }
} else {
    New-Item -ItemType Directory -Path $OutputDirectory | Out-Null
}

$agentProject = Join-Path $projectDirectory "src\Hbpos.RemoteStatus\Hbpos.RemoteStatus.csproj"
$helperProject = Join-Path $projectDirectory "src\Hbpos.RemoteMaintenance.Setup\Hbpos.RemoteMaintenance.Setup.csproj"
$agentOutput = Join-Path $OutputDirectory "agent"
$helperOutput = Join-Path $OutputDirectory "helper"

dotnet publish $agentProject -c $Configuration -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableWindowsTargeting=true -o $agentOutput
dotnet publish $helperProject -c $Configuration -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableWindowsTargeting=true -o $helperOutput

if (-not [string]::IsNullOrWhiteSpace($WpfOutputDirectory)) {
    if (-not (Test-Path -LiteralPath $WpfOutputDirectory -PathType Container)) {
        throw "WPF 输出目录不存在：$WpfOutputDirectory"
    }
    # WPF 安装目录应由 MSI/管理员写入；普通用户可写的 journal 目录不承载 helper。
    Copy-Item -LiteralPath (Join-Path $agentOutput "Hbpos.RemoteStatus.exe") -Destination $WpfOutputDirectory -Force
    Copy-Item -LiteralPath (Join-Path $helperOutput "Hbpos.RemoteMaintenance.Setup.exe") -Destination $WpfOutputDirectory -Force
}

$readmePath = Join-Path $OutputDirectory "README.txt"
Set-Content -LiteralPath $readmePath -Encoding UTF8 -Value @"
HB POS Remote Maintenance Windows x64

Hbpos.RemoteStatus.exe and Hbpos.RemoteMaintenance.Setup.exe must be installed by the
all-users WPF installer into Program Files. Do not run either executable from an ordinary
user-writable extracted directory; the elevated helper intentionally rejects that layout.
"@

$zipPath = Join-Path $OutputDirectory "hbpos-remote-maintenance-win-x64.zip"
Compress-Archive -Path (Join-Path $agentOutput "Hbpos.RemoteStatus.exe"), (Join-Path $helperOutput "Hbpos.RemoteMaintenance.Setup.exe"), $readmePath -DestinationPath $zipPath
Get-FileHash -Algorithm SHA256 $zipPath
Get-Item -LiteralPath $zipPath | Select-Object FullName, Length
