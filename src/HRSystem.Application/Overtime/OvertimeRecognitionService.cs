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

namespace HRSystem.Application.Overtime;

public sealed class OvertimeRecognitionService(
    IApplicationDbContext db,
    ICurrentUser currentUser,
    TimeProvider clock) : IOvertimeRecognitionService
{
    public async Task<IReadOnlyList<OvertimeRecognitionDto>> SearchAsync(
        OvertimeRecognitionQuery query,
        CancellationToken cancellationToken = default)
    {
        EnsureManager();
        ArgumentNullException.ThrowIfNull(query);
        if (query.EndDate < query.StartDate ||
            query.EndDate.DayNumber - query.StartDate.DayNumber > 92)
            throw new ApplicationValidationException("查詢日期區間不可超過 92 天。");

        var requests = db.OvertimeRequests.AsNoTracking()
            .Where(item => item.Status == OvertimeRequestStatus.Approved &&
                item.OvertimeDate >= query.StartDate &&
                item.OvertimeDate <= query.EndDate);
        if (query.DepartmentId.HasValue)
            requests = requests.Where(item =>
                item.Employee.DepartmentId == query.DepartmentId.Value);
        if (query.EmployeeId.HasValue)
            requests = requests.Where(item => item.EmployeeId == query.EmployeeId.Value);
        var entities = await requests
            .Include(item => item.Employee).ThenInclude(item => item.Department)
            .Include(item => item.Recognition).ThenInclude(item => item!.Histories)
            .OrderByDescending(item => item.OvertimeDate)
            .ThenBy(item => item.Employee.EmployeeNumber)
            .ToListAsync(cancellationToken);

        var result = new List<OvertimeRecognitionDto>(entities.Count);
        foreach (var entity in entities)
        {
            var source = await BuildSourceAsync(entity, cancellationToken);
            var dto = ToDto(entity, entity.Recognition, source);
            if (Matches(dto, query.Filter)) result.Add(dto);
        }
        return result;
    }

    public async Task<OvertimeRecognitionDto> ConfirmAsync(
        ConfirmOvertimeRecognitionRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureManager();
        RequestValidator.Validate(request);
        try
        {
            return await db.ExecuteSerializableAsync(async ct =>
            {
                var entity = await LoadRequestAsync(request.OvertimeRequestId, ct);
                var source = await BuildSourceAsync(entity, ct);
                EnsureFingerprint(source.Fingerprint, request.SourceFingerprint);
                var now = clock.GetUtcNow();
                var actor = RequireUserId();
                var recognition = entity.Recognition;
                if (recognition is null)
                {
                    recognition = NewRecognition(entity, source, now);
                    db.OvertimeRecognitions.Add(recognition);
                    db.OvertimeRecognitionHistories.Add(new(
                        Guid.NewGuid(), recognition.Id,
                        OvertimeRecognitionHistoryAction.Created, null,
                        recognition.Status, actor, null, null, null, null, now));
                }
                else
                {
                    if (request.RecognitionId != recognition.Id)
                        throw new ConcurrencyConflictException();
                    EnsureVersion(recognition.RowVersion, request.RowVersion);
                }

                var from = recognition.Status;
                var previous = recognition.RecognizedMinutes;
                var action = recognition.Confirm(
                    request.RecognizedStartAt, request.RecognizedEndAt,
                    request.Reason, request.Note, source.Fingerprint, actor, now);
                db.OvertimeRecognitionHistories.Add(new(
                    Guid.NewGuid(), recognition.Id, action, from,
                    recognition.Status, actor, recognition.Reason,
                    recognition.Note, previous, recognition.RecognizedMinutes, now));
                AddAudit(action == OvertimeRecognitionHistoryAction.Adjusted
                    ? AuditActions.OvertimeRecognitionAdjusted
                    : AuditActions.OvertimeRecognitionConfirmed,
                    recognition, action);
                await SaveAsync(ct);
                return ToDto(entity, recognition, source);
            }, cancellationToken);
        }
        catch { db.ClearTrackedChanges(); throw; }
    }

    public async Task<OvertimeRecognitionDto> ReopenAsync(
        ReopenOvertimeRecognitionRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureManager();
        RequestValidator.Validate(request);
        try
        {
            return await db.ExecuteSerializableAsync(async ct =>
            {
                var recognition = await db.OvertimeRecognitions
                    .Include(item => item.OvertimeRequest)
                        .ThenInclude(item => item.Employee)
                            .ThenInclude(item => item.Department)
                    .Include(item => item.Histories)
                    .SingleOrDefaultAsync(item => item.Id == request.RecognitionId, ct)
                    ?? throw new EntityNotFoundException("找不到加班認列資料。");
                EnsureVersion(recognition.RowVersion, request.RowVersion);
                var source = await BuildSourceAsync(recognition.OvertimeRequest, ct);
                EnsureFingerprint(source.Fingerprint, request.SourceFingerprint);
                var from = recognition.Status;
                var previous = recognition.RecognizedMinutes;
                var now = clock.GetUtcNow();
                var actor = RequireUserId();
                recognition.Reopen(source.ObservedClockOutAt,
                    source.SuggestedStartAt, source.SuggestedEndAt,
                    source.SuggestedMinutes, source.Fingerprint, actor, now);
                db.OvertimeRecognitionHistories.Add(new(
                    Guid.NewGuid(), recognition.Id,
                    OvertimeRecognitionHistoryAction.Reopened, from,
                    recognition.Status, actor, null, request.Note,
                    previous, previous, now));
                AddAudit(AuditActions.OvertimeRecognitionReopened,
                    recognition, OvertimeRecognitionHistoryAction.Reopened);
                await SaveAsync(ct);
                return ToDto(recognition.OvertimeRequest, recognition, source);
            }, cancellationToken);
        }
        catch { db.ClearTrackedChanges(); throw; }
    }

    internal static OvertimeRecognitionDto ToDto(
        OvertimeRequest request,
        OvertimeRecognition? recognition,
        OvertimeRecognitionSource source)
    {
        var status = OvertimeRecognitionPolicy.EffectiveStatus(
            recognition, source, out var stale);
        return new(
            recognition?.Id, request.Id, request.EmployeeId,
            request.Employee.EmployeeNumber, request.Employee.ChineseName,
            request.Employee.Department.Name, request.OvertimeDate,
            request.PlannedStartAt, request.PlannedEndAt,
            request.RequestedMinutes, source.ScheduledEndAt,
            source.ObservedClockOutAt, source.ObservedOvertimeMinutes,
            source.SuggestedStartAt, source.SuggestedEndAt,
            source.SuggestedMinutes, source.ExcessBeyondApprovalMinutes,
            recognition?.RecognizedStartAt, recognition?.RecognizedEndAt,
            recognition?.RecognizedMinutes, status, recognition?.Reason,
            recognition?.Note, stale, source.MissingClockOut,
            Convert.ToBase64String(source.Fingerprint),
            recognition is null ? null : Convert.ToBase64String(recognition.RowVersion),
            recognition?.Histories.OrderBy(item => item.OccurredAtUtc)
                .Select(item => new OvertimeRecognitionHistoryDto(
                    item.Action, item.FromStatus, item.ToStatus, item.Reason,
                    item.Note, item.PreviousRecognizedMinutes,
                    item.NewRecognizedMinutes, item.OccurredAtUtc)).ToArray()
                ?? []);
    }

    private async Task<OvertimeRequest> LoadRequestAsync(Guid id, CancellationToken ct) =>
        await db.OvertimeRequests
            .Include(item => item.Employee).ThenInclude(item => item.Department)
            .Include(item => item.Recognition).ThenInclude(item => item!.Histories)
            .SingleOrDefaultAsync(item => item.Id == id &&
                item.Status == OvertimeRequestStatus.Approved, ct)
            ?? throw new EntityNotFoundException("找不到已核准的加班申請。");

    private async Task<OvertimeRecognitionSource> BuildSourceAsync(
        OvertimeRequest request, CancellationToken ct)
    {
        var attendance = await db.DailyAttendanceResults.AsNoTracking()
            .Where(item => item.EmployeeId == request.EmployeeId &&
                item.WorkDate == request.OvertimeDate)
            .Select(item => new AttendanceSource(
                item.Id, item.RowVersion, item.ScheduledEndTimeSnapshot,
                item.IsOvernightShiftSnapshot == true,
                item.EffectiveClockOutLocalTime,
                item.MissingClockIn, item.MissingClockOut))
            .SingleOrDefaultAsync(ct);
        return OvertimeRecognitionPolicy.Build(
            request.Id, request.EmployeeId, request.OvertimeDate,
            request.Status, request.RowVersion,
            request.PlannedStartAt, request.PlannedEndAt,
            attendance?.Id, attendance?.RowVersion,
            attendance?.ScheduledEndTime,
            attendance?.IsOvernightShift ?? false,
            attendance?.ClockOut,
            attendance?.MissingClockIn ?? true,
            attendance?.MissingClockOut ?? true);
    }

    private static OvertimeRecognition NewRecognition(
        OvertimeRequest request, OvertimeRecognitionSource source,
        DateTimeOffset now) => new(
            Guid.NewGuid(), request.Id, request.EmployeeId,
            request.OvertimeDate, request.PlannedStartAt, request.PlannedEndAt,
            source.ObservedClockOutAt, source.SuggestedStartAt,
            source.SuggestedEndAt, source.SuggestedMinutes,
            source.InitialStatus, source.Fingerprint, now);

    private static bool Matches(OvertimeRecognitionDto item,
        OvertimeRecognitionFilter filter) => filter switch
    {
        OvertimeRecognitionFilter.Pending => item.Status is
            OvertimeRecognitionStatus.Pending or OvertimeRecognitionStatus.Reopened,
        OvertimeRecognitionFilter.Confirmed =>
            item.Status == OvertimeRecognitionStatus.Confirmed,
        OvertimeRecognitionFilter.NeedsReview =>
            item.Status == OvertimeRecognitionStatus.NeedsReview,
        OvertimeRecognitionFilter.ExcessBeyondApproval =>
            item.ExcessBeyondApprovalMinutes > 0,
        OvertimeRecognitionFilter.MissingClockOut => item.MissingClockOut,
        _ => true
    };

    private void AddAudit(string action, OvertimeRecognition item,
        OvertimeRecognitionHistoryAction transition) =>
        db.AuditLogs.Add(AuditLogFactory.Create(
            currentUser, clock, action, nameof(OvertimeRecognition),
            item.Id.ToString(), null, new
            {
                RecognitionId = item.Id,
                item.OvertimeRequestId,
                item.EmployeeId,
                item.WorkDate,
                item.RecognizedMinutes,
                Action = transition
            }));

    private void EnsureManager()
    {
        if (!currentUser.IsAuthenticated ||
            !currentUser.HasPermission(PolicyNames.OvertimeManage))
            throw new ForbiddenAccessException("您沒有加班認列權限。");
    }

    private string RequireUserId() => currentUser.UserId ??
        throw new ForbiddenAccessException("無法識別目前登入帳號。");

    private static void EnsureFingerprint(byte[] current, string supplied)
    {
        byte[] expected;
        try { expected = Convert.FromBase64String(supplied ?? string.Empty); }
        catch (FormatException) { throw new ConcurrencyConflictException(); }
        if (expected.Length != 32 ||
            !CryptographicOperations.FixedTimeEquals(current, expected))
            throw new ConcurrencyConflictException(
                "出勤或加班申請資料已變更，請重新載入後再確認。");
    }

    private static void EnsureVersion(byte[] current, string? supplied)
    {
        byte[] expected;
        try { expected = Convert.FromBase64String(supplied ?? string.Empty); }
        catch (FormatException) { throw new ConcurrencyConflictException(); }
        if (!current.SequenceEqual(expected)) throw new ConcurrencyConflictException();
    }

    private async Task SaveAsync(CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw new ConcurrencyConflictException(); }
    }

    private sealed record AttendanceSource(
        Guid Id, byte[] RowVersion, TimeOnly? ScheduledEndTime,
        bool IsOvernightShift, DateTime? ClockOut,
        bool MissingClockIn, bool MissingClockOut);
}
