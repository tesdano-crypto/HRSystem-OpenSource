# Payroll totals (P.6)

## Calculation boundary

Payroll totals are a downstream aggregation of immutable employee payroll
component snapshots. The calculator uses only component category, final
`ResolvedAmount`, typed calculation status, and stable snapshot identity. It
does not query or recalculate attendance, leave, overtime recognition, labor
insurance, or health insurance.

- Gross pay is the sum of resolved earning components.
- Total deductions is the sum of resolved deduction components.
- Net pay is `GrossPay - TotalDeductions` when all present applicable components
  are resolved.
- Informational and explicitly disabled/not-applicable components are excluded.
- An optional component that is absent does not block; if it is present, its
  status and amount must still be valid.
- Component amounts are already payroll-rounded. P.6 sums them without another
  rounding pass.

This prevents double counting: base salary remains an earning while leave
deduction is a separate deduction, and the three overtime component amounts are
included once without also adding overtime evidence totals.

## Readiness and blocking

Known gross and known deductions are retained even when net pay cannot be
resolved. `NeedsSetup`, `PolicyPending`, `NeedsReview`, `NotCalculated`, and
`SourceChanged` block net pay and leave it null. The centralized precedence is:

1. `NeedsReview`
2. `SourceChanged`
3. `NeedsSetup`
4. `PolicyPending`
5. `NotCalculated`
6. `Resolved`

Missing resolved amounts and illegal negative component amounts become
`NeedsReview`; the calculator does not apply `Abs` or silently normalize them.
When deductions exceed earnings, the mathematical negative net remains visible
but the overall status requires review.

Each blocker stores only component definition/stable code, typed status, and a
typed reason. It does not copy punch details, leave reasons, insurance details,
or HR notes.

## Snapshot and fingerprint

`PayrollEmployeeSnapshot` persists gross, deductions, nullable net, total
status, calculation time, blocking count, fingerprint version, and a 32-byte
SHA-256 fingerprint. The v1 fingerprint covers sorted component snapshot
definition/source identities, category, final status, and resolved amount. It
does not re-hash upstream business source payloads.

Totals are applied once during the existing Draft transaction. Blocking evidence
is append-only. Query-time fingerprint comparison can report `SourceChanged`
without silently rewriting the historical snapshot; an explicit future rebuild
must create new totals.

## UI and authorization

The Admin Draft detail groups earning and deduction components, shows known
gross and deduction totals, and displays net only when resolved. A blocked Draft
shows `薪資尚未完成計算` and the safe blocking list rather than zero. Access remains
under `PayrollManage`; no totals or blocking evidence are exposed on employee or
manager self-service pages.
