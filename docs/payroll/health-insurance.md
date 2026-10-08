# Payroll P.5B health-insurance deduction

## Scope and authority

P.5B calculates only the general National Health Insurance amount paid by the
employee. It does not calculate supplementary premiums, employer or government
shares, pension, tax, gross/net pay, or payslips.

The authoritative inputs are effective-dated `EmployeeHealthInsuranceEnrollment`
rows and `HealthInsuranceRatePolicy` rows. `MonthlyInsuredAmount` is explicit
and is never derived from base salary, allowances, overtime, leave deductions,
labor-insurance salary, or total earnings. `DependentCount` is nullable so an
unknown value remains distinct from an explicit zero. No dependent names,
identifiers, medical information, or relationship data are stored in payroll
evidence.

## Calculation

When the month-end insurance authority is `Enrolled` and a complete-period
policy exists:

```text
ChargeableDependentCount = min(ActualDependentCount, DependentCap)
ContributionUnits = 1 employee + ChargeableDependentCount
RawEmployeeAmount = MonthlyInsuredAmount
                    × GeneralPremiumRate
                    × EmployeeShareRate
                    × ContributionUnits
FinalEmployeeDeduction = centralized NTD payroll rounding(RawEmployeeAmount)
```

Only the final employee deduction is resolved into `HEALTH_INSURANCE`.
Rounding uses `PayrollMoneyRoundingPolicy`, not the default `Math.Round`
midpoint behavior.

## Typed incomplete states

- No enrollment, insured amount, or dependent count: `NeedsSetup`.
- Explicit month-end `NotEnrolled`, or a configured month with no company
  coverage: resolved zero without a policy.
- Mid-month enrollment that remains active at month end: full-month premium;
  health insurance is never prorated by `/30`.
- No policy when coverage authority is otherwise complete: `PolicyPending`.
- Mid-month transfer-out without month-end coverage/transfer authority, or
  overlapping month-end enrollments/policies: `NeedsReview`.

Insurance effective dates are authoritative. `HireDate` and `TerminationDate`
remain fingerprint compatibility inputs but no longer clip or override health
coverage decisions. Attendance, leave, suspension, and calendar-day counts do
not affect monthly health billing.

## Snapshot, evidence, and privacy

Draft creation stores an append-only `PayrollHealthInsuranceSnapshot` and one
minimal `PayrollHealthInsuranceEvidence` row. They record insured amount,
actual/capped dependent counts, contribution units, applicable policy/rates,
raw/final employee amounts, status, and source fingerprint. The v1 SHA-256
fingerprint changes when enrollment, insured amount, dependent count,
employment boundary, policy, or applicable period changes.

Snapshots and evidence cannot be updated or deleted. Draft queries batch-load
all employee health enrollments and policies before per-employee calculation;
there is no employee-by-employee database query loop.

## Production policy seed

**Production Policy Seed = Pending official verification.**

The migration creates schema only. It does not seed a rate, insured-amount
grade, dependent cap, or partial-period rule. Until an authoritative policy is
entered, enrolled employees remain `PolicyPending`; the system does not write
zero or guess an amount.
