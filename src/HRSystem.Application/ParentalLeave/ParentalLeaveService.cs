using HRSystem.Application.Abstractions.Persistence;
using HRSystem.Application.Abstractions.Security;
using HRSystem.Application.Attendance;
using HRSystem.Application.Common.Auditing;
using HRSystem.Application.Common.Exceptions;
using HRSystem.Application.Common.Models;
using HRSystem.Application.Common.Validation;
using HRSystem.Application.Security;
using HRSystem.Domain.ParentalLeave;
using Microsoft.EntityFrameworkCore;

namespace HRSystem.Application.ParentalLeave;

public sealed class ParentalLeaveService(
    IApplicationDbContext dbContext,
    ICurrentUser currentUser,
    IAttendanceRecalculationEngine attendanceRecalculationEngine,
    TimeProvider timeProvider) : IParentalLeaveService
{
    private static readonly TimeZoneInfo TaipeiZone = ResolveTaipeiZone();

    public async Task<IReadOnlyList<ParentalLeaveChildOptionDto>>
        GetChildOptionsAsync(CancellationToken cancellationToken = default)
    {
        var employeeId = RequireEmployeeId();
        var requests = await dbContext.ParentalLeaveRequests
            .AsNoTracking()
            .Where(item => item.EmployeeId == employeeId)
            .OrderBy(item => item.ChildBirthDate)
            .ThenBy(item => item.CreatedAtUtc)
            .ToListAsync(cancellationToken);
        return requests
            .GroupBy(item => item.ChildReferenceId)
            .Select(group =>
            {
                var latest = group.OrderByDescending(item => item.CreatedAtUtc).First();
                var counted = group.Where(item =>
                    ParentalLeavePolicy.CountsTowardUsage(item.Status)).ToArray();
                return new ParentalLeaveChildOptionDto(
                    group.Key,
                    latest.ChildBirthDate,
                    latest.ChildDisplayName,
                    counted.Where(item => item.ApplicationType ==
                            ParentalLeaveApplicationType.DailyUnder30Days)
                        .Sum(item => item.CalendarDayCount),
                    counted.Count(item => item.ApplicationType ==
                        ParentalLeaveApplicationType.ShortTerm30DaysOrMore),
                    counted.Sum(item => item.CalendarDayCount));
            })
            .ToArray();
    }

    public async Task<ParentalLeaveEstimateDto> EstimateAsync(
        CreateParentalLeaveDraftRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureAuthenticated();
        RequestValidator.Validate(request);
        var type = ParentalLeavePolicy.Classify(request.StartDate, request.EndDate);
        var days = ParentalLeavePolicy.CalendarDays(request.StartDate, request.EndDate);
        var usage = request.ChildReferenceId.HasValue
            ? await GetUsageAsync(
                RequireEmployeeId(),
                request.ChildReferenceId.Value,
                null,
                cancellationToken)
            : Usage.Empty;
        var today = Today();
        var actualNotice = request.StartDate.DayNumber - today.DayNumber;
        var requiredNotice = ParentalLeavePolicy.RequiredNoticeDays(
            type,
            request.NoticeType);
        var messages = new List<string>();
        if (actualNotice < requiredNotice)
        {
            messages.Add($"提前申請天數不足：至少需要 {requiredNotice} 日。");
        }

        if (type == ParentalLeaveApplicationType.DailyUnder30Days &&
            usage.DailyDays + days > ParentalLeavePolicy.MaximumDailyApplicationDays)
        {
            messages.Add("按日申請累計不得超過 30 日。");
        }

        if (type == ParentalLeaveApplicationType.ShortTerm30DaysOrMore &&
            usage.ShortTermCount >= ParentalLeavePolicy.MaximumShortTermApplications)
        {
            messages.Add("30 日以上未滿 6 個月的申請最多 2 次。");
        }

        if (usage.TotalDays + days > ParentalLeavePolicy.MaximumChildLeaveDays)
        {
            messages.Add("同一子女育嬰留職停薪合計不得超過 2 年。");
        }

        return new ParentalLeaveEstimateDto(
            days,
            type,
            requiredNotice,
            actualNotice,
            actualNotice >= requiredNotice,
            usage.DailyDays,
            Math.Max(0, ParentalLeavePolicy.MaximumDailyApplicationDays - usage.DailyDays),
            usage.ShortTermCount,
            Math.Max(0, ParentalLeavePolicy.MaximumShortTermApplications - usage.ShortTermCount),
            usage.TotalDays,
            Math.Max(0, ParentalLeavePolicy.MaximumChildLeaveDays - usage.TotalDays),
            messages);
    }

    public Task<PagedResult<ParentalLeaveRequestDto>> GetMyRequestsAsync(
        ParentalLeaveQuery query,
        CancellationToken cancellationToken = default) =>
        QueryAsync(
            ApplyFilters(BaseQuery().Where(item =>
                item.EmployeeId == RequireEmployeeId()), query),
            query,
            cancellationToken);

    public async Task<PagedResult<ParentalLeaveRequestDto>>
        GetPendingApprovalsAsync(
            ParentalLeaveQuery query,
            CancellationToken cancellationToken = default)
    {
        EnsureApprover();
        var requests = BaseQuery().Where(item =>
            item.Status == ParentalLeaveStatus.Submitted ||
            item.Status == ParentalLeaveStatus.CancellationRequested ||
            item.Status == ParentalLeaveStatus.Approved &&
                item.EarlyReturnRequestedAtUtc.HasValue &&
                !item.EarlyReturnApprovedAtUtc.HasValue);
        requests = await ApplyReviewerScopeAsync(requests, cancellationToken);
        if (currentUser.EmployeeId.HasValue)
        {
            requests = requests.Where(item =>
                item.EmployeeId != currentUser.EmployeeId.Value);
        }

        return await QueryAsync(
            ApplyFilters(requests, query),
            query,
            cancellationToken);
    }

    public async Task<ParentalLeaveRequestDto> GetDetailAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        EnsureAuthenticated();
        var entity = await DetailQuery().SingleOrDefaultAsync(
            item => item.Id == id,
            cancellationToken)
            ?? throw new EntityNotFoundException("找不到指定的育嬰留停申請。");
        if (entity.EmployeeId != currentUser.EmployeeId)
        {
            await EnsureCanReviewAsync(entity, false, cancellationToken);
        }

        return Map(entity);
    }

    public async Task<ParentalLeaveRequestDto> CreateDraftAsync(
        CreateParentalLeaveDraftRequest request,
        CancellationToken cancellationToken = default)
    {
        RequestValidator.Validate(request);
        var employeeId = RequireEmployeeId();
        await EnsureEmployeeActiveAsync(employeeId, cancellationToken);
        var childReferenceId = request.ChildReferenceId ?? Guid.NewGuid();
        await EnsureChildReferenceMatchesAsync(
            employeeId,
            childReferenceId,
            request.ChildBirthDate,
            null,
            cancellationToken);
        var now = timeProvider.GetUtcNow();
        var entity = new ParentalLeaveRequest(
            Guid.NewGuid(),
            GenerateRequestNumber(now),
            employeeId,
            childReferenceId,
            request.ChildBirthDate,
            request.StartDate,
            request.EndDate,
            request.ContactAddress,
            request.ContactPhone,
            request.ContinueSocialInsurance,
            request.NoticeType,
            request.Reason,
            request.EmergencyCareReason,
            request.ChildDisplayName,
            RequireUserId(),
            now);
        dbContext.ParentalLeaveRequests.Add(entity);
        AddAudit(AuditActions.ParentalLeaveCreated, entity, null, Snapshot(entity));
        await SaveAsync(cancellationToken);
        return await GetOwnedAfterWriteAsync(entity.Id, cancellationToken);
    }

    public async Task<ParentalLeaveRequestDto> UpdateDraftAsync(
        UpdateParentalLeaveDraftRequest request,
        CancellationToken cancellationToken = default)
    {
        RequestValidator.Validate(request);
        var entity = await GetTrackedOwnedAsync(request.Id, cancellationToken);
        EnsureRowVersion(entity.RowVersion, request.RowVersion);
        await EnsureChildReferenceMatchesAsync(
            entity.EmployeeId,
            entity.ChildReferenceId,
            request.ChildBirthDate,
            entity.Id,
            cancellationToken);
        entity.UpdateDraft(
            request.ChildBirthDate,
            request.StartDate,
            request.EndDate,
            request.ContactAddress,
            request.ContactPhone,
            request.ContinueSocialInsurance,
            request.NoticeType,
            request.Reason,
            request.EmergencyCareReason,
            request.ChildDisplayName,
            RequireUserId(),
            timeProvider.GetUtcNow());
        await SaveAsync(cancellationToken);
        return await GetOwnedAfterWriteAsync(entity.Id, cancellationToken);
    }

    public async Task<ParentalLeaveRequestDto> SubmitAsync(
        ParentalLeaveActionRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await dbContext.ExecuteSerializableAsync(
                async transactionToken =>
                {
                    var entity = await GetTrackedOwnedAsync(
                        request.Id,
                        transactionToken);
                    EnsureRowVersion(entity.RowVersion, request.RowVersion);
                    await ValidateEligibilityAsync(entity, transactionToken);
                    var from = entity.Status;
                    entity.Submit(Today(), RequireUserId(), timeProvider.GetUtcNow());
                    AddHistory(
                        entity,
                        ParentalLeaveApprovalAction.Submitted,
                        from,
                        entity.Status,
                        request.Comment);
                    AddAudit(
                        AuditActions.ParentalLeaveSubmitted,
                        entity,
                        new { Status = from },
                        Snapshot(entity));
                    await SaveAsync(transactionToken);
                    return await GetOwnedAfterWriteAsync(entity.Id, transactionToken);
                },
                cancellationToken);
        }
        catch
        {
            dbContext.ClearTrackedChanges();
            throw;
        }
    }

    public async Task<ParentalLeaveRequestDto> WithdrawAsync(
        ParentalLeaveActionRequest request,
        CancellationToken cancellationToken = default)
    {
        var entity = await GetTrackedOwnedAsync(request.Id, cancellationToken);
        EnsureRowVersion(entity.RowVersion, request.RowVersion);
        var from = entity.Status;
        entity.Withdraw(RequireUserId(), timeProvider.GetUtcNow());
        AddHistory(entity, ParentalLeaveApprovalAction.Withdrawn, from, entity.Status, request.Comment);
        AddAudit(AuditActions.ParentalLeaveWithdrawn, entity, new { Status = from }, Snapshot(entity));
        await SaveAsync(cancellationToken);
        return await GetOwnedAfterWriteAsync(entity.Id, cancellationToken);
    }

    public async Task<ParentalLeaveRequestDto> ApproveAsync(
        ParentalLeaveActionRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await dbContext.ExecuteSerializableAsync(
                async transactionToken =>
                {
                    var entity = await GetTrackedForReviewAsync(request.Id, transactionToken);
                    EnsureRowVersion(entity.RowVersion, request.RowVersion);
                    await ValidateEligibilityAsync(entity, transactionToken);
                    var from = entity.Status;
                    entity.Approve(RequireUserId(), timeProvider.GetUtcNow());
                    AddHistory(entity, ParentalLeaveApprovalAction.Approved, from, entity.Status, request.Comment);
                    AddAudit(AuditActions.ParentalLeaveApproved, entity, new { Status = from }, Snapshot(entity));
                    await attendanceRecalculationEngine
                        .RecalculateRangeWithEmploymentSuspensionsAsync(
                        entity.StartDate,
                        entity.EndDate,
                        entity.EmployeeId,
                        [
                            new PendingEmploymentSuspension(
                                entity.Id,
                                entity.EmployeeId,
                                entity.StartDate,
                                entity.EndDate)
                        ],
                        cancellationToken: transactionToken);
                    await SaveAsync(transactionToken);
                    return await GetAfterReviewAsync(entity.Id, transactionToken);
                },
                cancellationToken);
        }
        catch
        {
            dbContext.ClearTrackedChanges();
            throw;
        }
    }

    public async Task<ParentalLeaveRequestDto> RejectAsync(
        ParentalLeaveReasonActionRequest request,
        CancellationToken cancellationToken = default)
    {
        RequestValidator.Validate(request);
        var entity = await GetTrackedForReviewAsync(request.Id, cancellationToken);
        EnsureRowVersion(entity.RowVersion, request.RowVersion);
        var from = entity.Status;
        entity.Reject(request.Comment!, RequireUserId(), timeProvider.GetUtcNow());
        AddHistory(entity, ParentalLeaveApprovalAction.Rejected, from, entity.Status, request.Comment);
        AddAudit(AuditActions.ParentalLeaveRejected, entity, new { Status = from }, Snapshot(entity));
        await SaveAsync(cancellationToken);
        return await GetAfterReviewAsync(entity.Id, cancellationToken);
    }

    public async Task<ParentalLeaveRequestDto> RequestCancellationAsync(
        ParentalLeaveReasonActionRequest request,
        CancellationToken cancellationToken = default)
    {
        RequestValidator.Validate(request);
        var entity = await GetTrackedOwnedAsync(request.Id, cancellationToken);
        EnsureRowVersion(entity.RowVersion, request.RowVersion);
        var from = entity.Status;
        entity.RequestCancellation(request.Comment!, RequireUserId(), timeProvider.GetUtcNow());
        AddHistory(entity, ParentalLeaveApprovalAction.CancellationRequested, from, entity.Status, request.Comment);
        AddAudit(AuditActions.ParentalLeaveCancellationRequested, entity, new { Status = from }, Snapshot(entity));
        await SaveAsync(cancellationToken);
        return await GetOwnedAfterWriteAsync(entity.Id, cancellationToken);
    }

    public async Task<ParentalLeaveRequestDto> ApproveCancellationAsync(
        ParentalLeaveActionRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await dbContext.ExecuteSerializableAsync(
                async transactionToken =>
                {
                    var entity = await GetTrackedForReviewAsync(request.Id, transactionToken);
                    EnsureRowVersion(entity.RowVersion, request.RowVersion);
                    var from = entity.Status;
                    entity.ApproveCancellation(RequireUserId(), timeProvider.GetUtcNow());
                    AddHistory(entity, ParentalLeaveApprovalAction.CancellationApproved, from, entity.Status, request.Comment);
                    AddAudit(AuditActions.ParentalLeaveCancelled, entity, new { Status = from }, Snapshot(entity));
                    await attendanceRecalculationEngine
                        .RecalculateKeysWithEmploymentSuspensionsAsync(
                        RecalculationKeys(entity.EmployeeId, entity.StartDate, entity.EndDate),
                        excludedParentalLeaveRequestIds: [entity.Id],
                        cancellationToken: transactionToken);
                    await SaveAsync(transactionToken);
                    return await GetAfterReviewAsync(entity.Id, transactionToken);
                },
                cancellationToken);
        }
        catch
        {
            dbContext.ClearTrackedChanges();
            throw;
        }
    }

    public async Task<ParentalLeaveRequestDto> RejectCancellationAsync(
        ParentalLeaveReasonActionRequest request,
        CancellationToken cancellationToken = default)
    {
        RequestValidator.Validate(request);
        var entity = await GetTrackedForReviewAsync(request.Id, cancellationToken);
        EnsureRowVersion(entity.RowVersion, request.RowVersion);
        var from = entity.Status;
        entity.RejectCancellation(request.Comment!, RequireUserId(), timeProvider.GetUtcNow());
        AddHistory(entity, ParentalLeaveApprovalAction.CancellationRejected, from, entity.Status, request.Comment);
        AddAudit(AuditActions.ParentalLeaveCancellationRejected, entity, new { Status = from }, Snapshot(entity));
        await SaveAsync(cancellationToken);
        return await GetAfterReviewAsync(entity.Id, cancellationToken);
    }

    public async Task<ParentalLeaveRequestDto> RequestEarlyReturnAsync(
        RequestParentalLeaveEarlyReturnRequest request,
        CancellationToken cancellationToken = default)
    {
        RequestValidator.Validate(request);
        var entity = await GetTrackedOwnedAsync(request.Id, cancellationToken);
        EnsureRowVersion(entity.RowVersion, request.RowVersion);
        var from = entity.Status;
        entity.RequestEarlyReturn(
            request.ReturnDate,
            request.Comment!,
            RequireUserId(),
            timeProvider.GetUtcNow());
        AddHistory(entity, ParentalLeaveApprovalAction.EarlyReturnRequested, from, entity.Status, request.Comment);
        AddAudit(AuditActions.ParentalLeaveEarlyReturnRequested, entity, null, new
        {
            entity.Status,
            entity.EarlyReturnDate
        });
        await SaveAsync(cancellationToken);
        return await GetOwnedAfterWriteAsync(entity.Id, cancellationToken);
    }

    public async Task<ParentalLeaveRequestDto> ApproveEarlyReturnAsync(
        ParentalLeaveActionRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await dbContext.ExecuteSerializableAsync(
                async transactionToken =>
                {
                    var entity = await GetTrackedForReviewAsync(request.Id, transactionToken);
                    EnsureRowVersion(entity.RowVersion, request.RowVersion);
                    var from = entity.Status;
                    var returnDate = entity.EarlyReturnDate
                        ?? throw new ApplicationValidationException("找不到提前復職日期。");
                    entity.ApproveEarlyReturn(RequireUserId(), timeProvider.GetUtcNow());
                    AddHistory(entity, ParentalLeaveApprovalAction.EarlyReturnApproved, from, entity.Status, request.Comment);
                    AddAudit(AuditActions.ParentalLeaveEarlyReturnApproved, entity, null, new
                    {
                        entity.Status,
                        entity.EarlyReturnDate
                    });
                    await attendanceRecalculationEngine
                        .RecalculateKeysWithEmploymentSuspensionsAsync(
                        RecalculationKeys(entity.EmployeeId, returnDate, entity.EndDate),
                        excludedParentalLeaveRequestIds: [entity.Id],
                        cancellationToken: transactionToken);
                    await SaveAsync(transactionToken);
                    return await GetAfterReviewAsync(entity.Id, transactionToken);
                },
                cancellationToken);
        }
        catch
        {
            dbContext.ClearTrackedChanges();
            throw;
        }
    }

    private async Task ValidateEligibilityAsync(
        ParentalLeaveRequest entity,
        CancellationToken cancellationToken)
    {
        var employee = await dbContext.Employees.AsNoTracking()
            .SingleOrDefaultAsync(item =>
                item.Id == entity.EmployeeId && item.IsActive,
                cancellationToken)
            ?? throw new ApplicationValidationException("員工資料不存在或已停用。");
        if (employee.HireDate.AddMonths(6) > entity.StartDate)
        {
            throw new ApplicationValidationException("申請開始日須已任職滿 6 個月。");
        }

        var thirdBirthday = entity.ChildBirthDate.AddYears(3);
        if (entity.StartDate >= thirdBirthday || entity.EndDate >= thirdBirthday)
        {
            throw new ApplicationValidationException("育嬰留停必須在子女滿 3 歲前結束。");
        }

        if (entity.CalendarDayCount > ParentalLeavePolicy.MaximumChildLeaveDays)
        {
            throw new ApplicationValidationException("單次申請不得超過 2 年。");
        }

        await EnsureChildReferenceMatchesAsync(
            entity.EmployeeId,
            entity.ChildReferenceId,
            entity.ChildBirthDate,
            entity.Id,
            cancellationToken);
        var existing = await dbContext.ParentalLeaveRequests
            .AsNoTracking()
            .Where(item =>
                item.EmployeeId == entity.EmployeeId &&
                item.Id != entity.Id &&
                (item.Status == ParentalLeaveStatus.Submitted ||
                    item.Status == ParentalLeaveStatus.Approved ||
                    item.Status == ParentalLeaveStatus.CancellationRequested ||
                    item.Status == ParentalLeaveStatus.Completed))
            .ToListAsync(cancellationToken);
        if (existing.Any(item =>
            entity.StartDate <= item.EffectiveEndDate &&
            entity.EndDate >= item.StartDate))
        {
            throw new ApplicationValidationException("申請期間與既有有效育嬰留停重疊。");
        }

        var usage = Usage.From(existing.Where(item =>
            item.ChildReferenceId == entity.ChildReferenceId));
        if (entity.ApplicationType == ParentalLeaveApplicationType.ShortTerm30DaysOrMore &&
            usage.ShortTermCount >= ParentalLeavePolicy.MaximumShortTermApplications)
        {
            throw new ApplicationValidationException("30 日以上未滿 6 個月的申請最多 2 次。");
        }

        if (entity.ApplicationType == ParentalLeaveApplicationType.DailyUnder30Days &&
            usage.DailyDays + entity.CalendarDayCount >
                ParentalLeavePolicy.MaximumDailyApplicationDays)
        {
            throw new ApplicationValidationException("按日申請累計不得超過 30 日。");
        }

        if (usage.TotalDays + entity.CalendarDayCount >
            ParentalLeavePolicy.MaximumChildLeaveDays)
        {
            throw new ApplicationValidationException("同一子女育嬰留職停薪合計不得超過 2 年。");
        }

        ParentalLeavePolicy.EnsureNotice(
            Today(),
            entity.StartDate,
            entity.ApplicationType,
            entity.NoticeType,
            entity.EmergencyCareReason);
    }

    private async Task EnsureChildReferenceMatchesAsync(
        Guid employeeId,
        Guid childReferenceId,
        DateOnly childBirthDate,
        Guid? excludedId,
        CancellationToken cancellationToken)
    {
        var mismatch = await dbContext.ParentalLeaveRequests.AsNoTracking()
            .AnyAsync(item =>
                item.EmployeeId == employeeId &&
                item.ChildReferenceId == childReferenceId &&
                (!excludedId.HasValue || item.Id != excludedId.Value) &&
                item.ChildBirthDate != childBirthDate,
                cancellationToken);
        if (mismatch)
        {
            throw new ApplicationValidationException("同一子女識別的出生日期必須一致。");
        }
    }

    private async Task<Usage> GetUsageAsync(
        Guid employeeId,
        Guid childReferenceId,
        Guid? excludedId,
        CancellationToken cancellationToken)
    {
        var requests = await dbContext.ParentalLeaveRequests.AsNoTracking()
            .Where(item =>
                item.EmployeeId == employeeId &&
                item.ChildReferenceId == childReferenceId &&
                (!excludedId.HasValue || item.Id != excludedId.Value) &&
                (item.Status == ParentalLeaveStatus.Submitted ||
                    item.Status == ParentalLeaveStatus.Approved ||
                    item.Status == ParentalLeaveStatus.CancellationRequested ||
                    item.Status == ParentalLeaveStatus.Completed))
            .ToListAsync(cancellationToken);
        return Usage.From(requests);
    }

    private IQueryable<ParentalLeaveRequest> BaseQuery() =>
        dbContext.ParentalLeaveRequests.AsNoTracking()
            .Include(item => item.Employee)
            .ThenInclude(item => item.Department);

    private IQueryable<ParentalLeaveRequest> DetailQuery() =>
        BaseQuery().Include(item => item.ApprovalHistories.OrderBy(history =>
            history.ActionAtUtc));

    private static IQueryable<ParentalLeaveRequest> ApplyFilters(
        IQueryable<ParentalLeaveRequest> queryable,
        ParentalLeaveQuery query)
    {
        if (query.Status.HasValue)
        {
            queryable = queryable.Where(item => item.Status == query.Status.Value);
        }

        if (!string.IsNullOrWhiteSpace(query.Keyword))
        {
            var keyword = query.Keyword.Trim();
            queryable = queryable.Where(item =>
                item.RequestNumber.Contains(keyword) ||
                item.Employee.EmployeeNumber.Contains(keyword) ||
                item.Employee.ChineseName.Contains(keyword));
        }

        return queryable;
    }

    private async Task<PagedResult<ParentalLeaveRequestDto>> QueryAsync(
        IQueryable<ParentalLeaveRequest> queryable,
        ParentalLeaveQuery query,
        CancellationToken cancellationToken)
    {
        var page = Math.Max(1, query.PageNumber);
        var size = Math.Clamp(query.PageSize, 1, 100);
        var total = await queryable.CountAsync(cancellationToken);
        var entities = await queryable
            .OrderByDescending(item => item.CreatedAtUtc)
            .Skip((page - 1) * size)
            .Take(size)
            .ToListAsync(cancellationToken);
        return new PagedResult<ParentalLeaveRequestDto>(
            entities.Select(Map).ToArray(),
            total,
            page,
            size);
    }

    private async Task<ParentalLeaveRequest> GetTrackedOwnedAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        var employeeId = RequireEmployeeId();
        var entity = await dbContext.ParentalLeaveRequests.SingleOrDefaultAsync(
            item => item.Id == id,
            cancellationToken)
            ?? throw new EntityNotFoundException("找不到指定的育嬰留停申請。");
        if (entity.EmployeeId != employeeId)
        {
            throw new ForbiddenAccessException("您只能操作自己的育嬰留停申請。");
        }

        return entity;
    }

    private async Task<ParentalLeaveRequest> GetTrackedForReviewAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        EnsureApprover();
        var entity = await dbContext.ParentalLeaveRequests
            .Include(item => item.Employee)
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken)
            ?? throw new EntityNotFoundException("找不到指定的育嬰留停申請。");
        await EnsureCanReviewAsync(entity, false, cancellationToken);
        return entity;
    }

    private async Task EnsureCanReviewAsync(
        ParentalLeaveRequest entity,
        bool allowOwnRequest,
        CancellationToken cancellationToken)
    {
        EnsureApprover();
        if (!allowOwnRequest && currentUser.EmployeeId == entity.EmployeeId)
        {
            throw new ForbiddenAccessException("不可簽核自己的育嬰留停申請。");
        }

        if (currentUser.HasPermission(PolicyNames.LeaveManage))
        {
            return;
        }

        var reviewerEmployeeId = currentUser.EmployeeId
            ?? throw new ForbiddenAccessException("Manager 帳號必須綁定員工資料。");
        var reviewerDepartment = await dbContext.Employees.AsNoTracking()
            .Where(item => item.Id == reviewerEmployeeId && item.IsActive)
            .Select(item => (Guid?)item.DepartmentId)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new ForbiddenAccessException("找不到有效的 Manager 員工資料。");
        var targetDepartment = entity.Employee?.DepartmentId ??
            await dbContext.Employees.AsNoTracking()
                .Where(item => item.Id == entity.EmployeeId)
                .Select(item => item.DepartmentId)
                .SingleAsync(cancellationToken);
        if (reviewerDepartment != targetDepartment)
        {
            throw new ForbiddenAccessException("Manager 只能處理同部門員工的申請。");
        }
    }

    private async Task<IQueryable<ParentalLeaveRequest>> ApplyReviewerScopeAsync(
        IQueryable<ParentalLeaveRequest> requests,
        CancellationToken cancellationToken)
    {
        if (currentUser.HasPermission(PolicyNames.LeaveManage))
        {
            return requests;
        }

        var employeeId = currentUser.EmployeeId
            ?? throw new ForbiddenAccessException("Manager 帳號必須綁定員工資料。");
        var departmentId = await dbContext.Employees.AsNoTracking()
            .Where(item => item.Id == employeeId && item.IsActive)
            .Select(item => (Guid?)item.DepartmentId)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new ForbiddenAccessException("找不到有效的 Manager 員工資料。");
        return requests.Where(item => item.Employee.DepartmentId == departmentId);
    }

    private async Task EnsureEmployeeActiveAsync(
        Guid employeeId,
        CancellationToken cancellationToken)
    {
        if (!await dbContext.Employees.AsNoTracking().AnyAsync(
            item => item.Id == employeeId && item.IsActive,
            cancellationToken))
        {
            throw new ApplicationValidationException("員工資料不存在或已停用。");
        }
    }

    private void AddHistory(
        ParentalLeaveRequest entity,
        ParentalLeaveApprovalAction action,
        ParentalLeaveStatus from,
        ParentalLeaveStatus to,
        string? comment) =>
        dbContext.ParentalLeaveApprovalHistories.Add(
            new ParentalLeaveApprovalHistory(
                Guid.NewGuid(),
                entity.Id,
                action,
                RequireUserId(),
                currentUser.DisplayName ?? RequireUserId(),
                comment,
                timeProvider.GetUtcNow(),
                from,
                to));

    private void AddAudit(
        string action,
        ParentalLeaveRequest entity,
        object? oldValues,
        object? newValues) =>
        dbContext.AuditLogs.Add(AuditLogFactory.Create(
            currentUser,
            timeProvider,
            action,
            nameof(ParentalLeaveRequest),
            entity.Id.ToString(),
            oldValues,
            newValues));

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConcurrencyConflictException();
        }
        catch (DbUpdateException)
        {
            throw new ApplicationValidationException("育嬰留停資料寫入失敗，請重新整理後再試。");
        }
    }

    private async Task<ParentalLeaveRequestDto> GetOwnedAfterWriteAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        var employeeId = RequireEmployeeId();
        var entity = await DetailQuery().SingleAsync(
            item => item.Id == id && item.EmployeeId == employeeId,
            cancellationToken);
        return Map(entity);
    }

    private async Task<ParentalLeaveRequestDto> GetAfterReviewAsync(
        Guid id,
        CancellationToken cancellationToken) =>
        Map(await DetailQuery().SingleAsync(item => item.Id == id, cancellationToken));

    private ParentalLeaveRequestDto Map(ParentalLeaveRequest entity) =>
        new(
            entity.Id,
            entity.RequestNumber,
            entity.EmployeeId,
            entity.Employee.EmployeeNumber,
            entity.Employee.ChineseName,
            entity.Employee.DepartmentId,
            entity.Employee.Department.Name,
            entity.ChildReferenceId,
            entity.ChildBirthDate,
            entity.ChildDisplayName,
            entity.StartDate,
            entity.EndDate,
            entity.EffectiveEndDate,
            entity.CalendarDayCount,
            entity.ApplicationType,
            entity.NoticeType,
            entity.Reason,
            entity.ContactAddress,
            entity.ContactPhone,
            entity.ContinueSocialInsurance,
            entity.EmergencyCareReason,
            entity.Status,
            entity.GetEffectiveStatus(Today()),
            entity.RequestedAtUtc,
            entity.ApprovedAtUtc,
            entity.CancellationReason,
            entity.EarlyReturnDate,
            entity.EarlyReturnReason,
            entity.EarlyReturnApprovedAtUtc.HasValue,
            entity.CreatedAtUtc,
            Convert.ToBase64String(entity.RowVersion),
            entity.ApprovalHistories
                .OrderBy(item => item.ActionAtUtc)
                .Select(item => new ParentalLeaveApprovalHistoryDto(
                    item.Id,
                    item.Action,
                    item.ActionByDisplayName,
                    item.Comment,
                    item.ActionAtUtc,
                    item.FromStatus,
                    item.ToStatus))
                .ToArray());

    private void EnsureAuthenticated()
    {
        if (!currentUser.IsAuthenticated ||
            !currentUser.HasPermission(PolicyNames.ParentalLeaveSelfService))
        {
            throw new ForbiddenAccessException("請先登入。");
        }
    }

    private void EnsureApprover()
    {
        if (!currentUser.IsAuthenticated ||
            !currentUser.HasPermission(PolicyNames.ParentalLeaveApprove))
        {
            throw new ForbiddenAccessException("您沒有育嬰留停簽核權限。");
        }
    }

    private Guid RequireEmployeeId()
    {
        EnsureAuthenticated();
        return currentUser.EmployeeId
            ?? throw new ForbiddenAccessException("帳號尚未綁定員工資料。");
    }

    private string RequireUserId() => currentUser.UserId
        ?? throw new ForbiddenAccessException("無法識別目前登入帳號。");

    private static void EnsureRowVersion(byte[] current, string supplied)
    {
        byte[] expected;
        try
        {
            expected = string.IsNullOrWhiteSpace(supplied)
                ? []
                : Convert.FromBase64String(supplied);
        }
        catch (FormatException)
        {
            throw new ConcurrencyConflictException();
        }

        if (!current.SequenceEqual(expected))
        {
            throw new ConcurrencyConflictException();
        }
    }

    private DateOnly Today() => DateOnly.FromDateTime(
        TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), TaipeiZone).DateTime);

    private static string GenerateRequestNumber(DateTimeOffset nowUtc) =>
        $"PL-{nowUtc:yyyyMMdd}-{Guid.NewGuid():N}"[..24].ToUpperInvariant();

    private static object Snapshot(ParentalLeaveRequest entity) => new
    {
        entity.RequestNumber,
        entity.EmployeeId,
        entity.ChildReferenceId,
        entity.StartDate,
        entity.EndDate,
        entity.ApplicationType,
        entity.NoticeType,
        entity.Status
    };

    private static IReadOnlyList<AttendanceRecalculationKey> RecalculationKeys(
        Guid employeeId,
        DateOnly from,
        DateOnly to) =>
        Enumerable.Range(0, to.DayNumber - from.DayNumber + 1)
            .Select(offset => new AttendanceRecalculationKey(
                employeeId,
                from.AddDays(offset)))
            .ToArray();

    private static TimeZoneInfo ResolveTaipeiZone()
    {
        foreach (var id in new[] { "Taipei Standard Time", "Asia/Taipei" })
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (TimeZoneNotFoundException)
            {
            }
        }

        throw new InvalidOperationException("找不到台北時區設定。");
    }

    private sealed record Usage(int DailyDays, int ShortTermCount, int TotalDays)
    {
        public static readonly Usage Empty = new(0, 0, 0);

        public static Usage From(IEnumerable<ParentalLeaveRequest> requests)
        {
            var counted = requests.Where(item =>
                ParentalLeavePolicy.CountsTowardUsage(item.Status)).ToArray();
            return new Usage(
                counted.Where(item => item.ApplicationType ==
                        ParentalLeaveApplicationType.DailyUnder30Days)
                    .Sum(item => item.CalendarDayCount),
                counted.Count(item => item.ApplicationType ==
                    ParentalLeaveApplicationType.ShortTerm30DaysOrMore),
                counted.Sum(item => item.CalendarDayCount));
        }
    }
}
