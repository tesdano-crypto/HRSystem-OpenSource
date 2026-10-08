[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")][string]$Configuration = "Release",
    [switch]$NoBuild,
    [string]$DotNetPath
)

$unit = @(
    "HRSystem.UnitTests.NonWorkingDayPunchTests",
    "HRSystem.UnitTests.AttendanceImportFoundationTests",
    "HRSystem.UnitTests.AttendanceImportMigrationTests",
    "HRSystem.UnitTests.AttendanceManagementFoundationTests",
    "HRSystem.UnitTests.AttendanceManagementMigrationTests",
    "HRSystem.UnitTests.AttendanceMappingLoadTests",
    "HRSystem.UnitTests.AttendancePunchRecordServiceTests",
    "HRSystem.UnitTests.AttendanceRawEventFingerprintV1Tests",
    "HRSystem.UnitTests.AttendanceReviewServiceTests",
    "HRSystem.UnitTests.AttendanceReviewResolutionTests",
    "HRSystem.UnitTests.AttendanceReviewResolutionMigrationTests",
    "HRSystem.UnitTests.BioWebTaImportBatchDomainTests",
    "HRSystem.UnitTests.BioWebTaScheduledImportMigrationTests",
    "HRSystem.UnitTests.LeaveAttendanceRecalculationEngineTests",
    "HRSystem.UnitTests.LeaveAwareAttendanceCalculatorTests",
    "HRSystem.UnitTests.LeaveAwareAttendanceMigrationTests",
    "HRSystem.UnitTests.ParentalLeaveWorkflowTests"
    "HRSystem.UnitTests.OvertimeRequestWorkflowTests"
    "HRSystem.UnitTests.OvertimeRecognitionTests"
    "HRSystem.UnitTests.AttendanceCorrectionWorkflowTests"
    "HRSystem.UnitTests.EmployeeDashboardServiceTests"
)
$integration = @(
    "HRSystem.IntegrationTests.NonWorkingDayPunchSqlTests",
    "HRSystem.IntegrationTests.NonWorkingPunchComponentTests",
    "HRSystem.IntegrationTests.AttendanceRefreshComponentTests",
    "HRSystem.IntegrationTests.AttendanceReviewComponentTests",
    "HRSystem.IntegrationTests.MyAttendanceComponentTests",
    "HRSystem.IntegrationTests.AttendanceReviewResolutionMigrationSqlIntegrationTests",
    "HRSystem.IntegrationTests.AttendanceImportIntegrationTests",
    "HRSystem.IntegrationTests.AttendanceManagementSqlIntegrationTests",
    "HRSystem.IntegrationTests.AttendanceManagementWebIntegrationTests",
    "HRSystem.IntegrationTests.AttendanceMappingSqlIntegrationTests",
    "HRSystem.IntegrationTests.AttendancePunchRecordsIntegrationTests",
    "HRSystem.IntegrationTests.AttendancePunchRecordSqlIntegrationTests",
    "HRSystem.IntegrationTests.BioWebTaFingerprintMigrationSqlIntegrationTests",
    "HRSystem.IntegrationTests.LeaveAwareAttendanceSqlIntegrationTests",
    "HRSystem.IntegrationTests.ParentalLeaveMigrationSqlIntegrationTests"
    "HRSystem.IntegrationTests.OvertimeRequestSqlIntegrationTests"
    "HRSystem.IntegrationTests.OvertimeRequestComponentTests"
    "HRSystem.IntegrationTests.OvertimeRecognitionMigrationSqlIntegrationTests"
    "HRSystem.IntegrationTests.OvertimeRecognitionSqlIntegrationTests"
    "HRSystem.IntegrationTests.AttendanceCorrectionComponentTests"
    "HRSystem.IntegrationTests.AttendanceCorrectionMigrationSqlIntegrationTests"
    "HRSystem.IntegrationTests.AttendanceCorrectionSqlIntegrationTests"
    "HRSystem.IntegrationTests.EmployeeDashboardComponentTests"
)

& (Join-Path $PSScriptRoot "test-scoped.ps1") -Scope "Attendance" `
    -SolutionFilter "HRSystem.Attendance.slnf" -UnitClasses $unit `
    -IntegrationClasses $integration -Configuration $Configuration `
    -NoBuild:$NoBuild -DotNetPath $DotNetPath
exit $LASTEXITCODE
