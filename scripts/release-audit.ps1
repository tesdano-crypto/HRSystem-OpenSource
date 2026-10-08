[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")][string]$Configuration = "Release",
    [string]$DotNetPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "common.ps1")

function Assert-NoMatches {
    param(
        [Parameter(Mandatory = $true)][string]$Label,
        [Parameter(Mandatory = $true)][string]$Pattern,
        [Parameter(Mandatory = $true)][string[]]$Paths
    )

    $rg = Get-Command rg -ErrorAction SilentlyContinue
    if ($null -eq $rg) { throw "Release Audit 需要 ripgrep（rg）執行安全掃描。" }
    $arguments = @("-n", "-i", $Pattern) + $Paths + @("-g", "!**/bin/**", "-g", "!**/obj/**", "-g", "!**/TestResults/**")
    $matches = @(& $rg.Source @arguments 2>$null)
    $exitCode = $LASTEXITCODE
    if ($exitCode -eq 0 -and $matches.Count -gt 0) { throw "$Label 發現 $($matches.Count) 筆不允許內容。" }
    if ($exitCode -notin @(0, 1)) { throw "$Label 掃描失敗。" }
    Write-HRResult PASS "$Label：0 筆。"
}

try {
    $repo = Get-HRRepoRoot
    $powershell = Get-HRPowerShellPath
    Assert-HRGitClean -RepoRoot $repo
    $dotnet = Get-HRDotNetPath -ExplicitPath $DotNetPath -RepoRoot $repo
    $identity = Get-HRGitIdentity -RepoRoot $repo

    Write-HRStep "Git Baseline"
    Write-Host "Branch: $($identity.Branch)"
    Write-Host "Commit: $($identity.Commit)"
    Write-Host "Tags  : $($identity.Tags -join ', ')"

    Write-HRStep "Environment Check"
    & $powershell -NoProfile -File (Join-Path $PSScriptRoot "verify-environment.ps1") -DotNetPath $dotnet -RepoRoot $repo
    if ($LASTEXITCODE -ne 0) { throw "Environment Check 失敗。" }

    Write-HRStep "Clean"
    Invoke-HRNative -FilePath $dotnet -Arguments @("clean", (Get-HRSolutionPath -RepoRoot $repo), "--configuration", $Configuration) -WorkingDirectory $repo

    Write-HRStep "Restore + Build"
    & $powershell -NoProfile -File (Join-Path $PSScriptRoot "build.ps1") -Configuration $Configuration -DotNetPath $dotnet
    if ($LASTEXITCODE -ne 0) { throw "Build Script 失敗。" }

    Write-HRStep "Unit + Integration Tests"
    & $powershell -NoProfile -File (Join-Path $PSScriptRoot "test.ps1") -Configuration $Configuration -NoBuild -DotNetPath $dotnet
    if ($LASTEXITCODE -ne 0) { throw "Test Script 失敗。" }

    Write-HRStep "Migration Check"
    & $powershell -NoProfile -File (Join-Path $PSScriptRoot "check-migrations.ps1") -Configuration $Configuration -DotNetPath $dotnet
    if ($LASTEXITCODE -ne 0) { throw "Migration Check 失敗。" }

    Write-HRStep "Static Release Scans"
    $projectSummary = Get-HRSolutionProjectSummary -RepoRoot $repo
    if ($projectSummary.Formal -ne 5 -or $projectSummary.Tests -ne 2 -or $projectSummary.Total -ne 7) { throw "Solution 含非正式專案。" }
    Write-HRResult PASS "Solution 僅含正式 5 專案與測試 2 專案。"

    $projectFiles = @(Get-ChildItem -LiteralPath (Join-Path $repo 'src'), (Join-Path $repo 'tests') -Filter *.csproj -File -Recurse)
    if ($projectFiles.Count -ne 7) { throw "src/tests 內的 csproj 數量不是 7。" }
    $temporaryProjects = @($projectFiles | Where-Object { $_.Name -match '(?i)(temp|scenario|runner|console|debug|browser|tool)' })
    if ($temporaryProjects.Count -gt 0) { throw "偵測到 Temporary/Scenario/Console Project。" }
    Write-HRResult PASS "無 Temporary Tool、Scenario Runner 或 Console Project。"

    $temporaryFiles = @(Get-ChildItem -LiteralPath $repo -File -Recurse -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch '[\\/](\.git|bin|obj|TestResults|\.artifacts)[\\/]' -and $_.Extension -in @('.tmp', '.bak', '.orig') })
    if ($temporaryFiles.Count -gt 0) { throw "偵測到暫存或備份檔。" }
    Write-HRResult PASS "無 *.tmp、*.bak、*.orig。"

    Assert-NoMatches -Label "TODO/FIXME/HACK/TEMP/DEBUG" -Pattern '\b(TODO|FIXME|HACK|TEMP|DEBUG)\b' -Paths @((Join-Path $repo 'src'), (Join-Path $repo 'tests'))
    Assert-NoMatches -Label "Console.WriteLine" -Pattern 'Console\.WriteLine' -Paths @((Join-Path $repo 'src'), (Join-Path $repo 'tests'))
    Assert-NoMatches -Label "硬編碼敏感值" -Pattern '(Password|Token|Secret|Api.?Key)\s*[:=]\s*\x22[^\x22{][^\x22]+\x22' -Paths @((Join-Path $repo 'src'))
    Assert-NoMatches -Label "Privileged SQL login" -Pattern 'User\s+Id\s*=\s*sa|UID\s*=\s*sa' -Paths @((Join-Path $repo 'src'))
    Assert-NoMatches -Label "Razor 直接 DbContext" -Pattern 'DbContext|IApplicationDbContext|SaveChanges|\.Database\.' -Paths @((Join-Path $repo 'src\HRSystem.Web\Components'))

    $migrationFiles = @(Get-ChildItem -LiteralPath (Join-Path $repo 'src\HRSystem.Infrastructure\Persistence\Migrations') -Filter '*_*.cs' -File |
        Where-Object { $_.Name -notlike '*.Designer.cs' })
    if ($migrationFiles.Count -ne 5) { throw "Migration 檔案數應為 5，實際為 $($migrationFiles.Count)。" }
    if (@($migrationFiles | Where-Object { $_.Name -like '*AddCompanyCalendar.cs' }).Count -ne 1) {
        throw "AddCompanyCalendar Migration 缺少或重複。"
    }
    Write-HRResult PASS "Migration 檔案 5/5，包含 AddCompanyCalendar。"

    Assert-HRGitClean -RepoRoot $repo
    Write-HRStep "Release Audit Summary"
    Write-HRResult PASS "Release Audit 全部通過；未 Push、未修改安全政策。"
    exit 0
}
catch {
    Write-HRResult FAIL (Protect-HRSensitiveText $_.Exception.Message)
    exit 1
}
