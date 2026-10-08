[CmdletBinding()]
param(
    [string]$Url = "http://localhost:5136",
    [string]$DotNetPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "common.ps1")

try {
    if (Test-HRAdministrator) { throw "Development Web App 不應使用系統管理員權限啟動。" }
    $repo = Get-HRRepoRoot
    $dotnet = Get-HRDotNetPath -ExplicitPath $DotNetPath -RepoRoot $repo
    $db = Assert-HRSafeDevelopmentDatabase -RepoRoot $repo
    Write-HRResult PASS "Development DB：Server=$($db.Server)、Database=$($db.Database)、Windows Integrated。"
    Write-Host "Web URL：$Url"
    Write-Host "本腳本不會套用 Migration；按 Ctrl+C 正常停止。"
    $env:ASPNETCORE_ENVIRONMENT = "Development"
    Invoke-HRNative -FilePath $dotnet -Arguments @("run", "--project", (Join-Path $repo "src\HRSystem.Web\HRSystem.Web.csproj"), "--no-launch-profile", "--urls", $Url) -WorkingDirectory $repo
    exit 0
}
catch {
    Write-HRResult FAIL (Protect-HRSensitiveText $_.Exception.Message)
    exit 1
}
