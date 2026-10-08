[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")][string]$Configuration = "Release",
    [switch]$NoBuild,
    [string]$DotNetPath
)

$unit = @(
    "HRSystem.UnitTests.AttendanceImportFoundationTests",
    "HRSystem.UnitTests.AttendanceImportMigrationTests",
    "HRSystem.UnitTests.AttendanceMappingLoadTests",
    "HRSystem.UnitTests.AttendancePunchRecordServiceTests",
    "HRSystem.UnitTests.AttendanceRawEventFingerprintV1Tests",
    "HRSystem.UnitTests.BioWebTaImportBatchDomainTests",
    "HRSystem.UnitTests.BioWebTaImportStatusServiceTests",
    "HRSystem.UnitTests.BioWebTaScheduledImportMigrationTests",
    "HRSystem.UnitTests.BioWebTaImportCoordinatorTests"
)
$integration = @(
    "HRSystem.IntegrationTests.AttendanceImportIntegrationTests",
    "HRSystem.IntegrationTests.AttendanceMappingSqlIntegrationTests",
    "HRSystem.IntegrationTests.AttendancePunchRecordsIntegrationTests",
    "HRSystem.IntegrationTests.AttendancePunchRecordSqlIntegrationTests",
    "HRSystem.IntegrationTests.BioWebTaFingerprintMigrationSqlIntegrationTests",
    "HRSystem.IntegrationTests.BioWebTaImportStatusSqlIntegrationTests",
    "HRSystem.IntegrationTests.AttendanceRefreshComponentTests"
)

& (Join-Path $PSScriptRoot "test-scoped.ps1") -Scope "BioWebTaImport" `
    -SolutionFilter "HRSystem.BioWebTaImport.slnf" -UnitClasses $unit `
    -IntegrationClasses $integration -Configuration $Configuration `
    -NoBuild:$NoBuild -DotNetPath $DotNetPath
exit $LASTEXITCODE
