using System.Security.Cryptography;
using HRSystem.Application.Abstractions.Persistence;
using HRSystem.Application.Abstractions.Security;
using HRSystem.Application.Common.Auditing;
using HRSystem.Application.Common.Exceptions;
using HRSystem.Application.Common.Validation;
using HRSystem.Application.Security;
using HRSystem.Domain.Attendance;
using HRSystem.Domain.Overtime;
using Microsoft.EntityFrameworkCore;

namespace HRSystem.Application.Attendance;

public sealed class AttendanceCorrectionService(
    IApplicationDbContext db,
    ICurrentUser currentUser,
    TimeProvider clock,
    IAttendanceManagementService attendanceManagement,
    IAttendanceRecalculationEngine recalculationEngine)
    : IAttendanceCorrectionService
{
    private const string ActiveRequestIndex =
        "UX_AttendanceCorrectionRequests_ActiveType";

    public async Task<IReadOnlyList<AttendanceCorrectionRequestDto>>
        GetMyRequestsAsync(
            AttendanceCorrectionQuery query,
            CancellationToken cancellationToken = default)
    {
        var employeeId = RequireEmployeeId();
        var entities = await BuildQuery(query)
            .Where(item => item.EmployeeId == employeeId)
            .ToListAsync(cancellationToken);
        return await MapManyAsync(entities, false, cancellationToken);
    }

    public async Task<AttendanceCorrectionRequestDto> GetMyRequestAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var employeeId = RequireEmployeeId();
        var entity = await DetailQuery().AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Id == id && item.EmployeeId == employeeId,
                cancellationToken)
            ?? throw new EntityNotFoundException("找不到出勤更正申請。");
        return await MapAsync(entity, false, cancellationToken);
    }

    public async Task<AttendanceCorrectionContextDto?>
        GetMyAttendanceContextAsync(
            Guid attendanceResultId,
            CancellationToken cancellationToken = default)
    {
        var employeeId = RequireEmployeeId();
        var result = await db.DailyAttendanceResults.AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Id == attendanceResultId &&
                    item.EmployeeId == employeeId,
                cancellationToken);
        return result is null
            ? null
            : await MapContextAsync(result, false, cancellationToken);
    }

    public async Task<AttendanceCorrectionRequestDto> CreateDraftAsync(
        CreateAttendanceCorrectionDraftRequest request,
        CancellationToken cancellationToken = default)
    {
        RequestValidator.Validate(request);
        var employeeId = RequireEmployeeId();
        var result = await GetOwnedAttendanceAsync(
            request.AttendanceResultId,
            employeeId,
            cancellationToken);
        ValidateRequestAgainstAttendance(result, request.RequestType,
            request.ProposedClockInAt, request.ProposedClockOutAt);
        var now = clock.GetUtcNow();
        var entity = new AttendanceCorrectionRequest(
            Guid.NewGuid(), employeeId, result.WorkDate, result.Id,
            request.RequestType, result.EffectiveClockInLocalTime,
            result.EffectiveClockOutLocalTime,
            AsLocal(request.ProposedClockInAt),
            AsLocal(request.ProposedClockOutAt), request.Reason,
            request.EmployeeReason, AttendanceCorrectionFingerprint.Compute(result),
            now);
        db.AttendanceCorrectionRequests.Add(entity);
        AddHistory(entity, AttendanceCorrectionRequestHistoryAction.Created,
            null, AttendanceCorrectionRequestStatus.Draft,
            request.EmployeeReason, now);
        AddAudit(AuditActions.AttendanceCorrectionRequestCreated, entity,
            null, AttendanceCorrectionRequestStatus.Draft, null);
        await SaveAsync(cancellationToken);
        return await GetMyRequestAsync(entity.Id, cancellationToken);
    }

    public async Task<AttendanceCorrectionRequestDto> UpdateDraftAsync(
        UpdateAttendanceCorrectionDraftRequest request,
        CancellationToken cancellationToken = default)
    {
        RequestValidator.Validate(request);
        var employeeId = RequireEmployeeId();
        var entity = await GetOwnedEntityAsync(request.Id, employeeId,
            cancellationToken);
        EnsureVersion(entity.RowVersion, request.RowVersion);
        var result = await GetOwnedAttendanceAsync(
            entity.AttendanceResultId!.Value, employeeId, cancellationToken);
        ValidateRequestAgainstAttendance(result, request.RequestType,
            request.ProposedClockInAt, request.ProposedClockOutAt);
        var now = clock.GetUtcNow();
        entity.UpdateDraft(request.RequestType,
            AsLocal(request.ProposedClockInAt),
            AsLocal(request.ProposedClockOutAt), request.Reason,
            request.EmployeeReason, AttendanceCorrectionFingerprint.Compute(result),
            now);
        AddHistory(entity, AttendanceCorrectionRequestHistoryAction.Updated,
            AttendanceCorrectionRequestStatus.Draft,
            AttendanceCorrectionRequestStatus.Draft,
            request.EmployeeReason, now);
        await SaveAsync(cancellationToken);
        return await GetMyRequestAsync(entity.Id, cancellationToken);
    }

    public Task<AttendanceCorrectionRequestDto> SubmitAsync(
        AttendanceCorrectionActionRequest request,
        CancellationToken cancellationToken = default) =>
        ExecuteOwnedTransitionAsync(request,
            AttendanceCorrectionRequestHistoryAction.Submitted,
            AuditActions.AttendanceCorrectionRequestSubmitted,
            async (entity, now, ct) =>
            {
                var result = await GetOwnedAttendanceAsync(
                    entity.AttendanceResultId!.Value, entity.EmployeeId, ct);
                EnsureFingerprint(entity.SourceFingerprint,
                    AttendanceCorrectionFingerprint.Compute(result));
                ValidateRequestAgainstAttendance(result, entity.RequestType,
                    entity.ProposedClockInAt, entity.ProposedClockOutAt);
                var duplicate = await db.AttendanceCorrectionRequests.AnyAsync(
                    item => item.Id != entity.Id &&
                        item.EmployeeId == entity.EmployeeId &&
                        item.WorkDate == entity.WorkDate &&
                        item.RequestType == entity.RequestType &&
                        (item.Status == AttendanceCorrectionRequestStatus.Submitted ||
                         item.Status == AttendanceCorrectionRequestStatus.Approved), ct);
                if (duplicate)
                    throw new ApplicationValidationException(
                        "同一日期與類型已有待審或已核准申請。");
                entity.Submit(now);
            }, cancellationToken);

    public Task<AttendanceCorrectionRequestDto> WithdrawAsync(
        AttendanceCorrectionActionRequest request,
        CancellationToken cancellationToken = default) =>
        ExecuteOwnedTransitionAsync(request,
            AttendanceCorrectionRequestHistoryAction.Withdrawn,
            AuditActions.AttendanceCorrectionRequestWithdrawn,
            (entity, now, _) =>
            {
                entity.Withdraw(request.Note, now);
                return Task.CompletedTask;
            }, cancellationToken);

    public async Task<AttendanceCorrectionFilterOptions>
        GetReviewFilterOptionsAsync(
            CancellationToken cancellationToken = default)
    {
        EnsureReviewer();
        var departments = await db.Departments.AsNoTracking()
            .OrderBy(item => item.Code)
            .Select(item => new AttendanceReviewDepartmentOption(
                item.Id, item.Code, item.Name))
            .ToListAsync(cancellationToken);
        var employees = await db.Employees.AsNoTracking()
            .OrderBy(item => item.EmployeeNumber)
            .Select(item => new AttendanceReviewEmployeeOption(
                item.Id, item.EmployeeNumber, item.ChineseName,
                item.DepartmentId, item.Department.Name))
            .ToListAsync(cancellationToken);
        return new AttendanceCorrectionFilterOptions(departments, employees);
    }

    public async Task<IReadOnlyList<AttendanceCorrectionRequestDto>>
        SearchForReviewAsync(
            AttendanceCorrectionQuery query,
            CancellationToken cancellationToken = default)
    {
        EnsureReviewer();
        var entities = await BuildQuery(query).ToListAsync(cancellationToken);
        return await MapManyAsync(entities, true, cancellationToken);
    }

    public async Task<AttendanceCorrectionRequestDto> ApproveAsync(
        ReviewAttendanceCorrectionRequest request,
        CancellationToken cancellationToken = default)
    {
        RequestValidator.Validate(request);
        EnsureReviewer();
        try
        {
            var id = await db.ExecuteSerializableAsync(async ct =>
            {
                var entity = await TrackedDetailQuery().SingleOrDefaultAsync(
                    item => item.Id == request.Id, ct)
                    ?? throw new EntityNotFoundException(
                        "找不到出勤更正申請。");
                EnsureVersion(entity.RowVersion, request.RowVersion);
                var result = await db.DailyAttendanceResults
                    .Include(item => item.LeaveSegments)
                    .SingleOrDefaultAsync(item =>
                        item.Id == entity.AttendanceResultId &&
                        item.EmployeeId == entity.EmployeeId, ct)
                    ?? throw new ApplicationValidationException(
                        "來源出勤結果已不存在，無法核准。");
                EnsureFingerprint(entity.SourceFingerprint,
                    AttendanceCorrectionFingerprint.Compute(result));
                EnsureFingerprint(entity.SourceFingerprint,
                    DecodeFingerprint(request.SourceFingerprint));

                AttendanceCorrectionAdjustmentResult? adjustment = null;
                if (entity.IsTimeCorrection)
                {
                    adjustment = await attendanceManagement
                        .ApplyCorrectionAdjustmentAsync(
                            BuildAdjustmentRequest(entity, result), ct);
                    await recalculationEngine.RecalculateKeysAsync(
                        [new AttendanceRecalculationKey(
                            entity.EmployeeId, entity.WorkDate)], ct);
                    await db.SaveChangesAsync(ct);
                }

                var now = clock.GetUtcNow();
                var from = entity.Status;
                entity.Approve(adjustment?.AdjustmentId, request.Note, now);
                AddHistory(entity,
                    AttendanceCorrectionRequestHistoryAction.Approved,
                    from, entity.Status, request.Note, now);
                AddAudit(AuditActions.AttendanceCorrectionRequestApproved,
                    entity, from, entity.Status, adjustment?.AdjustmentId);
                if (adjustment is not null)
                {
                    AddHistory(entity,
                        AttendanceCorrectionRequestHistoryAction.AdjustmentApplied,
                        AttendanceCorrectionRequestStatus.Approved,
                        AttendanceCorrectionRequestStatus.Approved,
                        $"AttendanceAdjustmentId={adjustment.AdjustmentId:D}", now);
                    AddAudit(AuditActions.AttendanceCorrectionAdjustmentApplied,
                        entity, AttendanceCorrectionRequestStatus.Approved,
                        AttendanceCorrectionRequestStatus.Approved,
                        adjustment.AdjustmentId);
                }
                await SaveAsync(ct);
                return entity.Id;
            }, cancellationToken);
            return await LoadDtoAsync(id, true, cancellationToken);
        }
        catch
        {
            db.ClearTrackedChanges();
            throw;
        }
    }

    public async Task<AttendanceCorrectionRequestDto> RejectAsync(
        ReviewAttendanceCorrectionRequest request,
        CancellationToken cancellationToken = default)
    {
        RequestValidator.Validate(request);
        EnsureReviewer();
        try
        {
            var id = await db.ExecuteSerializableAsync(async ct =>
            {
                var entity = await TrackedDetailQuery().SingleOrDefaultAsync(
                    item => item.Id == request.Id, ct)
                    ?? throw new EntityNotFoundException(
                        "找不到出勤更正申請。");
                EnsureVersion(entity.RowVersion, request.RowVersion);
                var from = entity.Status;
                var now = clock.GetUtcNow();
                entity.Reject(request.Note ?? string.Empty, now);
                AddHistory(entity,
                    AttendanceCorrectionRequestHistoryAction.Rejected,
                    from, entity.Status, request.Note, now);
                AddAudit(AuditActions.AttendanceCorrectionRequestRejected,
                    entity, from, entity.Status, null);
                await SaveAsync(ct);
                return entity.Id;
            }, cancellationToken);
            return await LoadDtoAsync(id, true, cancellationToken);
        }
        catch
        {
            db.ClearTrackedChanges();
            throw;
        }
    }

    private async Task<AttendanceCorrectionRequestDto>
        ExecuteOwnedTransitionAsync(
            AttendanceCorrectionActionRequest request,
            AttendanceCorrectionRequestHistoryAction action,
            string auditAction,
            Func<AttendanceCorrectionRequest, DateTimeOffset,
                CancellationToken, Task> transition,
            CancellationToken cancellationToken)
    {
        RequestValidator.Validate(request);
        var employeeId = RequireEmployeeId();
        try
        {
            var id = await db.ExecuteSerializableAsync(async ct =>
            {
                var entity = await GetOwnedEntityAsync(request.Id, employeeId, ct);
                EnsureVersion(entity.RowVersion, request.RowVersion);
                var from = entity.Status;
                var now = clock.GetUtcNow();
                await transition(entity, now, ct);
                AddHistory(entity, action, from, entity.Status,
                    request.Note, now);
                AddAudit(auditAction, entity, from, entity.Status, null);
                await SaveAsync(ct);
                return entity.Id;
            }, cancellationToken);
            return await GetMyRequestAsync(id, cancellationToken);
        }
        catch (DbUpdateException exception) when (
            db.IsUniqueConstraintViolation(exception, ActiveRequestIndex))
        {
            db.ClearTrackedChanges();
            throw new ApplicationValidationException(
                "同一日期與類型已有待審或已核准申請。");
        }
        catch
        {
            db.ClearTrackedChanges();
            throw;
        }
    }

    private IQueryable<AttendanceCorrectionRequest> BuildQuery(
        AttendanceCorrectionQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        var data = DetailQuery().AsNoTracking();
        if (query.StartDate.HasValue)
            data = data.Where(item => item.WorkDate >= query.StartDate.Value);
        if (query.EndDate.HasValue)
            data = data.Where(item => item.WorkDate <= query.EndDate.Value);
        if (query.DepartmentId.HasValue)
            data = data.Where(item =>
                item.Employee.DepartmentId == query.DepartmentId.Value);
        if (query.EmployeeId.HasValue)
            data = data.Where(item => item.EmployeeId == query.EmployeeId.Value);
        if (query.Status.HasValue)
            data = data.Where(item => item.Status == query.Status.Value);
        if (query.RequestType.HasValue)
            data = data.Where(item => item.RequestType == query.RequestType.Value);
        if (query.PendingOnly)
            data = data.Where(item =>
                item.Status == AttendanceCorrectionRequestStatus.Submitted);
        return query.Descending
            ? data.OrderByDescending(item => item.WorkDate)
                .ThenByDescending(item => item.SubmittedAtUtc)
            : data.OrderBy(item => item.WorkDate)
                .ThenBy(item => item.SubmittedAtUtc);
    }

    private IQueryable<AttendanceCorrectionRequest> DetailQuery() =>
        db.AttendanceCorrectionRequests
            .Include(item => item.Employee)
                .ThenInclude(item => item.Department)
            .Include(item => item.AttendanceResult)
            .Include(item => item.Histories.OrderBy(history =>
                history.OccurredAtUtc));

    private IQueryable<AttendanceCorrectionRequest> TrackedDetailQuery() =>
        db.AttendanceCorrectionRequests
            .Include(item => item.Employee)
                .ThenInclude(item => item.Department)
            .Include(item => item.AttendanceResult)
            .Include(item => item.Histories);

    private async Task<AttendanceCorrectionRequest> GetOwnedEntityAsync(
        Guid id, Guid employeeId, CancellationToken cancellationToken) =>
        await TrackedDetailQuery().SingleOrDefaultAsync(
            item => item.Id == id && item.EmployeeId == employeeId,
            cancellationToken)
        ?? throw new EntityNotFoundException("找不到出勤更正申請。");

    private async Task<DailyAttendanceResult> GetOwnedAttendanceAsync(
        Guid id, Guid employeeId, CancellationToken cancellationToken) =>
        await db.DailyAttendanceResults.SingleOrDefaultAsync(
            item => item.Id == id && item.EmployeeId == employeeId,
            cancellationToken)
        ?? throw new EntityNotFoundException("找不到本人的出勤結果。");

    private static void ValidateRequestAgainstAttendance(
        DailyAttendanceResult result,
        AttendanceCorrectionRequestType requestType,
        DateTime? proposedClockInAt,
        DateTime? proposedClockOutAt)
    {
        var validSource = requestType switch
        {
            AttendanceCorrectionRequestType.MissingClockIn =>
                result.MissingClockIn,
            AttendanceCorrectionRequestType.MissingClockOut =>
                result.MissingClockOut,
            AttendanceCorrectionRequestType.MissingBoth =>
                result.MissingClockIn && result.MissingClockOut,
            AttendanceCorrectionRequestType.ClockInCorrection =>
                !result.MissingClockIn &&
                result.EffectiveClockInLocalTime.HasValue,
            AttendanceCorrectionRequestType.ClockOutCorrection =>
                !result.MissingClockOut &&
                result.EffectiveClockOutLocalTime.HasValue,
            AttendanceCorrectionRequestType.LateExplanation => result.IsLate,
            AttendanceCorrectionRequestType.EarlyLeaveExplanation =>
                result.IsEarlyLeave,
            _ => false
        };
        if (!validSource)
            throw new ApplicationValidationException(
                "目前出勤狀態不符合此申請類型。");

        ValidateProposedTime(result.WorkDate, proposedClockInAt, false, "上班");
        ValidateProposedTime(result.WorkDate, proposedClockOutAt, true, "下班");
        if (proposedClockInAt.HasValue && proposedClockOutAt.HasValue &&
            proposedClockOutAt.Value <= proposedClockInAt.Value)
            throw new ApplicationValidationException(
                "建議下班時間必須晚於建議上班時間。");
    }

    private static void ValidateProposedTime(
        DateOnly workDate, DateTime? value, bool allowNextDate, string field)
    {
        if (!value.HasValue) return;
        var date = DateOnly.FromDateTime(value.Value);
        if (date != workDate && !(allowNextDate && date == workDate.AddDays(1)))
            throw new ApplicationValidationException(
                allowNextDate
                    ? $"建議{field}時間必須在工作日或隔日。"
                    : $"建議{field}時間必須在工作日。");
    }

    private AttendanceAdjustmentRequest BuildAdjustmentRequest(
        AttendanceCorrectionRequest request,
        DailyAttendanceResult result)
    {
        var adjustClockIn = request.RequestType is
            AttendanceCorrectionRequestType.MissingClockIn or
            AttendanceCorrectionRequestType.MissingBoth or
            AttendanceCorrectionRequestType.ClockInCorrection;
        var adjustClockOut = request.RequestType is
            AttendanceCorrectionRequestType.MissingClockOut or
            AttendanceCorrectionRequestType.MissingBoth or
            AttendanceCorrectionRequestType.ClockOutCorrection;
        return new AttendanceAdjustmentRequest
        {
            DailyAttendanceResultId = result.Id,
            AdjustClockIn = adjustClockIn,
            RecognizedClockInLocalTime = adjustClockIn
                ? request.ProposedClockInAt : null,
            AdjustClockOut = adjustClockOut,
            RecognizedClockOutLocalTime = adjustClockOut
                ? request.ProposedClockOutAt : null,
            Reason = MapAdjustmentReason(request.Reason),
            Note = $"出勤更正申請 {request.Id:D}（{request.Reason}）",
            RowVersion = Convert.ToBase64String(result.RowVersion)
        };
    }

    private static AttendanceAdjustmentReason MapAdjustmentReason(
        AttendanceCorrectionReason reason) => reason switch
        {
            AttendanceCorrectionReason.ForgotPunch =>
                AttendanceAdjustmentReason.ForgotPunch,
            AttendanceCorrectionReason.DeviceFailure =>
                AttendanceAdjustmentReason.DeviceFailure,
            AttendanceCorrectionReason.OfficialBusiness =>
                AttendanceAdjustmentReason.OfficialBusiness,
            _ => AttendanceAdjustmentReason.ManagerApproved
        };

    private async Task<IReadOnlyList<AttendanceCorrectionRequestDto>>
        MapManyAsync(
            IReadOnlyList<AttendanceCorrectionRequest> entities,
            bool includeReviewerDetails,
            CancellationToken cancellationToken)
    {
        var result = new List<AttendanceCorrectionRequestDto>(entities.Count);
        foreach (var entity in entities)
            result.Add(await MapAsync(entity, includeReviewerDetails,
                cancellationToken));
        return result;
    }

    private async Task<AttendanceCorrectionRequestDto> LoadDtoAsync(
        Guid id, bool includeReviewerDetails,
        CancellationToken cancellationToken)
    {
        var entity = await DetailQuery().AsNoTracking().SingleAsync(
            item => item.Id == id, cancellationToken);
        return await MapAsync(entity, includeReviewerDetails, cancellationToken);
    }

    private async Task<AttendanceCorrectionRequestDto> MapAsync(
        AttendanceCorrectionRequest entity,
        bool includeReviewerDetails,
        CancellationToken cancellationToken)
    {
        var currentFingerprint = entity.AttendanceResult is null
            ? null
            : AttendanceCorrectionFingerprint.Compute(entity.AttendanceResult);
        var stale = currentFingerprint is null ||
            !CryptographicOperations.FixedTimeEquals(
                entity.SourceFingerprint, currentFingerprint);
        AttendanceCorrectionContextDto? context = entity.AttendanceResult is null
            ? null
            : await MapContextAsync(entity.AttendanceResult,
                includeReviewerDetails, cancellationToken);
        var histories = entity.Histories.OrderBy(item => item.OccurredAtUtc)
            .Select(item => new AttendanceCorrectionHistoryDto(
                item.Action, item.FromStatus, item.ToStatus,
                includeReviewerDetails || item.Action is
                    AttendanceCorrectionRequestHistoryAction.Created or
                    AttendanceCorrectionRequestHistoryAction.Updated or
                    AttendanceCorrectionRequestHistoryAction.Submitted or
                    AttendanceCorrectionRequestHistoryAction.Withdrawn
                    ? item.Note : null,
                item.OccurredAtUtc))
            .ToList();
        return new AttendanceCorrectionRequestDto(
            entity.Id, entity.EmployeeId, entity.Employee.EmployeeNumber,
            entity.Employee.ChineseName, entity.Employee.DepartmentId,
            entity.Employee.Department.Name, entity.WorkDate,
            entity.AttendanceResultId, entity.RequestType, entity.Status,
            entity.OriginalClockInAt, entity.OriginalClockOutAt,
            entity.ProposedClockInAt, entity.ProposedClockOutAt,
            entity.Reason, entity.EmployeeReason,
            includeReviewerDetails ? entity.ReviewerNote : null,
            stale, Convert.ToBase64String(entity.SourceFingerprint),
            entity.AppliedAttendanceAdjustmentId, entity.SubmittedAtUtc,
            entity.ApprovedAtUtc, entity.RejectedAtUtc,
            entity.WithdrawnAtUtc, Convert.ToBase64String(entity.RowVersion),
            context, histories);
    }

    private async Task<AttendanceCorrectionContextDto> MapContextAsync(
        DailyAttendanceResult result,
        bool includeAdminRawSummary,
        CancellationToken cancellationToken)
    {
        var adjustmentCount = await db.AttendanceAdjustments.AsNoTracking()
            .CountAsync(item => item.DailyAttendanceResultId == result.Id,
                cancellationToken);
        var localStart = DateTime.SpecifyKind(
            result.WorkDate.ToDateTime(TimeOnly.MinValue),
            DateTimeKind.Unspecified);
        var localEnd = localStart.AddDays(1);
        var rawCount = includeAdminRawSummary
            ? await db.AttendanceRawEvents.AsNoTracking().CountAsync(item =>
                item.EmployeeId == result.EmployeeId &&
                item.EventLocalDateTime >= localStart &&
                item.EventLocalDateTime < localEnd,
                cancellationToken)
            : 0;
        var overtime = await db.OvertimeRequests.AsNoTracking()
            .Where(item => item.EmployeeId == result.EmployeeId &&
                item.OvertimeDate == result.WorkDate)
            .OrderByDescending(item => item.CreatedAtUtc)
            .Select(item => item.Status.ToString())
            .FirstOrDefaultAsync(cancellationToken);
        return new AttendanceCorrectionContextDto(
            result.Id, result.WorkDate, result.ScheduledStartTimeSnapshot,
            result.ScheduledEndTimeSnapshot,
            result.EffectiveClockInLocalTime,
            result.EffectiveClockOutLocalTime, result.MissingClockIn,
            result.MissingClockOut, result.LateSeconds / 60,
            result.EarlyLeaveSeconds / 60, result.Status.ToString(),
            result.IsAdjusted, adjustmentCount,
            result.ApprovedLeaveMinutes,
            overtime is null ? "無" : overtime,
            includeAdminRawSummary ? $"{rawCount} 筆原始打卡" : "僅管理員可檢視");
    }

    private void AddHistory(
        AttendanceCorrectionRequest entity,
        AttendanceCorrectionRequestHistoryAction action,
        AttendanceCorrectionRequestStatus? from,
        AttendanceCorrectionRequestStatus to,
        string? note,
        DateTimeOffset now)
    {
        db.AttendanceCorrectionRequestHistories.Add(
            new AttendanceCorrectionRequestHistory(
                Guid.NewGuid(), entity.Id, action, from, to,
                RequireUserId(), currentUser.EmployeeId, note, now));
    }

    private void AddAudit(
        string action,
        AttendanceCorrectionRequest entity,
        AttendanceCorrectionRequestStatus? from,
        AttendanceCorrectionRequestStatus to,
        Guid? adjustmentId)
    {
        db.AuditLogs.Add(AuditLogFactory.Create(
            currentUser, clock, action, nameof(AttendanceCorrectionRequest),
            entity.Id.ToString(),
            from.HasValue ? new { Status = from.Value } : null,
            new
            {
                RequestId = entity.Id,
                entity.EmployeeId,
                entity.WorkDate,
                entity.RequestType,
                FromStatus = from,
                ToStatus = to,
                AdjustmentId = adjustmentId
            }));
    }

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConcurrencyConflictException();
        }
        catch (DbUpdateException exception) when (
            db.IsUniqueConstraintViolation(exception, ActiveRequestIndex))
        {
            throw new ApplicationValidationException(
                "同一日期與類型已有待審或已核准申請。");
        }
    }

    private Guid RequireEmployeeId()
    {
        if (!currentUser.IsAuthenticated ||
            !currentUser.HasPermission(PolicyNames.AttendanceSelfService))
            throw new ForbiddenAccessException("沒有員工出勤自助服務權限。");
        return currentUser.EmployeeId
            ?? throw new ForbiddenAccessException("目前帳號未綁定員工資料。");
    }

    private void EnsureReviewer()
    {
        if (!currentUser.IsAuthenticated ||
            !currentUser.HasPermission(PolicyNames.AttendanceManage))
            throw new ForbiddenAccessException("沒有出勤更正審核權限。");
    }

    private string RequireUserId() => currentUser.UserId
        ?? throw new ForbiddenAccessException("必須先登入系統。");

    private static void EnsureVersion(byte[] current, string supplied)
    {
        byte[] expected;
        try
        {
            expected = string.IsNullOrWhiteSpace(supplied)
                ? [] : Convert.FromBase64String(supplied);
        }
        catch (FormatException)
        {
            throw new ConcurrencyConflictException();
        }
        if (!current.SequenceEqual(expected))
            throw new ConcurrencyConflictException();
    }

    private static byte[] DecodeFingerprint(string supplied)
    {
        try
        {
            var value = Convert.FromBase64String(supplied);
            if (value.Length == 32) return value;
        }
        catch (FormatException)
        {
        }
        throw new ConcurrencyConflictException(
            "出勤資料已變更，請重新確認。");
    }

    private static void EnsureFingerprint(byte[] expected, byte[] actual)
    {
        if (!CryptographicOperations.FixedTimeEquals(expected, actual))
            throw new ConcurrencyConflictException(
                "出勤資料已變更，請重新確認。");
    }

    private static DateTime? AsLocal(DateTime? value) => value.HasValue
        ? DateTime.SpecifyKind(value.Value, DateTimeKind.Unspecified)
        : null;
}
