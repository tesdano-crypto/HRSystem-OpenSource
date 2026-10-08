[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")][string]$Configuration = "Release",
    [switch]$NoBuild,
    [string]$DotNetPath
)

$unit = @(
    "HRSystem.UnitTests.AttendanceExceptionTests",
    "HRSystem.UnitTests.AttendanceExceptionWorkflowTests",
    "HRSystem.UnitTests.AttendanceExceptionMigrationTests",
    "HRSystem.UnitTests.AttendanceManagementFoundationTests",
    "HRSystem.UnitTests.LeaveAwareAttendanceCalculatorTests",
    "HRSystem.UnitTests.ParentalLeaveDomainTests"
)
$integration = @(
    "HRSystem.IntegrationTests.AttendanceExceptionMigrationSqlIntegrationTests",
    "HRSystem.IntegrationTests.AttendanceExceptionWebTests",
    "HRSystem.IntegrationTests.AttendanceManagementSqlIntegrationTests",
    "HRSystem.IntegrationTests.AttendanceManagementWebIntegrationTests",
    "HRSystem.IntegrationTests.LeaveAwareAttendanceSqlIntegrationTests"
)

& (Join-Path $PSScriptRoot "test-scoped.ps1") -Scope "AttendanceException" `
    -SolutionFilter "HRSystem.Attendance.slnf" -UnitClasses $unit `
    -IntegrationClasses $integration -Configuration $Configuration `
    -NoBuild:$NoBuild -DotNetPath $DotNetPath
exit $LASTEXITCODE
