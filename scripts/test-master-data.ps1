[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")][string]$Configuration = "Release",
    [switch]$NoBuild,
    [string]$DotNetPath
)

$unit = @(
    "HRSystem.UnitTests.EmployeeNumberFormatterTests",
    "HRSystem.UnitTests.LeaveTypeRuleTests",
    "HRSystem.UnitTests.MasterDataIntegrityTests",
    "HRSystem.UnitTests.MasterDataValidationTests"
)
$integration = @(
    "HRSystem.IntegrationTests.EmployeeNumberIntegrationTests",
    "HRSystem.IntegrationTests.EmployeeNumberSequenceSqlIntegrationTests",
    "HRSystem.IntegrationTests.LeaveTypeCatalogIntegrationTests",
    "HRSystem.IntegrationTests.LeaveTypeCatalogMigrationSqlIntegrationTests",
    "HRSystem.IntegrationTests.LeaveTypeCatalogWebTests",
    "HRSystem.IntegrationTests.LeaveTypeEmployeeRequestModeConstraintMigrationSqlIntegrationTests",
    "HRSystem.IntegrationTests.MasterDataConcurrencyIntegrationTests",
    "HRSystem.IntegrationTests.MasterDataIntegrationTests"
)

& (Join-Path $PSScriptRoot "test-scoped.ps1") -Scope "MasterData" `
    -SolutionFilter "HRSystem.slnx" -UnitClasses $unit `
    -IntegrationClasses $integration -Configuration $Configuration `
    -NoBuild:$NoBuild -DotNetPath $DotNetPath
exit $LASTEXITCODE
