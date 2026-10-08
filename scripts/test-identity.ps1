[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")][string]$Configuration = "Release",
    [switch]$NoBuild,
    [string]$DotNetPath
)

$unit = @(
    "HRSystem.UnitTests.IdentityRulesTests"
)
$integration = @(
    "HRSystem.IntegrationTests.IdentityIntegrationTests"
)

& (Join-Path $PSScriptRoot "test-scoped.ps1") -Scope "Identity" `
    -SolutionFilter "HRSystem.slnx" -UnitClasses $unit `
    -IntegrationClasses $integration -Configuration $Configuration `
    -NoBuild:$NoBuild -DotNetPath $DotNetPath
exit $LASTEXITCODE
