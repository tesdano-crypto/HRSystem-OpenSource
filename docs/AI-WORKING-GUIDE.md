# AI Working Guide for HRSystem

Use this guide to keep AI-assisted work scoped, reproducible, and economical. It complements the repository development rules; it does not override safety or approval gates.

## Start small

1. Confirm repository root, branch, HEAD, staged/unstaged/untracked state, and `git diff --check`.
2. Classify the task with `docs/architecture/change-risk-levels.md`.
3. Use `docs/architecture/module-map.md` to choose the smallest reading order and solution filter.
4. Record allowed paths, forbidden actions, validation, and stop conditions with `docs/ai-prompts/scoped-change-template.md`.
5. Inspect exact dependencies with `rg`; do not repeatedly enumerate the whole repository.

## Build and test commands

Run from the repository root in a normal non-elevated PowerShell session:

```powershell
.\scripts\test-attendance.ps1
.\scripts\test-leave.ps1
.\scripts\test-biowebta-import.ps1
.\scripts\test-web-component.ps1
.\scripts\test-master-data.ps1
.\scripts\test-identity.ps1
.\scripts\test-company-calendar.ps1
.\scripts\test-payroll.ps1
.\scripts\test-full.ps1
```

Each scoped script contains explicit test-class allowlists and prints Unit, Integration/Web, total, failed, and skipped counts. Use `-NoBuild` only after the matching solution filter has already built successfully. Use `test-full.ps1` for the single final full regression required by the risk gate.

Direct solution-filter commands are also supported:

```powershell
dotnet restore .\HRSystem.Attendance.slnf
dotnet build .\HRSystem.Attendance.slnf -c Release --no-restore
```

Replace the filter with `HRSystem.Leave.slnf`, `HRSystem.BioWebTaImport.slnf`, or `HRSystem.Web.slnf` as appropriate.

## Scope and evidence rules

- Prefer `rg` and direct file reads over broad repository dumps.
- Read historical reports only when relevant to the requested investigation or regression root-cause analysis; avoid bulk-loading unrelated reports.
- For authorized migration work, read the migration `.cs` first. Read its generated Designer or the model snapshot only when model metadata is required.
- Preserve existing uncommitted work. Never use reset, clean, restore, or broad stash without explicit authorization.
- Do not print secrets, connection strings, credentials, employee identifiers, or attendance times in reports.
- Do not connect to formal databases or external sources merely to validate local code.
- No database migration, publish, runtime restart, deployment, scheduler, external BioWebTA, or `<RUNTIME_DATA_DIRECTORY>` action is implicit.
- Keep implementation, Migration Gate, and Deployment Gate as separate approval points.
- Report actual evidence and command counts; do not restate the full history or every global safety rule.

## Stable facts versus task facts

Stable architecture and module ownership belong in the module map and ADRs. Mutable branch, HEAD, test totals, pending migrations, runtime PID, database fingerprints, and deployment paths must be rediscovered for the current task. Do not copy stale values into prompts as evidence.

## Updating this tooling

When a new test class is added, place it in the narrowest relevant script allowlist. When a new project is added, update only the solution filters that need it and verify restore/build. Tooling changes must not alter product behavior and should end with one full regression.
