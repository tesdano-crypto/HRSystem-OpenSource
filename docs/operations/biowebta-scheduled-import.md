# BioWebTA Scheduled Attendance Import

## Runtime boundary

The scheduled importer is the independent `HRSystem.AttendanceImport.Job`
executable. HRSystem.Web does not register an import `BackgroundService` and
does not import attendance merely because the website starts.

Both Windows Task Scheduler execution and the Admin **Run Now** action call
`IBioWebTaImportCoordinator`. The coordinator owns the source window,
fingerprint/idempotency plan, cross-process lock, HRSystem transaction, and
focused attendance recalculation.

## Production-safe defaults

`BioWebTA:ScheduledImport:Enabled` is `false` in committed configuration.
Enabling it, publishing the Job, creating the Windows task, and running against
the formal BioWebTA source require separate deployment authorization.

The source connection uses only `BIOWEBTA_READONLY_USERNAME` and
`BIOWEBTA_READONLY_PASSWORD` from environment or secret configuration. The
adapter sets SQL `ApplicationIntent=ReadOnly` and executes parameterized
`SELECT` statements only.

## Windows Task Scheduler plan

Choose your own task name, installation path, service account and schedule.
Do not enable or install tasks as part of a development build. Example command:

```powershell
<YOUR_JOB_DIRECTORY>\HRSystem.AttendanceImport.Job.exe run --trigger scheduled --overlap-days 3
```

Use a least-privilege service account and disable overlapping task instances.
Choose retry and timeout settings for your own installation.

The task-level single-instance rule is reinforced by SQL Server
`sp_getapplock` (`HRSystem:BioWebTA:ScheduledImport`) so Admin Run Now and the
scheduled executable cannot write concurrently.

## Window and idempotency

For an ordinary run, the half-open local-time window is:

```text
QueryFromLocal = local execution date - (OverlapDays - 1), 00:00
QueryToLocal   = actual local start time
```

The default overlap is 3 calendar days. Stable source paging is ordered by
`AttLogTime, Id`. `AttLog.Id` is traceability/paging metadata; the authoritative
deduplication identity is the Phase 1 v1 SHA-256 fingerprint and its database
unique index. A reused `SourceSystem + ExternalEventId` with different content
fails the whole import plan without overwriting a raw event.

## Manual catch-up and preview

An inclusive historical range must be previewed first:

```powershell
HRSystem.AttendanceImport.Job.exe dry-run --trigger manual --from 2026-07-01 --to 2026-07-31
```

After human review, the same inclusive dates may be applied:

```powershell
HRSystem.AttendanceImport.Job.exe run --trigger manual --from 2026-07-01 --to 2026-07-31
```

Internally, the latter boundary becomes `2026-08-01 00:00` exclusive. Dry-run
reads source and HRSystem identity/mapping metadata but creates no batch, raw
event, audit, or attendance result.

## Transactions and failures

Source rows are read outside the HRSystem transaction. Planning and fingerprint
comparison complete before writes. The HRSystem transaction then persists the
batch, append-only raw events, and focused `EmployeeId + WorkDate`
recalculation. A recalculation or persistence failure rolls back raw events,
attendance results, and the in-transaction batch; a separate safe operational
failure batch is retained without source payload, PIN, employee name, SQL text,
or connection details.

No-change executions retain a completed operational batch, insert no raw
events, execute no attendance recalculation, and create no repetitive audit
entry. Manual Run Now writes one safe request audit; scheduled executions use
the import batch as operational history.
