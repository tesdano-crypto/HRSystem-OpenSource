using HRSystem.Application.Abstractions.Persistence;
using HRSystem.Application.Abstractions.Security;
using HRSystem.Application.Common.Auditing;
using HRSystem.Application.Common.Exceptions;
using HRSystem.Application.Common.Models;
using HRSystem.Application.Common.Validation;
using HRSystem.Application.Security;
using HRSystem.Application.Attendance;
using HRSystem.Application.AnnualLeave;
using HRSystem.Application.CompTime;
using HRSystem.Domain.CompTime;
using HRSystem.Domain.LeaveRequests;
using HRSystem.Domain.MasterData;
using Microsoft.EntityFrameworkCore;

namespace HRSystem.Application.LeaveRequests;

public sealed class LeaveRequestService(
    IApplicationDbContext dbContext,
    ICurrentUser currentUser,
    ILeaveDurationCalculator durationCalculator,
    TimeProvider timeProvider,
    IAttendanceRecalculationEngine? attendanceRecalculationEngine = null,
    IAnnualLeaveService? annualLeaveService = null,
    ICompTimeService? compTimeService = null)
    : ILeaveRequestService
{
    private static readonly string[] SupportedCalendarLeaveTypeCodes =
    [
        CalendarDayLeavePolicy.MaternityCode,
        CalendarDayLeavePolicy.MiscarriageCode,
        CalendarDayLeavePolicy.PregnancyBedRestCode
    ];
    private readonly IAttendanceRecalculationEngine _attendanceRecalculationEngine =
        attendanceRecalculationEngine ??
        new AttendanceRecalculationEngine(dbContext, timeProvider);
    private readonly IAnnualLeaveService _annualLeaveService = annualLeaveService ??
        new AnnualLeaveService(dbContext, currentUser, durationCalculator, timeProvider);
    private readonly ICompTimeService _compTimeService = compTimeService ??
        new CompTimeService(dbContext, currentUser, timeProvider);
    private static readonly TimeZoneInfo TaipeiZone = ResolveTaipeiZone();
    public async Task<IReadOnlyList<LeaveTypeOptionDto>> GetAvailableLeaveTypesAsync(
        CancellationToken cancellationToken = default)
    {
        EnsureAuthenticated();
        return await dbContext.LeaveTypes.AsNoTracking()
            .Where(x =>
                x.IsActive &&
                x.IsEmployeeRequestEnabled &&
                ((x.CalculationMode == LeaveCalculationMode.WorkingSchedule &&
                    !x.RequiresAttachment) ||
                 (x.CalculationMode == LeaveCalculationMode.CalendarDays &&
                    SupportedCalendarLeaveTypeCodes.Contains(x.Code))))
            .OrderBy(x => x.SortOrder).ThenBy(x => x.Code)
            .Select(x => new LeaveTypeOptionDto(
                x.Id,
                x.Code,
                x.Name,
                x.AllowHourlyRequest,
                x.MinimumRequestMinutes,
                x.Description,
                x.CalculationMode,
                x.RequiresAttachment))
            .ToListAsync(cancellationToken);
    }

    public Task<LeaveDurationEstimateDto> EstimateDurationAsync(
        DateTimeOffset startAt,
        DateTimeOffset endAt,
        CancellationToken cancellationToken = default) =>
        durationCalculator.CalculateAsync(
            RequireEmployeeId(),
            startAt,
            endAt,
            cancellationToken);

    public async Task<LeaveDurationEstimateDto> EstimateDurationAsync(
        LeaveDurationEstimateRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureAuthenticated();
        var leaveType = await GetLeaveTypeAsync(
            request.LeaveTypeId,
            cancellationToken);
        var normalized = NormalizeInput(leaveType, request);
        var estimate = await durationCalculator.CalculateAsync(
            RequireEmployeeId(),
            normalized.StartAt,
            normalized.EndAt,
            leaveType.CalculationMode,
            cancellationToken);
        EnsureCompTimeIncrement(leaveType, estimate.DurationHours);
        return estimate with
        {
            CalendarDayCount = normalized.CalendarRange?.CalendarDayCount,
            CalendarEndDate = normalized.CalendarRange?.EndDate
        };
    }

    public Task<PagedResult<LeaveRequestDto>> GetMyRequestsAsync(
        LeaveRequestQuery query,
        CancellationToken cancellationToken = default)
    {
        var employeeId = RequireEmployeeId();
        return QueryAsync(ApplyFilters(BaseQuery().Where(x => x.EmployeeId == employeeId), query), query, cancellationToken);
    }

    public async Task<LeaveRequestDto> GetRequestDetailAsync(Guid id, CancellationToken cancellationToken = default)
    {
        EnsureAuthenticated();
        var entity = await DetailQuery().SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new EntityNotFoundException("找不到指定的請假申請。");
        if (currentUser.EmployeeId != entity.EmployeeId)
        {
            await EnsureCanReviewAsync(entity, allowOwnRequest: false, cancellationToken);
        }

        return Map(entity);
    }

    public async Task<PagedResult<LeaveRequestDto>> GetPendingApprovalsAsync(
        LeaveRequestQuery query,
        CancellationToken cancellationToken = default)
    {
        EnsureApprover();
        var requests = BaseQuery().Where(x =>
            x.Status == LeaveRequestStatus.Submitted ||
            x.Status == LeaveRequestStatus.CancellationRequested);
        requests = await ApplyReviewerScopeAsync(requests, cancellationToken);
        if (currentUser.EmployeeId.HasValue)
        {
            requests = requests.Where(x => x.EmployeeId != currentUser.EmployeeId.Value);
        }

        return await QueryAsync(ApplyFilters(requests, query), query, cancellationToken);
    }

    public async Task<PagedResult<LeaveRequestDto>> GetProcessedApprovalsAsync(
        LeaveRequestQuery query,
        CancellationToken cancellationToken = default)
    {
        EnsureApprover();
        var userId = RequireUserId();
        var processedIds = dbContext.LeaveApprovalHistories.AsNoTracking()
            .Where(x => x.ActionByUserId == userId &&
                (x.Action == ApprovalAction.Approved ||
                    x.Action == ApprovalAction.Rejected ||
                    x.Action == ApprovalAction.CancellationApproved ||
                    x.Action == ApprovalAction.CancellationRejected))
            .Select(x => x.LeaveRequestId);
        var requests = BaseQuery().Where(x => processedIds.Contains(x.Id));
        requests = await ApplyReviewerScopeAsync(requests, cancellationToken);
        return await QueryAsync(ApplyFilters(requests, query), query, cancellationToken);
    }

    public Task<PagedResult<LeaveRequestDto>> SearchAllRequestsAsync(
        LeaveRequestQuery query,
        CancellationToken cancellationToken = default)
    {
        if (!currentUser.IsAuthenticated || !currentUser.HasPermission(PolicyNames.LeaveRequestReadAll))
        {
            throw new ForbiddenAccessException("您沒有檢視全部請假申請的權限。");
        }

        return QueryAsync(ApplyFilters(BaseQuery(), query), query, cancellationToken);
    }

    public async Task<LeaveRequestDto> CreateDraftAsync(
        CreateLeaveDraftRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureAuthenticated();
        RequestValidator.Validate(request);
        var employeeId = RequireEmployeeId();
        await EnsureEmployeeActiveAsync(employeeId, cancellationToken);
        var leaveType = await GetRequestableLeaveTypeAsync(request.LeaveTypeId, cancellationToken);
        var normalized = NormalizeInput(leaveType, request);
        await EnsureNoOverlapAsync(employeeId, normalized.StartAt, normalized.EndAt, null, cancellationToken);
        var estimate = await CalculateValidDurationAsync(
            employeeId,
            normalized.StartAt,
            normalized.EndAt,
            leaveType.CalculationMode,
            cancellationToken);
        EnsureMinimumRequestMinutes(leaveType, estimate);
        EnsureCompTimeIncrement(leaveType, estimate.DurationHours);
        var now = timeProvider.GetUtcNow();
        var entity = new LeaveRequest(
            Guid.NewGuid(), GenerateRequestNumber(now), employeeId, request.LeaveTypeId,
            normalized.StartAt, normalized.EndAt, estimate.DurationHours, request.Reason,
            RequireUserId(), now, leaveType.CalculationMode, leaveType.Code,
            normalized.CalendarRange?.PregnancyDurationCategory);
        dbContext.LeaveRequests.Add(entity);
        AddAudit(AuditActions.LeaveDraftCreated, entity, null, Snapshot(entity));
        await SaveAsync(cancellationToken);
        return await GetOwnedAfterWriteAsync(entity.Id, cancellationToken);
    }

    public async Task<LeaveRequestDto> UpdateDraftAsync(
        UpdateLeaveDraftRequest request,
        CancellationToken cancellationToken = default)
    {
        RequestValidator.Validate(request);
        var entity = await GetTrackedOwnedAsync(request.Id, cancellationToken);
        EnsureRowVersion(entity.RowVersion, request.RowVersion);
        var leaveType = request.LeaveTypeId != entity.LeaveTypeId
            ? await GetRequestableLeaveTypeAsync(request.LeaveTypeId, cancellationToken)
            : await GetLeaveTypeAsync(request.LeaveTypeId, cancellationToken);
        var normalized = NormalizeInput(leaveType, request);
        await EnsureNoOverlapAsync(entity.EmployeeId, normalized.StartAt, normalized.EndAt, entity.Id, cancellationToken);
        var estimate = await CalculateValidDurationAsync(
            entity.EmployeeId,
            normalized.StartAt,
            normalized.EndAt,
            leaveType.CalculationMode,
            cancellationToken);
        EnsureMinimumRequestMinutes(leaveType, estimate);
        EnsureCompTimeIncrement(leaveType, estimate.DurationHours);
        var oldValues = Snapshot(entity);
        entity.UpdateDraft(
            request.LeaveTypeId,
            normalized.StartAt,
            normalized.EndAt,
            estimate.DurationHours,
            request.Reason,
            RequireUserId(),
            timeProvider.GetUtcNow(),
            leaveType.CalculationMode,
            leaveType.Code,
            normalized.CalendarRange?.PregnancyDurationCategory);
        AddAudit(AuditActions.LeaveDraftUpdated, entity, oldValues, Snapshot(entity));
        await SaveAsync(cancellationToken);
        return await GetOwnedAfterWriteAsync(entity.Id, cancellationToken);
    }

    public async Task DeleteDraftAsync(
        LeaveRequestActionRequest request,
        CancellationToken cancellationToken = default)
    {
        var entity = await GetTrackedOwnedAsync(request.Id, cancellationToken);
        EnsureRowVersion(entity.RowVersion, request.RowVersion);
        if (!entity.CanDeleteDraft)
        {
            throw new ApplicationValidationException("只有從未送出的草稿可以刪除。");
        }

        dbContext.LeaveRequests.Remove(entity);
        AddAudit(AuditActions.LeaveDraftDeleted, entity, Snapshot(entity), null);
        await SaveAsync(cancellationToken);
    }

    public async Task<LeaveRequestDto> SubmitAsync(
        LeaveRequestActionRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await dbContext.ExecuteSerializableAsync(async transactionToken =>
            {
                var entity = await GetTrackedOwnedAsync(request.Id, transactionToken);
                EnsureRowVersion(entity.RowVersion, request.RowVersion);
                var leaveType = await GetRequestableLeaveTypeAsync(entity.LeaveTypeId, transactionToken);
                ValidatePersistedCalendarShape(entity, leaveType);
                await EnsureNoOverlapAsync(entity.EmployeeId, entity.StartAt, entity.EndAt, entity.Id, transactionToken);
                var estimate = await CalculateValidDurationAsync(
                    entity.EmployeeId, entity.StartAt, entity.EndAt,
                    leaveType.CalculationMode, transactionToken);
                EnsureMinimumRequestMinutes(leaveType, estimate);
                EnsureCompTimeIncrement(leaveType, estimate.DurationHours);
                entity.RefreshDraftDuration(estimate.DurationHours, leaveType.CalculationMode);
                await _annualLeaveService.ReserveForSubmissionAsync(entity, transactionToken);
                var from = entity.Status;
                entity.Submit(RequireUserId(), timeProvider.GetUtcNow());
                AddHistory(entity, ApprovalAction.Submitted, from, entity.Status, request.Comment);
                AddAudit(AuditActions.LeaveSubmitted, entity, new { Status = from }, Snapshot(entity));
                await SaveAsync(transactionToken);
                return await GetOwnedAfterWriteAsync(entity.Id, transactionToken);
            }, cancellationToken);
        }
        catch
        {
            dbContext.ClearTrackedChanges();
            throw;
        }
    }

    public async Task<LeaveRequestDto> WithdrawAsync(
        LeaveRequestActionRequest request,
        CancellationToken cancellationToken = default)
    {
        return await dbContext.ExecuteSerializableAsync(async transactionToken =>
        {
            var entity = await GetTrackedOwnedAsync(request.Id, transactionToken);
            EnsureRowVersion(entity.RowVersion, request.RowVersion);
            await _annualLeaveService.ReleaseReservationAsync(entity.Id, transactionToken);
            var from = entity.Status;
            entity.Withdraw(RequireUserId(), timeProvider.GetUtcNow());
            AddHistory(entity, ApprovalAction.Withdrawn, from, entity.Status, request.Comment);
            AddAudit(AuditActions.LeaveWithdrawn, entity, new { Status = from }, Snapshot(entity));
            await SaveAsync(transactionToken);
            return await GetOwnedAfterWriteAsync(entity.Id, transactionToken);
        }, cancellationToken);
    }

    public async Task<LeaveRequestDto> RequestCancellationAsync(
        RequestLeaveCancellationRequest request,
        CancellationToken cancellationToken = default)
    {
        RequestValidator.Validate(request);
        var entity = await GetTrackedOwnedAsync(request.Id, cancellationToken);
        EnsureRowVersion(entity.RowVersion, request.RowVersion);
        var from = entity.Status;
        entity.RequestCancellation(
            request.Comment!,
            RequireUserId(),
            timeProvider.GetUtcNow());
        AddHistory(
            entity,
            ApprovalAction.CancellationRequested,
            from,
            entity.Status,
            request.Comment);
        AddAudit(
            AuditActions.LeaveCancellationRequested,
            entity,
            new { Status = from },
            new { entity.Status });
        await SaveAsync(cancellationToken);
        return await GetOwnedAfterWriteAsync(entity.Id, cancellationToken);
    }

    public async Task<LeaveRequestDto> ApproveAsync(
        LeaveRequestActionRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await dbContext.ExecuteSerializableAsync(
                async transactionToken =>
                {
                    var entity = await GetTrackedForReviewAsync(
                        request.Id,
                        transactionToken);
                    EnsureRowVersion(entity.RowVersion, request.RowVersion);
                    ValidatePersistedCalendarShape(entity, entity.LeaveType);
                    await EnsureNoOverlapAsync(
                        entity.EmployeeId,
                        entity.StartAt,
                        entity.EndAt,
                        entity.Id,
                        transactionToken);
                    var estimate = await CalculateValidDurationAsync(
                        entity.EmployeeId,
                        entity.StartAt,
                        entity.EndAt,
                        entity.LeaveType.CalculationMode,
                        transactionToken);
                    if (estimate.DurationHours != entity.DurationHours)
                    {
                        throw new ApplicationValidationException(
                            "目前有效班別計算的請假時數與送出時不一致，請退回申請並由員工重新確認。");
                    }

                    EnsureCompTimeIncrement(entity.LeaveType, entity.DurationHours);

                    await _annualLeaveService.ConsumeForApprovalAsync(entity.Id, transactionToken);
                    await _compTimeService.ConsumeForApprovalAsync(entity, transactionToken);

                    var from = entity.Status;
                    entity.Approve(
                        RequireUserId(),
                        timeProvider.GetUtcNow());
                    AddHistory(
                        entity,
                        ApprovalAction.Approved,
                        from,
                        entity.Status,
                        request.Comment);
                    AddAudit(
                        AuditActions.LeaveApproved,
                        entity,
                        new { Status = from },
                        Snapshot(entity));
                    var (dateFrom, dateTo) = ResolveLocalDateRange(
                        entity.StartAt,
                        entity.EndAt);
                    await _attendanceRecalculationEngine
                        .RecalculateRangeAsync(
                            dateFrom,
                            dateTo,
                            entity.EmployeeId,
                            [new PendingApprovedLeave(
                                entity.Id,
                                entity.EmployeeId,
                                entity.LeaveTypeId,
                                entity.LeaveType.Code,
                                entity.LeaveType.Name,
                                entity.StartAt,
                                entity.EndAt)],
                            transactionToken);
                    await SaveAsync(transactionToken);
                    return await GetAfterReviewAsync(
                        entity.Id,
                        transactionToken);
                },
                cancellationToken);
        }
        catch
        {
            dbContext.ClearTrackedChanges();
            throw;
        }
    }

    public async Task<LeaveRequestDto> RejectAsync(
        RejectLeaveRequestRequest request,
        CancellationToken cancellationToken = default)
    {
        RequestValidator.Validate(request);
        return await dbContext.ExecuteSerializableAsync(async transactionToken =>
        {
            var entity = await GetTrackedForReviewAsync(request.Id, transactionToken);
            EnsureRowVersion(entity.RowVersion, request.RowVersion);
            await _annualLeaveService.ReleaseReservationAsync(entity.Id, transactionToken);
            var from = entity.Status;
            entity.Reject(request.Comment!, RequireUserId(), timeProvider.GetUtcNow());
            AddHistory(entity, ApprovalAction.Rejected, from, entity.Status, request.Comment);
            AddAudit(AuditActions.LeaveRejected, entity, new { Status = from }, Snapshot(entity));
            await SaveAsync(transactionToken);
            return await GetAfterReviewAsync(entity.Id, transactionToken);
        }, cancellationToken);
    }

    public async Task<LeaveRequestDto> ApproveCancellationAsync(
        LeaveRequestActionRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await dbContext.ExecuteSerializableAsync(
                async transactionToken =>
                {
                    var entity = await GetTrackedForReviewAsync(
                        request.Id,
                        transactionToken);
                    EnsureRowVersion(entity.RowVersion, request.RowVersion);
                    var from = entity.Status;
                    entity.ApproveCancellation(
                        RequireUserId(),
                        timeProvider.GetUtcNow());
                    await _annualLeaveService.RestoreAfterCancellationAsync(entity.Id, transactionToken);
                    await _compTimeService.RestoreAfterCancellationAsync(entity, transactionToken);
                    AddHistory(
                        entity,
                        ApprovalAction.CancellationApproved,
                        from,
                        entity.Status,
                        request.Comment);
                    AddAudit(
                        AuditActions.LeaveCancellationApproved,
                        entity,
                        new { Status = from },
                        new { entity.Status });

                    await _attendanceRecalculationEngine.RecalculateKeysAsync(
                        ResolveRecalculationKeys(entity),
                        transactionToken,
                        [entity.Id]);
                    await SaveAsync(transactionToken);
                    return await GetAfterReviewAsync(
                        entity.Id,
                        transactionToken);
                },
                cancellationToken);
        }
        catch
        {
            dbContext.ClearTrackedChanges();
            throw;
        }
    }

    public async Task<LeaveRequestDto> RejectCancellationAsync(
        RejectLeaveCancellationRequest request,
        CancellationToken cancellationToken = default)
    {
        RequestValidator.Validate(request);
        var entity = await GetTrackedForReviewAsync(request.Id, cancellationToken);
        EnsureRowVersion(entity.RowVersion, request.RowVersion);
        var from = entity.Status;
        entity.RejectCancellation(
            request.Comment!,
            RequireUserId(),
            timeProvider.GetUtcNow());
        AddHistory(
            entity,
            ApprovalAction.CancellationRejected,
            from,
            entity.Status,
            request.Comment);
        AddAudit(
            AuditActions.LeaveCancellationRejected,
            entity,
            new { Status = from },
            new { entity.Status });
        await SaveAsync(cancellationToken);
        return await GetAfterReviewAsync(entity.Id, cancellationToken);
    }

    public async Task<LeaveRequestDto> CopyToDraftAsync(Guid sourceId, CancellationToken cancellationToken = default)
    {
        var source = await GetTrackedOwnedAsync(sourceId, cancellationToken);
        if (source.Status is not (LeaveRequestStatus.Rejected or LeaveRequestStatus.Withdrawn))
        {
            throw new ApplicationValidationException("只有已退回或已撤回的申請可以複製為新草稿。");
        }

        return await CreateDraftAsync(new CreateLeaveDraftRequest
        {
            LeaveTypeId = source.LeaveTypeId,
            StartAt = source.StartAt,
            EndAt = source.EndAt,
            CalendarStartDate = source.LeaveType.CalculationMode == LeaveCalculationMode.CalendarDays
                ? ResolveCalendarStartDate(source.StartAt)
                : null,
            CalendarEndDate = source.LeaveType.CalculationMode == LeaveCalculationMode.CalendarDays
                ? ResolveCalendarEndDate(source.EndAt)
                : null,
            PregnancyDurationCategory = source.PregnancyDurationCategory,
            Reason = source.Reason
        }, cancellationToken);
    }

    private IQueryable<LeaveRequest> BaseQuery() => dbContext.LeaveRequests.AsNoTracking()
        .Include(x => x.Employee).ThenInclude(x => x.Department)
        .Include(x => x.LeaveType);

    private IQueryable<LeaveRequest> DetailQuery() => BaseQuery()
        .Include(x => x.ApprovalHistories.OrderBy(y => y.ActionAtUtc));

    private static IQueryable<LeaveRequest> ApplyFilters(IQueryable<LeaveRequest> requests, LeaveRequestQuery query)
    {
        if (!string.IsNullOrWhiteSpace(query.Keyword))
        {
            var keyword = query.Keyword.Trim().ToUpperInvariant();
            requests = requests.Where(x => x.RequestNumber.ToUpper().Contains(keyword) ||
                x.Employee.EmployeeNumber.ToUpper().Contains(keyword) ||
                x.Employee.ChineseName.ToUpper().Contains(keyword));
        }

        if (query.Status.HasValue) requests = requests.Where(x => x.Status == query.Status.Value);
        if (query.EmployeeId.HasValue) requests = requests.Where(x => x.EmployeeId == query.EmployeeId.Value);
        if (query.DepartmentId.HasValue) requests = requests.Where(x => x.Employee.DepartmentId == query.DepartmentId.Value);
        if (query.LeaveTypeId.HasValue) requests = requests.Where(x => x.LeaveTypeId == query.LeaveTypeId.Value);
        if (query.StartDate.HasValue)
        {
            var start = new DateTimeOffset(query.StartDate.Value.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            requests = requests.Where(x => x.EndAt >= start);
        }
        if (query.EndDate.HasValue)
        {
            var endExclusive = new DateTimeOffset(query.EndDate.Value.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            requests = requests.Where(x => x.StartAt < endExclusive);
        }
        return requests;
    }

    private static async Task<PagedResult<LeaveRequestDto>> QueryAsync(
        IQueryable<LeaveRequest> requests,
        LeaveRequestQuery query,
        CancellationToken cancellationToken)
    {
        var pageNumber = Math.Max(1, query.PageNumber);
        var pageSize = Math.Clamp(query.PageSize, 1, 100);
        var total = await requests.CountAsync(cancellationToken);
        var entities = await requests.OrderByDescending(x => x.CreatedAtUtc)
            .Skip((pageNumber - 1) * pageSize).Take(pageSize)
            .ToListAsync(cancellationToken);
        return new PagedResult<LeaveRequestDto>(entities.Select(Map).ToList(), total, pageNumber, pageSize);
    }

    private async Task<LeaveRequest> GetTrackedOwnedAsync(Guid id, CancellationToken cancellationToken)
    {
        var employeeId = RequireEmployeeId();
        var entity = await dbContext.LeaveRequests
            .Include(x => x.LeaveType)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new EntityNotFoundException("找不到指定的請假申請。");
        if (entity.EmployeeId != employeeId) throw new ForbiddenAccessException("您只能操作自己的請假申請。");
        return entity;
    }

    private async Task<LeaveRequest> GetTrackedForReviewAsync(Guid id, CancellationToken cancellationToken)
    {
        EnsureApprover();
        var entity = await dbContext.LeaveRequests
            .Include(x => x.Employee)
            .Include(x => x.LeaveType)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new EntityNotFoundException("找不到指定的請假申請。");
        await EnsureCanReviewAsync(entity, allowOwnRequest: false, cancellationToken);
        return entity;
    }

    private async Task EnsureCanReviewAsync(LeaveRequest entity, bool allowOwnRequest, CancellationToken cancellationToken)
    {
        EnsureApprover();
        if (!allowOwnRequest && currentUser.EmployeeId == entity.EmployeeId)
        {
            throw new ForbiddenAccessException("不可簽核自己的請假申請。");
        }
        if (currentUser.HasPermission(PolicyNames.LeaveManage)) return;
        var reviewerEmployeeId = currentUser.EmployeeId
            ?? throw new ForbiddenAccessException("Manager 帳號必須綁定員工資料才能簽核。");
        var reviewerDepartment = await dbContext.Employees.AsNoTracking()
            .Where(x => x.Id == reviewerEmployeeId && x.IsActive)
            .Select(x => (Guid?)x.DepartmentId)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new ForbiddenAccessException("找不到有效的 Manager 員工資料。");
        var requestDepartment = entity.Employee is null
            ? await dbContext.Employees.Where(x => x.Id == entity.EmployeeId).Select(x => x.DepartmentId).SingleAsync(cancellationToken)
            : entity.Employee.DepartmentId;
        if (reviewerDepartment != requestDepartment)
        {
            throw new ForbiddenAccessException("Manager 只能簽核同部門員工的申請。");
        }
    }

    private async Task<IQueryable<LeaveRequest>> ApplyReviewerScopeAsync(
        IQueryable<LeaveRequest> requests,
        CancellationToken cancellationToken)
    {
        if (currentUser.HasPermission(PolicyNames.LeaveManage)) return requests;
        var employeeId = currentUser.EmployeeId
            ?? throw new ForbiddenAccessException("Manager 帳號必須綁定員工資料才能簽核。");
        var departmentId = await dbContext.Employees.AsNoTracking()
            .Where(x => x.Id == employeeId && x.IsActive)
            .Select(x => (Guid?)x.DepartmentId)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new ForbiddenAccessException("找不到有效的 Manager 員工資料。");
        return requests.Where(x => x.Employee.DepartmentId == departmentId);
    }

    private async Task EnsureNoOverlapAsync(
        Guid employeeId,
        DateTimeOffset startAt,
        DateTimeOffset endAt,
        Guid? excludedId,
        CancellationToken cancellationToken)
    {
        var startUtc = startAt.ToUniversalTime();
        var endUtc = endAt.ToUniversalTime();
        var overlaps = await dbContext.LeaveRequests.AsNoTracking().AnyAsync(x =>
            x.EmployeeId == employeeId &&
            (!excludedId.HasValue || x.Id != excludedId.Value) &&
            (x.Status == LeaveRequestStatus.Submitted ||
                x.Status == LeaveRequestStatus.Approved ||
                x.Status == LeaveRequestStatus.CancellationRequested) &&
            startUtc < x.EndAt && endUtc > x.StartAt,
            cancellationToken);
        if (overlaps)
        {
            throw new ApplicationValidationException("此時段與既有的待簽核或已核准請假重疊，請調整日期時間。");
        }
    }

    private async Task EnsureEmployeeActiveAsync(Guid employeeId, CancellationToken cancellationToken)
    {
        if (!await dbContext.Employees.AsNoTracking().AnyAsync(x => x.Id == employeeId && x.IsActive, cancellationToken))
        {
            throw new ApplicationValidationException("目前綁定的員工資料不存在或已停用，無法建立請假申請。");
        }
    }

    private async Task<LeaveType> GetLeaveTypeAsync(
        Guid leaveTypeId,
        CancellationToken cancellationToken)
    {
        return await dbContext.LeaveTypes.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == leaveTypeId, cancellationToken)
            ?? throw new ApplicationValidationException("請選擇存在的假別。");
    }

    private async Task<LeaveType> GetRequestableLeaveTypeAsync(
        Guid leaveTypeId,
        CancellationToken cancellationToken)
    {
        var leaveType = await GetLeaveTypeAsync(leaveTypeId, cancellationToken);
        if (!leaveType.IsActive)
        {
            throw new ApplicationValidationException("請選擇存在且啟用的假別。");
        }

        if (!leaveType.IsEmployeeRequestEnabled)
        {
            throw new ApplicationValidationException("此假別尚未開放員工申請。");
        }

        if (leaveType.CalculationMode == LeaveCalculationMode.LeaveOfAbsence ||
            (leaveType.CalculationMode == LeaveCalculationMode.CalendarDays &&
             !CalendarDayLeavePolicy.IsSupported(leaveType.Code)))
        {
            throw new ApplicationValidationException("此假別必須使用專用流程，無法由一般請假申請送出。");
        }

        if (leaveType.RequiresAttachment &&
            leaveType.CalculationMode == LeaveCalculationMode.WorkingSchedule)
        {
            throw new ApplicationValidationException("此假別需要附件；附件功能尚未開放，暫時無法申請。");
        }

        return leaveType;
    }

    private static void EnsureMinimumRequestMinutes(
        LeaveType leaveType,
        LeaveDurationEstimateDto estimate)
    {
        if (leaveType.CalculationMode == LeaveCalculationMode.WorkingSchedule &&
            leaveType.MinimumRequestMinutes is int minimum &&
            estimate.DurationHours * 60m < minimum)
        {
            throw new ApplicationValidationException($"此假別每次至少須申請 {minimum} 分鐘。");
        }
    }

    private static void EnsureCompTimeIncrement(
        LeaveType leaveType,
        decimal durationHours)
    {
        if (CompTimePolicy.IsCompTime(leaveType.Code))
        {
            CompTimePolicy.ValidateHours(durationHours);
        }
    }

    private async Task<LeaveDurationEstimateDto> CalculateValidDurationAsync(
        Guid employeeId,
        DateTimeOffset startAt,
        DateTimeOffset endAt,
        LeaveCalculationMode calculationMode,
        CancellationToken cancellationToken)
    {
        var estimate = await durationCalculator.CalculateAsync(
            employeeId,
            startAt,
            endAt,
            calculationMode,
            cancellationToken);
        if (!estimate.CanSubmit)
        {
            throw new ApplicationValidationException(
                estimate.Messages.FirstOrDefault() ??
                "無法依有效班別計算請假時數。");
        }

        return estimate;
    }

    private static NormalizedLeaveInput NormalizeInput(
        LeaveType leaveType,
        CreateLeaveDraftRequest request) =>
        NormalizeInput(
            leaveType,
            request.StartAt,
            request.EndAt,
            request.CalendarStartDate,
            request.CalendarEndDate,
            request.PregnancyDurationCategory);

    private static NormalizedLeaveInput NormalizeInput(
        LeaveType leaveType,
        LeaveDurationEstimateRequest request) =>
        NormalizeInput(
            leaveType,
            request.StartAt,
            request.EndAt,
            request.CalendarStartDate,
            request.CalendarEndDate,
            request.PregnancyDurationCategory);

    private static NormalizedLeaveInput NormalizeInput(
        LeaveType leaveType,
        DateTimeOffset startAt,
        DateTimeOffset endAt,
        DateOnly? calendarStartDate,
        DateOnly? calendarEndDate,
        PregnancyDurationCategory? pregnancyDurationCategory)
    {
        if (leaveType.CalculationMode == LeaveCalculationMode.WorkingSchedule)
        {
            if (pregnancyDurationCategory.HasValue)
            {
                throw new ApplicationValidationException(
                    "一般班表假別不得設定流產假妊娠期間類別。");
            }

            return new NormalizedLeaveInput(
                startAt.ToUniversalTime(),
                endAt.ToUniversalTime(),
                null);
        }

        if (leaveType.CalculationMode != LeaveCalculationMode.CalendarDays ||
            !CalendarDayLeavePolicy.IsSupported(leaveType.Code))
        {
            throw new ApplicationValidationException(
                "此假別尚未支援員工申請流程。");
        }

        var startDate = calendarStartDate ??
            throw new ApplicationValidationException(
                "連續曆日假別必須填寫開始日期。");
        var range = CalendarDayLeavePolicy.Resolve(
            leaveType.Code,
            startDate,
            calendarEndDate,
            pregnancyDurationCategory);
        return new NormalizedLeaveInput(
            ToUtcBoundary(range.StartDate),
            ToUtcBoundary(range.EndDate.AddDays(1)),
            range);
    }

    private static void ValidatePersistedCalendarShape(
        LeaveRequest entity,
        LeaveType leaveType)
    {
        if (leaveType.CalculationMode != LeaveCalculationMode.CalendarDays)
        {
            if (entity.PregnancyDurationCategory.HasValue)
            {
                throw new ApplicationValidationException(
                    "一般班表假別含有不相容的流產假妊娠期間類別。");
            }

            return;
        }

        var startDate = ResolveCalendarStartDate(entity.StartAt);
        var endDate = ResolveCalendarEndDate(entity.EndAt);
        var normalized = NormalizeInput(
            leaveType,
            entity.StartAt,
            entity.EndAt,
            startDate,
            endDate,
            entity.PregnancyDurationCategory);
        if (normalized.StartAt != entity.StartAt.ToUniversalTime() ||
            normalized.EndAt != entity.EndAt.ToUniversalTime())
        {
            throw new ApplicationValidationException(
                "連續曆日請假日期與假別規則不一致，請返回草稿重新儲存。");
        }
    }

    private static DateTimeOffset ToUtcBoundary(DateOnly localDate)
    {
        var local = DateTime.SpecifyKind(
            localDate.ToDateTime(TimeOnly.MinValue),
            DateTimeKind.Unspecified);
        return new DateTimeOffset(
            TimeZoneInfo.ConvertTimeToUtc(local, TaipeiZone),
            TimeSpan.Zero);
    }

    private static DateOnly ResolveCalendarStartDate(
        DateTimeOffset startAtUtc) => DateOnly.FromDateTime(
        TimeZoneInfo.ConvertTime(startAtUtc, TaipeiZone).DateTime);

    private static DateOnly ResolveCalendarEndDate(
        DateTimeOffset endAtUtc) => DateOnly.FromDateTime(
        TimeZoneInfo.ConvertTime(endAtUtc, TaipeiZone).DateTime).AddDays(-1);

    private void AddHistory(
        LeaveRequest entity,
        ApprovalAction action,
        LeaveRequestStatus from,
        LeaveRequestStatus to,
        string? comment)
    {
        dbContext.LeaveApprovalHistories.Add(new LeaveApprovalHistory(
            Guid.NewGuid(), entity.Id, action, RequireUserId(),
            currentUser.DisplayName ?? RequireUserId(), comment, timeProvider.GetUtcNow(), from, to));
    }

    private void AddAudit(string action, LeaveRequest entity, object? oldValues, object? newValues) =>
        dbContext.AuditLogs.Add(AuditLogFactory.Create(
            currentUser, timeProvider, action, nameof(LeaveRequest), entity.Id.ToString(), oldValues, newValues));

    private async Task<LeaveRequestDto> GetOwnedAfterWriteAsync(Guid id, CancellationToken cancellationToken)
    {
        var employeeId = RequireEmployeeId();
        var entity = await DetailQuery().SingleAsync(x => x.Id == id && x.EmployeeId == employeeId, cancellationToken);
        return Map(entity);
    }

    private async Task<LeaveRequestDto> GetAfterReviewAsync(Guid id, CancellationToken cancellationToken)
    {
        var entity = await DetailQuery().SingleAsync(x => x.Id == id, cancellationToken);
        return Map(entity);
    }

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
            throw new ApplicationValidationException("請假資料寫入失敗，請重新整理後再試。");
        }
    }

    private void EnsureAuthenticated()
    {
        if (!currentUser.IsAuthenticated || !currentUser.HasPermission(PolicyNames.LeaveRequestSelfService))
        {
            throw new ForbiddenAccessException("請先登入。");
        }
    }

    private void EnsureApprover()
    {
        if (!currentUser.IsAuthenticated || !currentUser.HasPermission(PolicyNames.LeaveRequestApprove))
        {
            throw new ForbiddenAccessException("您沒有請假簽核權限。");
        }
    }

    private Guid RequireEmployeeId()
    {
        EnsureAuthenticated();
        return currentUser.EmployeeId
            ?? throw new ForbiddenAccessException("帳號尚未綁定員工資料，請洽系統管理員。");
    }

    private string RequireUserId() => currentUser.UserId
        ?? throw new ForbiddenAccessException("無法識別目前登入帳號。");

    private static void EnsureRowVersion(byte[] current, string supplied)
    {
        byte[] expected;
        try { expected = string.IsNullOrWhiteSpace(supplied) ? [] : Convert.FromBase64String(supplied); }
        catch (FormatException) { throw new ConcurrencyConflictException(); }
        if (!current.SequenceEqual(expected)) throw new ConcurrencyConflictException();
    }

    private static string GenerateRequestNumber(DateTimeOffset nowUtc) =>
        $"LR-{nowUtc:yyyyMMdd}-{Guid.NewGuid():N}"[..24].ToUpperInvariant();

    private static object Snapshot(LeaveRequest entity) => new
    {
        entity.RequestNumber,
        entity.EmployeeId,
        entity.LeaveTypeId,
        entity.StartAt,
        entity.EndAt,
        entity.DurationHours,
        entity.PregnancyDurationCategory,
        entity.Status
    };

    private static (DateOnly DateFrom, DateOnly DateTo)
        ResolveLocalDateRange(
            DateTimeOffset startAtUtc,
            DateTimeOffset endAtUtc)
    {
        var startLocal = TimeZoneInfo.ConvertTime(
            startAtUtc,
            TaipeiZone);
        var endLocal = TimeZoneInfo.ConvertTime(endAtUtc, TaipeiZone);
        var from = DateOnly.FromDateTime(startLocal.DateTime);
        var to = DateOnly.FromDateTime(endLocal.DateTime);
        if (endLocal.TimeOfDay == TimeSpan.Zero)
        {
            to = to.AddDays(-1);
        }

        return (from, to);
    }

    private static IReadOnlyList<AttendanceRecalculationKey>
        ResolveRecalculationKeys(LeaveRequest entity)
    {
        var (dateFrom, dateTo) = ResolveLocalDateRange(
            entity.StartAt,
            entity.EndAt);
        return Enumerable.Range(
                0,
                dateTo.DayNumber - dateFrom.DayNumber + 1)
            .Select(offset => new AttendanceRecalculationKey(
                entity.EmployeeId,
                dateFrom.AddDays(offset)))
            .Distinct()
            .ToArray();
    }

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

    private static LeaveRequestDto Map(LeaveRequest entity) => new(
        entity.Id,
        entity.RequestNumber,
        entity.EmployeeId,
        entity.Employee.EmployeeNumber,
        entity.Employee.ChineseName,
        entity.Employee.DepartmentId,
        entity.Employee.Department.Name,
        entity.LeaveTypeId,
        entity.LeaveType.Code,
        entity.LeaveType.Name,
        entity.StartAt,
        entity.EndAt,
        entity.DurationHours,
        entity.Reason,
        entity.Status,
        entity.SubmittedAtUtc,
        entity.ApprovedAtUtc,
        entity.RejectedAtUtc,
        entity.WithdrawnAtUtc,
        entity.CancellationRequestedAtUtc,
        entity.CancellationReason,
        entity.CancellationRequestedByUserId,
        entity.CreatedAtUtc,
        entity.UpdatedAtUtc,
        Convert.ToBase64String(entity.RowVersion),
        entity.ApprovalHistories.OrderBy(x => x.ActionAtUtc).Select(x => new LeaveApprovalHistoryDto(
            x.Id, x.Action, x.ActionByDisplayName, x.Comment, x.ActionAtUtc, x.FromStatus, x.ToStatus)).ToList(),
        entity.LeaveType.CalculationMode,
        entity.LeaveType.CalculationMode == LeaveCalculationMode.CalendarDays
            ? ResolveCalendarStartDate(entity.StartAt)
            : null,
        entity.LeaveType.CalculationMode == LeaveCalculationMode.CalendarDays
            ? ResolveCalendarEndDate(entity.EndAt)
            : null,
        entity.LeaveType.CalculationMode == LeaveCalculationMode.CalendarDays
            ? ResolveCalendarEndDate(entity.EndAt).DayNumber -
              ResolveCalendarStartDate(entity.StartAt).DayNumber + 1
            : null,
        entity.PregnancyDurationCategory);

    private sealed record NormalizedLeaveInput(
        DateTimeOffset StartAt,
        DateTimeOffset EndAt,
        CalendarDayLeaveRange? CalendarRange);
}
