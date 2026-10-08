[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")][string]$Configuration = "Release",
    [string]$DotNetPath,
    [switch]$CheckDatabase
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "common.ps1")

function Write-MigrationStatus {
    param(
        [Parameter(Mandatory = $true)]
        [ValidateSet("PASS", "INFO", "BLOCKED")]
        [string]$Status,
        [Parameter(Mandatory = $true)][string]$Message
    )

    $color = switch ($Status) {
        "PASS" { "Green" }
        "INFO" { "Cyan" }
        "BLOCKED" { "Red" }
    }
    Write-Host "[$Status] $Message" -ForegroundColor $color
}

try {
    $repo = Get-HRRepoRoot
    $dotnet = Get-HRDotNetPath -ExplicitPath $DotNetPath -RepoRoot $repo
    $project = Join-Path $repo "src\HRSystem.Infrastructure\HRSystem.Infrastructure.csproj"
    $startup = Join-Path $repo "src\HRSystem.Web\HRSystem.Web.csproj"
    $migrationDirectory = Join-Path $repo "src\HRSystem.Infrastructure\Persistence\Migrations"
    $commonArgs = @(
        "--project", $project,
        "--startup-project", $startup,
        "--configuration", $Configuration,
        "--no-build"
    )

    Write-HRStep "Repository Migrations"
    $repositoryMigrations = @(
        Get-ChildItem -LiteralPath $migrationDirectory -File -Filter "*.cs" |
            Where-Object {
                $_.Name -notlike "*.Designer.cs" -and
                $_.Name -match '^\d{14}_.+\.cs$'
            } |
            Sort-Object Name
    )
    if ($repositoryMigrations.Count -eq 0) {
        throw "No repository migrations were found."
    }

    $migrationIds = @($repositoryMigrations | ForEach-Object {
        $_.BaseName.Substring(0, 14)
    })
    $duplicateIds = @($migrationIds | Group-Object | Where-Object Count -gt 1)
    if ($duplicateIds.Count -gt 0) {
        throw "Duplicate repository migration IDs were found."
    }
    Write-MigrationStatus PASS "Repository migrations: $($repositoryMigrations.Count); IDs are unique."

    Write-HRStep "Pending Model Changes (offline)"
    $connectionVariable = "ConnectionStrings__HRSystemDb"
    $originalConnection = [Environment]::GetEnvironmentVariable(
        $connectionVariable,
        [EnvironmentVariableTarget]::Process)
    if (-not $CheckDatabase) {
        [Environment]::SetEnvironmentVariable(
            $connectionVariable,
            "Server=(localdb)\MSSQLLocalDB;Database=HRSystem_ModelCheck;Integrated Security=true;TrustServerCertificate=true",
            [EnvironmentVariableTarget]::Process)
    }
    try {
        $modelRun = Invoke-HRNativeCapture -FilePath $dotnet -Arguments (@(
            "ef", "migrations", "has-pending-model-changes") + $commonArgs) -WorkingDirectory $repo
    }
    finally {
        if (-not $CheckDatabase) {
            [Environment]::SetEnvironmentVariable(
                $connectionVariable,
                $originalConnection,
                [EnvironmentVariableTarget]::Process)
        }
    }
    if ($modelRun.ExitCode -ne 0) {
        throw "Pending model check failed. Build the selected configuration first."
    }
    $modelText = $modelRun.Output -join [Environment]::NewLine
    if ($modelText -notmatch 'No changes have been made to the model') {
        throw "Pending model changes were detected or the result was inconclusive."
    }
    Write-MigrationStatus PASS "Repository model matches the latest ModelSnapshot."

    if (-not $CheckDatabase) {
        Write-MigrationStatus INFO "Database not checked. Use -CheckDatabase to compare Applied and Pending migrations."
        exit 0
    }

    Write-HRStep "Development Database Migrations (read-only)"
    $db = Assert-HRSafeDevelopmentDatabase -RepoRoot $repo
    Write-MigrationStatus PASS "DB target: Server=$($db.Server), Database=$($db.Database), Windows Integrated."
    $listRun = Invoke-HRNativeCapture -FilePath $dotnet -Arguments (@(
        "ef", "migrations", "list") + $commonArgs) -WorkingDirectory $repo
    if ($listRun.ExitCode -ne 0) {
        throw "Unable to read Development Database migration status."
    }

    $migrationLines = @($listRun.Output | Where-Object { $_ -match '^\d{14}_' })
    if ($migrationLines.Count -ne $repositoryMigrations.Count) {
        throw "EF and repository migration counts differ: EF=$($migrationLines.Count), Repository=$($repositoryMigrations.Count)."
    }
    $pendingMigrations = @($migrationLines | Where-Object { $_ -match '\(Pending\)' })
    $appliedCount = $migrationLines.Count - $pendingMigrations.Count
    Write-MigrationStatus PASS "Applied migrations: $appliedCount."
    if ($pendingMigrations.Count -gt 0) {
        throw "$($pendingMigrations.Count) pending migration(s) were found."
    }
    Write-MigrationStatus PASS "Pending migrations: 0; pending model changes: none."
    exit 0
}
catch {
    Write-MigrationStatus BLOCKED (Protect-HRSensitiveText $_.Exception.Message)
    exit 1
}
