[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")][string]$Configuration = "Release",
    [switch]$NoBuild,
    [string]$DotNetPath
)

$unit = @(
    "HRSystem.UnitTests.DashboardServiceTests",
    "HRSystem.UnitTests.EmployeeDashboardServiceTests"
)
$integration = @(
    "HRSystem.IntegrationTests.DashboardIntegrationTests",
    "HRSystem.IntegrationTests.EmployeeDashboardComponentTests",
    "HRSystem.IntegrationTests.EmployeeDashboardWebIntegrationTests"
)

& (Join-Path $PSScriptRoot "test-scoped.ps1") -Scope "Dashboard" `
    -SolutionFilter "HRSystem.Web.slnf" -UnitClasses $unit `
    -IntegrationClasses $integration -Configuration $Configuration `
    -NoBuild:$NoBuild -DotNetPath $DotNetPath
exit $LASTEXITCODE
