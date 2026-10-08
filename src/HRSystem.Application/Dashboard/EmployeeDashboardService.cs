using HRSystem.Application.Abstractions.Persistence;
using HRSystem.Application.Abstractions.Security;
using HRSystem.Application.Attendance;
using HRSystem.Application.Common.Exceptions;
using HRSystem.Application.Overtime;
using HRSystem.Application.Security;
using HRSystem.Domain.Attendance;
using HRSystem.Domain.LeaveRequests;
using HRSystem.Domain.Overtime;
using Microsoft.EntityFrameworkCore;

namespace HRSystem.Application.Dashboard;

public sealed class EmployeeDashboardService(
    IApplicationDbContext dbContext,
    ICurrentUser currentUser,
    IAttendanceReviewService attendanceReviewService,
    TimeProvider timeProvider) : IEmployeeDashboardService
{
    private static readonly TimeZoneInfo Taipei = ResolveTaipeiTimeZone();
    private static readonly EmployeeDashboardMonthSummaryDto EmptyMonth =
        new(0, 0, 0, 0, 0, 0);
    private static readonly EmployeeDashboardWorkflowSummaryDto EmptyWorkflow =
        new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
    private static readonly IReadOnlyList<EmployeeDashboardQuickActionDto> QuickActions =
    [
        new("查看我的出勤", "/my-attendance"),
        new("申請加班", "/my-overtime"),
        new("出勤更正申請", "/my-attendance-requests"),
        new("新增請假", "/leave-requests")
    ];

    public async Task<EmployeeDashboardDto> GetAsync(
        CancellationToken cancellationToken = default)
    {
        EnsureAccess();
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(
            timeProvider.GetUtcNow(), Taipei).DateTime);
        if (!currentUser.EmployeeId.HasValue)
            return Empty(today);

        var employeeId = currentUser.EmployeeId.Value;
        var employeeName = await dbContext.Employees.AsNoTracking()
            .Where(x => x.Id == employeeId)
            .Select(x => x.ChineseName)
            .SingleOrDefaultAsync(cancellationToken);
        if (employeeName is null)
            return Empty(today);

        var monthStart = new DateOnly(today.Year, today.Month, 1);
        var attendance = await attendanceReviewService.SearchMineAsync(
            new MyAttendanceQuery
            {
                StartDate = monthStart,
                EndDate = today,
                SortDirection = AttendanceReviewSortDirection.Descending
            }, cancellationToken);
        var recent = attendance.Items.Where(x => x.IsRequiredWorkday)
            .Take(7).ToArray();
        var monthSummary = new EmployeeDashboardMonthSummaryDto(
            attendance.Summary.WorkdayCount,
            attendance.Summary.NormalCount,
            attendance.Summary.AnomalyCount,
            attendance.Summary.LateCount,
            attendance.Summary.EarlyLeaveCount,
            attendance.Summary.MissingPunchCount);

        var correctionItems = await dbContext.AttendanceCorrectionRequests
            .AsNoTracking()
            .Where(x => x.EmployeeId == employeeId &&
                (x.Status == AttendanceCorrectionRequestStatus.Draft ||
                 x.Status == AttendanceCorrectionRequestStatus.Submitted ||
                 x.Status == AttendanceCorrectionRequestStatus.Rejected))
            .OrderByDescending(x => x.UpdatedAtUtc)
            .Take(20)
            .Select(x => new CorrectionProjection(x.Id, x.WorkDate, x.RequestType, x.Status))
            .ToListAsync(cancellationToken);

        var overtimeSources = await dbContext.OvertimeRequests.AsNoTracking()
            .Where(x => x.EmployeeId == employeeId &&
                x.OvertimeDate >= monthStart && x.OvertimeDate <= today.AddDays(7) &&
                (x.Status == OvertimeRequestStatus.Draft ||
                 x.Status == OvertimeRequestStatus.Submitted ||
                 x.Status == OvertimeRequestStatus.Approved ||
                 x.Status == OvertimeRequestStatus.Rejected))
            .OrderByDescending(x => x.UpdatedAtUtc)
            .Take(20)
            .Select(x => new OvertimeSourceProjection(
                x.Id, x.EmployeeId, x.OvertimeDate, x.Status,
                x.RowVersion, x.PlannedStartAt, x.PlannedEndAt,
                x.Recognition == null ? null : x.Recognition.Status,
                x.Recognition == null ? null : x.Recognition.RecognizedMinutes,
                x.Recognition == null ? null : x.Recognition.SourceFingerprint))
            .ToListAsync(cancellationToken);
        var overtimeDates = overtimeSources
            .Where(x => x.Status == OvertimeRequestStatus.Approved)
            .Select(x => x.WorkDate).Distinct().ToArray();
        var overtimeAttendance = await dbContext.DailyAttendanceResults
            .AsNoTracking()
            .Where(x => x.EmployeeId == employeeId &&
                overtimeDates.Contains(x.WorkDate))
            .Select(x => new OvertimeAttendanceProjection(
                x.Id, x.WorkDate, x.RowVersion,
                x.ScheduledEndTimeSnapshot, x.IsOvernightShiftSnapshot,
                x.EffectiveClockOutLocalTime, x.MissingClockIn,
                x.MissingClockOut))
            .ToDictionaryAsync(x => x.WorkDate, cancellationToken);
        var overtimeItems = overtimeSources
            .Select(x => ToOvertimeProjection(x, overtimeAttendance))
            .ToArray();

        var localStart = LocalBoundary(monthStart);
        var upcomingEnd = LocalBoundary(today.AddDays(8));
        var leaveItems = await dbContext.LeaveRequests.AsNoTracking()
            .Where(x => x.EmployeeId == employeeId &&
                (x.Status == LeaveRequestStatus.Draft ||
                 x.Status == LeaveRequestStatus.Submitted ||
                 x.Status == LeaveRequestStatus.Rejected ||
                 x.Status == LeaveRequestStatus.Approved ||
                 x.Status == LeaveRequestStatus.CancellationRequested) &&
                x.EndAt >= localStart && x.StartAt < upcomingEnd)
            .OrderByDescending(x => x.UpdatedAtUtc)
            .Take(20)
            .Select(x => new LeaveProjection(
                x.Id, x.RequestNumber, x.Status, x.StartAt, x.EndAt,
                x.LeaveType.Name))
            .ToListAsync(cancellationToken);

        var todayItem = attendance.Items.FirstOrDefault(x => x.WorkDate == today);
        var nowLocal = TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), Taipei).DateTime;
        var todayDto = todayItem is null ? null : ToToday(todayItem, nowLocal);
        var todayStatuses = BuildTodayStatuses(leaveItems, overtimeItems, today);
        var needs = BuildNeedsAction(recent, correctionItems, overtimeItems, leaveItems, today, nowLocal);
        var waiting = BuildWaiting(correctionItems, overtimeItems, leaveItems);
        var upcoming = leaveItems
            .Where(x => x.Status is LeaveRequestStatus.Approved or
                LeaveRequestStatus.CancellationRequested)
            .Where(x => LocalDate(x.StartAt) >= today && LocalDate(x.StartAt) <= today.AddDays(7))
            .OrderBy(x => x.StartAt)
            .Take(5)
            .Select(x => new EmployeeDashboardUpcomingDto(
                LocalDate(x.StartAt), $"{x.LeaveTypeName}｜{x.RequestNumber}",
                "已核准", $"/leave-requests/{x.Id:D}"))
            .ToArray();

        return new EmployeeDashboardDto(
            true, employeeName, today, todayDto, todayStatuses, needs, waiting, recent,
            monthSummary,
            ToWorkflowSummary(correctionItems, overtimeItems, leaveItems,
                monthStart, today),
            upcoming, QuickActions)
        { NonWorkingDayPendingCount = attendance.Items.Count(x => x.NonWorkingPunchPending) };
    }

    private void EnsureAccess()
    {
        if (!currentUser.IsAuthenticated ||
            !currentUser.HasPermission(PolicyNames.EmployeeSelfService))
            throw new ForbiddenAccessException("無權限查看個人首頁。");
    }

    private static EmployeeDashboardDto Empty(DateOnly today) => new(
        false, null, today, null, [], [], [], [], EmptyMonth, EmptyWorkflow, [], QuickActions);

    private static EmployeeDashboardTodayDto ToToday(
        MyAttendanceRowDto item,
        DateTime nowLocal)
    {
        var inProgress = item.IsRequiredWorkday && item.ScheduledEndTime.HasValue &&
            nowLocal.Date == item.WorkDate.ToDateTime(TimeOnly.MinValue).Date &&
            TimeOnly.FromDateTime(nowLocal) < item.ScheduledEndTime.Value;
        var schedule = item.ScheduledStartTime.HasValue && item.ScheduledEndTime.HasValue
            ? $"{item.ScheduledStartTime:HH\\:mm}–{item.ScheduledEndTime:HH\\:mm}"
            : item.IsRequiredWorkday ? "未指派班別" : "非工作日";
        var status = inProgress
            ? item.EffectiveClockInLocalTime.HasValue
                ? "已上班，尚未下班"
                : "尚未有上班紀錄"
            : PublicAttendanceStatus(item);
        var leaveStatus = item.LeaveCoverageStatus switch
        {
            "Full" => $"全日請假（{item.ApprovedLeaveMinutes} 分鐘）",
            "Partial" => $"部分請假（{item.ApprovedLeaveMinutes} 分鐘）",
            _ => null
        };
        var overtimeStatus = item.OvertimeRequest.State switch
        {
            AttendanceReviewOvertimeRequestState.PendingRequest => "加班申請待審核",
            AttendanceReviewOvertimeRequestState.ApprovedPendingRecognition => "加班已核准，待實際工時確認",
            AttendanceReviewOvertimeRequestState.RecognitionConfirmed => $"加班已認列 {item.OvertimeRequest.RecognizedMinutes ?? 0} 分鐘",
            AttendanceReviewOvertimeRequestState.RecognitionNeedsReview or AttendanceReviewOvertimeRequestState.RecognitionReopened => "加班認列待重新確認",
            _ => null
        };
        return new(item.DailyAttendanceResultId, schedule,
            item.EffectiveClockInLocalTime, item.EffectiveClockOutLocalTime,
            status, leaveStatus, overtimeStatus, inProgress,
            !inProgress && item.IsAnomaly,
            item.DailyAttendanceResultId == Guid.Empty ? "/my-attendance?nonWorking=true" : $"/my-attendance/{item.DailyAttendanceResultId:D}");
    }

    private static IReadOnlyList<EmployeeDashboardTodayStatusDto> BuildTodayStatuses(
        IReadOnlyList<LeaveProjection> leave,
        IReadOnlyList<OvertimeProjection> overtime,
        DateOnly today)
    {
        var start = LocalBoundary(today);
        var end = LocalBoundary(today.AddDays(1));
        var result = leave
            .Where(x => (x.Status is LeaveRequestStatus.Approved or LeaveRequestStatus.CancellationRequested) &&
                x.StartAt < end && x.EndAt > start)
            .Select(x => new EmployeeDashboardTodayStatusDto(
                "請假", $"{x.LeaveTypeName}｜{LocalPeriod(x.StartAt, x.EndAt)}",
                $"/leave-requests/{x.Id:D}"))
            .ToList();
        result.AddRange(overtime.Where(x => x.WorkDate == today && x.Status != OvertimeRequestStatus.Withdrawn)
            .Select(x => new EmployeeDashboardTodayStatusDto(
                "加班", OvertimeStatusText(x), "/my-overtime")));
        return result;
    }

    private static IReadOnlyList<EmployeeDashboardTaskDto> BuildNeedsAction(
        IReadOnlyList<MyAttendanceRowDto> recent,
        IReadOnlyList<CorrectionProjection> corrections,
        IReadOnlyList<OvertimeProjection> overtime,
        IReadOnlyList<LeaveProjection> leave,
        DateOnly today,
        DateTime nowLocal)
    {
        var result = new List<EmployeeDashboardTaskDto>();
        foreach (var item in recent)
        {
            if (item.NonWorkingPunchPending)
                result.Add(new("出勤", NonWorkingDayAttendanceDisplay.Queue, NonWorkingDayAttendanceDisplay.Pending,
                    item.WorkDate, "/my-attendance?nonWorking=true"));
            var dayComplete = item.WorkDate < today ||
                item.ScheduledEndTime.HasValue && item.WorkDate == today &&
                TimeOnly.FromDateTime(nowLocal) >= item.ScheduledEndTime.Value;
            var hasCorrection = item.CorrectionRequests.Count > 0;
            if (dayComplete && (item.MissingClockIn || item.MissingClockOut) && !hasCorrection)
                result.Add(new("出勤", "缺卡待處理", PublicAttendanceStatus(item), item.WorkDate,
                    $"/my-attendance-requests?attendanceResultId={item.DailyAttendanceResultId:D}"));
            if (item.OverstayLevel == AttendanceReviewOverstayLevel.PotentialUnreportedOvertime &&
                item.OvertimeRequest.State == AttendanceReviewOvertimeRequestState.NoRequest)
                result.Add(new("加班", "疑似未申報加班", $"延後 {item.OverstayMinutes} 分鐘", item.WorkDate,
                    "/my-overtime"));
        }
        result.AddRange(corrections.Where(x => x.Status is AttendanceCorrectionRequestStatus.Draft or AttendanceCorrectionRequestStatus.Rejected)
            .Select(x => new EmployeeDashboardTaskDto("出勤更正", CorrectionTypeText(x.Type), StatusText(x.Status), x.WorkDate, "/my-attendance-requests")));
        result.AddRange(overtime.Where(x => x.Status == OvertimeRequestStatus.Draft)
            .Select(x => new EmployeeDashboardTaskDto("加班", "加班申請草稿", "草稿", x.WorkDate, "/my-overtime")));
        result.AddRange(leave.Where(x => x.Status is LeaveRequestStatus.Draft or LeaveRequestStatus.Rejected)
            .Select(x => new EmployeeDashboardTaskDto("請假", x.RequestNumber, LeaveStatusText(x.Status), LocalDate(x.StartAt), $"/leave-requests/{x.Id:D}")));
        return result.DistinctBy(x => (x.Category, x.Title, x.WorkDate, x.Url))
            .OrderByDescending(x => x.WorkDate).Take(10).ToArray();
    }

    private static IReadOnlyList<EmployeeDashboardTaskDto> BuildWaiting(
        IReadOnlyList<CorrectionProjection> corrections,
        IReadOnlyList<OvertimeProjection> overtime,
        IReadOnlyList<LeaveProjection> leave)
    {
        var result = new List<EmployeeDashboardTaskDto>();
        result.AddRange(corrections.Where(x => x.Status == AttendanceCorrectionRequestStatus.Submitted)
            .Select(x => new EmployeeDashboardTaskDto("出勤更正", CorrectionTypeText(x.Type), "待審核", x.WorkDate, "/my-attendance-requests")));
        result.AddRange(overtime.Where(x => x.Status == OvertimeRequestStatus.Submitted ||
                x.Status == OvertimeRequestStatus.Approved && x.RecognitionStatus is not OvertimeRecognitionStatus.Confirmed)
            .Select(x => new EmployeeDashboardTaskDto("加班", "加班申請",
                x.Status == OvertimeRequestStatus.Submitted
                    ? "待審核"
                    : x.RecognitionIsStale
                        ? "出勤資料已變更，待重新確認"
                        : x.RecognitionStatus is OvertimeRecognitionStatus.NeedsReview or
                            OvertimeRecognitionStatus.Reopened
                            ? "實際加班時數需重新確認"
                            : "待實際工時確認",
                x.WorkDate, "/my-overtime")));
        result.AddRange(leave.Where(x => x.Status is LeaveRequestStatus.Submitted or LeaveRequestStatus.CancellationRequested)
            .Select(x => new EmployeeDashboardTaskDto("請假", x.RequestNumber, LeaveStatusText(x.Status), LocalDate(x.StartAt), $"/leave-requests/{x.Id:D}")));
        return result.DistinctBy(x => (x.Category, x.Title, x.WorkDate, x.Url))
            .OrderByDescending(x => x.WorkDate).Take(10).ToArray();
    }

    private static EmployeeDashboardWorkflowSummaryDto ToWorkflowSummary(
        IReadOnlyList<CorrectionProjection> corrections,
        IReadOnlyList<OvertimeProjection> overtime,
        IReadOnlyList<LeaveProjection> leave,
        DateOnly monthStart,
        DateOnly today)
    {
        var monthlyOvertime = overtime.Where(x => x.WorkDate >= monthStart && x.WorkDate <= today).ToArray();
        var monthlyLeave = leave.Where(x => LocalDate(x.EndAt) >= monthStart && LocalDate(x.StartAt) <= today).ToArray();
        return new(
        monthlyOvertime.Count(x => x.Status == OvertimeRequestStatus.Draft),
        monthlyOvertime.Count(x => x.Status == OvertimeRequestStatus.Submitted),
        monthlyOvertime.Count(x => x.Status == OvertimeRequestStatus.Approved),
        monthlyOvertime.Count(x => x.Status == OvertimeRequestStatus.Approved &&
            x.RecognitionStatus == OvertimeRecognitionStatus.Pending),
        monthlyOvertime.Count(x => x.Status == OvertimeRequestStatus.Approved &&
            x.RecognitionStatus is OvertimeRecognitionStatus.NeedsReview or
                OvertimeRecognitionStatus.Reopened),
        monthlyOvertime.Where(x => x.RecognitionStatus == OvertimeRecognitionStatus.Confirmed)
            .Sum(x => x.RecognizedMinutes ?? 0),
        corrections.Count(x => x.Status == AttendanceCorrectionRequestStatus.Draft),
        corrections.Count(x => x.Status == AttendanceCorrectionRequestStatus.Submitted),
        corrections.Count(x => x.Status == AttendanceCorrectionRequestStatus.Rejected),
        monthlyLeave.Count(x => x.Status is LeaveRequestStatus.Draft or LeaveRequestStatus.Rejected),
        monthlyLeave.Count(x => x.Status is LeaveRequestStatus.Submitted or LeaveRequestStatus.CancellationRequested),
        monthlyLeave.Count(x => x.Status is LeaveRequestStatus.Approved or LeaveRequestStatus.CancellationRequested));
    }

    private static string PublicAttendanceStatus(MyAttendanceRowDto x) =>
        x.IsEmploymentSuspended ? "留職停薪" : x.IsAttendanceExempted ? "出勤豁免" :
        x.LeaveCoverageStatus == "Full" ? "全日請假" : !x.IsRequiredWorkday ? "非工作日" :
        x.MissingClockIn && x.MissingClockOut ? "上下班缺卡" : x.MissingClockIn ? "上班缺卡" :
        x.MissingClockOut ? "下班缺卡" : x.IsLate ? $"遲到 {x.LateMinutes} 分鐘" :
        x.IsEarlyLeave ? $"早退 {x.EarlyLeaveMinutes} 分鐘" : x.IsAnomaly ? "出勤異常" : "正常";

    private static string CorrectionTypeText(AttendanceCorrectionRequestType type) => type switch
    {
        AttendanceCorrectionRequestType.MissingClockIn => "補上班卡申請",
        AttendanceCorrectionRequestType.MissingClockOut => "補下班卡申請",
        AttendanceCorrectionRequestType.MissingBoth => "補卡申請",
        AttendanceCorrectionRequestType.LateExplanation => "遲到說明",
        AttendanceCorrectionRequestType.EarlyLeaveExplanation => "早退說明",
        _ => "出勤時間更正"
    };

    private static string StatusText(AttendanceCorrectionRequestStatus status) =>
        status == AttendanceCorrectionRequestStatus.Rejected ? "已駁回，請確認" : "草稿";

    private static string LeaveStatusText(LeaveRequestStatus status) => status switch
    {
        LeaveRequestStatus.Submitted => "待簽核",
        LeaveRequestStatus.CancellationRequested => "撤簽申請中",
        LeaveRequestStatus.Rejected => "已駁回，請確認",
        _ => "草稿"
    };

    private static string OvertimeStatusText(OvertimeProjection item) => item.Status switch
    {
        OvertimeRequestStatus.Draft => "加班申請草稿",
        OvertimeRequestStatus.Submitted => "加班申請待審核",
        OvertimeRequestStatus.Rejected => "加班申請已駁回",
        OvertimeRequestStatus.Approved when item.RecognitionStatus == OvertimeRecognitionStatus.Confirmed =>
            $"實際認列 {item.RecognizedMinutes ?? 0} 分鐘",
        OvertimeRequestStatus.Approved when item.RecognitionIsStale =>
            "出勤資料已變更，待重新確認",
        OvertimeRequestStatus.Approved when item.RecognitionStatus is
            OvertimeRecognitionStatus.NeedsReview or OvertimeRecognitionStatus.Reopened =>
            "實際加班時數需重新確認",
        OvertimeRequestStatus.Approved => "加班已核准／待實際工時確認",
        _ => "加班申請已撤回"
    };

    private static OvertimeProjection ToOvertimeProjection(
        OvertimeSourceProjection request,
        IReadOnlyDictionary<DateOnly, OvertimeAttendanceProjection> attendanceByDate)
    {
        if (request.Status != OvertimeRequestStatus.Approved)
            return new(request.Id, request.WorkDate, request.Status,
                request.RecognitionStatus, request.RecognizedMinutes, false);

        attendanceByDate.TryGetValue(request.WorkDate, out var attendance);
        var source = OvertimeRecognitionPolicy.Build(
            request.Id, request.EmployeeId, request.WorkDate, request.Status,
            request.RequestRowVersion, request.Start, request.End,
            attendance?.Id, attendance?.RowVersion,
            attendance?.ScheduledEndTime,
            attendance?.IsOvernightShift == true,
            attendance?.EffectiveClockOut,
            attendance?.MissingClockIn ?? true,
            attendance?.MissingClockOut ?? true);
        var effectiveStatus = OvertimeRecognitionPolicy.EffectiveStatus(
            request.RecognitionStatus, request.RecognitionFingerprint,
            source, out var stale);
        return new(request.Id, request.WorkDate, request.Status,
            effectiveStatus, request.RecognizedMinutes, stale);
    }

    private static DateTimeOffset LocalBoundary(DateOnly date) =>
        new(date.Year, date.Month, date.Day, 0, 0, 0, Taipei.GetUtcOffset(date.ToDateTime(TimeOnly.MinValue)));

    private static DateOnly LocalDate(DateTimeOffset value) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(value, Taipei).DateTime);

    private static string LocalPeriod(DateTimeOffset start, DateTimeOffset end)
    {
        var localStart = TimeZoneInfo.ConvertTime(start, Taipei);
        var localEnd = TimeZoneInfo.ConvertTime(end, Taipei);
        return localStart.Date == localEnd.Date
            ? $"{localStart:HH:mm}–{localEnd:HH:mm}"
            : $"{localStart:MM/dd HH:mm}–{localEnd:MM/dd HH:mm}";
    }

    private static TimeZoneInfo ResolveTaipeiTimeZone()
    {
        foreach (var id in new[] { "Asia/Taipei", "Taipei Standard Time" })
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { }
        throw new InvalidOperationException("找不到台北時區設定。");
    }

    private sealed record CorrectionProjection(Guid Id, DateOnly WorkDate,
        AttendanceCorrectionRequestType Type, AttendanceCorrectionRequestStatus Status);
    private sealed record OvertimeProjection(Guid Id, DateOnly WorkDate,
        OvertimeRequestStatus Status, OvertimeRecognitionStatus? RecognitionStatus,
        int? RecognizedMinutes, bool RecognitionIsStale);
    private sealed record OvertimeSourceProjection(
        Guid Id, Guid EmployeeId, DateOnly WorkDate,
        OvertimeRequestStatus Status, byte[] RequestRowVersion,
        DateTime Start, DateTime End,
        OvertimeRecognitionStatus? RecognitionStatus,
        int? RecognizedMinutes, byte[]? RecognitionFingerprint);
    private sealed record OvertimeAttendanceProjection(
        Guid Id, DateOnly WorkDate, byte[] RowVersion,
        TimeOnly? ScheduledEndTime, bool? IsOvernightShift,
        DateTime? EffectiveClockOut, bool MissingClockIn,
        bool MissingClockOut);
    private sealed record LeaveProjection(Guid Id, string RequestNumber,
        LeaveRequestStatus Status, DateTimeOffset StartAt, DateTimeOffset EndAt,
        string LeaveTypeName);
}
