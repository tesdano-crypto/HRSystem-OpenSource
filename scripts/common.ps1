Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Write-HRStep {
    param([Parameter(Mandatory = $true)][string]$Title)
    Write-Host ""
    Write-Host "=== $Title ===" -ForegroundColor Cyan
}

function Write-HRResult {
    param(
        [Parameter(Mandatory = $true)][ValidateSet("PASS", "WARN", "FAIL")][string]$Status,
        [Parameter(Mandatory = $true)][string]$Message
    )

    $color = switch ($Status) {
        "PASS" { "Green" }
        "WARN" { "Yellow" }
        "FAIL" { "Red" }
    }
    Write-Host "[$Status] $Message" -ForegroundColor $color
}

function Get-HRRepoRoot {
    param([string]$StartPath = $PSScriptRoot)

    $candidate = [IO.Path]::GetFullPath((Join-Path $StartPath ".."))
    if (-not (Test-Path -LiteralPath (Join-Path $candidate "HRSystem.slnx"))) {
        throw "找不到 HRSystem.slnx，無法判斷 Repo Root：$candidate"
    }
    return $candidate
}

function Get-HRSolutionPath {
    param([string]$RepoRoot = (Get-HRRepoRoot))
    $solution = Join-Path $RepoRoot "HRSystem.slnx"
    if (-not (Test-Path -LiteralPath $solution)) { throw "Solution 不存在：$solution" }
    return $solution
}

function Get-HRTestProjects {
    param([string]$RepoRoot = (Get-HRRepoRoot))
    return [pscustomobject]@{
        Unit = Join-Path $RepoRoot "tests\HRSystem.UnitTests\HRSystem.UnitTests.csproj"
        Integration = Join-Path $RepoRoot "tests\HRSystem.IntegrationTests\HRSystem.IntegrationTests.csproj"
    }
}

function Get-HRDotNetPath {
    param(
        [string]$ExplicitPath,
        [string]$RepoRoot = (Get-HRRepoRoot)
    )

    if (-not [string]::IsNullOrWhiteSpace($ExplicitPath)) {
        if (-not (Test-Path -LiteralPath $ExplicitPath -PathType Leaf)) {
            throw "指定的 dotnet 不存在：$ExplicitPath"
        }
        return (Resolve-Path -LiteralPath $ExplicitPath).Path
    }

    $localDotNet = Join-Path $RepoRoot ".dotnet\dotnet.exe"
    if (Test-Path -LiteralPath $localDotNet -PathType Leaf) { return $localDotNet }

    $command = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($null -eq $command) { throw "找不到 dotnet。請依 DEVELOPMENT_SETUP.md 安裝 global.json 指定的 SDK。" }
    return $command.Source
}

function Get-HRPowerShellPath {
    $name = if ($PSVersionTable.PSEdition -eq "Core") { "pwsh.exe" } else { "powershell.exe" }
    $path = Join-Path $PSHOME $name
    if (-not (Test-Path -LiteralPath $path)) { throw "找不到目前 PowerShell Host：$path" }
    return $path
}

function Invoke-HRNative {
    param(
        [Parameter(Mandatory = $true)][string]$FilePath,
        [Parameter(Mandatory = $true)][string[]]$Arguments,
        [string]$WorkingDirectory = (Get-Location).Path
    )

    Push-Location $WorkingDirectory
    try {
        & $FilePath @Arguments
        $exitCode = $LASTEXITCODE
    }
    finally {
        Pop-Location
    }
    if ($exitCode -ne 0) { throw "命令失敗（Exit Code $exitCode）：$([IO.Path]::GetFileName($FilePath)) $($Arguments -join ' ')" }
}

function Invoke-HRNativeCapture {
    param(
        [Parameter(Mandatory = $true)][string]$FilePath,
        [Parameter(Mandatory = $true)][string[]]$Arguments,
        [string]$WorkingDirectory = (Get-Location).Path
    )

    Push-Location $WorkingDirectory
    try {
        $output = @(& $FilePath @Arguments 2>&1 | ForEach-Object { Write-Host $_; $_.ToString() })
        $exitCode = $LASTEXITCODE
    }
    finally {
        Pop-Location
    }
    return [pscustomobject]@{ ExitCode = $exitCode; Output = $output }
}

function Protect-HRSensitiveText {
    param([AllowEmptyString()][string]$Text)
    if ($null -eq $Text) { return "" }
    $redacted = $Text -replace '(?i)(Password|Pwd|Token|Secret|ApiKey)\s*=\s*[^;\s]+', '$1=***'
    $redacted = $redacted -replace '(?i)(https?://)[^/@:\s]+:[^/@\s]+@', '$1***:***@'
    return $redacted
}

function Test-HRAdministrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Get-HRGitStatus {
    param([string]$RepoRoot = (Get-HRRepoRoot))
    $git = Get-Command git -ErrorAction Stop
    $status = @(& $git.Source -C $RepoRoot status --porcelain 2>&1)
    if ($LASTEXITCODE -ne 0) { throw "無法讀取 Git 狀態。" }
    return $status
}

function Assert-HRGitClean {
    param([string]$RepoRoot = (Get-HRRepoRoot))
    if (@(Get-HRGitStatus -RepoRoot $RepoRoot).Count -gt 0) { throw "Working Tree 不是 clean。" }
}

function Get-HRGitIdentity {
    param([string]$RepoRoot = (Get-HRRepoRoot))
    $git = (Get-Command git -ErrorAction Stop).Source
    $branch = (& $git -C $RepoRoot branch --show-current).Trim()
    $commit = (& $git -C $RepoRoot rev-parse HEAD).Trim()
    $tags = @(& $git -C $RepoRoot tag --points-at HEAD)
    return [pscustomobject]@{ Branch = $branch; Commit = $commit; Tags = $tags }
}

function ConvertFrom-HRConnectionString {
    param([Parameter(Mandatory = $true)][string]$ConnectionString)
    $values = @{}
    foreach ($part in ($ConnectionString -split ';')) {
        if ([string]::IsNullOrWhiteSpace($part) -or $part.IndexOf('=') -lt 1) { continue }
        $index = $part.IndexOf('=')
        $key = $part.Substring(0, $index).Trim().ToLowerInvariant()
        $value = $part.Substring($index + 1).Trim()
        $values[$key] = $value
    }
    return $values
}

function Get-HRDevelopmentDatabaseSummary {
    param([string]$RepoRoot = (Get-HRRepoRoot))

    $projectPath = Join-Path $RepoRoot "src\HRSystem.Web\HRSystem.Web.csproj"
    [xml]$project = Get-Content -LiteralPath $projectPath -Raw
    $secretsId = [string]$project.Project.PropertyGroup.UserSecretsId
    if ([string]::IsNullOrWhiteSpace($secretsId)) { throw "HRSystem.Web 未設定 UserSecretsId。" }
    $secretsPath = Join-Path $env:APPDATA "Microsoft\UserSecrets\$secretsId\secrets.json"
    if (-not (Test-Path -LiteralPath $secretsPath)) { throw "Development User Secrets 尚未設定。" }
    $secrets = Get-Content -LiteralPath $secretsPath -Raw | ConvertFrom-Json
    $property = $secrets.PSObject.Properties['ConnectionStrings:HRSystemDb']
    if ($null -eq $property -or [string]::IsNullOrWhiteSpace([string]$property.Value)) {
        throw "缺少 ConnectionStrings:HRSystemDb。"
    }

    $values = ConvertFrom-HRConnectionString -ConnectionString ([string]$property.Value)
    $server = @('server', 'data source', 'address', 'addr', 'network address') | ForEach-Object { if ($values.ContainsKey($_)) { $values[$_] } } | Select-Object -First 1
    $database = @('database', 'initial catalog') | ForEach-Object { if ($values.ContainsKey($_)) { $values[$_] } } | Select-Object -First 1
    $integrated = @('integrated security', 'trusted_connection') | ForEach-Object { if ($values.ContainsKey($_)) { $values[$_] } } | Select-Object -First 1
    $userId = @('user id', 'uid') | ForEach-Object { if ($values.ContainsKey($_)) { $values[$_] } } | Select-Object -First 1
    $isIntegrated = $integrated -match '^(true|sspi|yes)$'
    $safeServer = $server -in @('.\SQLEXPRESS', 'localhost\SQLEXPRESS')
    $safeDatabase = $database -eq 'HRSystemDb'
    $usesSqlLogin = -not [string]::IsNullOrWhiteSpace([string]$userId)

    return [pscustomobject]@{
        Server = $server
        Database = $database
        IntegratedSecurity = $isIntegrated
        UsesSqlLogin = $usesSqlLogin
        UsesSa = [string]::Equals([string]$userId, 'sa', [StringComparison]::OrdinalIgnoreCase)
        IsSafe = ($safeServer -and $safeDatabase -and $isIntegrated -and -not $usesSqlLogin)
    }
}

function Assert-HRSafeDevelopmentDatabase {
    param([string]$RepoRoot = (Get-HRRepoRoot))
    $summary = Get-HRDevelopmentDatabaseSummary -RepoRoot $RepoRoot
    if (-not $summary.IsSafe) { throw "Development Database 安全閘門失敗。只允許本機 SQLEXPRESS、HRSystemDb 與 Windows Integrated。" }
    return $summary
}

function Get-HRSolutionProjectSummary {
    param([string]$RepoRoot = (Get-HRRepoRoot))
    [xml]$solution = Get-Content -LiteralPath (Get-HRSolutionPath -RepoRoot $RepoRoot) -Raw
    $projects = @($solution.Solution.Folder.Project | ForEach-Object { [string]$_.Path })
    return [pscustomobject]@{
        Total = $projects.Count
        Formal = @($projects | Where-Object { $_ -like 'src/*' }).Count
        Tests = @($projects | Where-Object { $_ -like 'tests/*' }).Count
        Paths = $projects
    }
}
