[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")][string]$Configuration = "Release",
    [switch]$NoBuild,
    [string]$DotNetPath
)

$unit = @(
    "HRSystem.UnitTests.CompanyCalendarDomainTests",
    "HRSystem.UnitTests.CompanyCalendarManifestTests",
    "HRSystem.UnitTests.CompanyCalendarServiceTests"
)
$integration = @(
    "HRSystem.IntegrationTests.CompanyCalendarIntegrationTests",
    "HRSystem.IntegrationTests.CompanyCalendarSqlIntegrationTests"
)

& (Join-Path $PSScriptRoot "test-scoped.ps1") -Scope "CompanyCalendar" `
    -SolutionFilter "HRSystem.slnx" -UnitClasses $unit `
    -IntegrationClasses $integration -Configuration $Configuration `
    -NoBuild:$NoBuild -DotNetPath $DotNetPath
exit $LASTEXITCODE
