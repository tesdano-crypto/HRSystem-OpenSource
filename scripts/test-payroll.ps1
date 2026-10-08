[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")][string]$Configuration = "Release",
    [switch]$NoBuild,
    [string]$DotNetPath
)

$unit = @(
    "HRSystem.UnitTests.PayrollFoundationTests",
    "HRSystem.UnitTests.PayrollFixedEarningsTests",
    "HRSystem.UnitTests.PayrollAttendanceAndLeaveTests",
    "HRSystem.UnitTests.PayrollOvertimePayTests",
    "HRSystem.UnitTests.PayrollLaborInsuranceTests",
    "HRSystem.UnitTests.PayrollOccupationalInsuranceTests",
    "HRSystem.UnitTests.PayrollHealthInsuranceTests",
    "HRSystem.UnitTests.PayrollTotalsTests",
    "HRSystem.UnitTests.PayrollRevisionTests",
    "HRSystem.UnitTests.PayrollApprovalSourceProviderTests",
    "HRSystem.UnitTests.PayrollFinalizationTests",
    "HRSystem.UnitTests.PayrollFoundationMigrationTests",
    "HRSystem.UnitTests.PayrollSpecialPayPatternsTests"
    "HRSystem.UnitTests.PayrollPeriodicAccrualTests"
    "HRSystem.UnitTests.PayrollPeriodicAccrualMigrationTests"
    "HRSystem.UnitTests.OccupationalInsuranceMigrationTests"
)
$integration = @(
    "HRSystem.IntegrationTests.PayrollFoundationIntegrationTests",
    "HRSystem.IntegrationTests.PayrollFixedEarningsComponentTests",
    "HRSystem.IntegrationTests.PayrollFoundationMigrationSqlIntegrationTests",
    "HRSystem.IntegrationTests.PayrollRevisionMigrationSqlIntegrationTests",
    "HRSystem.IntegrationTests.PayrollFinalizationComponentTests",
    "HRSystem.IntegrationTests.PayrollFinalizationMigrationSqlIntegrationTests",
    "HRSystem.IntegrationTests.PayrollPayCycleMigrationSqlIntegrationTests",
    "HRSystem.IntegrationTests.PayrollLegacyAdjustmentMigrationSqlIntegrationTests"
    "HRSystem.IntegrationTests.PayrollPeriodicAccrualMigrationSqlIntegrationTests"
    "HRSystem.IntegrationTests.InsuranceManagementComponentTests"
    "HRSystem.IntegrationTests.OccupationalInsuranceMigrationSqlIntegrationTests"
)

& (Join-Path $PSScriptRoot "test-scoped.ps1") -Scope "Payroll" `
    -SolutionFilter "HRSystem.slnx" -UnitClasses $unit `
    -IntegrationClasses $integration -Configuration $Configuration `
    -NoBuild:$NoBuild -DotNetPath $DotNetPath
exit $LASTEXITCODE
