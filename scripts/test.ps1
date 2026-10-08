[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")][string]$Configuration = "Release",
    [switch]$NoBuild,
    [string]$DotNetPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "common.ps1")

function Read-TrxCounters {
    param([Parameter(Mandatory = $true)][string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) { throw "找不到 TRX：$Path" }
    [xml]$trx = Get-Content -LiteralPath $Path -Raw
    $counters = $trx.TestRun.ResultSummary.Counters
    return [pscustomobject]@{
        Total = [int]$counters.total
        Passed = [int]$counters.passed
        Failed = [int]$counters.failed
        Skipped = [int]$counters.notExecuted
    }
}

function Show-ApplicationControlHint {
    param([string[]]$Output)
    $joined = $Output -join [Environment]::NewLine
    if ($joined -match '0x800711C7|應用程式控制原則已封鎖') {
        $blocked = [regex]::Match($joined, '(?im)([A-Z]:\\[^\r\n]+?\.dll)').Value
        Write-HRResult FAIL "偵測到 Windows Application Control（0x800711C7）。"
        if (-not [string]::IsNullOrWhiteSpace($blocked)) { Write-Host "被封鎖 DLL：$blocked" }
        Write-Host "請執行 scripts\verify-environment.ps1；不要關閉 WDAC、AppLocker、Smart App Control 或 Defender。"
    }
}

try {
    $repo = Get-HRRepoRoot
    $dotnet = Get-HRDotNetPath -ExplicitPath $DotNetPath -RepoRoot $repo
    $projects = Get-HRTestProjects -RepoRoot $repo
    $resultsRoot = Join-Path $repo ".artifacts\TestResults"
    if (-not (Test-Path -LiteralPath $resultsRoot)) { New-Item -ItemType Directory -Path $resultsRoot | Out-Null }

    if (-not $NoBuild) {
        Write-HRStep "Build before Tests"
        $powershell = Get-HRPowerShellPath
        & $powershell -NoProfile -File (Join-Path $PSScriptRoot "build.ps1") -Configuration $Configuration -DotNetPath $dotnet
        if ($LASTEXITCODE -ne 0) { throw "測試前 Build 失敗。" }
    }

    $unitDirectory = Join-Path $resultsRoot "Unit"
    $integrationDirectory = Join-Path $resultsRoot "Integration"
    foreach ($directory in @($unitDirectory, $integrationDirectory)) {
        if (Test-Path -LiteralPath $directory) { Remove-Item -LiteralPath $directory -Recurse -Force }
        New-Item -ItemType Directory -Path $directory | Out-Null
    }

    Write-HRStep "Unit Tests"
    $unitArgs = @("test", $projects.Unit, "--configuration", $Configuration, "--no-build", "--no-restore", "--results-directory", $unitDirectory, "--logger", "trx;LogFileName=unit.trx")
    $unitRun = Invoke-HRNativeCapture -FilePath $dotnet -Arguments $unitArgs -WorkingDirectory $repo
    if ($unitRun.ExitCode -ne 0) {
        Show-ApplicationControlHint -Output $unitRun.Output
        throw "Unit Tests 失敗（Exit Code $($unitRun.ExitCode)）。"
    }
    $unit = Read-TrxCounters -Path (Join-Path $unitDirectory "unit.trx")

    Write-HRStep "Integration Tests"
    $integrationArgs = @("test", $projects.Integration, "--configuration", $Configuration, "--no-build", "--no-restore", "--results-directory", $integrationDirectory, "--logger", "trx;LogFileName=integration.trx")
    $integrationRun = Invoke-HRNativeCapture -FilePath $dotnet -Arguments $integrationArgs -WorkingDirectory $repo
    if ($integrationRun.ExitCode -ne 0) {
        Show-ApplicationControlHint -Output $integrationRun.Output
        throw "Integration Tests 失敗（Exit Code $($integrationRun.ExitCode)）。"
    }
    $integration = Read-TrxCounters -Path (Join-Path $integrationDirectory "integration.trx")

    if ($unit.Failed -ne 0 -or $integration.Failed -ne 0 -or $unit.Skipped -ne 0 -or $integration.Skipped -ne 0) {
        throw "測試結果包含失敗或略過。"
    }

    Write-HRStep "Test Summary"
    Write-Host "Unit       : $($unit.Passed)/$($unit.Total)"
    Write-Host "Integration: $($integration.Passed)/$($integration.Total)"
    Write-Host "Total      : $(($unit.Passed + $integration.Passed))/$(($unit.Total + $integration.Total))"
    exit 0
}
catch {
    Write-HRResult FAIL (Protect-HRSensitiveText $_.Exception.Message)
    exit 1
}
