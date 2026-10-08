[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")][string]$Configuration = "Release",
    [switch]$NoBuild,
    [string]$DotNetPath
)

$unit = @(
    "HRSystem.UnitTests.AnnualLeavePolicyTests",
    "HRSystem.UnitTests.AnnualLeaveLedgerTests",
    "HRSystem.UnitTests.AnnualLeaveServiceTests"
)
$integration = @(
    "HRSystem.IntegrationTests.AnnualLeaveMigrationSqlIntegrationTests",
    "HRSystem.IntegrationTests.AnnualLeaveWebTests"
)

& (Join-Path $PSScriptRoot "test-scoped.ps1") -Scope "AnnualLeave" `
    -SolutionFilter "HRSystem.Leave.slnf" -UnitClasses $unit `
    -IntegrationClasses $integration -Configuration $Configuration `
    -NoBuild:$NoBuild -DotNetPath $DotNetPath
exit $LASTEXITCODE
