# Holiday training comp time — Phase 1

Scope: independently approved training credits and traceable COMP_TIME allocation.
No attendance, raw punch, overtime request, payroll earning or expiry job is created.
Expiration is nullable metadata used only for ordering/display, even when in the past.

## Sources and approval

`TrainingCompTimeGrant.Id` is the stable command/source identity. Employee, training
date and normalized course have a unique database index to prevent obvious duplicates.
Draft creates no credit. Approval records the calendar day/year IDs, detailed day type,
classification, day RowVersion (or explicit fallback version), actor/time and append-only
history. Approval, audit and the unique source-linked ledger Grant commit atomically.
Approved data is immutable. Retrying a completed approval returns the existing result.

Both create and approval read raw punches, overtime requests and daily time evidence.
Conflicting evidence produces a warning; approval requires a review note and the current
evidence fingerprint. Missing published calendar dates require explicit fallback review.
A currently required workday cannot be approved as holiday training. Date types are the
existing company calendar taxonomy, not a new statutory/pay-rate classification.
The ordinary overtime pipeline is unchanged; this guard is at the training boundary,
not a new bidirectional prohibition on future overtime activity.

## Legacy cutover

Migration `20260930061925_AddHolidayTrainingCompTime` takes a transaction-held ledger
table lock, rejects negative balances and unmatched restores, and creates one immutable
pool per employee who has historical ledger entries. Pool IDs are the employee IDs.
Each pool stores Grant/Consume/Restore totals, ledger count, UTC cutover timestamp and
the migration identity in Watermark. `CompTimeLegacyMembers` records the exact set of
ledger transaction IDs: timestamps alone are not used as membership boundaries.

Pool baseline = Grant + Restore - Consume. The migration adds no ledger transactions
and changes no old rows. A zero-balance pool is retained when ledger entries exist:
an old fully consumed leave may later be cancelled. Employees without ledger rows need
no pool. Post-cutover opening corrections are concrete new Grants, never pool members.

EF migration-history gating makes application idempotent. Baseline identities and
membership are deterministic; the cutover timestamp records the actual application.
On any invariant failure, THROW rolls back the migration transaction; no difference
is automatically repaired. A reviewed deployment window must prevent old application
instances from continuing to write unallocated Consume rows after cutover. This phase
does not perform that deployment or stop/restart any process.

## Allocation and returns

An allocation references exactly one concrete Grant OR one Legacy Pool, plus its
Consume transaction and allocated hours. Both targets, and the ledger, are read in the
same Serializable transaction used by existing leave approval/cancellation.

Ordering is deterministic:
1. Dated concrete credits first: expiration, EffectiveDate, creation timestamp, stable ID.
2. Undated credits and pools: proven credit creation/cutover timestamp, stable ID.

The pool's cutover is deliberately not an invented original acquisition date.
Allocations and allocation-return links are append-only. A new leave cancellation
adds the existing Restore ledger entry and one unique return link for every original
allocation; it never reallocates. An old unallocated Consume is accepted only if it
belongs to the exact legacy watermark, then a unique LegacyReturn links the Consume,
Restore and pool. Baseline totals themselves never change.

`pool available = baseline + post-cutover legacy returns - net allocated usage`

`concrete available = Grant.Hours - net allocated usage`

Before allocation/return and training approval, the service requires every post-cutover
Consume/Restore to reconcile and total available to equal the official ledger balance.
Negative or inconsistent state fails closed. Concurrent SQL transactions can deadlock;
the losing transaction is rolled back and can be retried after reload. No process-local
lock is treated as a database integrity guarantee.

## Production migration gates (not executed by implementation)

1. Back up and verify recovery; obtain separate migration/deployment authorization.
2. Arrange a write-free cutover window across all application instances. Do not run the
   old writer after migration; it does not persist allocations.
3. Inspect preflight aggregate validation and the generated migration SQL. Negative
   balances/unmatched returns require investigation, never automatic repair.
4. Apply using EF's transactional migration mechanism. Do not suppress transactions.
5. Run the read-only validation script in `scripts/validate-comp-time-allocation.sql`.
   Any difference is a STOP/rollback condition during the approved deployment gate.
6. Confirm old ledger row counts/IDs/RowVersions unchanged, new source separation,
   authorization and UI with separately approved smoke steps.

Down is refused once Training, allocation or legacy-return records exist, even for
training drafts. Use a reviewed forward fix or separately approved full-backup recovery.

Next phases only: expiry execution, expired-balance removal, overtime-pay conversion,
payroll settlement and non-working-day overtime rate policy. No such behavior is enabled.
