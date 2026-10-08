# Payroll monthly workflow

The accounting workflow treats a `PayrollPeriod` as one logical payroll month.
It can contain many append-only `PayrollRun` calculation operations. A run has a
period-scoped, increasing revision number and records why it ran: initial batch,
single-employee recalculation, or changed-source recalculation.

## Calculation revision model

- `PayrollPeriod` identifies the month. Historical months are allowed and use
  settings effective for that month; no previous snapshot is copied.
- `PayrollRun` records one calculation operation. It contains only snapshots
  newly calculated by that operation, so changing one employee does not clone
  the rest of the month.
- `PayrollEmployeeSnapshot` and all P.1–P.6 evidence are immutable. Recalculation
  appends a new snapshot and never updates or deletes an old one.
- `PayrollPeriodEmployeeCurrentSnapshot` is the mutable selection pointer for one
  period and employee. Its composite lineage foreign key guarantees that the
  selected snapshot belongs to the same period and employee. This projection is
  intentionally excluded from the immutable-evidence guard.
- A pointer switch and its new snapshot commit in the same serializable database
  transaction. Calculation, invariant, database, or concurrency failure leaves
  the previous pointer intact.

Existing pre-revision snapshots are preserved byte-for-byte by migration. Each
legacy period/employee snapshot receives exactly one current pointer. Once a
period contains multiple revisions, application/schema rollback must use the
pre-migration backup; the Down migration refuses to merge or discard history.

## Current set, totals, and fingerprint

Month UI, employee count, readiness, gross pay, deductions, net pay, and approval
all query the explicit current pointer set. Historical snapshots are never
included by a `latest timestamp` guess and are never double-counted.

`PayrollMonthFingerprintV1` sorts current items by `EmployeeId` and hashes the
period, employee, selected snapshot ID, P.6 total fingerprint, gross,
deductions, net, status, and source-changed flag with SHA-256. It deliberately
reuses snapshot evidence hashes instead of re-hashing raw attendance sources.
The same current set produces the same fingerprint; a pointer switch or stale
flag changes it.

Payroll approval uses the Period ID plus this month fingerprint. A changed
current set cannot reuse a Pending approval. An Approved-but-not-finalized
fingerprint mismatch is displayed as requiring a new submission. Approval
summary and private notification contain month/count/totals only, not employee
salary details.

## Calculation and input workflow

## Employment and payroll participation

Employment overlap and payroll participation are separate decisions. The
central eligibility pipeline first checks historical employment overlap, then
employee lifecycle state, effective pay-cycle participation, and finally the
availability of calculation rules. An active employee continues through the
pipeline. An inactive employee with a termination date remains eligible for an
overlapping historical month, while an inactive record without termination
history is excluded.

Employees without an explicit pay-cycle row default to `Monthly`（每月發薪）.
`SemiannualFixed`（每 6 個月固定給付）uses an effective-dated anchor month and
fixed periodic amount. A month participates only when the signed month distance
from the anchor is non-negative and divisible by six, including across year
boundaries. For example, a December 2025 anchor participates in December 2025,
June 2026, December 2026, and June 2027. A non-pay month is displayed as
「本月不發薪」and is excluded from batch snapshots, totals, approval population,
month fingerprint, and finalization; attendance, leave, overtime, and employment
records are unaffected.

The generic `PERIODIC_FIXED_PAY`（週期固定給付）earning stores the fixed amount
for a participating period; it does not multiply a monthly salary or apply
employment/attendance proration. Insurance deductions remain governed by their
existing evidence and policies. Effective-dated monthly assignments and
overrides continue to represent ordinary fixed amounts, so a full-time amount
through August and a different monthly fixed amount beginning in September can
coexist without rewriting historical July or August results.

`PeriodicAccruedFixed`（每月固定薪資／週期集中支付）is the
effective-dated accrual model. It records the contractual monthly amount,
cycle length, anchor pay month, and `CycleStart` timing. A payment month resolves
each covered calendar month independently and sums those resolved amounts; it
never multiplies the payment-month amount by the cycle length. The immutable
snapshot retains the covered range, every month's source amount and status,
the total, cycle length, and a SHA-256 source fingerprint. Missing authority is
`NeedsSetup`; overlapping or schedule-inconsistent authority is `NeedsReview`.
Retroactive changes make the affected non-finalized calculation stale, but do
not create an automatic payment or deduction adjustment. Reconciliation of an
already paid cycle remains a pending business policy. The legacy
`SemiannualFixed` type remains supported unchanged and existing rows are not
converted automatically.

1. Create the payroll month.
2. Review effective-dated employee plan, override, and insurance setup.
3. Create the initial employee batch. One setup problem is returned as a typed
   item result and does not prevent calculable employees from committing.
4. Add or edit monthly temporary earnings/deductions. The current pointer is
   marked `SourceChanged`; the old snapshot remains unchanged.
5. Recalculate one employee or all changed employees. If freshly calculated
   evidence matches the current snapshot, no duplicate snapshot/run is stored.
6. Review the current totals/readiness and submit the month for approval only
   when every eligible employee has a resolved, non-stale current snapshot.

Batch item outcomes are `Created`, `AlreadyCurrent`, `NeedsSetup`,
`PolicyPending`, `NeedsReview`, and `Failed`; the UI maps them to Chinese.
True database/invariant/concurrency failures roll back the transaction rather
than being silently converted to a partial result.

## Historical / Legacy Payroll Adjustments

`LEGACY_ATTENDANCE_ALLOWANCE`（出席補貼（歷史過渡））and
`LEGACY_OVERTIME_PAY`（加班費（歷史過渡））are explicit, non-recurring manual
earnings for an HRSystem pre-launch month or a documented transition month
whose source attendance or overtime evidence is incomplete. They require an
external payroll authority, an operator-entered reason, PayrollManage
permission, and an append-only audit entry.

These adjustments contribute their explicit amounts to gross pay and revision,
month, approval, and finalization fingerprints. They do not resolve or replace
the normal attendance-allowance or overtime components and never create raw
attendance, attendance adjustments, overtime requests, recognitions, minutes,
rate buckets, or hourly-base evidence. They must not be used as a normal-month
balancing entry. The employee payslip shows only the historical-transition
component label and amount, without fabricated source evidence.

## Concurrency and query boundary

Revision allocation and pointer switching execute under a serializable
transaction and are backed by a unique `(PayrollPeriodId, RevisionNumber)`
constraint plus pointer row versions. Employee/source inputs are batch-loaded
for the selected employees and period before calculation. The current set is a
centralized query; it does not issue a query per employee.

Formal finalization, locking, payslips, bank files, and closing a payroll month
are not enabled in this phase.
# 正式結算銜接

Approved 只核准當下的月份 current-set fingerprint；後續必須由 Admin 執行獨立正式結算 Gate。正式結算會固定每名員工的不可變 snapshot、將月份標記為 Finalized 並發行薪資單。完整規則見 [薪資正式結算與薪資單](payroll-finalization.md)。
