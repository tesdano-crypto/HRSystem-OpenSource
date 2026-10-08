using HRSystem.Application.Common.Exceptions;
using HRSystem.Application.Common.Auditing;
using HRSystem.Domain.Attendance;
using HRSystem.Domain.Overtime;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;

namespace HRSystem.Application.Attendance;

public sealed partial class AttendanceReviewService
{
    // The daily query has already authorized and bounded its employee/date scope.
    // Reuse the same projection (including stale resolution and overtime semantics)
    // without imposing Review's 92-day UI limit on Daily's existing 366-day range.
    internal async Task<IReadOnlyDictionary<Guid, AttendanceReviewRowDto>> ReadDailyPunchProjectionAsync(
        Guid[] employeeIds, DateOnly from, DateOnly to, CancellationToken ct)
    {
        if (employeeIds.Length == 0) return new Dictionary<Guid, AttendanceReviewRowDto>();
        var result = await ReadProjectionAsync(new AttendanceReviewQuery
        {
            EmployeeIds = employeeIds, StartDate = from, EndDate = to
        }, ct);
        return result.Items.Where(x => x.DailyAttendanceResultId != Guid.Empty && x.PunchEvidence is not null)
            .ToDictionary(x => x.DailyAttendanceResultId);
    }

    private async Task<List<AttendanceReviewRowDto>> MergePunchEvidenceAsync(
        List<AttendanceReviewRowDto> rows, AttendanceReviewQuery query,
        IReadOnlyList<AttendanceReviewResolution> resolutions, CancellationToken ct)
    {
        var retained = resolutions.Where(x => x.AnomalyType == AttendanceReviewAnomalyType.NonWorkingDayPunch)
            .Select(x => (x.EmployeeId, x.WorkDate)).ToHashSet();
        var evidence = await NonWorkingDayPunchReader.ReadAsync(dbContext, query.EmployeeIds.Distinct().ToArray(),
            query.StartDate, query.EndDate, retained, ct);
        var byKey = rows.ToDictionary(x => (x.EmployeeId, x.WorkDate));
        var missingEmployees = evidence.Where(x => !byKey.ContainsKey(x.Key) &&
                (x.Value.HasNonWorkingPunch || retained.Contains(x.Key)))
            .Select(x => x.Key.EmployeeId).Distinct().ToArray();
        var employees = await dbContext.Employees.AsNoTracking().Where(x => missingEmployees.Contains(x.Id))
            .Select(x => new { x.Id, x.EmployeeNumber, x.ChineseName, DepartmentName = x.Department.Name })
            .ToDictionaryAsync(x => x.Id, ct);
        foreach (var (key, value) in evidence)
        {
            if (!value.HasNonWorkingPunch && !retained.Contains(key)) continue;
            if (!byKey.TryGetValue(key, out var row))
            {
                if (!employees.TryGetValue(key.EmployeeId, out var employee)) continue;
                // Guid.Empty is a view-only "no persisted attendance result" marker.
                // Never insert a DailyAttendanceResult for read projection or review.
                row = new(Guid.Empty, key.EmployeeId, employee.EmployeeNumber, employee.ChineseName,
                    employee.DepartmentName, key.Date, false, value.Classification.ToString(),
                    null, null, null, null, null, null, AttendanceDailyStatus.RestDay.ToString(),
                    false, false, false, false, 0, 0, 0, 0, 0, 0, 0,
                    LeaveCoverageStatus.None.ToString(), false, false, 0, []);
            }
            byKey[key] = value.HasNonWorkingPunch
                ? row with
                {
                    PunchEvidence = value, IsRequiredWorkday = false,
                    Status = AttendanceDailyStatus.RestDay.ToString(),
                    CalendarClassification = value.Classification.ToString(),
                    IsLate = false, IsEarlyLeave = false, MissingClockIn = false, MissingClockOut = false,
                    LateMinutes = 0, EarlyLeaveMinutes = 0, MissingMinutes = 0,
                    RequiredAttendanceMinutes = 0, RecognizedWorkMinutes = 0,
                    WorkedDuringApprovedLeaveMinutes = 0
                }
                : row with { PunchEvidence = value };
        }
        return byKey.Values.ToList();
    }

    private static IEnumerable<AnomalySource> ReviewSources(AttendanceReviewRowDto item,
        IReadOnlyDictionary<(Guid EmployeeId, DateOnly WorkDate, AttendanceReviewAnomalyType AnomalyType), AttendanceReviewResolution> resolutions)
    {
        var sources = BuildAnomalySources(item).AsEnumerable();
        if (item.PunchEvidence is { } evidence && (evidence.HasNonWorkingPunch ||
            resolutions.ContainsKey((item.EmployeeId, item.WorkDate, AttendanceReviewAnomalyType.NonWorkingDayPunch))))
            sources = sources.Append(new(AttendanceReviewAnomalyType.NonWorkingDayPunch, 0,
                Convert.FromBase64String(evidence.Fingerprint)));
        return sources;
    }

    private static IOrderedEnumerable<AttendanceReviewRowDto> OrderMerged(
        IEnumerable<AttendanceReviewRowDto> rows, AttendanceReviewQuery query)
    {
        var descending = query.SortDirection == AttendanceReviewSortDirection.Descending;
        return query.SortBy switch
        {
            AttendanceReviewSortField.Employee => (descending ? rows.OrderByDescending(x => x.EmployeeNumber, StringComparer.Ordinal)
                : rows.OrderBy(x => x.EmployeeNumber, StringComparer.Ordinal)).ThenByDescending(x => x.WorkDate),
            AttendanceReviewSortField.Status => (descending ? rows.OrderByDescending(x => Enum.Parse<AttendanceDailyStatus>(x.Status))
                : rows.OrderBy(x => Enum.Parse<AttendanceDailyStatus>(x.Status))).ThenByDescending(x => x.WorkDate).ThenBy(x => x.EmployeeNumber),
            _ => (descending ? rows.OrderByDescending(x => x.WorkDate) : rows.OrderBy(x => x.WorkDate)).ThenBy(x => x.EmployeeNumber)
        };
    }

    private static int? ValidLinkedRecognition(AttendanceReviewRowDto row, OvertimeProjection request)
    {
        if (request.Status != OvertimeRequestStatus.Approved) return null;
        var value = BuildOvertimeRequest(row, [request]);
        return value.State == AttendanceReviewOvertimeRequestState.RecognitionConfirmed && !value.RecognitionIsStale
            ? value.RecognizedMinutes : null;
    }

    private async Task<AttendanceReviewResolutionResult> ResolveNonWorkingAsync(
        ResolveAttendanceReviewRequest request, CancellationToken cancellationToken)
    {
        if (request.EmployeeId is not { } employee || employee == Guid.Empty || request.WorkDate is not { } date)
            throw new ApplicationValidationException("非工作日打卡來源必須包含員工與日期。");
        var supplied = DecodeFingerprint(request.SourceFingerprint);
        try
        {
            return await dbContext.ExecuteSerializableAsync<AttendanceReviewResolutionResult>(async ct =>
            {
                var evidence = (await NonWorkingDayPunchReader.ReadAsync(dbContext, [employee], date, date, [], ct))
                    .GetValueOrDefault((employee, date));
                if (evidence?.HasNonWorkingPunch != true)
                    throw new ApplicationValidationException("目前已無非工作日打卡證據，請重新查詢。");
                var fingerprint = Convert.FromBase64String(evidence.Fingerprint);
                if (!CryptographicOperations.FixedTimeEquals(supplied, fingerprint))
                    throw new ConcurrencyConflictException("打卡或行事曆已變更，請重新查詢。");
                var resultId = await dbContext.DailyAttendanceResults.Where(x => x.EmployeeId == employee && x.WorkDate == date)
                    .Select(x => (Guid?)x.Id).SingleOrDefaultAsync(ct);
                if (request.DailyAttendanceResultId != Guid.Empty && resultId != request.DailyAttendanceResultId)
                    throw new ApplicationValidationException("出勤來源與員工日期不符。");
                var resolution = await dbContext.AttendanceReviewResolutions.SingleOrDefaultAsync(x =>
                    x.EmployeeId == employee && x.WorkDate == date && x.AnomalyType == AttendanceReviewAnomalyType.NonWorkingDayPunch, ct);
                var now = timeProvider.GetUtcNow();
                var actor = RequireUserId();
                var initial = resolution is null;
                if (initial)
                {
                    resolution = new(Guid.NewGuid(), employee, date, resultId,
                        AttendanceReviewAnomalyType.NonWorkingDayPunch, fingerprint, request.Reason, request.Note, actor, now);
                    dbContext.AttendanceReviewResolutions.Add(resolution);
                    dbContext.AttendanceReviewResolutionHistories.Add(CreateHistory(resolution,
                        AttendanceReviewResolutionHistoryAction.Created, null, AttendanceReviewResolutionStatus.Resolved,
                        null, null, actor, now));
                }
                else
                {
                    EnsureVersion(resolution!.RowVersion, request.RowVersion);
                    resolution.ResolveAgain(fingerprint, request.Reason, request.Note, actor, now);
                }
                dbContext.AttendanceReviewResolutionHistories.Add(CreateHistory(resolution!,
                    AttendanceReviewResolutionHistoryAction.Resolved, initial ? null : AttendanceReviewResolutionStatus.Reopened,
                    AttendanceReviewResolutionStatus.Resolved, request.Reason, request.Note, actor, now));
                AddAudit(AuditActions.NonWorkingDayPunchMarkedNonOvertime, resolution!);
                await dbContext.SaveChangesAsync(ct);
                return new(resolution!.Id, resolution.Status, Convert.ToBase64String(resolution.RowVersion));
            }, cancellationToken);
        }
        catch (DbUpdateConcurrencyException) { throw new ConcurrencyConflictException(); }
        catch (DbUpdateException exception) when (dbContext.IsUniqueConstraintViolation(exception,
            "UX_AttendanceReviewResolutions_Employee_WorkDate_Anomaly"))
        { throw new ConcurrencyConflictException(); }
    }
}
