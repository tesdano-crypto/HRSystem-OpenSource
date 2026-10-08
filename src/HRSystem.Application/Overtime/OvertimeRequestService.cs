using HRSystem.Application.Abstractions.Persistence;
using HRSystem.Application.Abstractions.Security;
using HRSystem.Application.Common.Auditing;
using HRSystem.Application.Common.Exceptions;
using HRSystem.Application.Common.Validation;
using HRSystem.Application.Security;
using HRSystem.Application.Attendance;
using HRSystem.Domain.Attendance;
using HRSystem.Domain.Overtime;
using Microsoft.EntityFrameworkCore;

namespace HRSystem.Application.Overtime;

public sealed class OvertimeRequestService(
    IApplicationDbContext db,
    ICurrentUser currentUser,
    TimeProvider clock) : IOvertimeRequestService
{
    public async Task<IReadOnlyList<OvertimeRequestDto>> GetMyRequestsAsync(
        OvertimeRequestQuery query, CancellationToken cancellationToken = default)
    {
        var employeeId = RequireEmployeeId();
        var entities = await Query(query).Where(item => item.EmployeeId == employeeId)
            .Include(item => item.Employee).ThenInclude(item => item.Department)
            .Include(item => item.Histories)
            .Include(item => item.Recognition).ThenInclude(item => item!.Histories)
            .ToListAsync(cancellationToken);
        var result = new List<OvertimeRequestDto>(entities.Count);
        foreach (var entity in entities)
            result.Add(await MapWithContextAsync(entity, cancellationToken));
        return result;
    }

    public async Task<OvertimeRequestDto> GetMyRequestAsync(
        Guid id, CancellationToken cancellationToken = default)
    {
        var employeeId = RequireEmployeeId();
        var entity = await DetailQuery().SingleOrDefaultAsync(
            item => item.Id == id && item.EmployeeId == employeeId,
            cancellationToken) ?? throw new EntityNotFoundException("找不到加班申請。");
        return await MapWithContextAsync(entity, cancellationToken);
    }

    public async Task<OvertimeRequestDto> CreateDraftAsync(
        CreateOvertimeDraftRequest request,
        CancellationToken cancellationToken = default)
    {
        RequestValidator.Validate(request);
        var employeeId = RequireEmployeeId();
        await EnsureActiveEmployeeAsync(employeeId, cancellationToken);
        if (request.NonWorkingDayEvidenceDate.HasValue)
            return await CreateEvidenceDraftAsync(request, employeeId, cancellationToken);
        var now = clock.GetUtcNow();
        var actor = RequireUserId();
        var entity = new OvertimeRequest(Guid.NewGuid(), employeeId,
            AsLocal(request.PlannedStartAt), AsLocal(request.PlannedEndAt),
            request.Reason, actor, now);
        db.OvertimeRequests.Add(entity);
        AddHistory(entity, OvertimeRequestHistoryAction.Created, null,
            OvertimeRequestStatus.Draft, entity.Reason, now);
        AddAudit(AuditActions.OvertimeRequestCreated, entity, null,
            Snapshot(entity));
        await SaveAsync(cancellationToken);
        return await GetMyRequestAsync(entity.Id, cancellationToken);
    }

    public async Task<OvertimeRequestDto> UpdateDraftAsync(
        UpdateOvertimeDraftRequest request,
        CancellationToken cancellationToken = default)
    {
        RequestValidator.Validate(request);
        var entity = await GetOwnedEntityAsync(request.Id, cancellationToken);
        EnsureVersion(entity.RowVersion, request.RowVersion);
        var now = clock.GetUtcNow();
        entity.UpdateDraft(AsLocal(request.PlannedStartAt),
            AsLocal(request.PlannedEndAt), request.Reason, RequireUserId(), now);
        AddHistory(entity, OvertimeRequestHistoryAction.Updated,
            OvertimeRequestStatus.Draft, OvertimeRequestStatus.Draft,
            entity.Reason, now);
        await SaveAsync(cancellationToken);
        return await GetMyRequestAsync(entity.Id, cancellationToken);
    }

    private async Task<OvertimeRequestDto> CreateEvidenceDraftAsync(CreateOvertimeDraftRequest request,
        Guid employeeId, CancellationToken cancellationToken)
    {
        var date = request.NonWorkingDayEvidenceDate!.Value;
        if (DateOnly.FromDateTime(request.PlannedStartAt) != date ||
            request.PlannedEndAt > date.AddDays(1).ToDateTime(TimeOnly.MinValue))
            throw new ApplicationValidationException("申請時段必須屬於所選打卡日期。");
        var id = await db.ExecuteSerializableAsync(async ct =>
        {
            var evidence = (await NonWorkingDayPunchReader.ReadAsync(db, [employeeId], date, date, [], ct))
                .GetValueOrDefault((employeeId, date));
            if (evidence?.HasNonWorkingPunch != true)
                throw new ApplicationValidationException("查無此日期的非工作日打卡證據。");
            var existing = await db.OvertimeRequests.Where(x => x.EmployeeId == employeeId &&
                (x.Status == OvertimeRequestStatus.Draft || x.Status == OvertimeRequestStatus.Submitted || x.Status == OvertimeRequestStatus.Approved) &&
                x.PlannedStartAt < request.PlannedEndAt && x.PlannedEndAt > request.PlannedStartAt)
                .Select(x => (Guid?)x.Id).FirstOrDefaultAsync(ct);
            if (existing.HasValue) throw new OvertimeDraftOverlapException(existing.Value);
            var now = clock.GetUtcNow();
            var entity = new OvertimeRequest(Guid.NewGuid(), employeeId, AsLocal(request.PlannedStartAt),
                AsLocal(request.PlannedEndAt), request.Reason, RequireUserId(), now);
            db.OvertimeRequests.Add(entity);
            AddHistory(entity, OvertimeRequestHistoryAction.Created, null, OvertimeRequestStatus.Draft, entity.Reason, now);
            AddAudit(AuditActions.OvertimeRequestCreated, entity, null, Snapshot(entity));
            AddAudit(AuditActions.OvertimeDraftCreatedFromNonWorkingDayPunch, entity, null,
                new { entity.EmployeeId, AttendanceDate = date, EvidenceFingerprint = evidence.Fingerprint, entity.Status });
            await SaveAsync(ct);
            return entity.Id;
        }, cancellationToken);
        return await GetMyRequestAsync(id, cancellationToken);
    }

    public Task<OvertimeRequestDto> SubmitAsync(
        OvertimeActionRequest request, CancellationToken cancellationToken = default) =>
        ExecuteOwnedAsync(request, OvertimeRequestHistoryAction.Submitted,
            AuditActions.OvertimeRequestSubmitted,
            async (entity, actor, now, ct) =>
            {
                await EnsureNoOverlapAsync(entity, ct);
                entity.Submit(actor, now);
            }, cancellationToken);

    public Task<OvertimeRequestDto> WithdrawAsync(
        OvertimeActionRequest request, CancellationToken cancellationToken = default) =>
        ExecuteOwnedAsync(request, OvertimeRequestHistoryAction.Withdrawn,
            AuditActions.OvertimeRequestWithdrawn,
            (entity, actor, now, _) =>
            {
                entity.Withdraw(request.Note, actor, now);
                return Task.CompletedTask;
            }, cancellationToken);

    public async Task<IReadOnlyList<OvertimeRequestDto>> SearchForReviewAsync(
        OvertimeRequestQuery query, CancellationToken cancellationToken = default)
    {
        EnsureReviewer();
        var entities = await Query(query).Include(item => item.Histories)
            .Include(item => item.Employee).ThenInclude(item => item.Department)
            .Include(item => item.Recognition).ThenInclude(item => item!.Histories)
            .ToListAsync(cancellationToken);
        var result = new List<OvertimeRequestDto>(entities.Count);
        foreach (var entity in entities)
            result.Add(await MapWithContextAsync(entity, cancellationToken));
        return result;
    }

    public Task<OvertimeRequestDto> ApproveAsync(
        ReviewOvertimeRequest request, CancellationToken cancellationToken = default) =>
        ExecuteReviewAsync(request, OvertimeRequestHistoryAction.Approved,
            AuditActions.OvertimeRequestApproved,
            async (entity, actor, now, ct) =>
            {
                await EnsureNoOverlapAsync(entity, ct);
                entity.Approve(request.Reason, request.Note, actor, now);
            }, cancellationToken);

    public Task<OvertimeRequestDto> RejectAsync(
        ReviewOvertimeRequest request, CancellationToken cancellationToken = default) =>
        ExecuteReviewAsync(request, OvertimeRequestHistoryAction.Rejected,
            AuditActions.OvertimeRequestRejected,
            (entity, actor, now, _) =>
            {
                entity.Reject(request.Reason, request.Note, actor, now);
                return Task.CompletedTask;
            }, cancellationToken);

    private async Task<OvertimeRequestDto> ExecuteOwnedAsync(
        OvertimeActionRequest request,
        OvertimeRequestHistoryAction action,
        string auditAction,
        Func<OvertimeRequest, string, DateTimeOffset, CancellationToken, Task> transition,
        CancellationToken cancellationToken)
    {
        RequestValidator.Validate(request);
        try
        {
            return await db.ExecuteSerializableAsync(async ct =>
            {
                var entity = await GetOwnedEntityAsync(request.Id, ct);
                EnsureVersion(entity.RowVersion, request.RowVersion);
                var from = entity.Status;
                var now = clock.GetUtcNow();
                await transition(entity, RequireUserId(), now, ct);
                AddHistory(entity, action, from, entity.Status,
                    action == OvertimeRequestHistoryAction.Withdrawn
                        ? request.Note : entity.Reason, now);
                AddAudit(auditAction, entity, new { Status = from }, Snapshot(entity));
                await SaveAsync(ct);
                return await GetMyRequestAsync(entity.Id, ct);
            }, cancellationToken);
        }
        catch { db.ClearTrackedChanges(); throw; }
    }

    private async Task<OvertimeRequestDto> ExecuteReviewAsync(
        ReviewOvertimeRequest request,
        OvertimeRequestHistoryAction action,
        string auditAction,
        Func<OvertimeRequest, string, DateTimeOffset, CancellationToken, Task> transition,
        CancellationToken cancellationToken)
    {
        RequestValidator.Validate(request);
        EnsureReviewer();
        try
        {
            return await db.ExecuteSerializableAsync(async ct =>
            {
                var entity = await DetailQuery().SingleOrDefaultAsync(
                    item => item.Id == request.Id, ct)
                    ?? throw new EntityNotFoundException("找不到加班申請。");
                EnsureVersion(entity.RowVersion, request.RowVersion);
                var from = entity.Status;
                var now = clock.GetUtcNow();
                await transition(entity, RequireUserId(), now, ct);
                AddHistory(entity, action, from, entity.Status,
                    request.Note ?? request.Reason.ToString(), now);
                AddAudit(auditAction, entity, new { Status = from }, Snapshot(entity));
                await SaveAsync(ct);
                return await MapWithContextAsync(entity, ct);
            }, cancellationToken);
        }
        catch { db.ClearTrackedChanges(); throw; }
    }

    private IQueryable<OvertimeRequest> Query(OvertimeRequestQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        var data = db.OvertimeRequests.AsNoTracking();
        if (query.RequestId.HasValue) data = data.Where(x => x.Id == query.RequestId);
        if (query.StartDate.HasValue) data = data.Where(x => x.OvertimeDate >= query.StartDate);
        if (query.EndDate.HasValue) data = data.Where(x => x.OvertimeDate <= query.EndDate);
        if (query.DepartmentId.HasValue) data = data.Where(x => x.Employee.DepartmentId == query.DepartmentId);
        if (query.EmployeeId.HasValue) data = data.Where(x => x.EmployeeId == query.EmployeeId);
        if (query.Status.HasValue) data = data.Where(x => x.Status == query.Status);
        if (query.PendingOnly) data = data.Where(x => x.Status == OvertimeRequestStatus.Submitted);
        return query.Descending
            ? data.OrderByDescending(x => x.OvertimeDate).ThenByDescending(x => x.PlannedStartAt)
            : data.OrderBy(x => x.OvertimeDate).ThenBy(x => x.PlannedStartAt);
    }

    private IQueryable<OvertimeRequest> DetailQuery() => db.OvertimeRequests
        .Include(item => item.Employee).ThenInclude(item => item.Department)
        .Include(item => item.Histories.OrderBy(history => history.OccurredAtUtc))
        .Include(item => item.Recognition).ThenInclude(item => item!.Histories);

    private async Task<OvertimeRequest> GetOwnedEntityAsync(Guid id, CancellationToken ct)
    {
        var employeeId = RequireEmployeeId();
        return await DetailQuery().SingleOrDefaultAsync(
            item => item.Id == id && item.EmployeeId == employeeId, ct)
            ?? throw new EntityNotFoundException("找不到加班申請。");
    }

    private async Task EnsureNoOverlapAsync(OvertimeRequest entity, CancellationToken ct)
    {
        var overlap = await db.OvertimeRequests.AnyAsync(item =>
            item.Id != entity.Id && item.EmployeeId == entity.EmployeeId &&
            (item.Status == OvertimeRequestStatus.Submitted ||
             item.Status == OvertimeRequestStatus.Approved) &&
            item.PlannedStartAt < entity.PlannedEndAt &&
            item.PlannedEndAt > entity.PlannedStartAt, ct);
        if (overlap) throw new ApplicationValidationException(
            "此時段與已送出或已核准的加班申請重疊。");
    }

    private async Task EnsureActiveEmployeeAsync(Guid employeeId, CancellationToken ct)
    {
        if (!await db.Employees.AnyAsync(item => item.Id == employeeId && item.IsActive, ct))
            throw new ForbiddenAccessException("目前員工資料未啟用，無法申請加班。");
    }

    private async Task<OvertimeRequestDto> MapWithContextAsync(
        OvertimeRequest entity, CancellationToken ct)
    {
        var attendance = await db.DailyAttendanceResults.AsNoTracking()
            .Where(item => item.EmployeeId == entity.EmployeeId &&
                item.WorkDate == entity.OvertimeDate)
            .Select(item => new
            {
                item.Id,
                item.RowVersion,
                item.WorkDate,
                item.ScheduledEndTimeSnapshot,
                item.EffectiveClockOutLocalTime,
                item.IsOvernightShiftSnapshot,
                item.MissingClockIn,
                item.MissingClockOut,
                item.ApprovedLeaveMinutes,
                item.IsRequiredWorkday
            })
            .SingleOrDefaultAsync(ct);
        OvertimeAttendanceContextDto? context = null;
        if (attendance is not null)
        {
            var overstayMinutes = 0;
            if (attendance.ScheduledEndTimeSnapshot.HasValue &&
                attendance.EffectiveClockOutLocalTime.HasValue)
            {
                var scheduledEnd = attendance.WorkDate.ToDateTime(
                    attendance.ScheduledEndTimeSnapshot.Value);
                if (attendance.IsOvernightShiftSnapshot == true)
                    scheduledEnd = scheduledEnd.AddDays(1);
                overstayMinutes = Math.Max(0, (int)(
                    attendance.EffectiveClockOutLocalTime.Value - scheduledEnd).TotalMinutes);
            }
            context = new OvertimeAttendanceContextDto(
                attendance.ScheduledEndTimeSnapshot,
                attendance.EffectiveClockOutLocalTime,
                overstayMinutes,
                null,
                attendance.MissingClockIn || attendance.MissingClockOut,
                attendance.ApprovedLeaveMinutes,
                attendance.IsRequiredWorkday);
        }
        var dto = Map(entity, context);
        if (entity.Status != OvertimeRequestStatus.Approved) return dto;
        var source = OvertimeRecognitionPolicy.Build(
            entity.Id, entity.EmployeeId, entity.OvertimeDate, entity.Status,
            entity.RowVersion, entity.PlannedStartAt, entity.PlannedEndAt,
            attendance?.Id, attendance?.RowVersion,
            attendance?.ScheduledEndTimeSnapshot,
            attendance?.IsOvernightShiftSnapshot == true,
            attendance?.EffectiveClockOutLocalTime,
            attendance?.MissingClockIn ?? true,
            attendance?.MissingClockOut ?? true);
        return dto with
        {
            Recognition = OvertimeRecognitionService.ToDto(
                entity, entity.Recognition, source)
        };
    }

    private static OvertimeRequestDto Map(
        OvertimeRequest entity, OvertimeAttendanceContextDto? context) => new(
        entity.Id, entity.EmployeeId, entity.Employee.EmployeeNumber,
        entity.Employee.ChineseName, entity.Employee.DepartmentId,
        entity.Employee.Department.Name, entity.OvertimeDate,
        entity.PlannedStartAt, entity.PlannedEndAt, entity.RequestedMinutes,
        entity.Reason, entity.Status, entity.ReviewReason, entity.ReviewNote,
        entity.SubmittedAtUtc, entity.ApprovedAtUtc, entity.RejectedAtUtc,
        entity.WithdrawnAtUtc, entity.CreatedAtUtc,
        Convert.ToBase64String(entity.RowVersion),
        entity.Histories.OrderBy(item => item.OccurredAtUtc)
            .Select(item => new OvertimeRequestHistoryDto(
                item.Action, item.FromStatus, item.ToStatus, item.Note,
                item.OccurredAtUtc)).ToList(), context);

    private void AddHistory(
        OvertimeRequest entity,
        OvertimeRequestHistoryAction action,
        OvertimeRequestStatus? from,
        OvertimeRequestStatus to,
        string? note,
        DateTimeOffset now) => db.OvertimeRequestHistories.Add(new(
            Guid.NewGuid(), entity.Id, action, from, to, RequireUserId(),
            currentUser.EmployeeId, note, now));

    private void AddAudit(string action, OvertimeRequest entity, object? oldValues, object? newValues) =>
        db.AuditLogs.Add(AuditLogFactory.Create(currentUser, clock, action,
            nameof(OvertimeRequest), entity.Id.ToString(), oldValues, newValues));

    private static object Snapshot(OvertimeRequest entity) => new
    {
        OvertimeRequestId = entity.Id,
        entity.EmployeeId,
        entity.OvertimeDate,
        entity.RequestedMinutes,
        entity.Status
    };

    private Guid RequireEmployeeId()
    {
        EnsureSelfService();
        return currentUser.EmployeeId ?? throw new ForbiddenAccessException(
            "帳號尚未綁定員工資料，請洽系統管理員。");
    }

    private void EnsureSelfService()
    {
        if (!currentUser.IsAuthenticated ||
            !currentUser.HasPermission(PolicyNames.OvertimeSelfService))
            throw new ForbiddenAccessException("請先登入。");
    }

    private void EnsureReviewer()
    {
        if (!currentUser.IsAuthenticated ||
            !currentUser.HasPermission(PolicyNames.OvertimeManage))
            throw new ForbiddenAccessException("您沒有加班審核權限。");
    }

    private string RequireUserId() => currentUser.UserId ??
        throw new ForbiddenAccessException("無法識別目前登入帳號。");

    private static DateTime AsLocal(DateTime value) =>
        DateTime.SpecifyKind(value, DateTimeKind.Unspecified);

    private static void EnsureVersion(byte[] current, string? supplied)
    {
        byte[] expected;
        try { expected = string.IsNullOrWhiteSpace(supplied) ? [] : Convert.FromBase64String(supplied); }
        catch (FormatException) { throw new ConcurrencyConflictException(); }
        if (!current.SequenceEqual(expected)) throw new ConcurrencyConflictException();
    }

    private async Task SaveAsync(CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw new ConcurrencyConflictException(); }
    }
}
