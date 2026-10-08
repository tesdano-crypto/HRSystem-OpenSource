[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")][string]$Configuration = "Release",
    [switch]$NoBuild,
    [string]$DotNetPath
)

$integration = @(
    "HRSystem.IntegrationTests.ApplicationSmokeTests",
    "HRSystem.IntegrationTests.AttendanceRefreshComponentTests",
    "HRSystem.IntegrationTests.AttendanceReviewComponentTests",
    "HRSystem.IntegrationTests.MyAttendanceComponentTests",
    "HRSystem.IntegrationTests.AttendanceManagementWebIntegrationTests",
    "HRSystem.IntegrationTests.AttendanceExceptionWebTests",
    "HRSystem.IntegrationTests.DashboardIntegrationTests",
    "HRSystem.IntegrationTests.EmployeeDashboardComponentTests",
    "HRSystem.IntegrationTests.EmployeeDashboardWebIntegrationTests",
    "HRSystem.IntegrationTests.IdentityIntegrationTests",
    "HRSystem.IntegrationTests.LeaveReasonInputBindingTests",
    "HRSystem.IntegrationTests.LeaveWorkflowIntegrationTests",
    "HRSystem.IntegrationTests.MasterDataIntegrationTests",
    "HRSystem.IntegrationTests.NavigationComponentTests",
    "HRSystem.IntegrationTests.ParentalLeaveWebTests",
    "HRSystem.IntegrationTests.OvertimeRequestComponentTests"
)

& (Join-Path $PSScriptRoot "test-scoped.ps1") -Scope "WebComponent" `
    -SolutionFilter "HRSystem.Web.slnf" -IntegrationClasses $integration `
    -Configuration $Configuration -NoBuild:$NoBuild -DotNetPath $DotNetPath
exit $LASTEXITCODE
