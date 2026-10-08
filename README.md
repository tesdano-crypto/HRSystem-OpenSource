# HRSystem

HRSystem is a modular human resources application built with .NET 10, ASP.NET Core
Blazor Interactive Server, Entity Framework Core, SQL Server and ASP.NET Core Identity.
This independent source snapshot contains no upstream Git history or operational data.
The interface and several calendar/payroll rules are oriented toward Taiwan;
adopters must review their own policies before use.

## Features

- Employees, departments, roles and account administration.
- Company calendars, shift assignments, raw attendance evidence and calculation.
- Leave requests, approvals, annual leave, overtime, payroll and insurance records.
- Comp-time opening balances and independently approved holiday-training credits
  with persisted usage allocation. Expiration is metadata only; automated expiry
  and conversion to payroll are not implemented.
- **LINE integration is optional and disabled by default.** Private pairing,
  approval callbacks and notifications require a separately configured bridge.
  LINE attendance-report file import is not included.
- Optional external attendance import job; scheduling is disabled by default.

## Architecture

Dependencies flow `Domain <- Application <- Infrastructure <- Web`.
Web also references Application. The attendance import job is a separate executable;
starting the web application does not run an attendance scheduler.
See the [module map](docs/architecture/module-map.md) and
[architecture decisions](docs/adr/README.md).

## Prerequisites

- .NET SDK 10.0.302 with the latest compatible patch, as selected by `global.json`.
- Your own isolated SQL Server database. The current SQL integration fixtures
  specifically require Windows, local SQL Express and integrated authentication.
- PowerShell for repository scripts; Git and NuGet connectivity for development.
- No LINE account, external attendance system or cloud subscription is required
  for the core application or test suite.

## Local setup

From this repository's root:

```powershell
dotnet tool restore --tool-manifest dotnet-tools.json
dotnet restore HRSystem.slnx
dotnet build HRSystem.slnx -c Release --no-restore
```

Configure the settings below in your local secret store before initializing the
database and starting the app. `HRSystem-OpenSource-Isolated` is the shared,
independent User Secrets ID for Web, Infrastructure and the import job. Do not
reuse another installation's secrets. User Secrets is development storage, not
an encrypted production vault. appsettings.example.json is a reference only;
it is not automatically loaded under that filename.

## Configuration

See [appsettings.example.json](src/HRSystem.Web/appsettings.example.json).

| Key | Purpose |
| --- | --- |
| `ConnectionStrings:HRSystemDb` | Your isolated SQL connection; environment key is `ConnectionStrings__HRSystemDb` |
| `HR_ADMIN_USERNAME`, `HR_ADMIN_EMAIL`, `HR_ADMIN_PASSWORD`, `HR_ADMIN_DISPLAY_NAME` | All four initial administrator settings; no default password is supplied |
| `Organization:Name` | UI and payslip name; default `Example Company` |
| `SeedData:Enabled` | Development-only fictional employee seeding; true in Development configuration |
| `BioWebTA:Server` | Required only when configuring your own optional external attendance source |
| `BioWebTA:ScheduledImport:Enabled` | Defaults to false; enabling does not install a scheduler |
| `ApprovalLineBridge:Enabled` | Defaults to false; bridge endpoint and secret must be supplied separately |

Use `dotnet user-secrets set --project src/HRSystem.Web <KEY> <YOUR_VALUE>` for
non-sensitive examples; enter passwords through your chosen secure configuration
workflow so they do not remain in shell history. Production does not automatically
load development User Secrets. Current startup requires all four administrator
settings in Production even when the account already exists; startup seeding is
not an existing-account password rotation mechanism.

## Database setup

Review migrations and point configuration at an **EMPTY, isolated database**.
Only then run this initialization command in your own environment:

```powershell
dotnet ef database update --project src/HRSystem.Infrastructure --startup-project src/HRSystem.Infrastructure --configuration Release
dotnet run --project src/HRSystem.Web
```

The 38 retained migrations preserve sequences, seed definitions and custom SQL.
The removed import-only migration is excluded. This fork is not an upgrade path
for a pre-existing installation. Application startup does not apply migrations.
Development may seed fictional employees; do not enable this against real data.
Use the local URL printed by the application; configure/trust an appropriate
local HTTPS development certificate when needed.

## Tests

After a successful Release build:

```powershell
.\scripts\test-full.ps1 -NoBuild
```

Equivalent individual commands:

```powershell
dotnet test tests/HRSystem.UnitTests/HRSystem.UnitTests.csproj -c Release --no-build
dotnet test tests/HRSystem.IntegrationTests/HRSystem.IntegrationTests.csproj -c Release --no-build
```

SQL fixtures create/drop only locally validated, uniquely named disposable databases
using integrated authentication. Prefixes include `HRSystem_Test_` and legacy
fixture-specific `HRSystem_Phase*_Test_` variants. The test account needs permission
to create/drop its own test databases. Other tests use in-memory providers.
Do not run external import jobs or connect to existing business databases as tests.

## Security

Read [SECURITY.md](SECURITY.md). Keep secrets, employee data, reports, exports and
runtime artifacts out of Git. Never post actual HR data in issues or test fixtures.
Use least-privilege runtime credentials, protected secrets, audited administrator
accounts and HTTPS. Review third-party notices before distributing binaries.

## Generic deployment guidance

Provision a separate database and runtime identity, configure your own HTTPS host
and trusted proxy behavior, review migrations, back up and verify recovery, then
apply migrations through a separately approved deployment process. Publish the web
application only after that review. Disable development seeding; preserve and protect
ASP.NET Core Data Protection keys for the intended deployment topology. Test
access control and restore procedures before accepting real employee data.

Keep LINE and attendance integrations disabled until their endpoints, credentials
and authorization have been configured and reviewed independently. No production
host, service account, scheduler or cloud resource is provisioned by this repository.
SQL Server and external services have their own licensing/operational requirements.

## Contribution, dependencies and license

See [CONTRIBUTING.md](CONTRIBUTING.md) and the
[dependency/license review](docs/dependency-license-review.md).

This project's original code is licensed under the **Apache License, Version 2.0**.
See [LICENSE](LICENSE) for the full terms and [NOTICE](NOTICE) for attribution.
Third-party dependencies and attributed calendar data retain their own terms;
the project license does not relicense them.
See the [pre-publish review](docs/pre-publish-review.md) for current release gates.
