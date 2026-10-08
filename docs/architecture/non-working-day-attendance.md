# Non-working-day punch evidence and overtime review

Raw Punch → Attendance Evidence → Overtime Request → Approval → Recognized Minutes → Payroll

**非工作日有打卡 ≠ 已核准加班。打卡跨度不是工時，也不是應付加班。**

## Read projection

My Attendance and Attendance Review merge bounded employee/date raw evidence with existing daily results. Published Company Calendar is authoritative; the existing weekend fallback is used only if no published calendar day exists. Both paths share AttendanceCalendarResolver. Make-up working Saturdays retain working-day calculation semantics.

Raw-only historical dates use a view-only empty result identifier, never a persisted fake result. Existing result + raw evidence becomes one employee/date row. Empty nonworking days without requests or review are hidden. First/last/count/all punches and span remain evidence, independent of effective attendance times and recognition fingerprints. No attendance engine data is recalculated or backfilled for visibility.

Queries are restricted to selected employees and at most 92 days. Raw, calendar, requests and resolutions are loaded in batches, without per-row database queries. Existing daily results are ordered in SQL; a bounded merge orders the combined read models and applies the final limit after filtering, so historical evidence is not lost to a pre-merge limit.

## Resolution

NonWorkingDayPunch extends the existing resolution ledger. Only this type can omit DailyAttendanceResultId; Domain and SQL enforce that invariant. Only non-work, personal, incorrect-punch and Other reasons are accepted. Other requires a note. The existing actor/time, RowVersion, append-only history and Audit transaction are reused.

The versioned evidence fingerprint includes employee/date, published calendar identity/type/classification and the sorted raw event identities/times/fingerprints. Raw or calendar changes produce NeedsReview. Even a calendar change to working, or disappearance of raw evidence, retains prior review visibility. Reopen and re-resolve retain all history. Current raw/calendar evidence is reloaded inside a Serializable transaction before resolve.

## Overtime and authorization

Employee entry points prefill the date, not payable duration. Start/end require human confirmation; saving creates Draft only. The evidence path uses a Serializable overlap check for Draft, Submitted and Approved requests. Existing overlapping requests are linked, not duplicated. Request/recognition semantics and Payroll inputs remain unchanged.

Review uses existing AttendanceManage; self-service always derives employee identity from the authenticated user. The current matrix grants AttendanceManage to Admin, HR and Accounting; it is not expanded here. Manager retains existing self-service access, without new global review authority. Employee views never expose internal review notes, actor or audit details.

Unresolved nonworking evidence is an independent review queue, not a late/absence/completeness or attendance-allowance metric. Resolved non-overtime evidence remains searchable. No Payroll calculator reads punch spans or suggested time.

## Migration and rollback

AddNonWorkingDayPunchReviewSupport changes only the resolution source nullability and check constraints. Existing records are not updated. Down explicitly refuses when new-type/null-source resolutions exist; it never replaces a missing result with Guid.Empty or deletes evidence. Forward-fix or an independently approved whole-backup recovery is required in that case.

Production smoke is read-only: no resolution, overtime request, recognition, Payroll run/snapshot/adjustment or BioWebTA import is created.
