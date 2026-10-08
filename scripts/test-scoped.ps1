[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Scope,
    [Parameter(Mandatory = $true)][string]$SolutionFilter,
    [string[]]$UnitClasses = @(),
    [string[]]$IntegrationClasses = @(),
    [ValidateSet("Debug", "Release")][string]$Configuration = "Release",
    [switch]$NoBuild,
    [string]$DotNetPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "common.ps1")

function New-ClassFilter {
    param([string[]]$Classes)

    if ($Classes.Count -eq 0) { return $null }
    return (($Classes | ForEach-Object { "FullyQualifiedName~$($_)." }) -join "|")
}

function Invoke-ScopedTestProject {
    param(
        [string]$Label,
        [string]$Project,
        [string[]]$Classes,
        [string]$ResultsDirectory,
        [string]$LogFileName
    )

    if ($Classes.Count -eq 0) {
        return [pscustomobject]@{ Total = 0; Passed = 0; Failed = 0; Skipped = 0 }
    }

    $filter = New-ClassFilter -Classes $Classes
    Write-HRStep "$Scope - $Label"
    Write-Host "Filter classes: $($Classes.Count)"
    $arguments = @(
        "test", $Project,
        "--configuration", $Configuration,
        "--no-build", "--no-restore",
        "--filter", $filter,
        "--results-directory", $ResultsDirectory,
        "--logger", "trx;LogFileName=$LogFileName"
    )
    Invoke-HRNative -FilePath $dotnet -Arguments $arguments -WorkingDirectory $repo | Out-Host

    [xml]$trx = Get-Content -LiteralPath (Join-Path $ResultsDirectory $LogFileName) -Raw
    $counters = $trx.TestRun.ResultSummary.Counters
    $result = [pscustomobject]@{
        Total = [int]$counters.total
        Passed = [int]$counters.passed
        Failed = [int]$counters.failed
        Skipped = [int]$counters.notExecuted
    }
    if ($result.Total -eq 0) {
        throw "$Scope $Label allowlist selected zero tests."
    }
    return $result
}

try {
    $repo = Get-HRRepoRoot
    $dotnet = Get-HRDotNetPath -ExplicitPath $DotNetPath -RepoRoot $repo
    $filterPath = Join-Path $repo $SolutionFilter
    if (-not (Test-Path -LiteralPath $filterPath -PathType Leaf)) {
        throw "Solution Filter does not exist: $filterPath"
    }

    if (-not $NoBuild) {
        Write-HRStep "$Scope - Restore"
        Invoke-HRNative -FilePath $dotnet -Arguments @("restore", $filterPath) -WorkingDirectory $repo
        Write-HRStep "$Scope - Build"
        Invoke-HRNative -FilePath $dotnet -Arguments @(
            "build", $filterPath, "--configuration", $Configuration, "--no-restore") -WorkingDirectory $repo
    }

    $projects = Get-HRTestProjects -RepoRoot $repo
    $safeScope = $Scope -replace '[^A-Za-z0-9_.-]', '_'
    $resultsRoot = Join-Path $repo ".artifacts\ScopedTestResults\$safeScope"
    if (Test-Path -LiteralPath $resultsRoot) {
        Remove-Item -LiteralPath $resultsRoot -Recurse -Force
    }
    New-Item -ItemType Directory -Path $resultsRoot | Out-Null

    $unit = Invoke-ScopedTestProject -Label "Unit" -Project $projects.Unit `
        -Classes $UnitClasses -ResultsDirectory $resultsRoot -LogFileName "unit.trx"
    $integration = Invoke-ScopedTestProject -Label "Integration/Web" -Project $projects.Integration `
        -Classes $IntegrationClasses -ResultsDirectory $resultsRoot -LogFileName "integration.trx"

    $total = $unit.Total + $integration.Total
    $passed = $unit.Passed + $integration.Passed
    $failed = $unit.Failed + $integration.Failed
    $skipped = $unit.Skipped + $integration.Skipped
    if ($failed -ne 0 -or $skipped -ne 0) {
        throw "$Scope tests contain failures or skipped cases: Failed=$failed, Skipped=$skipped"
    }

    Write-HRStep "$Scope - Summary"
    Write-Host "Unit       : $($unit.Passed)/$($unit.Total)"
    Write-Host "Integration: $($integration.Passed)/$($integration.Total)"
    Write-Host "Total      : $passed/$total"
    Write-Host "Failed     : $failed"
    Write-Host "Skipped    : $skipped"
    exit 0
}
catch {
    Write-HRResult FAIL (Protect-HRSensitiveText $_.Exception.Message)
    exit 1
}
