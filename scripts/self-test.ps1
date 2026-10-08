[CmdletBinding()]
param([switch]$IncludeReleaseAuditDirtyCheck)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "common.ps1")

$passed = 0
$failed = 0
$tempRoot = Join-Path $env:TEMP ("HRSystem-ScriptSelfTest-" + [Guid]::NewGuid().ToString('N'))

function Confirm-SelfTest {
    param([string]$Name, [bool]$Condition)
    if ($Condition) { $script:passed++; Write-HRResult PASS $Name }
    else { $script:failed++; Write-HRResult FAIL $Name }
}

try {
    $repo = Get-HRRepoRoot
    $powershell = Get-HRPowerShellPath
    $dotnet = Get-HRDotNetPath -RepoRoot $repo
    New-Item -ItemType Directory -Path $tempRoot | Out-Null

    Write-HRStep "正常 Environment Check"
    $normalRun = Invoke-HRNativeCapture -FilePath $powershell -Arguments @(
        '-NoProfile',
        '-File',
        (Join-Path $PSScriptRoot 'verify-environment.ps1'),
        '-DotNetPath',
        $dotnet,
        '-RepoRoot',
        $repo
    ) -WorkingDirectory $repo
    $normalRun.Output | ForEach-Object { Write-Host $_ }
    $normalText = $normalRun.Output -join [Environment]::NewLine
    Confirm-SelfTest "verify-environment 正常條件回傳 0" ($normalRun.ExitCode -eq 0)
    Confirm-SelfTest "verify-environment 使用有效磁碟可用空間" ($normalText -match '磁碟剩餘空間：[1-9][0-9]*(\.[0-9]+)? GB')
    Confirm-SelfTest "verify-environment 實測 ADS 並清除 probe" (
        $normalText -match 'ADS probe 成功，暫存檔已清除' -and
        @(Get-ChildItem -LiteralPath ([IO.Path]::GetTempPath()) -Filter 'hrsystem-environment-ads-probe-*.tmp' -File -ErrorAction SilentlyContinue).Count -eq 0
    )

    Write-HRStep "缺少關鍵工具"
    $missingDotNet = Join-Path $tempRoot 'missing-dotnet.exe'
    & $powershell -NoProfile -File (Join-Path $PSScriptRoot 'verify-environment.ps1') -DotNetPath $missingDotNet -RepoRoot $repo
    Confirm-SelfTest "關鍵工具不存在時回傳非 0" ($LASTEXITCODE -ne 0)

    Write-HRStep "Test Script 失敗傳遞"
    $fakeDotNet = Join-Path $tempRoot 'dotnet-fail.cmd'
    Set-Content -LiteralPath $fakeDotNet -Value '@exit /b 9' -Encoding Ascii
    & $powershell -NoProfile -File (Join-Path $PSScriptRoot 'test.ps1') -NoBuild -DotNetPath $fakeDotNet
    Confirm-SelfTest "test.ps1 遇到失敗回傳非 0" ($LASTEXITCODE -ne 0)

    Write-HRStep "敏感文字遮蔽"
    $fakeSensitive = 'Password=ExampleOnly;Token=ExampleTokenOnly;Server=localhost'
    $redacted = Protect-HRSensitiveText $fakeSensitive
    Confirm-SelfTest "敏感字串不會完整輸出" ($redacted -notmatch 'ExampleOnly|ExampleTokenOnly')

    Write-HRStep "非 Repo Root 呼叫"
    Push-Location $tempRoot
    try {
        & $powershell -NoProfile -File (Join-Path $PSScriptRoot 'verify-environment.ps1') -DotNetPath $dotnet -RepoRoot $repo
        $outsideExit = $LASTEXITCODE
    }
    finally { Pop-Location }
    Confirm-SelfTest "scripts 可從非 Repo Root 呼叫" ($outsideExit -eq 0)

    if ($IncludeReleaseAuditDirtyCheck) {
        Write-HRStep "Dirty Worktree Release Audit"
        $worktree = Join-Path $tempRoot 'dirty-worktree'
        $git = (Get-Command git -ErrorAction Stop).Source
        & $git -C $repo worktree add --detach $worktree HEAD | Out-Host
        if ($LASTEXITCODE -ne 0) { throw "無法建立自我測試 worktree。" }
        Add-Content -LiteralPath (Join-Path $worktree 'README.md') -Value "`n"
        $dirtyRun = Invoke-HRNativeCapture -FilePath $powershell -Arguments @('-NoProfile', '-File', (Join-Path $worktree 'scripts\release-audit.ps1')) -WorkingDirectory $worktree
        $dirtyText = $dirtyRun.Output -join [Environment]::NewLine
        Confirm-SelfTest "release-audit 遇到 dirty working tree 回傳非 0" ($dirtyRun.ExitCode -ne 0 -and $dirtyText -match 'Working Tree 不是 clean')
        & $git -C $repo worktree remove --force $worktree | Out-Host
        if ($LASTEXITCODE -ne 0) { throw "無法移除自我測試 worktree。" }
    }
    else { Write-HRResult WARN "尚未執行 committed worktree 的 Dirty Release Audit 測試。" }
}
catch {
    $failed++
    Write-HRResult FAIL (Protect-HRSensitiveText $_.Exception.Message)
}
finally {
    if (Test-Path -LiteralPath $tempRoot) {
        $resolvedTemp = [IO.Path]::GetFullPath($tempRoot)
        $allowedRoot = [IO.Path]::GetFullPath($env:TEMP).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
        if ($resolvedTemp.StartsWith($allowedRoot, [StringComparison]::OrdinalIgnoreCase) -and (Split-Path -Leaf $resolvedTemp) -like 'HRSystem-ScriptSelfTest-*') {
            Remove-Item -LiteralPath $resolvedTemp -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}

Write-HRStep "Self Test Summary"
Write-Host "PASS=$passed FAIL=$failed"
if ($failed -gt 0) { exit 1 }
exit 0
