# HRSystem Module Map

This map is the first stop for scoped development. HRSystem is a modular monolith: module boundaries are folders and services inside shared projects, not separate assemblies.

## Assembly dependency direction

`HRSystem.Domain <- HRSystem.Application <- HRSystem.Infrastructure <- HRSystem.Web`

`HRSystem.Web` also references `HRSystem.Application` directly. Unit tests reference Domain, Application, and Infrastructure. Integration tests reference Web and therefore load the complete application dependency graph.

## Module ownership

| Module | Domain | Application | Infrastructure | Web | Primary tests | Scoped command |
|---|---|---|---|---|---|---|
| Attendance | `Domain/Attendance` | `Application/Attendance` | `Infrastructure/Attendance`, attendance persistence configurations | attendance and attendance-admin pages | `Attendance*`, leave-aware attendance tests | `scripts/test-attendance.ps1` |
| Attendance exception | `Domain/AttendanceExceptions` | `Application/AttendanceExceptions`, attendance recalculation | attendance-exception persistence configurations | `/attendance-exceptions*`, daily attendance projection | `AttendanceException*` plus attendance/leave/suspension regression | `scripts/test-attendance-exception.ps1` |
| Leave | `Domain/LeaveRequests` | `Application/LeaveRequests` | leave persistence configurations | `LeaveRequest*`, `LeaveApprovals`, `AdminLeaveRequests` | `Leave*` and Dashboard leave tests | `scripts/test-leave.ps1` |
| BioWebTA import | raw-event fingerprint and BioWebTA batch domain types | attendance source abstractions, shared import coordinator, and focused recalculation | `BioWebTaAttendanceSource`, SQL application lock, fingerprint SQL, import migrations, independent `HRSystem.AttendanceImport.Job` | mapping, punch-record, and import administration pages | explicit BioWebTA/import/mapping/punch/coordinator class list | `scripts/test-biowebta-import.ps1` |
| Web/components | none | view contracts consumed by pages | DI, Identity, persistence composition | `Components`, `Authorization`, `Program.cs` | bUnit and web-host integration class list | `scripts/test-web-component.ps1` |
| Master data | `Domain/MasterData` | Departments, Employees, LeaveTypes | matching persistence configurations | master-data pages | `MasterData*`, `EmployeeNumber*` | `scripts/test-master-data.ps1` |
| Identity | none | UserAccounts and security policy contracts | `Infrastructure/Identity` | account and authorization endpoints/pages | `Identity*` | `scripts/test-identity.ps1` |
| Company calendar | `Domain/CompanyCalendars` | `Application/CompanyCalendars` | manifest reader and persistence | calendar pages | `CompanyCalendar*` | `scripts/test-company-calendar.ps1` |
| Dashboard | shared and employee-scoped read models | `Application/Dashboard` | persistence through application context | `/`, `/my` | `Dashboard*`, `EmployeeDashboard*` | `scripts/test-dashboard.ps1` |
| Payroll P.1 | `Domain/Payroll` component, plan, assignment, override, period, run, adjustment, immutable snapshot | `Application/Payroll` | payroll persistence configurations and `AddFlexiblePayrollFoundation` | `/admin/payroll*` (Admin only) | `PayrollFoundation*` | `scripts/test-payroll.ps1` |
| Insurance management | `Domain/Payroll` labor/occupational and health enrollments | `Application/Payroll/InsuranceManagement*` | existing payroll insurance persistence configurations | `/admin/insurance*` | `InsuranceManagement*` plus payroll insurance calculators | `scripts/test-payroll.ps1` |
| Approval foundation | `Domain/Approvals` aggregate, append-only history, LINE binding/token | `Application/Approvals`, source providers | approval persistence, Identity actor directory, private LINE bridge adapter | `/approvals`, Payroll送簽、LINE postback endpoint | `Approval*`, navigation | `scripts/test-approval.ps1` |

Paths in the table are relative to `src/HRSystem.*`.

## Targeted reading order

1. Read the requested page, service, or domain type.
2. Follow only its constructor dependencies and interfaces.
3. Read its persistence configuration only when storage behavior is involved.
4. Read the matching test classes listed by the scoped script.
5. Expand to another module only after an actual project reference, shared entity, transaction, or test failure proves the dependency.

Do not scan migrations, generated files, all Razor pages, and all tests for a local UI change. Migration work is the exception: inspect the DbContext, model snapshot, complete ordered migration list, and database-specific tests.

## Solution filters

- `HRSystem.Attendance.slnf`: Domain, Application, Infrastructure, UnitTests, IntegrationTests.
- `HRSystem.Leave.slnf`: Domain, Application, Infrastructure, Web, UnitTests, IntegrationTests.
- `HRSystem.BioWebTaImport.slnf`: Domain, Application, Infrastructure, independent import Job, UnitTests, IntegrationTests.
- `HRSystem.Web.slnf`: Web and IntegrationTests; project references restore/build their required dependencies.

Solution filters reduce solution loading and command intent. They do not turn shared modular-monolith projects into isolated assemblies.

## Protected boundaries

- Unrelated repositories and projects are outside the task scope unless explicitly authorized.
- Database, migration, publish, deployment, runtime, scheduler, external BioWebTA, and `<RUNTIME_DATA_DIRECTORY>` actions require separate explicit authorization.
- `AttendanceRawEvents` are append-only. Test or tooling work must not synthesize or mutate formal attendance data.
