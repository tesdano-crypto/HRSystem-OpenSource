# Payroll flexible foundation (P.1)

HRSystem payroll is deliberately **component-based**. Do not add a single
`Employee.MonthlySalary` field as the authoritative source. The resolution path is:

`Employee → effective PayrollPlan → plan components → employee overrides → period adjustments → immutable run snapshot`

## P.1 capabilities

- Typed earning, deduction, and informational component definitions.
- Effective-dated plans, plan components, employee assignments, and overrides.
- `STANDARD_MONTHLY` seed with configurable base salary (29,500), meal allowance
  (2,000), attendance standard amount (2,000), and versionable seniority tiers.
- `CUSTOM_FIXED` seed: an employee receives an explicit BASE_SALARY Replace
  override and may disable other components; there are no employee-code exceptions.
- Positive adjustment amounts plus explicit earning/deduction direction.
- Monthly periods, append-only calculation runs, employment-overlap eligibility,
  and atomic current-snapshot selection. See
  [payroll-monthly-workflow.md](payroll-monthly-workflow.md).
- Snapshots copy employee/department/plan labels and component source metadata.
  Later master-data or configuration edits never silently rewrite history.
- Missing assignments or required fixed amounts block draft creation. Pending
  rule/external lines store `ResolvedAmount = null`, never a false zero.
- `PayrollManage` is Admin-only. Payroll values are not added to employee or
  manager DTOs. Audit payloads record identifiers/counts, not salary amounts.

No formal employee assignment is seeded or inferred. Formal setup requires a
separate reviewed operation.

## Explicitly not implemented in P.1

P.1 does not calculate attendance allowance, leave deduction, overtime pay,
labor/health insurance, gross pay, total deductions, net pay, tax, pension,
payslips, or payroll exports. It does not read attendance, leave, or overtime
recognition to manufacture amounts.

Future calculation phases must consume the snapshot inputs and preserve nullable
pending semantics until every required rule has been evaluated. A future
historical-regression phase must compare the completed engine against the 2026-07
payroll workbook component by component; P.1 does not import that workbook.

## P.2 fixed earnings and employment proration

P.2 resolves fixed earnings without reading attendance data. Employment overlap
uses `Employee.HireDate` and `Employee.TerminationDate`; it does not use worked
days, leave, lateness, attendance exceptions, suspensions, overtime, or raw punch
events.

- `BASE_SALARY` and `MEAL_ALLOWANCE` use the shared
  `Monthly30DayProrationPolicy`. A full-month employee receives the full monthly
  amount in every calendar month (including February and 31-day months).
- Partial employment uses inclusive calendar overlap days and `amount × days / 30`.
  New Taiwan dollar rounding is centralized and explicitly uses midpoint away
  from zero. Approved regressions are `29,500 × 19 / 30 = 18,683` and
  `2,000 × 19 / 30 = 1,267`.
- `PERFORMANCE` resolves its seniority-tier full-month amount. Seniority is
  evaluated through the centralized `SeniorityEvaluationPolicy`; P.2 currently
  selects `PeriodEnd` as an explicit, replaceable policy.
- Partial-month proration for `PERFORMANCE`, `JOB_ALLOWANCE`, and
  `CERTIFICATE_ALLOWANCE` is not evidenced. These lines retain the resolved
  full-month amount but use `PolicyPending` with no payable amount.
- In the P.2-only stage, attendance allowance, overtime, leave deduction, and
  insurance lines used `NotCalculated`; P.3 now resolves the attendance and
  leave lines while overtime and insurance remain not calculated.
- `CUSTOM_FIXED` continues to resolve explicit employee Replace overrides without
  leaking `STANDARD_MONTHLY` values; Replace, Add, Disable, and effective-date
  precedence remain the P.1 rules.

Draft snapshots persist the full-month amount, proration kind, payable employment
days, factor, raw prorated amount, final resolved amount, and typed calculation
status. The UI labels the sum only as **已解析固定應發小計（非應發總額）**;
policy-pending and not-calculated lines are excluded.

P.2 still does not calculate a complete gross amount, total deductions, net pay,
statutory deductions, payslips, or exports.

## P.3 attendance allowance and leave deduction

P.3 adds two independent calculations to Payroll Draft and Admin preview. It
does not calculate gross pay or net pay.

### Attendance allowance

- The standard full-month amount is NTD 2,000 and uses the shared
  `Monthly30DayProrationPolicy`.
- A final `DailyAttendanceResult` date is ineligible when it contains Late,
  Early Leave, Missing ClockIn, Missing ClockOut, or any authoritative approved
  leave segment.
- Multiple reasons on the same WorkDate produce one ineligible day and one
  evidence row with combined flags. Any partial-day approved leave still makes
  the whole WorkDate ineligible for this allowance.
- Full-month protection applies to 28-, 29-, 30-, and 31-day months. Partial
  employment uses inclusive employment overlap, capped at a 30-day basis.
- Payroll never derives these outcomes from `AttendanceRawEvents`; corrections
  are reflected only through the recalculated current `DailyAttendanceResult`.
- Missing authoritative daily results produce `NeedsReview`; Payroll does not
  silently grant the full allowance.

### Leave deduction

- Only the complete monthly `BASE_SALARY` is the deduction basis. The P.2
  employment-prorated resolved base is deliberately not reused, avoiding
  double proration.
- Effective-dated policies are seeded only for stable codes `PERSONAL` (100%)
  and `SICK` (50%), with calculation basis `BaseSalaryOnly`.
- Authoritative `DailyAttendanceLeaveSegment` minutes and each final result's
  scheduled minutes are used. Request start/end subtraction and fixed 480-minute
  assumptions are not used.
- Unsupported leave codes are `PolicyPending`; no deduction rate or amount is
  guessed. Absence, parental leave/suspension, and other special policies remain
  outside P.3.
- Raw decimal contributions are aggregated and rounded once using
  `PayrollMoneyRoundingPolicy`.

### Audit evidence and source changes

- Drafts persist append-only attendance-allowance date/reason evidence and
  leave-deduction date/code/minutes/rate/raw-amount evidence. Free-text leave
  reasons, raw punch payloads, internal review notes, and actors are not copied.
- Attendance and leave source fingerprints (version 1, binary SHA-256) are
  stored with the P.3 snapshot. Draft snapshots never refresh in the
  background. A later attendance or leave correction does not mutate history;
  a future explicit rebuild workflow may compare fingerprints and mark a draft
  for recalculation.
- Draft source queries batch-load final attendance results, leave segments, and
  payroll policies for all eligible employees and the payroll period before
  bounded in-memory calculation; there is no employee-by-day query loop.

P.3 still does not calculate overtime pay, labor/health insurance, pension,
income tax, complete gross/net pay, or payslips.

## P.4 recognized overtime pay

Payroll is a downstream consumer of Actual Overtime Recognition. It pays only a
current-fingerprint `Confirmed` recognition; it never substitutes clock-out,
overstay warnings, requested minutes, or approved request minutes. Missing,
stale, reopened, overlapping, or review-required recognition makes the monthly
overtime result `NeedsReview`. A confirmed zero-minute outcome is a valid,
resolved zero.

The overtime hourly base is component-driven through
`IncludeInOvertimeHourlyBase`. The initial base includes full-month amounts for
base salary, performance, meal allowance, job allowance, and attendance
allowance. It excludes certificate allowance, case bonus, other temporary
earnings, overtime itself, and deductions. Employment proration and reductions
to the actually paid attendance allowance do not reduce this base:

`HourlyBase = sum(included FullMonthlyAmount) / 30 / 8`

The effective-dated `2026-v1` rate policy allocates cumulative recognized
minutes independently per recognition `WorkDate`: the first 120 minutes use
1.34, minutes 121 through 480 use 1.67, and minutes above 480 use 2.67. Multiple
segments on one work date share the same cumulative buckets; a cross-midnight
segment remains on its recognition work date. Minute precision is preserved.
Raw pay is aggregated by bucket for the payroll month and each bucket is rounded
once with `PayrollMoneyRoundingPolicy`.

Draft creation snapshots the included component amounts, hourly base, rate
policy version, recognition outcome identifiers and fingerprints, daily bucket
allocation, raw/final bucket amounts, total minutes, total pay, and calculation
status. It deliberately excludes punches, notes, free text, and actor details.
Later configuration or recognition changes do not mutate historical snapshots.

The normal 1.34/1.67/2.67 policy applies only where the persisted attendance
classification authoritatively identifies a working day. Weekend and holiday
classifications remain `PolicyPending`; P.4 does not invent statutory
rest-day/holiday rates. Labor insurance, health insurance, complete gross/net,
and payslip generation remain unsupported in this phase.

## P.5A labor-insurance employee deduction

`LABOR_INSURANCE` now uses an explicit effective-dated enrollment and monthly
insured salary; it is never inferred from payroll earnings. A versioned policy
supplies coverage rates, employee share, and contribution-period behavior.
Only employee-paid amounts enter this deduction. Missing setup, missing policy,
partial-period uncertainty, and ambiguous sources remain explicit typed states
instead of zero or guessed values.

Drafts persist append-only enrollment/policy/contribution evidence and a v1
SHA-256 source fingerprint. Production policy seed is pending complete official
verification. See
`docs/payroll/labor-insurance.md` for the calculation and evidence boundary.

## P.5B health-insurance employee deduction

`HEALTH_INSURANCE` uses its own effective-dated enrollment, authoritative
monthly insured amount, and nullable actual dependent count. It does not derive
any of these from payroll earnings, labor-insurance settings, or dependent
identity records. A versioned policy controls the general premium rate,
employee share, dependent cap, and contribution-period behavior. The employee
is one contribution unit and chargeable dependents are capped by policy.

Drafts persist an append-only calculation snapshot, minimal payroll evidence,
and a v1 SHA-256 source fingerprint. Missing, ambiguous, or partial-period
sources remain typed setup/policy/review states. Production policy seed is
pending official verification. Supplementary premium, employer/government
shares, pension, tax, and payslips remain outside this phase. See
`docs/payroll/health-insurance.md`.

## P.6 gross, deductions, and net pay

P.6 consumes the final `ResolvedAmount` and typed status already stored on each
employee's component snapshots. It does not rerun P.1–P.5B calculators or read
their attendance, leave, overtime, or insurance source records. Earning-category
components contribute once to known gross; deduction-category components
contribute once to known deductions. Net pay is gross minus deductions only
when every present, applicable component is resolved.

Known gross and deductions remain visible when a required component is blocked,
but net pay stays null and the Draft shows the typed blocking components instead
of a false zero. Status precedence is deterministic: `NeedsReview`,
`SourceChanged`, `NeedsSetup`, `PolicyPending`, `NotCalculated`, then `Resolved`.
A mathematically negative net is preserved and marked `NeedsReview`; values are
not clamped or rounded again.

Drafts persist immutable totals, calculation time, blocking count and evidence,
and a v1 SHA-256 fingerprint of component snapshot identities, categories,
statuses, and final amounts. Later source changes do not silently mutate the
Draft. See `docs/payroll/payroll-totals.md` for the detailed boundary.

P.6 totals remain immutable inside each employee snapshot. Monthly recalculation
creates another run/snapshot and changes only the explicit current pointer; it
does not rewrite P.1–P.6 evidence.

## Payroll UI terminology

The accounting-facing UI uses Chinese presentation terms while retaining the
existing English domain enums and database values:

- Payroll period: **薪資月份**; an open period is **處理中**.
- Draft run: **薪資試算**; a draft run is **試算中**.
- Earning / deduction: **應發** / **扣款**.
- Gross pay / total deductions / net pay: **應發總額** / **扣款總額** /
  **實領薪資**.
- `Resolved`: **已完成**.
- `NeedsSetup`: **資料待設定**.
- `PolicyPending`: **政策待確認**.
- `NeedsReview`: **需要確認**.
- `NotCalculated` / `Pending`: **尚未計算**.
- `SourceChanged`: **來源資料已變更**; the UI guides the accountant to
  create a new calculation.
- `Disabled` and a not-applicable total requirement: **不適用**.

Component names, adjustment direction, calculation source, period/run status,
and blocking reasons are rendered through the centralized Web-layer
`PayrollDisplay` helper. Domain enum names and component codes are not changed.
The UI does not infer policy or calculate payroll values; it only explains the
existing P.1–P.6 results and workflow.
