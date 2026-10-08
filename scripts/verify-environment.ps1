[CmdletBinding()]
param(
    [string]$DotNetPath,
    [string]$RepoRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "common.ps1")

$failures = 0
$warnings = 0

function Add-CheckResult {
    param([ValidateSet("PASS", "WARN", "FAIL")][string]$Status, [string]$Message)
    if ($Status -eq "FAIL") { $script:failures++ }
    if ($Status -eq "WARN") { $script:warnings++ }
    Write-HRResult -Status $Status -Message $Message
}

try {
    if ([string]::IsNullOrWhiteSpace($RepoRoot)) { $RepoRoot = Get-HRRepoRoot }
    $RepoRoot = [IO.Path]::GetFullPath($RepoRoot)
    Write-HRStep "Development Environment Check"
    Add-CheckResult PASS "Windows：$([Environment]::OSVersion.VersionString)"

    if (Test-HRAdministrator) {
        Add-CheckResult FAIL "目前 PowerShell 具系統管理員權限；日常 Build/Test 必須使用一般使用者。"
    }
    else { Add-CheckResult PASS "目前使用一般使用者權限。" }

    Add-CheckResult PASS "Repo Root：$RepoRoot"
    if ($RepoRoot.StartsWith('\\')) { Add-CheckResult FAIL "Repo 位於 UNC 路徑。" } else { Add-CheckResult PASS "Repo 不是 UNC 路徑。" }
    if ($RepoRoot -match '(?i)[\\/]Downloads[\\/]') { Add-CheckResult FAIL "Repo 位於 Downloads。" } else { Add-CheckResult PASS "Repo 不在 Downloads。" }
    if ($RepoRoot -match '(?i)[\\/](OneDrive|Dropbox|Google Drive)[\\/]') {
        Add-CheckResult WARN "Repo 可能位於同步資料夾。"
    }
    else { Add-CheckResult PASS "未偵測到常見同步資料夾路徑。" }

    $git = Get-Command git -ErrorAction SilentlyContinue
    if ($null -eq $git) { Add-CheckResult FAIL "找不到 Git。" } else { Add-CheckResult PASS "Git 可用。" }

    try {
        $resolvedDotNet = Get-HRDotNetPath -ExplicitPath $DotNetPath -RepoRoot $RepoRoot
        $sdkVersion = (& $resolvedDotNet --version).Trim()
        if ($LASTEXITCODE -ne 0) { throw "dotnet --version 失敗。" }
        Add-CheckResult PASS "dotnet 可用；SDK $sdkVersion。"

        $globalPath = Join-Path $RepoRoot "global.json"
        if (-not (Test-Path -LiteralPath $globalPath)) {
            Add-CheckResult FAIL "缺少 global.json。"
        }
        else {
            $global = Get-Content -LiteralPath $globalPath -Raw | ConvertFrom-Json
            if ([string]$global.sdk.version -eq $sdkVersion) {
                Add-CheckResult PASS "global.json 與實際 SDK 相符。"
            }
            else { Add-CheckResult FAIL "global.json 指定 $($global.sdk.version)，實際為 $sdkVersion。" }
        }

        $runtimes = @(& $resolvedDotNet --list-runtimes)
        if (@($runtimes | Where-Object { $_ -match '^Microsoft\.NETCore\.App 10\.' }).Count -gt 0) {
            Add-CheckResult PASS ".NET 10 Runtime 可用。"
        }
        else { Add-CheckResult FAIL "找不到 .NET 10 Runtime。" }
    }
    catch {
        Add-CheckResult FAIL (Protect-HRSensitiveText $_.Exception.Message)
        $resolvedDotNet = $null
    }

    $nugetNames = @()
    $nugetConfigs = @(
        (Join-Path $RepoRoot "NuGet.Config"),
        (Join-Path $RepoRoot "nuget.config"),
        (Join-Path $env:APPDATA "NuGet\NuGet.Config")
    ) | Where-Object { Test-Path -LiteralPath $_ }
    foreach ($configPath in $nugetConfigs) {
        try {
            [xml]$config = Get-Content -LiteralPath $configPath -Raw
            $nugetNames += @($config.configuration.packageSources.add | ForEach-Object { [string]$_.key })
        }
        catch { Add-CheckResult WARN "無法解析一份 NuGet.Config；未輸出來源內容。" }
    }
    if ($nugetNames.Count -eq 0) { Add-CheckResult WARN "未讀到具名 NuGet Source。" }
    else { Add-CheckResult PASS "NuGet Sources：$((@($nugetNames | Sort-Object -Unique)) -join ', ')" }

    if (Test-Path -LiteralPath (Get-HRSolutionPath -RepoRoot $RepoRoot)) { Add-CheckResult PASS "HRSystem.slnx 存在。" }
    $projectSummary = Get-HRSolutionProjectSummary -RepoRoot $RepoRoot
    if ($projectSummary.Formal -eq 5 -and $projectSummary.Tests -eq 2 -and $projectSummary.Total -eq 7) {
        Add-CheckResult PASS "Solution 專案：正式 5、測試 2。"
    }
    else { Add-CheckResult FAIL "Solution 專案數不符：正式 $($projectSummary.Formal)、測試 $($projectSummary.Tests)、總計 $($projectSummary.Total)。" }

    try {
        $db = Get-HRDevelopmentDatabaseSummary -RepoRoot $RepoRoot
        if ($db.IsSafe) {
            Add-CheckResult PASS "Development DB：Server=$($db.Server)、Database=$($db.Database)、Windows Integrated；未輸出連線字串。"
        }
        else { Add-CheckResult FAIL "Development DB 目標不符合本機安全限制。" }
        if ($db.UsesSa) { Add-CheckResult FAIL "偵測到 sa。" }
        elseif ($db.UsesSqlLogin) { Add-CheckResult FAIL "偵測到 SQL Login。" }
        else { Add-CheckResult PASS "未使用 sa 或 SQL Login。" }
    }
    catch { Add-CheckResult FAIL (Protect-HRSensitiveText $_.Exception.Message) }

    $trackedZoneFiles = 0
    if ($null -ne $git) {
        $tracked = @(& $git.Source -C $RepoRoot ls-files)
        foreach ($relative in $tracked) {
            $path = Join-Path $RepoRoot $relative
            if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { continue }
            $streams = @(Get-Item -LiteralPath $path -Stream * -ErrorAction SilentlyContinue)
            if ($streams.Stream -contains 'Zone.Identifier') { $trackedZoneFiles++ }
        }
    }
    if ($trackedZoneFiles -eq 0) { Add-CheckResult PASS "Git 追蹤檔無 Zone.Identifier。" }
    else { Add-CheckResult FAIL "$trackedZoneFiles 個 Git 追蹤檔含 Zone.Identifier。" }

    $generatedDlls = @(Get-ChildItem -LiteralPath (Join-Path $RepoRoot 'src'), (Join-Path $RepoRoot 'tests') -Filter *.dll -File -Recurse -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match '[\\/](bin|obj)[\\/]' })
    $dllZoneCount = 0
    foreach ($dll in $generatedDlls) {
        $streams = @(Get-Item -LiteralPath $dll.FullName -Stream * -ErrorAction SilentlyContinue)
        if ($streams.Stream -contains 'Zone.Identifier') { $dllZoneCount++ }
    }
    if ($dllZoneCount -eq 0) { Add-CheckResult PASS "bin/obj DLL 無 Zone.Identifier。" }
    else { Add-CheckResult FAIL "$dllZoneCount 個產生 DLL 含 Zone.Identifier。" }

    try {
        $latestDll = $generatedDlls | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
        $since = if ($null -eq $latestDll) { (Get-Date).AddMinutes(-15) } else { $latestDll.LastWriteTime.AddSeconds(-1) }
        $repoEventFragment = if ($RepoRoot.Length -gt 2 -and $RepoRoot[1] -eq ':') { $RepoRoot.Substring(2) } else { $RepoRoot }
        $recentBlocks = @(Get-WinEvent -FilterHashtable @{ LogName = 'Microsoft-Windows-CodeIntegrity/Operational'; StartTime = $since } -ErrorAction SilentlyContinue |
            Where-Object { $_.Id -in @(3033, 3077) -and $_.Message -like "*$repoEventFragment*" })
        if ($recentBlocks.Count -gt 0) {
            $ids = ($recentBlocks.Id | Sort-Object -Unique) -join ','
            Add-CheckResult FAIL "最新產物仍有 Code Integrity 封鎖事件（Event ID $ids）。"
        }
        else { Add-CheckResult PASS "最新產物之後沒有 Repo DLL 的 Code Integrity 封鎖事件。" }

        $historical = @(Get-WinEvent -FilterHashtable @{ LogName = 'Microsoft-Windows-CodeIntegrity/Operational'; StartTime = (Get-Date).AddDays(-1) } -ErrorAction SilentlyContinue |
            Where-Object { $_.Id -in @(3033, 3077) -and $_.Message -like "*$repoEventFragment*" })
        if ($historical.Count -gt 0) { Add-CheckResult WARN "最近 24 小時有歷史 Code Integrity 證據；目前產物未重現。" }
        else { Add-CheckResult PASS "最近 24 小時無 Repo Code Integrity 事件。" }
    }
    catch { Add-CheckResult WARN "Code Integrity Event Log 無法讀取。" }

    try {
        $appLocker = Get-AppLockerPolicy -Effective -ErrorAction Stop
        $ruleCount = @($appLocker.RuleCollections | ForEach-Object { $_.Count } | Measure-Object -Sum).Sum
        if ($ruleCount -eq 0) { Add-CheckResult PASS "AppLocker 有效規則數為 0。" }
        else { Add-CheckResult WARN "AppLocker 有效規則數：$ruleCount。" }
    }
    catch { Add-CheckResult WARN "AppLocker 有效政策無法讀取。" }

    try {
        $sac = Get-ItemProperty -Path 'HKLM:\SYSTEM\CurrentControlSet\Control\CI\Policy' -ErrorAction Stop
        Add-CheckResult PASS "Smart App Control 狀態值：$($sac.VerifiedAndReputablePolicyState)（唯讀）。"
    }
    catch { Add-CheckResult WARN "Smart App Control 狀態無法讀取。" }

    Add-CheckResult PASS "TEMP=$env:TEMP；TMP=$env:TMP"
    $driveRoot = [IO.Path]::GetPathRoot($RepoRoot)
    $drive = [System.IO.DriveInfo]::new($driveRoot)
    if (-not $drive.IsReady) {
        Add-CheckResult FAIL "Repository 所在磁碟尚未就緒。"
    }
    else {
        $freeGb = [Math]::Round($drive.AvailableFreeSpace / 1GB, 2)
        if ($freeGb -lt 2) { Add-CheckResult FAIL "磁碟剩餘空間不足：$freeGb GB。" }
        elseif ($freeGb -lt 10) { Add-CheckResult WARN "磁碟剩餘空間偏低：$freeGb GB。" }
        else { Add-CheckResult PASS "磁碟剩餘空間：$freeGb GB。" }
    }

    $adsProbePath = Join-Path ([IO.Path]::GetTempPath()) ("hrsystem-environment-ads-probe-" + [Guid]::NewGuid().ToString('N') + ".tmp")
    $adsProbeSucceeded = $false
    $adsProbeFailure = $null
    try {
        $probeRoot = [IO.Path]::GetPathRoot([IO.Path]::GetFullPath($adsProbePath))
        if (-not $probeRoot.Equals($driveRoot, [StringComparison]::OrdinalIgnoreCase)) {
            throw "TEMP 與 Repository 不在相同磁碟，無法驗證 Repository 磁碟的 ADS。"
        }

        Set-Content -LiteralPath $adsProbePath -Value "probe" -Encoding Ascii
        Set-Content -LiteralPath $adsProbePath -Stream "HRSystemTest" -Value "supported" -Encoding Ascii
        $streamValue = Get-Content -LiteralPath $adsProbePath -Stream "HRSystemTest"
        if ($streamValue -ne "supported") {
            throw "ADS probe 讀回值不符。"
        }

        $adsProbeSucceeded = $true
    }
    catch {
        $adsProbeFailure = Protect-HRSensitiveText $_.Exception.Message
    }
    finally {
        Remove-Item -LiteralPath $adsProbePath -Force -ErrorAction SilentlyContinue
    }

    if (Test-Path -LiteralPath $adsProbePath) {
        Add-CheckResult FAIL "ADS probe 暫存檔無法清除。"
    }
    elseif ($adsProbeSucceeded) {
        Add-CheckResult PASS "Repository 磁碟 ADS probe 成功，暫存檔已清除。"
    }
    else {
        Add-CheckResult FAIL "Repository 磁碟 ADS probe 失敗：$adsProbeFailure"
    }

    $gitStatus = @(Get-HRGitStatus -RepoRoot $RepoRoot)
    if ($gitStatus.Count -eq 0) { Add-CheckResult PASS "Working Tree clean。" }
    else { Add-CheckResult WARN "Working Tree 有 $($gitStatus.Count) 筆變更（開發中允許；Release Audit 不允許）。" }
    $sensitiveUntracked = @($gitStatus | Where-Object { $_ -match '^\?\?' -and $_ -match '(?i)(secret|\.env|\.pfx|\.p12|\.key|credential)' })
    if ($sensitiveUntracked.Count -eq 0) { Add-CheckResult PASS "未發現未追蹤的敏感檔案名稱。" }
    else { Add-CheckResult FAIL "發現疑似未追蹤敏感檔案。" }
}
catch {
    Add-CheckResult FAIL (Protect-HRSensitiveText $_.Exception.Message)
}

Write-HRStep "Environment Check Summary"
Write-Host "FAIL=$failures WARN=$warnings"
if ($failures -gt 0) { exit 1 }
exit 0
