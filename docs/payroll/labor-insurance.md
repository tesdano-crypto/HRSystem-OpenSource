# Labor-insurance payroll deduction (P.5A)

## Boundary

P.5A calculates only the employee-paid labor-insurance deduction represented
by `LABOR_INSURANCE`. Monthly insured salary is distinct from actual payroll
earnings. Leave, attendance allowance, overtime, and custom fixed earnings do
not change it. Health insurance, pension, tax, employer cost, gross/net pay,
and payslips are outside this phase.

## Effective-dated inputs

- `EmployeeLaborInsuranceEnrollment` records only labor/employment-insurance
  authority and its explicit labor-insured salary.
- `EmployeeOccupationalInsuranceEnrollment` separately records occupational
  insurance authority, salary, and effective period. It does not require a
  labor enrollment and may use different dates and salary.
- `LaborInsuranceRatePolicy` records a version, coverage flags, ordinary and
  employment rates, employee share, contribution-period policy, and effective
  period.
- Application validation rejects overlapping enrollment periods. Calculator
  ambiguity guards reject overlapping enrollment or policy sources.
- No production policy is seeded until the complete rates and contribution
  period rules have been verified from an official source.

Official references used to confirm the model boundary (not to seed an
incomplete policy) are the Bureau of Labor Insurance insured-salary table and
declaration guidance, and Labor Insurance Act Article 15. The available
evidence establishes the official grade table and employee-share concepts, but
did not establish every required production rate and partial-period billing
rule as one reviewable version. Therefore:

`Production Policy Seed = Pending official verification`

No occupational rate, employee burden, or employer burden is inferred in this
phase. Independent enrollment readiness is available; an enrolled row remains
`PolicyPending` until an official versioned occupational policy is implemented.

References reviewed:

- Bureau of Labor Insurance, insured-salary declaration guidance:
  https://www.bli.gov.tw/0005475.html
- Bureau of Labor Insurance, 2026 insured-salary grade table:
  https://www.bli.gov.tw/0103192.html
- Ministry of Labor Laws and Regulations, Labor Insurance Act Article 15:
  https://laws.mol.gov.tw/FLAW/FLAWDOC01.aspx?flno=15&id=FL014980

## Calculation and statuses

For an enrollment with exactly one applicable policy, each enabled coverage
item calculates `monthly insured salary × item rate × employee share × coverage factor`.
`ThirtyDayProrated` policies obtain the factor from the shared
`InsuranceCoverageDays` helper. The denominator is always 30: full February
and full 31-day months are both `30/30`; mid-month enrollment and withdrawal
use normalized inclusive insurance coverage dates. A full-month
`FullPeriodOnly` policy remains valid and produces the unchanged full amount.
Raw item amounts are aggregated and rounded once with
`PayrollMoneyRoundingPolicy`. Per-item rounded amounts are display evidence and
are not re-summed for the final deduction.

- explicit NotEnrolled: `Resolved`, amount 0;
- absent enrollment or insured salary: `NeedsSetup`, null amount;
- absent policy, or partial coverage under a policy that does not authorize
  30-day proration: `PolicyPending`, null amount;
- overlapping sources: `NeedsReview`, null amount.

Insurance effective dates are authoritative. Employee hire and termination
dates do not clip labor, employment, or occupational coverage. Ordinary labor
and employment contributions share the same 30-day factor but retain their own
rates. Occupational readiness uses its own enrollment and can reuse the same
day-count helper without coupling the sources.

## Historical evidence

The payroll draft persists immutable historical snapshots plus typed
contribution evidence. Existing v1 snapshots are not rewritten. New labor
snapshots use labor fingerprint v2, which no longer reads occupational salary;
the independent occupational source has its own v1 fingerprint. Applying a new
occupational setting marks affected non-finalized current snapshots as source
changed without automatically recalculating payroll.
