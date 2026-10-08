# Comp Time Ledger

`COMP_TIME` is the stable leave-type code for paid compensatory leave. The
display name is `補休`; application and payroll logic must never identify it by
the localized name.

## Authority and precision

- `CompTimeTransactions` is the balance authority. No mutable employee balance
  field is authoritative.
- Hours are positive decimal values in 0.5-hour increments. Transaction type
  supplies the sign: Grant and Restore add; Consume subtracts.
- Transactions are append-only. Corrections require a future compensating
  transaction, never an update or hard delete.
- Current balance is `Grant + Restore - Consume`.

## Sources

- `LegacyOpeningBalance` represents the balance at HRSystem cutover. It is not
  an overtime event and has no expiry in the current policy.
- `LeaveRequest` links approval-time Consume and approved-cancellation Restore
  entries to the original request.
- Overtime-to-comp-time conversion is outside this phase. Do not create fake
  overtime requests, recognition, work dates, or raw events.

## Leave workflow

- Draft, Submitted, Rejected, and Withdrawn requests do not consume hours.
- Approval revalidates the 0.5-hour increment and available balance inside the
  existing serializable leave transaction. Insufficient balance aborts the
  entire approval.
- Approved cancellation appends a Restore in the same transaction as the leave
  state transition and attendance recalculation.
- A unique source/action index prevents duplicate Consume or Restore rows for
  one LeaveRequest.

## Payroll and attendance

- `COMP_TIME` is paid leave. Leave deduction resolves to zero.
- Attendance allowance uses the existing centralized policy for any approved
  leave; this phase does not add a COMP_TIME exception.
- Leave duration continues to use the employee's effective work schedule and
  lunch break rather than a hard-coded eight-hour day.

## Administration and privacy

- `LeaveManage` is required to view all balances and create a legacy opening
  balance.
- Employees with `LeaveRequestSelfService` can only view their own balance and
  ledger and use the normal leave-request workflow.
- Historical comp-time expiry policy is pending; opening balances have no
  expiry until a separate policy is approved.
