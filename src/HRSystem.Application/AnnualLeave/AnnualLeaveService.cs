using HRSystem.Application.Abstractions.Persistence;
using HRSystem.Application.Abstractions.Security;
using HRSystem.Application.Common.Auditing;
using HRSystem.Application.Common.Exceptions;
using HRSystem.Application.LeaveRequests;
using HRSystem.Application.Security;
using HRSystem.Domain.AnnualLeave;
using HRSystem.Domain.LeaveRequests;
using Microsoft.EntityFrameworkCore;

namespace HRSystem.Application.AnnualLeave;

public sealed class AnnualLeaveService(
    IApplicationDbContext db,
    ICurrentUser currentUser,
    ILeaveDurationCalculator durationCalculator,
    TimeProvider timeProvider) : IAnnualLeaveService
{
    private static readonly TimeZoneInfo TaipeiZone = ResolveTaipeiZone();

    public async Task<AnnualLeaveBalanceDto> GetMyBalanceAsync(
        DateOnly? asOf = null,
        CancellationToken cancellationToken = default)
    {
        EnsureSelfService();
        var employeeId = currentUser.EmployeeId ??
            throw new ForbiddenAccessException("帳號尚未綁定員工資料，請洽系統管理員。");
        var date = asOf ?? TaipeiToday();
        await InitializeEmployeeAsync(employeeId, date, cancellationToken);
        return await BuildBalanceAsync(employeeId, date, cancellationToken);
    }

    public async Task<IReadOnlyList<AnnualLeaveEmployeeSummaryDto>> GetAdminSummariesAsync(
        DateOnly? asOf = null,
        CancellationToken cancellationToken = default)
    {
        EnsureAdminReader();
        var date = asOf ?? TaipeiToday();
        var employees = await db.Employees.AsNoTracking()
            .OrderBy(x => x.EmployeeNumber)
            .Select(x => new { x.Id, x.EmployeeNumber, x.ChineseName, DepartmentName = x.Department.Name, x.HireDate })
            .ToListAsync(cancellationToken);
        var entitlements = await db.AnnualLeaveEntitlements.AsNoTracking()
            .Where(x => x.PeriodStart <= date)
            .ToListAsync(cancellationToken);
        return employees.Select(employee =>
        {
            var rows = entitlements.Where(x => x.EmployeeId == employee.Id).ToList();
            var current = rows.FirstOrDefault(x => x.PeriodStart <= date && date <= x.PeriodEnd);
            return new AnnualLeaveEmployeeSummaryDto(
                employee.Id, employee.EmployeeNumber, employee.ChineseName, employee.DepartmentName,
                employee.HireDate, current?.PeriodStart, current?.PeriodEnd, current?.Status.ToString(),
                rows.Sum(x => x.GrantedMinutes + x.CarriedInMinutes),
                rows.Sum(x => x.ReservedMinutes), rows.Sum(x => x.ConsumedMinutes),
                rows.Sum(x => x.AvailableMinutes));
        }).ToList();
    }

    public async Task<AnnualLeaveBalanceDto> GetAdminBalanceAsync(
        Guid employeeId,
        DateOnly? asOf = null,
        CancellationToken cancellationToken = default)
    {
        EnsureAdminReader();
        return await BuildBalanceAsync(employeeId, asOf ?? TaipeiToday(), cancellationToken);
    }

    public async Task<AnnualLeaveBackfillPreviewDto> PreviewBackfillAsync(
        DateOnly asOf,
        CancellationToken cancellationToken = default)
    {
        EnsureAdminReader();
        var employees = await db.Employees.AsNoTracking()
            .OrderBy(x => x.EmployeeNumber)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);
        var items = new List<AnnualLeaveBackfillPreviewItemDto>();
        foreach (var employeeId in employees)
        {
            var plan = await BuildHistoricalBackfillPlanAsync(employeeId, asOf, cancellationToken);
            if (plan.MissingGrants.Count > 0 || plan.Requests.Count > 0)
            {
                items.Add(new AnnualLeaveBackfillPreviewItemDto(
                    plan.EmployeeId, plan.EmployeeNumber, plan.HireDate,
                    plan.ExpectedGrants.LastOrDefault() is { } current
                        ? current.Milestone == AnnualLeaveMilestone.HalfYear
                            ? "HalfYear"
                            : $"Year{current.CompletedServiceYears}"
                        : "NotEligible",
                    plan.MissingGrants.Count, plan.Requests.Count,
                    plan.Requests.Count(x => x.IsSafe),
                    plan.Requests.Count(x => !x.IsSafe),
                    plan.MissingGrants
                        .Select(grant => new AnnualLeaveBackfillGrantDto(
                            grant.Milestone == AnnualLeaveMilestone.HalfYear
                                ? "HalfYear"
                                : $"Year{grant.CompletedServiceYears}",
                            grant.GrantedDate, grant.PeriodStart, grant.PeriodEnd,
                            grant.GrantedDays, grant.GrantedMinutes)).ToList(),
                    plan.Requests.GroupBy(x => x.Status.ToString())
                        .ToDictionary(group => group.Key, group => group.Count())));
            }
        }

        return new AnnualLeaveBackfillPreviewDto(
            asOf, employees.Count, items.Sum(x => x.MissingEntitlementCount),
            items.Sum(x => x.HistoricalAnnualRequestCount),
            items.Sum(x => x.ManualReviewRequestCount), items);
    }

    public async Task<AnnualLeaveBackfillApplyResultDto> ApplyBackfillForEmployeeAsync(
        Guid employeeId,
        DateOnly asOf,
        CancellationToken cancellationToken = default)
    {
        EnsureAdminReader();
        try
        {
            return await db.ExecuteSerializableAsync(async transactionToken =>
            {
                var plan = await BuildHistoricalBackfillPlanAsync(employeeId, asOf, transactionToken);
                var manual = plan.Requests.Where(x => !x.IsSafe).ToList();
                if (manual.Count > 0)
                    throw new ApplicationValidationException(
                        $"Historical annual leave backfill requires manual review for {manual.Count} request(s). Apply was not started.");

                if (plan.MissingGrants.Count == 0 && plan.Requests.Count == 0)
                    return new AnnualLeaveBackfillApplyResultDto(employeeId, 0, 0, 0);

                var now = timeProvider.GetUtcNow();
                var entitlements = await db.AnnualLeaveEntitlements
                    .Where(x => x.EmployeeId == employeeId)
                    .ToListAsync(transactionToken);
                foreach (var grant in plan.MissingGrants)
                {
                    var entitlement = new AnnualLeaveEntitlement(
                        Guid.NewGuid(), employeeId, grant.Milestone, grant.CompletedServiceYears,
                        grant.GrantedDate, grant.PeriodStart, grant.PeriodEnd,
                        grant.GrantedDays, grant.GrantedMinutes, now);
                    db.AnnualLeaveEntitlements.Add(entitlement);
                    entitlements.Add(entitlement);
                }

                if (plan.MissingGrants.Count > 0)
                    AddAudit(AuditActions.AnnualLeaveEntitlementsInitialized,
                        nameof(AnnualLeaveEntitlement), employeeId,
                        new
                        {
                            EmployeeId = employeeId,
                            ThroughDate = plan.EffectiveAsOf,
                            CreatedCount = plan.MissingGrants.Count
                        });

                var allocationCount = 0;
                foreach (var request in plan.Requests)
                {
                    foreach (var slice in request.Allocations)
                    {
                        var entitlement = entitlements.Single(x =>
                            x.Milestone == slice.Milestone &&
                            x.CompletedServiceYears == slice.CompletedServiceYears);
                        entitlement.Reserve(slice.Minutes, now);
                        var allocation = new AnnualLeaveAllocation(
                            Guid.NewGuid(), request.LeaveRequestId, entitlement.Id, slice.Minutes, now);
                        db.AnnualLeaveAllocations.Add(allocation);
                        ApplyHistoricalAllocationStatus(request.Status, allocation, entitlement, now);
                        allocationCount++;
                    }

                    AddAudit(AuditActions.AnnualLeaveReserved, nameof(LeaveRequest), request.LeaveRequestId,
                        new { LeaveRequestId = request.LeaveRequestId, ReservedMinutes = request.RequiredMinutes });
                    switch (request.Status)
                    {
                        case LeaveRequestStatus.Submitted:
                            break;
                        case LeaveRequestStatus.Approved:
                        case LeaveRequestStatus.CancellationRequested:
                            AddAudit(AuditActions.AnnualLeaveConsumed, nameof(LeaveRequest), request.LeaveRequestId,
                                new { LeaveRequestId = request.LeaveRequestId, Minutes = request.RequiredMinutes });
                            break;
                        case LeaveRequestStatus.Rejected:
                        case LeaveRequestStatus.Withdrawn:
                            AddAudit(AuditActions.AnnualLeaveReservationReleased,
                                nameof(LeaveRequest), request.LeaveRequestId,
                                new { LeaveRequestId = request.LeaveRequestId, Minutes = request.RequiredMinutes });
                            break;
                        case LeaveRequestStatus.Cancelled:
                            AddAudit(AuditActions.AnnualLeaveConsumed, nameof(LeaveRequest), request.LeaveRequestId,
                                new { LeaveRequestId = request.LeaveRequestId, Minutes = request.RequiredMinutes });
                            AddAudit(AuditActions.AnnualLeaveRestored, nameof(LeaveRequest), request.LeaveRequestId,
                                new { LeaveRequestId = request.LeaveRequestId, Minutes = request.RequiredMinutes });
                            break;
                        default:
                            throw new InvalidOperationException("The validated historical status is unsupported.");
                    }
                }

                AddAudit(AuditActions.AnnualLeaveBackfillApplied, nameof(AnnualLeaveEntitlement), employeeId,
                    new { EmployeeId = employeeId, AsOf = asOf, RequestsAllocated = plan.Requests.Count });
                await db.SaveChangesAsync(transactionToken);
                return new AnnualLeaveBackfillApplyResultDto(
                    employeeId, plan.MissingGrants.Count, plan.Requests.Count, allocationCount);
            }, cancellationToken);
        }
        catch
        {
            db.ClearTrackedChanges();
            throw;
        }
    }

    public async Task ApplyCarryForwardAsync(
        Guid sourceEntitlementId,
        Guid targetEntitlementId,
        int minutes,
        CancellationToken cancellationToken = default)
    {
        EnsureAdminReader();
        await db.ExecuteSerializableAsync(async transactionToken =>
        {
            var source = await db.AnnualLeaveEntitlements.SingleOrDefaultAsync(
                x => x.Id == sourceEntitlementId, transactionToken)
                ?? throw new EntityNotFoundException("找不到遞延來源特休額度。");
            var target = await db.AnnualLeaveEntitlements.SingleOrDefaultAsync(
                x => x.Id == targetEntitlementId, transactionToken)
                ?? throw new EntityNotFoundException("找不到遞延目的特休額度。");
            if (source.EmployeeId != target.EmployeeId ||
                target.PeriodStart != source.PeriodEnd.AddDays(1))
                throw new ApplicationValidationException("特休只可遞延至同一員工的次一週年期間。");
            if (await db.AnnualLeaveCarryForwards.AnyAsync(x =>
                x.SourceEntitlementId == source.Id && x.TargetEntitlementId == target.Id,
                transactionToken))
                throw new ApplicationValidationException("此額度已建立遞延紀錄，請勿重複操作。");

            var now = timeProvider.GetUtcNow();
            source.CarryForwardOut(minutes, now);
            target.AddCarriedIn(minutes, now);
            db.AnnualLeaveCarryForwards.Add(new AnnualLeaveCarryForward(
                Guid.NewGuid(), source.Id, target.Id, minutes, target.PeriodEnd,
                currentUser.UserId ?? throw new ForbiddenAccessException("無法識別目前登入帳號。"), now));
            AddAudit(AuditActions.AnnualLeaveCarryForwardApplied, nameof(AnnualLeaveEntitlement), source.Id,
                new { SourceEntitlementId = source.Id, TargetEntitlementId = target.Id, Minutes = minutes });
            await db.SaveChangesAsync(transactionToken);
        }, cancellationToken);
    }

    public async Task SettleEntitlementAsync(
        Guid entitlementId,
        int minutes,
        AnnualLeaveEntitlementStatus settlementStatus,
        CancellationToken cancellationToken = default)
    {
        EnsureAdminReader();
        await db.ExecuteSerializableAsync(async transactionToken =>
        {
            var entitlement = await db.AnnualLeaveEntitlements.SingleOrDefaultAsync(
                x => x.Id == entitlementId, transactionToken)
                ?? throw new EntityNotFoundException("找不到指定的特休額度。");
            entitlement.Settle(minutes, settlementStatus, timeProvider.GetUtcNow());
            AddAudit(AuditActions.AnnualLeaveEntitlementSettled,
                nameof(AnnualLeaveEntitlement), entitlement.Id,
                new { EntitlementId = entitlement.Id, Minutes = minutes, Status = settlementStatus });
            await db.SaveChangesAsync(transactionToken);
        }, cancellationToken);
    }

    public Task InitializeEmployeeAsync(
        Guid employeeId,
        DateOnly throughDate,
        CancellationToken cancellationToken = default) =>
        db.ExecuteSerializableAsync(async transactionToken =>
        {
            var employee = await db.Employees.SingleOrDefaultAsync(x => x.Id == employeeId, transactionToken)
                ?? throw new EntityNotFoundException("找不到指定的員工。");
            var effectiveThrough = employee.TerminationDate is { } end && end < throughDate ? end : throughDate;
            var grants = AnnualLeavePolicy.GetGrants(employee.HireDate, effectiveThrough);
            var existing = await db.AnnualLeaveEntitlements
                .Where(x => x.EmployeeId == employeeId).ToListAsync(transactionToken);
            var now = timeProvider.GetUtcNow();
            var created = 0;
            foreach (var grant in grants)
            {
                if (existing.Any(x => x.Milestone == grant.Milestone &&
                    x.CompletedServiceYears == grant.CompletedServiceYears)) continue;
                var entity = new AnnualLeaveEntitlement(
                    Guid.NewGuid(), employeeId, grant.Milestone, grant.CompletedServiceYears,
                    grant.GrantedDate, grant.PeriodStart, grant.PeriodEnd,
                    grant.GrantedDays, grant.GrantedMinutes, now);
                if (grant.PeriodEnd < throughDate) entity.MarkExpiredPendingSettlement(now);
                db.AnnualLeaveEntitlements.Add(entity);
                created++;
            }

            if (created > 0)
            {
                AddAudit(AuditActions.AnnualLeaveEntitlementsInitialized, nameof(AnnualLeaveEntitlement), employeeId,
                    new { EmployeeId = employeeId, ThroughDate = throughDate, CreatedCount = created });
                await db.SaveChangesAsync(transactionToken);
            }
        }, cancellationToken);

    public async Task ReserveForSubmissionAsync(LeaveRequest request, CancellationToken cancellationToken)
    {
        if (!await IsAnnualAsync(request.LeaveTypeId, cancellationToken)) return;
        var endDate = LocalDate(request.EndAt.AddTicks(-1));
        await EnsureEntitlementsInCurrentTransactionAsync(request.EmployeeId, endDate, cancellationToken);
        var requiredMinutes = ToMinutes(request.DurationHours);
        if (await db.AnnualLeaveAllocations.AnyAsync(x => x.LeaveRequestId == request.Id, cancellationToken))
            throw new ApplicationValidationException("此請假已存在特休額度配置，請勿重複送出。");

        var startDate = LocalDate(request.StartAt);
        var candidates = await db.AnnualLeaveEntitlements
            .Where(x => x.EmployeeId == request.EmployeeId && x.PeriodEnd >= startDate && x.PeriodStart <= endDate)
            .OrderBy(x => x.PeriodEnd).ThenBy(x => x.GrantedDate)
            .ToListAsync(cancellationToken);
        var totalAvailable = candidates.Sum(x => x.AvailableMinutes);
        if (totalAvailable < requiredMinutes)
            throw new ApplicationValidationException(
                $"特別休假可用餘額不足，目前可用 {totalAvailable / 60m:0.##} 小時，本次申請需要 {requiredMinutes / 60m:0.##} 小時。");
        var remaining = requiredMinutes;
        var now = timeProvider.GetUtcNow();
        foreach (var entitlement in candidates)
        {
            if (remaining == 0) break;
            var sliceStart = Max(request.StartAt, ToUtc(entitlement.PeriodStart));
            var sliceEnd = Min(request.EndAt, ToUtc(entitlement.PeriodEnd.AddDays(1)));
            if (sliceStart >= sliceEnd) continue;
            var slice = await durationCalculator.CalculateAsync(
                request.EmployeeId, sliceStart, sliceEnd, cancellationToken);
            var sliceMinutes = Math.Min(remaining, ToMinutes(slice.DurationHours));
            var amount = Math.Min(sliceMinutes, entitlement.AvailableMinutes);
            if (amount <= 0) continue;
            entitlement.Reserve(amount, now);
            db.AnnualLeaveAllocations.Add(new AnnualLeaveAllocation(
                Guid.NewGuid(), request.Id, entitlement.Id, amount, now));
            remaining -= amount;
        }

        if (remaining != 0)
            throw new ApplicationValidationException(
                $"特別休假可用餘額不足，目前可用 {(requiredMinutes - remaining) / 60m:0.##} 小時，本次申請需要 {requiredMinutes / 60m:0.##} 小時。");
        AddAudit(AuditActions.AnnualLeaveReserved, nameof(LeaveRequest), request.Id,
            new { LeaveRequestId = request.Id, ReservedMinutes = requiredMinutes });
    }

    public Task ConsumeForApprovalAsync(Guid leaveRequestId, CancellationToken cancellationToken) =>
        TransitionAsync(leaveRequestId, AnnualLeaveAllocationStatus.Reserved,
            (allocation, entitlement, now) => { entitlement.Consume(allocation.AllocatedMinutes, now); allocation.Consume(now); },
            AuditActions.AnnualLeaveConsumed, cancellationToken);

    public Task ReleaseReservationAsync(Guid leaveRequestId, CancellationToken cancellationToken) =>
        TransitionAsync(leaveRequestId, AnnualLeaveAllocationStatus.Reserved,
            (allocation, entitlement, now) => { entitlement.Release(allocation.AllocatedMinutes, now); allocation.Release(now); },
            AuditActions.AnnualLeaveReservationReleased, cancellationToken);

    public Task RestoreAfterCancellationAsync(Guid leaveRequestId, CancellationToken cancellationToken) =>
        TransitionAsync(leaveRequestId, AnnualLeaveAllocationStatus.Consumed,
            (allocation, entitlement, now) => { entitlement.Restore(allocation.AllocatedMinutes, now); allocation.Restore(now); },
            AuditActions.AnnualLeaveRestored, cancellationToken);

    private async Task TransitionAsync(
        Guid leaveRequestId,
        AnnualLeaveAllocationStatus expected,
        Action<AnnualLeaveAllocation, AnnualLeaveEntitlement, DateTimeOffset> apply,
        string auditAction,
        CancellationToken cancellationToken)
    {
        var allocations = await db.AnnualLeaveAllocations
            .Include(x => x.Entitlement)
            .Where(x => x.LeaveRequestId == leaveRequestId && x.Status == expected)
            .OrderBy(x => x.Entitlement.PeriodEnd).ToListAsync(cancellationToken);
        if (allocations.Count == 0)
        {
            var isAnnual = await db.LeaveRequests.AsNoTracking()
                .Where(x => x.Id == leaveRequestId)
                .Join(db.LeaveTypes.AsNoTracking(), x => x.LeaveTypeId, x => x.Id, (_, type) => type.Code)
                .SingleOrDefaultAsync(cancellationToken) == AnnualLeavePolicy.LeaveTypeCode;
            if (isAnnual) throw new ApplicationValidationException("此特休申請尚未建立額度配置，請先完成歷史資料初始化。");
            return;
        }

        var now = timeProvider.GetUtcNow();
        foreach (var allocation in allocations) apply(allocation, allocation.Entitlement, now);
        AddAudit(auditAction, nameof(LeaveRequest), leaveRequestId,
            new { LeaveRequestId = leaveRequestId, Minutes = allocations.Sum(x => x.AllocatedMinutes) });
    }

    private async Task<HistoricalBackfillPlan> BuildHistoricalBackfillPlanAsync(
        Guid employeeId,
        DateOnly asOf,
        CancellationToken cancellationToken)
    {
        var employee = await db.Employees.AsNoTracking()
            .Where(x => x.Id == employeeId)
            .Select(x => new
            {
                x.Id,
                x.EmployeeNumber,
                x.HireDate,
                x.TerminationDate
            })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new EntityNotFoundException("找不到指定員工。");
        var annualTypeId = await db.LeaveTypes.AsNoTracking()
            .Where(x => x.Code == AnnualLeavePolicy.LeaveTypeCode)
            .Select(x => (Guid?)x.Id)
            .SingleOrDefaultAsync(cancellationToken);
        if (!annualTypeId.HasValue)
            throw new ApplicationValidationException("找不到 ANNUAL 特休假別，無法執行歷史回填規劃。");

        var effectiveAsOf = employee.TerminationDate is { } terminationDate && terminationDate < asOf
            ? terminationDate
            : asOf;
        var expectedGrants = AnnualLeavePolicy.GetGrants(employee.HireDate, effectiveAsOf);
        var existing = await db.AnnualLeaveEntitlements.AsNoTracking()
            .Where(x => x.EmployeeId == employeeId)
            .ToListAsync(cancellationToken);
        var missingGrants = expectedGrants.Where(grant => !existing.Any(x =>
                x.Milestone == grant.Milestone &&
                x.CompletedServiceYears == grant.CompletedServiceYears))
            .ToList();
        var requests = await db.LeaveRequests.AsNoTracking()
            .Where(x => x.EmployeeId == employeeId &&
                x.LeaveTypeId == annualTypeId.Value &&
                x.Status != LeaveRequestStatus.Draft &&
                !db.AnnualLeaveAllocations.Any(a => a.LeaveRequestId == x.Id))
            .OrderBy(x => x.StartAt)
            .ThenBy(x => x.CreatedAtUtc)
            .ThenBy(x => x.Id)
            .ToListAsync(cancellationToken);

        var capacities = expectedGrants.ToDictionary(
            grant => new HistoricalEntitlementKey(grant.Milestone, grant.CompletedServiceYears),
            grant =>
            {
                var row = existing.SingleOrDefault(x =>
                    x.Milestone == grant.Milestone &&
                    x.CompletedServiceYears == grant.CompletedServiceYears);
                return new HistoricalEntitlementCapacity(
                    grant,
                    row?.AvailableMinutes ?? grant.GrantedMinutes,
                    row is null || row.Status is AnnualLeaveEntitlementStatus.Open or
                        AnnualLeaveEntitlementStatus.ExpiredPendingSettlement);
            });
        var available = capacities.ToDictionary(x => x.Key, x => x.Value.AvailableMinutes);
        var requestPlans = new List<HistoricalRequestPlan>();
        foreach (var request in requests)
        {
            if (!TryGetHistoricalMinutes(request.DurationHours, out var requiredMinutes))
            {
                requestPlans.Add(HistoricalRequestPlan.Manual(
                    request.Id, request.Status, "Stored historical duration is zero or cannot be represented as whole minutes."));
                continue;
            }

            if (!IsSupportedHistoricalStatus(request.Status))
            {
                requestPlans.Add(HistoricalRequestPlan.Manual(
                    request.Id, request.Status, "Historical request status is not supported."));
                continue;
            }

            var startDate = LocalDate(request.StartAt);
            var endDate = LocalDate(request.EndAt.AddTicks(-1));
            var candidates = capacities.Values
                .Where(x => x.Grant.PeriodEnd >= startDate && x.Grant.PeriodStart <= endDate)
                .OrderBy(x => x.Grant.PeriodEnd)
                .ThenBy(x => x.Grant.GrantedDate)
                .ToList();
            if (!IsCoveredByGrantPeriods(startDate, endDate, candidates.Select(x => x.Grant)))
            {
                requestPlans.Add(HistoricalRequestPlan.Manual(
                    request.Id, request.Status, "Request dates are not fully covered by generated entitlement periods."));
                continue;
            }

            var working = available.ToDictionary(x => x.Key, x => x.Value);
            var slices = new List<HistoricalAllocationPlan>();
            var remaining = requiredMinutes;
            foreach (var candidate in candidates)
            {
                if (remaining == 0) break;
                var key = new HistoricalEntitlementKey(
                    candidate.Grant.Milestone, candidate.Grant.CompletedServiceYears);
                if (!candidate.CanAllocate || working[key] <= 0) continue;
                var minutes = Math.Min(remaining, working[key]);
                slices.Add(new HistoricalAllocationPlan(
                    candidate.Grant.Milestone, candidate.Grant.CompletedServiceYears, minutes));
                working[key] -= minutes;
                remaining -= minutes;
            }

            if (remaining != 0)
            {
                requestPlans.Add(HistoricalRequestPlan.Manual(
                    request.Id, request.Status, "Historical entitlement capacity is insufficient."));
                continue;
            }

            if (request.Status is LeaveRequestStatus.Submitted or LeaveRequestStatus.Approved or
                LeaveRequestStatus.CancellationRequested)
                available = working;
            requestPlans.Add(HistoricalRequestPlan.Safe(
                request.Id, request.Status, requiredMinutes, slices));
        }

        return new HistoricalBackfillPlan(
            employee.Id,
            employee.EmployeeNumber,
            employee.HireDate,
            effectiveAsOf,
            expectedGrants,
            missingGrants,
            requestPlans);
    }

    private static void ApplyHistoricalAllocationStatus(
        LeaveRequestStatus requestStatus,
        AnnualLeaveAllocation allocation,
        AnnualLeaveEntitlement entitlement,
        DateTimeOffset now)
    {
        switch (requestStatus)
        {
            case LeaveRequestStatus.Submitted:
                return;
            case LeaveRequestStatus.Approved:
            case LeaveRequestStatus.CancellationRequested:
                entitlement.Consume(allocation.AllocatedMinutes, now);
                allocation.Consume(now);
                return;
            case LeaveRequestStatus.Rejected:
            case LeaveRequestStatus.Withdrawn:
                entitlement.Release(allocation.AllocatedMinutes, now);
                allocation.Release(now);
                return;
            case LeaveRequestStatus.Cancelled:
                entitlement.Consume(allocation.AllocatedMinutes, now);
                allocation.Consume(now);
                entitlement.Restore(allocation.AllocatedMinutes, now);
                allocation.Restore(now);
                return;
            default:
                throw new InvalidOperationException("The historical request status is unsupported.");
        }
    }

    private static bool TryGetHistoricalMinutes(decimal durationHours, out int minutes)
    {
        var rawMinutes = durationHours * 60m;
        if (rawMinutes <= 0 || rawMinutes > int.MaxValue || rawMinutes != decimal.Truncate(rawMinutes))
        {
            minutes = 0;
            return false;
        }

        minutes = decimal.ToInt32(rawMinutes);
        return true;
    }

    private static bool IsSupportedHistoricalStatus(LeaveRequestStatus status) => status is
        LeaveRequestStatus.Submitted or
        LeaveRequestStatus.Approved or
        LeaveRequestStatus.Rejected or
        LeaveRequestStatus.Withdrawn or
        LeaveRequestStatus.CancellationRequested or
        LeaveRequestStatus.Cancelled;

    private static bool IsCoveredByGrantPeriods(
        DateOnly startDate,
        DateOnly endDate,
        IEnumerable<AnnualLeaveGrantDefinition> grants)
    {
        var next = startDate;
        foreach (var grant in grants.OrderBy(x => x.PeriodStart))
        {
            if (grant.PeriodStart > next) return false;
            if (grant.PeriodEnd < next) continue;
            if (grant.PeriodEnd >= endDate) return true;
            next = grant.PeriodEnd.AddDays(1);
        }

        return false;
    }

    private async Task EnsureEntitlementsInCurrentTransactionAsync(
        Guid employeeId, DateOnly throughDate, CancellationToken cancellationToken)
    {
        var employee = await db.Employees.SingleAsync(x => x.Id == employeeId, cancellationToken);
        var grants = AnnualLeavePolicy.GetGrants(employee.HireDate, throughDate);
        var existing = await db.AnnualLeaveEntitlements.Where(x => x.EmployeeId == employeeId)
            .ToListAsync(cancellationToken);
        var now = timeProvider.GetUtcNow();
        var created = 0;
        foreach (var grant in grants)
        {
            if (existing.Any(x => x.Milestone == grant.Milestone && x.CompletedServiceYears == grant.CompletedServiceYears)) continue;
            db.AnnualLeaveEntitlements.Add(new AnnualLeaveEntitlement(
                Guid.NewGuid(), employeeId, grant.Milestone, grant.CompletedServiceYears,
                grant.GrantedDate, grant.PeriodStart, grant.PeriodEnd,
                grant.GrantedDays, grant.GrantedMinutes, now));
            created++;
        }
        if (created > 0)
        {
            AddAudit(AuditActions.AnnualLeaveEntitlementsInitialized, nameof(AnnualLeaveEntitlement), employeeId,
                new { EmployeeId = employeeId, ThroughDate = throughDate, CreatedCount = created });
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    private readonly record struct HistoricalEntitlementKey(
        AnnualLeaveMilestone Milestone,
        int CompletedServiceYears);

    private sealed record HistoricalEntitlementCapacity(
        AnnualLeaveGrantDefinition Grant,
        int AvailableMinutes,
        bool CanAllocate);

    private sealed record HistoricalAllocationPlan(
        AnnualLeaveMilestone Milestone,
        int CompletedServiceYears,
        int Minutes);

    private sealed record HistoricalRequestPlan(
        Guid LeaveRequestId,
        LeaveRequestStatus Status,
        int RequiredMinutes,
        IReadOnlyList<HistoricalAllocationPlan> Allocations,
        string? ManualReviewReason)
    {
        public bool IsSafe => ManualReviewReason is null;

        public static HistoricalRequestPlan Safe(
            Guid leaveRequestId,
            LeaveRequestStatus status,
            int requiredMinutes,
            IReadOnlyList<HistoricalAllocationPlan> allocations) =>
            new(leaveRequestId, status, requiredMinutes, allocations, null);

        public static HistoricalRequestPlan Manual(
            Guid leaveRequestId,
            LeaveRequestStatus status,
            string reason) =>
            new(leaveRequestId, status, 0, [], reason);
    }

    private sealed record HistoricalBackfillPlan(
        Guid EmployeeId,
        string EmployeeNumber,
        DateOnly HireDate,
        DateOnly EffectiveAsOf,
        IReadOnlyList<AnnualLeaveGrantDefinition> ExpectedGrants,
        IReadOnlyList<AnnualLeaveGrantDefinition> MissingGrants,
        IReadOnlyList<HistoricalRequestPlan> Requests);

    private async Task<AnnualLeaveBalanceDto> BuildBalanceAsync(Guid employeeId, DateOnly asOf, CancellationToken token)
    {
        var employee = await db.Employees.AsNoTracking().SingleAsync(x => x.Id == employeeId, token);
        var entitlements = await db.AnnualLeaveEntitlements.AsNoTracking()
            .Where(x => x.EmployeeId == employeeId && x.GrantedDate <= asOf)
            .OrderBy(x => x.PeriodStart).ToListAsync(token);
        var allocations = await db.AnnualLeaveAllocations.AsNoTracking()
            .Where(x => x.Entitlement.EmployeeId == employeeId)
            .Include(x => x.LeaveRequest).OrderByDescending(x => x.CreatedAtUtc).ToListAsync(token);
        return new AnnualLeaveBalanceDto(
            employee.Id, employee.EmployeeNumber, employee.ChineseName, employee.HireDate,
            CompletedMonths(employee.HireDate, asOf) / 12,
            CompletedMonths(employee.HireDate, asOf) % 12,
            AnnualLeavePolicy.GetGrants(employee.HireDate, asOf.AddYears(2))
                .Where(x => x.GrantedDate > asOf).OrderBy(x => x.GrantedDate)
                .Select(x => (DateOnly?)x.GrantedDate).FirstOrDefault(),
            entitlements.Sum(x => x.GrantedMinutes + x.CarriedInMinutes),
            entitlements.Sum(x => x.ReservedMinutes), entitlements.Sum(x => x.ConsumedMinutes),
            entitlements.Sum(x => x.AvailableMinutes),
            entitlements.Select(Map).ToList(),
            allocations.Select(x => new AnnualLeaveAllocationDto(
                x.LeaveRequestId, x.LeaveRequest.RequestNumber, x.AllocatedMinutes, x.Status.ToString())).ToList());
    }

    private async Task<bool> IsAnnualAsync(Guid leaveTypeId, CancellationToken token) =>
        await db.LeaveTypes.AsNoTracking().AnyAsync(x => x.Id == leaveTypeId && x.Code == AnnualLeavePolicy.LeaveTypeCode, token);

    private void EnsureSelfService()
    {
        if (!currentUser.IsAuthenticated || !currentUser.HasPermission(PolicyNames.LeaveRequestSelfService))
            throw new ForbiddenAccessException("請先登入。");
    }

    private void EnsureAdminReader()
    {
        if (!currentUser.IsAuthenticated || !currentUser.HasPermission(PolicyNames.LeaveRequestReadAll))
            throw new ForbiddenAccessException("您沒有檢視特休額度的權限。");
    }

    private void AddAudit(string action, string entityType, Guid entityId, object values) =>
        db.AuditLogs.Add(AuditLogFactory.Create(currentUser, timeProvider, action, entityType, entityId.ToString(), null, values));

    private DateOnly TaipeiToday() => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), TaipeiZone).DateTime);
    private static DateOnly LocalDate(DateTimeOffset utc) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(utc, TaipeiZone).DateTime);
    private static DateTimeOffset ToUtc(DateOnly localDate)
    {
        var local = DateTime.SpecifyKind(localDate.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified);
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, TaipeiZone), TimeSpan.Zero);
    }
    private static DateTimeOffset Max(DateTimeOffset a, DateTimeOffset b) => a > b ? a : b;
    private static DateTimeOffset Min(DateTimeOffset a, DateTimeOffset b) => a < b ? a : b;
    private static int ToMinutes(decimal hours) => checked((int)Math.Round(hours * 60m, MidpointRounding.AwayFromZero));
    private static int CompletedMonths(DateOnly hireDate, DateOnly asOf)
    {
        if (asOf < hireDate) return 0;
        var months = (asOf.Year - hireDate.Year) * 12 + asOf.Month - hireDate.Month;
        if (AnnualLeavePolicy.AddMonthsClamped(hireDate, months) > asOf) months--;
        return Math.Max(0, months);
    }
    private static AnnualLeaveEntitlementDto Map(AnnualLeaveEntitlement x) => new(
        x.Id, x.GrantedDate, x.PeriodStart, x.PeriodEnd, x.GrantedDays, x.GrantedMinutes,
        x.CarriedInMinutes, x.ReservedMinutes, x.ConsumedMinutes, x.SettledMinutes,
        x.AvailableMinutes, x.Status.ToString());
    private static TimeZoneInfo ResolveTaipeiZone()
    {
        foreach (var id in new[] { "Asia/Taipei", "Taipei Standard Time" })
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); } catch (TimeZoneNotFoundException) { }
        throw new InvalidOperationException("找不到 Asia/Taipei 時區。");
    }
}
