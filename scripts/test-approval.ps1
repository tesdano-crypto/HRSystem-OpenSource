[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")][string]$Configuration = "Release",
    [switch]$NoBuild,
    [string]$DotNetPath
)

$unit = @(
    "HRSystem.UnitTests.ApprovalWorkflowTests",
    "HRSystem.UnitTests.ApprovalServiceTests",
    "HRSystem.UnitTests.PayrollApprovalSourceProviderTests",
    "HRSystem.UnitTests.LineApprovalBridgeTests",
    "HRSystem.UnitTests.LinePairingTests",
    "HRSystem.UnitTests.IdentityRulesTests"
)
$integration = @(
    "HRSystem.IntegrationTests.ApprovalFoundationMigrationSqlIntegrationTests",
    "HRSystem.IntegrationTests.LinePairingMigrationSqlIntegrationTests",
    "HRSystem.IntegrationTests.ApprovalComponentTests",
    "HRSystem.IntegrationTests.LinePairingComponentTests",
    "HRSystem.IntegrationTests.LinePairingEndpointTests",
    "HRSystem.IntegrationTests.NavigationComponentTests"
)

& (Join-Path $PSScriptRoot "test-scoped.ps1") -Scope "Approval" `
    -SolutionFilter "HRSystem.slnx" -UnitClasses $unit `
    -IntegrationClasses $integration -Configuration $Configuration `
    -NoBuild:$NoBuild -DotNetPath $DotNetPath
exit $LASTEXITCODE
