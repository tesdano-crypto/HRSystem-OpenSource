[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")][string]$Configuration = "Release",
    [switch]$NoBuild,
    [string]$DotNetPath
)

$unit = @(
    "HRSystem.UnitTests.CalendarDayLeavePolicyTests",
    "HRSystem.UnitTests.DashboardServiceTests",
    "HRSystem.UnitTests.LeaveAttendanceRecalculationEngineTests",
    "HRSystem.UnitTests.LeaveAwareAttendanceCalculatorTests",
    "HRSystem.UnitTests.LeaveAwareAttendanceMigrationTests",
    "HRSystem.UnitTests.LeaveDurationCalculatorTests",
    "HRSystem.UnitTests.LeaveTypeRuleTests",
    "HRSystem.UnitTests.LeaveRequestWorkflowTests",
    "HRSystem.UnitTests.ParentalLeaveDomainTests",
    "HRSystem.UnitTests.ParentalLeaveMigrationTests",
    "HRSystem.UnitTests.ParentalLeaveWorkflowTests",
    "HRSystem.UnitTests.AnnualLeavePolicyTests",
    "HRSystem.UnitTests.AnnualLeaveLedgerTests",
    "HRSystem.UnitTests.AnnualLeaveServiceTests"
    "HRSystem.UnitTests.CompTimeServiceTests"
    "HRSystem.UnitTests.TrainingCompTimeTests"
)
$integration = @(
    "HRSystem.IntegrationTests.CalendarDayLeaveMigrationSqlIntegrationTests",
    "HRSystem.IntegrationTests.CalendarDayLeaveWebTests",
    "HRSystem.IntegrationTests.DashboardIntegrationTests",
    "HRSystem.IntegrationTests.LeaveAwareAttendanceSqlIntegrationTests",
    "HRSystem.IntegrationTests.LeaveCancellationMigrationSqlIntegrationTests",
    "HRSystem.IntegrationTests.LeaveReasonInputBindingTests",
    "HRSystem.IntegrationTests.LeaveTypeCatalogIntegrationTests",
    "HRSystem.IntegrationTests.LeaveTypeCatalogMigrationSqlIntegrationTests",
    "HRSystem.IntegrationTests.LeaveTypeEmployeeRequestModeConstraintMigrationSqlIntegrationTests",
    "HRSystem.IntegrationTests.LeaveTypeCatalogWebTests",
    "HRSystem.IntegrationTests.LeaveWorkflowIntegrationTests",
    "HRSystem.IntegrationTests.ParentalLeaveMigrationSqlIntegrationTests",
    "HRSystem.IntegrationTests.ParentalLeaveTransactionSqlIntegrationTests",
    "HRSystem.IntegrationTests.ParentalLeaveWebTests",
    "HRSystem.IntegrationTests.AnnualLeaveMigrationSqlIntegrationTests",
    "HRSystem.IntegrationTests.AnnualLeaveWebTests"
    "HRSystem.IntegrationTests.CompTimeWebTests"
    "HRSystem.IntegrationTests.CompTimeMigrationSqlIntegrationTests"
    "HRSystem.IntegrationTests.TrainingCompTimeSqlTests"
)

& (Join-Path $PSScriptRoot "test-scoped.ps1") -Scope "Leave" `
    -SolutionFilter "HRSystem.Leave.slnf" -UnitClasses $unit `
    -IntegrationClasses $integration -Configuration $Configuration `
    -NoBuild:$NoBuild -DotNetPath $DotNetPath
exit $LASTEXITCODE
