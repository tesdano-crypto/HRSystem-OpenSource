using HRSystem.Application.Abstractions.Persistence;
using HRSystem.Application.Abstractions.Security;
using HRSystem.Application.Common.Exceptions;
using HRSystem.Application.Common.Auditing;
using HRSystem.Application.Overtime;
using HRSystem.Application.Security;
using HRSystem.Domain.Attendance;
using HRSystem.Domain.Overtime;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;

namespace HRSystem.Application.Attendance;

public sealed partial class AttendanceReviewService(
    IApplicationDbContext dbContext,
    ICurrentUser currentUser,
    TimeProvider timeProvider) : IAttendanceReviewService
{
    public AttendanceReviewService(
        IApplicationDbContext dbContext,
        ICurrentUser currentUser)
        : this(dbContext, currentUser, TimeProvider.System)
    {
    }

    public async Task<AttendanceReviewFilterOptions> GetFilterOptionsAsync(
        CancellationToken cancellationToken = default)
    {
        AttendanceManagementAuthorization.EnsureManage(currentUser);

        var departments = await dbContext.Departments
            .AsNoTracking()
            .OrderBy(item => item.Code)
            .Select(item => new AttendanceReviewDepartmentOption(
                item.Id,
                item.Code,
                item.Name))
            .ToListAsync(cancellationToken);
        var employees = await dbContext.Employees
            .AsNoTracking()
            .OrderBy(item => item.EmployeeNumber)
            .Select(item => new AttendanceReviewEmployeeOption(
                item.Id,
                item.EmployeeNumber,
                item.ChineseName,
                item.DepartmentId,
                item.Department.Name))
            .ToListAsync(cancellationToken);

        return new AttendanceReviewFilterOptions(departments, employees);
    }

    public Task<AttendanceReviewResult> SearchAsync(
        AttendanceReviewQuery query,
        CancellationToken cancellationToken = default)
    {
        AttendanceManagementAuthorization.EnsureManage(currentUser);
        return SearchCoreAsync(query, cancellationToken);
    }

    private Task<AttendanceReviewResult> SearchCoreAsync(
        AttendanceReviewQuery query,
        CancellationToken cancellationToken)
    {
        Validate(query);
        return ReadProjectionAsync(query, cancellationToken);
    }

    private async Task<AttendanceReviewResult> ReadProjectionAsync(
        AttendanceReviewQuery query,
        CancellationToken cancellationToken)
    {
        var employeeIds = query.EmployeeIds.Distinct().ToArray();
        var results = dbContext.DailyAttendanceResults
            .AsNoTracking()
            .Where(item =>
                item.WorkDate >= query.StartDate &&
                item.WorkDate <= query.EndDate &&
                employeeIds.Contains(item.EmployeeId));

        // Read only the bounded employee/date range. Review filters and the limit must
        // run after merging raw-only days, otherwise historical evidence is lost.
        var ordered = Order(results, query.SortBy, query.SortDirection);
        var items = await ordered
            .Select(item => new AttendanceReviewRowDto(
                item.Id,
                item.EmployeeId,
                item.Employee.EmployeeNumber,
                item.Employee.ChineseName,
                item.Employee.Department.Name,
                item.WorkDate,
                item.IsRequiredWorkday,
                item.CalendarClassification.ToString(),
                item.ShiftCodeSnapshot,
                item.ShiftNameSnapshot,
                item.ScheduledEndTimeSnapshot,
                item.IsOvernightShiftSnapshot,
                item.EffectiveClockInLocalTime,
                item.EffectiveClockOutLocalTime,
                item.Status.ToString(),
                item.IsLate,
                item.IsEarlyLeave,
                item.MissingClockIn,
                item.MissingClockOut,
                (item.LateSeconds + 59) / 60,
                (item.EarlyLeaveSeconds + 59) / 60,
                item.ApprovedLeaveMinutes,
                item.RequiredAttendanceMinutes,
                item.RecognizedWorkMinutes,
                item.MissingMinutes,
                item.WorkedDuringApprovedLeaveMinutes,
                item.LeaveCoverageStatus.ToString(),
                item.IsEmploymentSuspended,
                item.IsAttendanceExempted,
                item.AttendanceExceptionMinutes,
                item.LeaveSegments
                    .OrderBy(segment => segment.StartAtUtc)
                    .Select(segment => new AttendanceReviewLeaveDto(
                        segment.LeaveRequestId,
                        segment.LeaveTypeCodeSnapshot,
                        segment.LeaveTypeNameSnapshot,
                        segment.CoveredMinutes))
                    .ToList())
                {
                    ScheduledStartTime = item.ScheduledStartTimeSnapshot,
                    AttendanceRowVersion = item.RowVersion
                })
            .ToListAsync(cancellationToken);

        var resolutions = await dbContext.AttendanceReviewResolutions.AsNoTracking()
            .Where(x => employeeIds.Contains(x.EmployeeId) && x.WorkDate >= query.StartDate && x.WorkDate <= query.EndDate)
            .ToListAsync(cancellationToken);
        items = await MergePunchEvidenceAsync(items, query, resolutions, cancellationToken);

        var rangeStart = DateTime.SpecifyKind(
            query.StartDate.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified);
        var rangeEndExclusive = DateTime.SpecifyKind(
            query.EndDate.AddDays(1).ToDateTime(TimeOnly.MinValue),
            DateTimeKind.Unspecified);
        var overtimeRequests = await dbContext.OvertimeRequests
            .AsNoTracking()
            .Where(request => employeeIds.Contains(request.EmployeeId) &&
                request.PlannedStartAt < rangeEndExclusive &&
                request.PlannedEndAt > rangeStart &&
                (request.Status == OvertimeRequestStatus.Draft || request.Status == OvertimeRequestStatus.Withdrawn ||
                 request.Status == OvertimeRequestStatus.Submitted ||
                 request.Status == OvertimeRequestStatus.Approved ||
                 request.Status == OvertimeRequestStatus.Rejected))
            .Select(request => new OvertimeProjection(
                request.Id, request.EmployeeId, request.OvertimeDate,
                request.PlannedStartAt, request.PlannedEndAt,
                request.RequestedMinutes, request.Status, request.RowVersion,
                request.Recognition == null ? null : request.Recognition.Id,
                request.Recognition == null ? null : request.Recognition.Status,
                request.Recognition == null ? null : request.Recognition.RecognizedMinutes,
                request.Recognition == null ? null : request.Recognition.Reason,
                request.Recognition == null ? null : request.Recognition.SourceFingerprint))
            .ToListAsync(cancellationToken);
        items = items.Select(item => item with
        {
            OvertimeRequest = BuildOvertimeRequest(item, overtimeRequests),
            OvertimeLinks = overtimeRequests.Where(x => x.EmployeeId == item.EmployeeId && x.WorkDate == item.WorkDate)
                .OrderBy(x => x.Start).Select(x => new AttendanceOvertimeLink(x.Id, x.Status, x.RequestedMinutes,
                    ValidLinkedRecognition(item, x))).ToArray()
        }).ToList();

        var resolutionsByKey = resolutions.ToDictionary(item =>
            (item.EmployeeId, item.WorkDate, item.AnomalyType));
        items = items.Select(item => item with
        {
            ReviewItems = BuildReviewItems(item, resolutionsByKey)
        }).ToList();

        var correctionRequests = await dbContext.AttendanceCorrectionRequests
            .AsNoTracking()
            .Where(request => employeeIds.Contains(request.EmployeeId) &&
                request.WorkDate >= query.StartDate &&
                request.WorkDate <= query.EndDate &&
                request.Status != AttendanceCorrectionRequestStatus.Draft)
            .Select(request => new
            {
                request.Id,
                request.EmployeeId,
                request.WorkDate,
                request.RequestType,
                request.Status
            })
            .ToListAsync(cancellationToken);
        items = items.Select(item => item with
        {
            CorrectionRequests = correctionRequests
                .Where(request => request.EmployeeId == item.EmployeeId &&
                    request.WorkDate == item.WorkDate)
                .OrderByDescending(request => request.Status)
                .Select(request => new AttendanceCorrectionPublicStatusDto(
                    request.Id, request.RequestType, request.Status,
                    CorrectionPublicStatus(request.RequestType, request.Status)))
                .ToArray()
        }).ToList();

        var filteredItems = items.AsEnumerable();
        if (query.OnlyAnomalies)
        {
            filteredItems = filteredItems.Where(item => item.IsAnomaly || item.NonWorkingPunchPending);
        }

        filteredItems = query.QuickFilter switch
        {
            AttendanceReviewQuickFilter.Late =>
                filteredItems.Where(item => item.IsLate),
            AttendanceReviewQuickFilter.EarlyLeave =>
                filteredItems.Where(item => item.IsEarlyLeave),
            AttendanceReviewQuickFilter.MissingPunch =>
                filteredItems.Where(item =>
                    item.MissingClockIn || item.MissingClockOut),
            AttendanceReviewQuickFilter.ExtendedStay =>
                filteredItems.Where(item =>
                    item.OverstayLevel == AttendanceReviewOverstayLevel.ExtendedStay),
            AttendanceReviewQuickFilter.PotentialUnreportedOvertime =>
                filteredItems.Where(item => item.OverstayLevel ==
                    AttendanceReviewOverstayLevel.PotentialUnreportedOvertime),
            AttendanceReviewQuickFilter.PendingReview =>
                filteredItems.Where(item => item.ReviewItems.Any(review => review.IsPending)),
            AttendanceReviewQuickFilter.ResolvedReview =>
                filteredItems.Where(item => item.ReviewItems.Any(review =>
                    review.ReviewState == AttendanceReviewState.Resolved)),
            AttendanceReviewQuickFilter.NeedsReview =>
                filteredItems.Where(item => item.ReviewItems.Any(review =>
                    review.ReviewState == AttendanceReviewState.NeedsReview)),
            AttendanceReviewQuickFilter.Normal =>
                filteredItems.Where(item => !item.IsAnomaly),
            AttendanceReviewQuickFilter.HasOvertimeRequest =>
                filteredItems.Where(item => item.OvertimeLinks.Count > 0),
            AttendanceReviewQuickFilter.NonWorkingDayPunch => filteredItems.Where(x => x.HasNonWorkingPunch),
            AttendanceReviewQuickFilter.NonWorkingWeekend => filteredItems.Where(x => x.HasNonWorkingPunch &&
                x.PunchEvidence!.Classification is AttendanceCalendarClassification.Weekend or AttendanceCalendarClassification.FallbackRestDay),
            AttendanceReviewQuickFilter.NonWorkingHoliday => filteredItems.Where(x => x.HasNonWorkingPunch &&
                x.PunchEvidence!.Classification == AttendanceCalendarClassification.Holiday),
            AttendanceReviewQuickFilter.NonWorkingWithoutRequest => filteredItems.Where(x => x.HasNonWorkingPunch &&
                !x.OvertimeLinks.Any(o => o.Status is OvertimeRequestStatus.Draft or OvertimeRequestStatus.Submitted or OvertimeRequestStatus.Approved)),
            AttendanceReviewQuickFilter.NonWorkingResolved => filteredItems.Where(x => x.HasNonWorkingPunch &&
                x.ReviewItems.Any(r => r.AnomalyType == AttendanceReviewAnomalyType.NonWorkingDayPunch && r.ReviewState == AttendanceReviewState.Resolved)),
            AttendanceReviewQuickFilter.RecognizedOvertime =>
                filteredItems.Where(item => item.OvertimeRequest.State ==
                    AttendanceReviewOvertimeRequestState.RecognitionConfirmed),
            _ => filteredItems
        };
        if (query.OnlyPending)
        {
            filteredItems = filteredItems.Where(item =>
                item.ReviewItems.Any(review => review.IsPending));
        }
        filteredItems = filteredItems.Where(x => x.IsRequiredWorkday || x.HasNonWorkingPunch ||
            x.OvertimeLinks.Count > 0 || x.ReviewItems.Count > 0);
        items = OrderMerged(filteredItems, query).Take(query.Limit ?? int.MaxValue).ToList();

        var summary = BuildSummary(items);
        var employeeSummaries = items
            .GroupBy(item => new
            {
                item.EmployeeId,
                item.EmployeeNumber,
                item.EmployeeName
            })
            .Select(group => new AttendanceReviewEmployeeSummary(
                group.Key.EmployeeId,
                group.Key.EmployeeNumber,
                group.Key.EmployeeName,
                group.Count(),
                group.Count(item => !item.IsAnomaly && !item.HasNonWorkingPunch),
                group.Count(item => item.IsAnomaly),
                group.Count(item => item.IsLate),
                group.Count(item => item.IsEarlyLeave),
                group.Count(item => item.MissingClockIn || item.MissingClockOut),
                group.Count(item => item.OverstayLevel ==
                    AttendanceReviewOverstayLevel.ExtendedStay),
                group.Count(item => item.OverstayLevel ==
                    AttendanceReviewOverstayLevel.PotentialUnreportedOvertime))
            {
                PendingReviewCount = group.Sum(item =>
                    item.ReviewItems.Count(review => review.IsPending)),
                ResolvedReviewCount = group.Sum(item =>
                    item.ReviewItems.Count(review =>
                        review.ReviewState == AttendanceReviewState.Resolved))
            })
            .OrderBy(item => item.EmployeeNumber)
            .ToList();

        return new AttendanceReviewResult(items, summary, employeeSummaries);
    }

    public async Task<MyAttendanceResult> SearchMineAsync(
        MyAttendanceQuery query,
        CancellationToken cancellationToken = default)
    {
        EnsureSelfService();
        Validate(query);
        if (!currentUser.EmployeeId.HasValue)
            return EmptyMyAttendance(false);

        var reviewQuery = new AttendanceReviewQuery
        {
            StartDate = query.StartDate,
            EndDate = query.EndDate,
            EmployeeIds = [currentUser.EmployeeId.Value],
            OnlyAnomalies = query.QuickFilter == MyAttendanceQuickFilter.Anomalies,
            QuickFilter = ToReviewFilter(query.QuickFilter),
            SortBy = AttendanceReviewSortField.WorkDate,
            SortDirection = query.SortDirection,
            Limit = query.Limit
        };
        var result = await SearchCoreAsync(reviewQuery, cancellationToken);
        var supplementalRequests = await dbContext.OvertimeRequests
            .AsNoTracking()
            .Where(request => request.EmployeeId == currentUser.EmployeeId.Value &&
                request.OvertimeDate >= query.StartDate &&
                request.OvertimeDate <= query.EndDate &&
                request.Status != OvertimeRequestStatus.Approved)
            .Select(request => new SelfOvertimeProjection(
                request.Id, request.OvertimeDate, request.RequestedMinutes,
                request.Status))
            .ToListAsync(cancellationToken);
        var items = result.Items.Select(item =>
            ToMyAttendance(item, supplementalRequests)).ToList();
        return new MyAttendanceResult(true, items,
            MyAttendanceSummaryCalculator.Build(items));
    }

    public async Task<MyAttendanceDetailResult> GetMineDetailAsync(
        Guid dailyAttendanceResultId,
        CancellationToken cancellationToken = default)
    {
        EnsureSelfService();
        if (dailyAttendanceResultId == Guid.Empty)
            throw new ApplicationValidationException("出勤結果識別碼不合法。");
        if (!currentUser.EmployeeId.HasValue)
            return new MyAttendanceDetailResult(false, null);

        var employeeId = currentUser.EmployeeId.Value;
        var workDate = await dbContext.DailyAttendanceResults
            .AsNoTracking()
            .Where(item => item.Id == dailyAttendanceResultId &&
                item.EmployeeId == employeeId)
            .Select(item => (DateOnly?)item.WorkDate)
            .SingleOrDefaultAsync(cancellationToken);
        if (!workDate.HasValue)
            return new MyAttendanceDetailResult(true, null);

        var result = await SearchCoreAsync(new AttendanceReviewQuery
        {
            StartDate = workDate.Value,
            EndDate = workDate.Value,
            EmployeeIds = [employeeId]
        }, cancellationToken);
        var item = result.Items.SingleOrDefault(value =>
            value.DailyAttendanceResultId == dailyAttendanceResultId);
        if (item is null) return new MyAttendanceDetailResult(true, null);
        var supplementalRequests = await dbContext.OvertimeRequests
            .AsNoTracking()
            .Where(request => request.EmployeeId == employeeId &&
                request.OvertimeDate == workDate.Value &&
                request.Status != OvertimeRequestStatus.Approved)
            .Select(request => new SelfOvertimeProjection(
                request.Id, request.OvertimeDate, request.RequestedMinutes,
                request.Status))
            .ToListAsync(cancellationToken);
        return new MyAttendanceDetailResult(true,
            ToMyAttendance(item, supplementalRequests));
    }

    public async Task<AttendanceReviewResolutionResult> ResolveAsync(
        ResolveAttendanceReviewRequest request,
        CancellationToken cancellationToken = default)
    {
        AttendanceManagementAuthorization.EnsureManage(currentUser);
        ArgumentNullException.ThrowIfNull(request);
        if (request.AnomalyType == AttendanceReviewAnomalyType.NonWorkingDayPunch)
            return await ResolveNonWorkingAsync(request, cancellationToken);
        var suppliedFingerprint = DecodeFingerprint(request.SourceFingerprint);
        try
        {
            return await dbContext.ExecuteSerializableAsync(async transactionToken =>
            {
                var result = await dbContext.DailyAttendanceResults
                    .SingleOrDefaultAsync(item =>
                        item.Id == request.DailyAttendanceResultId,
                        transactionToken)
                    ?? throw new EntityNotFoundException(
                        "找不到指定的每日出勤結果。");
                var source = BuildAnomalySources(result)
                    .SingleOrDefault(item => item.AnomalyType == request.AnomalyType)
                    ?? throw new ApplicationValidationException(
                        "目前資料已不存在指定異常，請重新查詢。");
                if (!CryptographicOperations.FixedTimeEquals(
                    suppliedFingerprint, source.Fingerprint))
                    throw new ConcurrencyConflictException(
                        "出勤異常內容已變更，請重新查詢後再處理。");

                var now = timeProvider.GetUtcNow();
                var actor = RequireUserId();
                var resolution = await dbContext.AttendanceReviewResolutions
                    .SingleOrDefaultAsync(item =>
                        item.EmployeeId == result.EmployeeId &&
                        item.WorkDate == result.WorkDate &&
                        item.AnomalyType == request.AnomalyType,
                        transactionToken);
                if (resolution is null)
                {
                    resolution = new AttendanceReviewResolution(
                        Guid.NewGuid(), result.EmployeeId, result.WorkDate,
                        result.Id, request.AnomalyType, source.Fingerprint,
                        request.Reason, request.Note, actor, now);
                    dbContext.AttendanceReviewResolutions.Add(resolution);
                    dbContext.AttendanceReviewResolutionHistories.Add(
                        CreateHistory(resolution,
                            AttendanceReviewResolutionHistoryAction.Created,
                            null, AttendanceReviewResolutionStatus.Resolved,
                            null, null, actor, now));
                }
                else
                {
                    EnsureVersion(resolution.RowVersion, request.RowVersion);
                    resolution.ResolveAgain(source.Fingerprint, request.Reason,
                        request.Note, actor, now);
                }

                dbContext.AttendanceReviewResolutionHistories.Add(
                    CreateHistory(resolution,
                        AttendanceReviewResolutionHistoryAction.Resolved,
                        resolution.Histories.Count == 0 && resolution.CreatedAtUtc == now
                            ? null
                            : AttendanceReviewResolutionStatus.Reopened,
                        AttendanceReviewResolutionStatus.Resolved,
                        request.Reason, request.Note, actor, now));
                AddAudit(AuditActions.AttendanceReviewResolved, resolution);
                await dbContext.SaveChangesAsync(transactionToken);
                return new AttendanceReviewResolutionResult(
                    resolution.Id, resolution.Status,
                    Convert.ToBase64String(resolution.RowVersion));
            }, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConcurrencyConflictException();
        }
        catch (DbUpdateException exception) when (
            dbContext.IsUniqueConstraintViolation(exception,
                "UX_AttendanceReviewResolutions_Employee_WorkDate_Anomaly"))
        {
            throw new ConcurrencyConflictException(
                "此異常已由其他使用者處理，請重新查詢。");
        }
        catch
        {
            dbContext.ClearTrackedChanges();
            throw;
        }
    }

    public async Task<AttendanceReviewResolutionResult> ReopenAsync(
        ReopenAttendanceReviewRequest request,
        CancellationToken cancellationToken = default)
    {
        AttendanceManagementAuthorization.EnsureManage(currentUser);
        ArgumentNullException.ThrowIfNull(request);
        try
        {
            return await dbContext.ExecuteSerializableAsync(async transactionToken =>
            {
                var resolution = await dbContext.AttendanceReviewResolutions
                    .SingleOrDefaultAsync(item => item.Id == request.ResolutionId,
                        transactionToken)
                    ?? throw new EntityNotFoundException("找不到指定的結案紀錄。");
                EnsureVersion(resolution.RowVersion, request.RowVersion);
                var now = timeProvider.GetUtcNow();
                var actor = RequireUserId();
                resolution.Reopen(request.Note, actor, now);
                dbContext.AttendanceReviewResolutionHistories.Add(
                    CreateHistory(resolution,
                        AttendanceReviewResolutionHistoryAction.Reopened,
                        AttendanceReviewResolutionStatus.Resolved,
                        AttendanceReviewResolutionStatus.Reopened,
                        null, request.Note, actor, now));
                AddAudit(AuditActions.AttendanceReviewReopened, resolution);
                await dbContext.SaveChangesAsync(transactionToken);
                return new AttendanceReviewResolutionResult(
                    resolution.Id, resolution.Status,
                    Convert.ToBase64String(resolution.RowVersion));
            }, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConcurrencyConflictException();
        }
        catch
        {
            dbContext.ClearTrackedChanges();
            throw;
        }
    }

    public async Task<IReadOnlyList<AttendanceReviewResolutionHistoryDto>>
        GetHistoryAsync(Guid resolutionId,
            CancellationToken cancellationToken = default)
    {
        AttendanceManagementAuthorization.EnsureManage(currentUser);
        if (resolutionId == Guid.Empty)
            throw new ApplicationValidationException("結案紀錄識別碼不合法。");
        return await dbContext.AttendanceReviewResolutionHistories
            .AsNoTracking()
            .Where(item => item.AttendanceReviewResolutionId == resolutionId)
            .OrderBy(item => item.ActionAtUtc)
            .Select(item => new AttendanceReviewResolutionHistoryDto(
                item.Action, item.FromStatus, item.ToStatus, item.Reason,
                item.Note, item.ActorUserId, item.ActionAtUtc))
            .ToListAsync(cancellationToken);
    }

    private static IReadOnlyList<AttendanceReviewAnomalyDto> BuildReviewItems(
        AttendanceReviewRowDto item,
        IReadOnlyDictionary<
            (Guid EmployeeId, DateOnly WorkDate, AttendanceReviewAnomalyType AnomalyType),
            AttendanceReviewResolution> resolutions)
    {
        return ReviewSources(item, resolutions)
            .Select(source =>
            {
                resolutions.TryGetValue(
                    (item.EmployeeId, item.WorkDate, source.AnomalyType),
                    out var resolution);
                var state = resolution is null
                    ? AttendanceReviewState.Pending
                    : resolution.Status == AttendanceReviewResolutionStatus.Reopened
                        ? AttendanceReviewState.Reopened
                        : CryptographicOperations.FixedTimeEquals(
                            resolution.SourceFingerprint, source.Fingerprint)
                            ? AttendanceReviewState.Resolved
                            : AttendanceReviewState.NeedsReview;
                var overtimeConflict = resolution is not null &&
                    resolution.Reason == AttendanceReviewResolutionReason.NonWorkActivity &&
                    item.OvertimeRequest.ChangesReviewMeaning &&
                    source.AnomalyType is AttendanceReviewAnomalyType.ExtendedStay or
                        AttendanceReviewAnomalyType.PotentialUnreportedOvertime;
                if (overtimeConflict) state = AttendanceReviewState.NeedsReview;
                return new AttendanceReviewAnomalyDto(
                    source.AnomalyType,
                    source.Minutes,
                    Convert.ToBase64String(source.Fingerprint),
                    state,
                    resolution?.Id,
                    resolution?.Reason,
                    resolution?.Note,
                    resolution is null
                        ? null
                        : Convert.ToBase64String(resolution.RowVersion))
                {
                    HasOvertimeResolutionConflict = overtimeConflict
                };
            })
            .ToArray();
    }

    private static IReadOnlyList<AnomalySource> BuildAnomalySources(
        AttendanceReviewRowDto item) => BuildAnomalySources(
            item.EmployeeId, item.WorkDate, item.EffectiveClockInLocalTime,
            item.EffectiveClockOutLocalTime, item.LateMinutes,
            item.EarlyLeaveMinutes, item.OverstayMinutes, item.IsLate,
            item.IsEarlyLeave, item.MissingClockIn, item.MissingClockOut,
            item.OverstayLevel);

    private static IReadOnlyList<AnomalySource> BuildAnomalySources(
        DailyAttendanceResult item)
    {
        var overstay = AttendanceReviewOverstayPolicy.Evaluate(
            item.WorkDate, item.ScheduledEndTimeSnapshot,
            item.IsOvernightShiftSnapshot, item.EffectiveClockOutLocalTime,
            item.IsRequiredWorkday, item.MissingClockOut,
            item.IsEmploymentSuspended, item.LeaveCoverageStatus.ToString());
        return BuildAnomalySources(
            item.EmployeeId, item.WorkDate, item.EffectiveClockInLocalTime,
            item.EffectiveClockOutLocalTime, (item.LateSeconds + 59) / 60,
            (item.EarlyLeaveSeconds + 59) / 60, overstay.Minutes,
            item.IsLate, item.IsEarlyLeave, item.MissingClockIn,
            item.MissingClockOut, overstay.Level);
    }

    private static IReadOnlyList<AnomalySource> BuildAnomalySources(
        Guid employeeId,
        DateOnly workDate,
        DateTime? clockIn,
        DateTime? clockOut,
        int lateMinutes,
        int earlyLeaveMinutes,
        int overstayMinutes,
        bool isLate,
        bool isEarlyLeave,
        bool missingClockIn,
        bool missingClockOut,
        AttendanceReviewOverstayLevel overstayLevel)
    {
        var sources = new List<(AttendanceReviewAnomalyType Type, int Minutes)>();
        if (isLate) sources.Add((AttendanceReviewAnomalyType.Late, lateMinutes));
        if (isEarlyLeave) sources.Add((AttendanceReviewAnomalyType.EarlyLeave, earlyLeaveMinutes));
        if (missingClockIn && missingClockOut)
            sources.Add((AttendanceReviewAnomalyType.MissingBoth, 0));
        else if (missingClockIn)
            sources.Add((AttendanceReviewAnomalyType.MissingClockIn, 0));
        else if (missingClockOut)
            sources.Add((AttendanceReviewAnomalyType.MissingClockOut, 0));
        if (overstayLevel == AttendanceReviewOverstayLevel.ExtendedStay)
            sources.Add((AttendanceReviewAnomalyType.ExtendedStay, overstayMinutes));
        else if (overstayLevel == AttendanceReviewOverstayLevel.PotentialUnreportedOvertime)
            sources.Add((AttendanceReviewAnomalyType.PotentialUnreportedOvertime, overstayMinutes));

        return sources.Select(source => new AnomalySource(
            source.Type,
            source.Minutes,
            AttendanceReviewFingerprint.Compute(
                employeeId, workDate, source.Type, clockIn, clockOut,
                lateMinutes, earlyLeaveMinutes, overstayMinutes,
                missingClockIn, missingClockOut)))
            .ToArray();
    }

    private AttendanceReviewResolutionHistory CreateHistory(
        AttendanceReviewResolution resolution,
        AttendanceReviewResolutionHistoryAction action,
        AttendanceReviewResolutionStatus? fromStatus,
        AttendanceReviewResolutionStatus toStatus,
        AttendanceReviewResolutionReason? reason,
        string? note,
        string actor,
        DateTimeOffset now) => new(
            Guid.NewGuid(), resolution.Id, action, fromStatus, toStatus,
            reason, note, actor, now, resolution.SourceFingerprint);

    private void AddAudit(string action, AttendanceReviewResolution resolution) =>
        dbContext.AuditLogs.Add(AuditLogFactory.Create(
            currentUser,
            timeProvider,
            action,
            nameof(AttendanceReviewResolution),
            resolution.Id.ToString(),
            null,
            new
            {
                resolution.EmployeeId,
                resolution.WorkDate,
                resolution.AnomalyType,
                resolution.Reason,
                ResolutionId = resolution.Id
            }));

    private string RequireUserId() => currentUser.UserId ??
        throw new ForbiddenAccessException("無法識別目前登入帳號。");

    private static byte[] DecodeFingerprint(string value)
    {
        try
        {
            var bytes = Convert.FromBase64String(value ?? string.Empty);
            if (bytes.Length != 32) throw new FormatException();
            return bytes;
        }
        catch (FormatException)
        {
            throw new ApplicationValidationException("出勤異常來源指紋不合法。");
        }
    }

    private static void EnsureVersion(byte[] current, string? supplied)
    {
        if (supplied is null) return;
        byte[] expected;
        try
        {
            expected = Convert.FromBase64String(supplied);
        }
        catch (FormatException)
        {
            throw new ConcurrencyConflictException();
        }
        if (!current.SequenceEqual(expected)) throw new ConcurrencyConflictException();
    }

    private sealed record AnomalySource(
        AttendanceReviewAnomalyType AnomalyType,
        int Minutes,
        byte[] Fingerprint);

    private static AttendanceReviewOvertimeRequestDto BuildOvertimeRequest(
        AttendanceReviewRowDto item,
        IReadOnlyList<OvertimeProjection> requests)
    {
        var approved = requests.Where(request =>
                request.EmployeeId == item.EmployeeId &&
                request.WorkDate == item.WorkDate &&
                request.Status == OvertimeRequestStatus.Approved)
            .OrderBy(request => request.Start).ToList();
        if (approved.Count > 0)
        {
            var first = approved[0];
            var source = OvertimeRecognitionPolicy.Build(
                first.Id, first.EmployeeId, first.WorkDate, first.Status,
                first.RequestRowVersion, first.Start, first.End,
                item.DailyAttendanceResultId == Guid.Empty ? null : item.DailyAttendanceResultId,
                item.DailyAttendanceResultId == Guid.Empty ? null : item.AttendanceRowVersion,
                item.ScheduledEndTime, item.IsOvernightShift == true,
                item.EffectiveClockOutLocalTime, item.MissingClockIn,
                item.MissingClockOut);
            var recognition = first.RecognitionStatus.HasValue &&
                first.RecognitionFingerprint is not null
                ? new RecognitionProjection(first.RecognitionStatus.Value,
                    first.RecognizedMinutes, first.RecognitionReason,
                    first.RecognitionFingerprint)
                : null;
            var stale = recognition is not null &&
                !CryptographicOperations.FixedTimeEquals(
                    recognition.Fingerprint, source.Fingerprint);
            var state = recognition switch
            {
                null => source.MissingClockOut
                    ? AttendanceReviewOvertimeRequestState.RecognitionNeedsReview
                    : AttendanceReviewOvertimeRequestState.ApprovedPendingRecognition,
                { Status: OvertimeRecognitionStatus.Confirmed } when stale =>
                    AttendanceReviewOvertimeRequestState.RecognitionNeedsReview,
                { Status: OvertimeRecognitionStatus.Confirmed } =>
                    AttendanceReviewOvertimeRequestState.RecognitionConfirmed,
                { Status: OvertimeRecognitionStatus.NeedsReview } =>
                    AttendanceReviewOvertimeRequestState.RecognitionNeedsReview,
                { Status: OvertimeRecognitionStatus.Reopened } =>
                    AttendanceReviewOvertimeRequestState.RecognitionReopened,
                _ => AttendanceReviewOvertimeRequestState.ApprovedPendingRecognition
            };
            var covered = item.ScheduledEndTime.HasValue &&
                item.EffectiveClockOutLocalTime.HasValue && item.OverstayMinutes > 0
                ? MergeCoverageMinutes(approved,
                    item.WorkDate.ToDateTime(item.ScheduledEndTime.Value)
                        .AddDays(item.IsOvernightShift == true ? 1 : 0),
                    item.EffectiveClockOutLocalTime.Value)
                : 0;
            return new(state, covered, item.OverstayMinutes,
                approved.Sum(request => request.RequestedMinutes), first.Id)
            {
                RecognizedMinutes = recognition?.RecognizedMinutes,
                RecognitionReason = recognition?.Reason,
                RecognitionIsStale = stale
            };
        }

        if (!item.ScheduledEndTime.HasValue || !item.EffectiveClockOutLocalTime.HasValue ||
            item.OverstayMinutes <= 0)
            return new(AttendanceReviewOvertimeRequestState.NoRequest, 0,
                item.OverstayMinutes, 0, null);

        var overstayStart = DateTime.SpecifyKind(
            item.WorkDate.ToDateTime(item.ScheduledEndTime.Value),
            DateTimeKind.Unspecified);
        if (item.IsOvernightShift == true) overstayStart = overstayStart.AddDays(1);
        var overstayEnd = DateTime.SpecifyKind(
            item.EffectiveClockOutLocalTime.Value, DateTimeKind.Unspecified);
        var relevant = requests.Where(request => request.EmployeeId == item.EmployeeId &&
            request.Start < overstayEnd && request.End > overstayStart).ToList();

        var pending = relevant.FirstOrDefault(request =>
            request.Status == OvertimeRequestStatus.Submitted);
        if (pending is not null)
            return new(AttendanceReviewOvertimeRequestState.PendingRequest, 0,
                item.OverstayMinutes, pending.RequestedMinutes, pending.Id);
        var rejected = relevant.FirstOrDefault(request =>
            request.Status == OvertimeRequestStatus.Rejected);
        return rejected is null
            ? new(AttendanceReviewOvertimeRequestState.NoRequest, 0,
                item.OverstayMinutes, 0, null)
            : new(AttendanceReviewOvertimeRequestState.RejectedRequest, 0,
                item.OverstayMinutes, rejected.RequestedMinutes, rejected.Id);
    }

    private static int MergeCoverageMinutes(
        IReadOnlyList<OvertimeProjection> requests,
        DateTime rangeStart,
        DateTime rangeEnd)
    {
        var intervals = requests.Select(request => (
                Start: request.Start > rangeStart ? request.Start : rangeStart,
                End: request.End < rangeEnd ? request.End : rangeEnd))
            .Where(interval => interval.End > interval.Start)
            .OrderBy(interval => interval.Start).ToList();
        if (intervals.Count == 0) return 0;
        var total = TimeSpan.Zero;
        var current = intervals[0];
        foreach (var interval in intervals.Skip(1))
        {
            if (interval.Start <= current.End)
                current.End = interval.End > current.End ? interval.End : current.End;
            else
            {
                total += current.End - current.Start;
                current = interval;
            }
        }
        total += current.End - current.Start;
        return (int)total.TotalMinutes;
    }

    private sealed record OvertimeProjection(
        Guid Id,
        Guid EmployeeId,
        DateOnly WorkDate,
        DateTime Start,
        DateTime End,
        int RequestedMinutes,
        OvertimeRequestStatus Status,
        byte[] RequestRowVersion,
        Guid? RecognitionId,
        OvertimeRecognitionStatus? RecognitionStatus,
        int? RecognizedMinutes,
        OvertimeRecognitionReason? RecognitionReason,
        byte[]? RecognitionFingerprint);

    private sealed record RecognitionProjection(
        OvertimeRecognitionStatus Status,
        int? RecognizedMinutes,
        OvertimeRecognitionReason? Reason,
        byte[] Fingerprint);

    private static IOrderedQueryable<DailyAttendanceResult> Order(
        IQueryable<DailyAttendanceResult> query,
        AttendanceReviewSortField sortBy,
        AttendanceReviewSortDirection direction) => (sortBy, direction) switch
        {
            (AttendanceReviewSortField.Employee,
                AttendanceReviewSortDirection.Descending) => query
                .OrderByDescending(item => item.Employee.EmployeeNumber)
                .ThenByDescending(item => item.WorkDate),
            (AttendanceReviewSortField.Employee,
                AttendanceReviewSortDirection.Ascending) => query
                .OrderBy(item => item.Employee.EmployeeNumber)
                .ThenByDescending(item => item.WorkDate),
            (AttendanceReviewSortField.Status,
                AttendanceReviewSortDirection.Descending) => query
                .OrderByDescending(item => item.Status)
                .ThenByDescending(item => item.WorkDate)
                .ThenBy(item => item.Employee.EmployeeNumber),
            (AttendanceReviewSortField.Status,
                AttendanceReviewSortDirection.Ascending) => query
                .OrderBy(item => item.Status)
                .ThenByDescending(item => item.WorkDate)
                .ThenBy(item => item.Employee.EmployeeNumber),
            (_, AttendanceReviewSortDirection.Ascending) => query
                .OrderBy(item => item.WorkDate)
                .ThenBy(item => item.Employee.EmployeeNumber),
            _ => query
                .OrderByDescending(item => item.WorkDate)
                .ThenBy(item => item.Employee.EmployeeNumber)
        };

    private static AttendanceReviewSummary BuildSummary(
        IReadOnlyCollection<AttendanceReviewRowDto> items) =>
        new(
            items.Count,
            items.Select(item => item.EmployeeId).Distinct().Count(),
            items.Count(item => item.IsAnomaly),
            items.Count(item => item.IsLate),
            items.Count(item => item.IsEarlyLeave),
            items.Count(item => item.MissingClockIn || item.MissingClockOut),
            items.Count(item => item.OverstayLevel ==
                AttendanceReviewOverstayLevel.ExtendedStay),
            items.Count(item => item.OverstayLevel ==
                AttendanceReviewOverstayLevel.PotentialUnreportedOvertime),
            items.Count(item => item.LeaveCoverageStatus == "Full"),
            items.Count(item => item.LeaveCoverageStatus == "Partial"),
            items.Count(item => item.IsAttendanceExempted),
            items.Count(item => item.IsEmploymentSuspended),
            items.Count(item => !item.IsRequiredWorkday))
        {
            PendingReviewCount = items.Sum(item =>
                item.ReviewItems.Count(review => review.IsPending)),
            ResolvedReviewCount = items.Sum(item =>
                item.ReviewItems.Count(review =>
                    review.ReviewState == AttendanceReviewState.Resolved)),
            NeedsReviewCount = items.Sum(item =>
                item.ReviewItems.Count(review =>
                    review.ReviewState == AttendanceReviewState.NeedsReview))
        };

    private static System.Linq.Expressions.Expression<Func<DailyAttendanceResult, bool>>
        IsOverstayCandidate() => item =>
            item.IsRequiredWorkday &&
            !item.IsEmploymentSuspended &&
            item.LeaveCoverageStatus != LeaveCoverageStatus.Full &&
            !item.MissingClockOut &&
            item.ScheduledEndTimeSnapshot.HasValue &&
            item.IsOvernightShiftSnapshot.HasValue &&
            item.EffectiveClockOutLocalTime.HasValue;

    private static void Validate(AttendanceReviewQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.EmployeeIds.Count == 0 ||
            query.EmployeeIds.Any(item => item == Guid.Empty))
        {
            throw new ApplicationValidationException("請至少選擇一位員工。");
        }

        if (query.EndDate < query.StartDate)
        {
            throw new ApplicationValidationException("結束日期不可早於開始日期。");
        }

        if (query.EndDate.DayNumber - query.StartDate.DayNumber + 1 > 92)
        {
            throw new ApplicationValidationException("查詢日期範圍不可超過 92 天。");
        }
        if (query.Limit is <= 0 or > 500)
            throw new ApplicationValidationException("查詢筆數限制不合法。");
    }

    private static void Validate(MyAttendanceQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.EndDate < query.StartDate)
            throw new ApplicationValidationException("結束日期不可早於開始日期。");
        if (query.EndDate.DayNumber - query.StartDate.DayNumber + 1 > 92)
            throw new ApplicationValidationException("查詢日期範圍不可超過 92 天。");
        if (!Enum.IsDefined(query.QuickFilter) ||
            !Enum.IsDefined(query.SortDirection))
            throw new ApplicationValidationException("個人出勤查詢條件不合法。");
        if (query.Limit is <= 0 or > 500)
            throw new ApplicationValidationException("個人出勤查詢筆數限制不合法。");
    }

    private void EnsureSelfService()
    {
        if (!currentUser.IsAuthenticated ||
            !currentUser.HasPermission(PolicyNames.AttendanceSelfService))
            throw new ForbiddenAccessException("您沒有個人出勤查詢權限。");
    }

    private static AttendanceReviewQuickFilter ToReviewFilter(
        MyAttendanceQuickFilter filter) => filter switch
        {
            MyAttendanceQuickFilter.Normal => AttendanceReviewQuickFilter.Normal,
            MyAttendanceQuickFilter.NonWorkingDayPunch => AttendanceReviewQuickFilter.NonWorkingDayPunch,
            MyAttendanceQuickFilter.Late => AttendanceReviewQuickFilter.Late,
            MyAttendanceQuickFilter.EarlyLeave => AttendanceReviewQuickFilter.EarlyLeave,
            MyAttendanceQuickFilter.MissingPunch => AttendanceReviewQuickFilter.MissingPunch,
            MyAttendanceQuickFilter.ExtendedStay => AttendanceReviewQuickFilter.ExtendedStay,
            MyAttendanceQuickFilter.PotentialUnreportedOvertime =>
                AttendanceReviewQuickFilter.PotentialUnreportedOvertime,
            MyAttendanceQuickFilter.HasOvertimeRequest =>
                AttendanceReviewQuickFilter.HasOvertimeRequest,
            MyAttendanceQuickFilter.RecognizedOvertime =>
                AttendanceReviewQuickFilter.RecognizedOvertime,
            _ => AttendanceReviewQuickFilter.All
        };

    private static MyAttendanceRowDto ToMyAttendance(
        AttendanceReviewRowDto item,
        IReadOnlyCollection<SelfOvertimeProjection>? supplementalRequests = null)
    {
        var overtime = item.OvertimeRequest;
        if (overtime.State == AttendanceReviewOvertimeRequestState.NoRequest)
        {
            var supplemental = supplementalRequests?
                .Where(request => request.WorkDate == item.WorkDate)
                .OrderBy(request => request.Status switch
                {
                    OvertimeRequestStatus.Submitted => 0,
                    OvertimeRequestStatus.Rejected => 1,
                    OvertimeRequestStatus.Draft => 2,
                    _ => 3
                })
                .FirstOrDefault();
            if (supplemental is not null)
            {
                overtime = new AttendanceReviewOvertimeRequestDto(
                    supplemental.Status switch
                    {
                        OvertimeRequestStatus.Submitted =>
                            AttendanceReviewOvertimeRequestState.PendingRequest,
                        OvertimeRequestStatus.Rejected =>
                            AttendanceReviewOvertimeRequestState.RejectedRequest,
                        OvertimeRequestStatus.Draft =>
                            AttendanceReviewOvertimeRequestState.DraftRequest,
                        _ => AttendanceReviewOvertimeRequestState.WithdrawnRequest
                    },
                    0, item.OverstayMinutes, supplemental.RequestedMinutes,
                    supplemental.Id);
            }
        }

        return new(
            item.DailyAttendanceResultId,
            item.WorkDate,
            item.IsRequiredWorkday,
            item.CalendarClassification,
            item.ShiftName,
            item.ScheduledStartTime,
            item.ScheduledEndTime,
            item.EffectiveClockInLocalTime,
            item.EffectiveClockOutLocalTime,
            item.Status,
            item.IsLate,
            item.IsEarlyLeave,
            item.MissingClockIn,
            item.MissingClockOut,
            item.LateMinutes,
            item.EarlyLeaveMinutes,
            item.ApprovedLeaveMinutes,
            item.RequiredAttendanceMinutes,
            item.RecognizedWorkMinutes,
            item.MissingMinutes,
            item.WorkedDuringApprovedLeaveMinutes,
            item.LeaveCoverageStatus,
            item.IsEmploymentSuspended,
            item.IsAttendanceExempted,
            item.AttendanceExceptionMinutes,
            item.LeaveItems,
            overtime,
            PublicReviewItems(item),
            item.OverstayMinutes,
            item.OverstayLevel,
            item.IsAnomaly)
        {
            CorrectionRequests = item.CorrectionRequests,
            PunchEvidence = item.PunchEvidence,
            OvertimeLinks = item.OvertimeLinks
        };
    }

    internal static IReadOnlyList<MyAttendanceReviewItemDto> PublicReviewItems(AttendanceReviewRowDto item) =>
        item.ReviewItems.Select(review => new MyAttendanceReviewItemDto(
            review.AnomalyType, review.Minutes, review.ReviewState, PublicReviewStatus(review))).ToArray();

    private static string PublicReviewStatus(AttendanceReviewAnomalyDto item)
    {
        if (item.HasOvertimeResolutionConflict ||
            item.ReviewState == AttendanceReviewState.NeedsReview)
            return "出勤狀態待管理人員重新確認";
        if (item.ReviewState is AttendanceReviewState.Pending or
            AttendanceReviewState.Reopened)
            return "待確認";
        if (item.AnomalyType == AttendanceReviewAnomalyType.NonWorkingDayPunch)
            return NonWorkingDayAttendanceDisplay.Resolved;
        return item.Reason == AttendanceReviewResolutionReason.NonWorkActivity
            ? "已確認為非工作時間"
            : "已處理";
    }

    private static string CorrectionPublicStatus(
        AttendanceCorrectionRequestType requestType,
        AttendanceCorrectionRequestStatus status) => status switch
        {
            AttendanceCorrectionRequestStatus.Submitted => "更正申請待審核",
            AttendanceCorrectionRequestStatus.Approved when requestType is
                AttendanceCorrectionRequestType.LateExplanation or
                AttendanceCorrectionRequestType.EarlyLeaveExplanation =>
                "說明已核准",
            AttendanceCorrectionRequestStatus.Approved => "更正申請已核准",
            AttendanceCorrectionRequestStatus.Rejected => "更正申請已駁回",
            AttendanceCorrectionRequestStatus.Withdrawn => "已撤回",
            _ => "草稿"
        };

    private static MyAttendanceResult EmptyMyAttendance(bool hasEmployeeBinding) =>
        new(hasEmployeeBinding, [], new MyAttendanceSummary(
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0));

    private sealed record SelfOvertimeProjection(
        Guid Id,
        DateOnly WorkDate,
        int RequestedMinutes,
        OvertimeRequestStatus Status);
}
