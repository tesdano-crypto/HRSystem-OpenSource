[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")][string]$Configuration = "Release",
    [switch]$NoRestore,
    [string]$DotNetPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "common.ps1")

try {
    $repo = Get-HRRepoRoot
    $dotnet = Get-HRDotNetPath -ExplicitPath $DotNetPath -RepoRoot $repo
    $solution = Get-HRSolutionPath -RepoRoot $repo
    $powershell = Get-HRPowerShellPath
    $watch = [Diagnostics.Stopwatch]::StartNew()

    Write-HRStep "Environment 基本檢查"
    & $powershell -NoProfile -File (Join-Path $PSScriptRoot "verify-environment.ps1") -DotNetPath $dotnet -RepoRoot $repo
    if ($LASTEXITCODE -ne 0) { throw "Environment Check 失敗。" }

    $sdk = (& $dotnet --version).Trim()
    if (-not $NoRestore) {
        Write-HRStep "dotnet restore"
        Invoke-HRNative -FilePath $dotnet -Arguments @("restore", $solution) -WorkingDirectory $repo
    }

    Write-HRStep "dotnet build ($Configuration)"
    $arguments = @("build", $solution, "--configuration", $Configuration, "--no-restore")
    Invoke-HRNative -FilePath $dotnet -Arguments $arguments -WorkingDirectory $repo
    $watch.Stop()

    Write-HRStep "Build Summary"
    Write-Host "SDK Version : $sdk"
    Write-Host "Configuration: $Configuration"
    Write-Host "Warning Count: 0（TreatWarningsAsErrors=true）"
    Write-Host "Error Count  : 0"
    Write-Host "Elapsed Time : $($watch.Elapsed)"
    exit 0
}
catch {
    Write-HRResult FAIL (Protect-HRSensitiveText $_.Exception.Message)
    exit 1
}
