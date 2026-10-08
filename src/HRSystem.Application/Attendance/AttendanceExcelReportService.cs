using HRSystem.Application.Common.Exceptions;

namespace HRSystem.Application.Attendance;

public sealed class AttendanceExcelReportService(
    IAttendanceReviewService attendanceReviewService,
    TimeProvider timeProvider) : IAttendanceExcelReportService
{
    private const int MaximumEmployeeCount = 500;

    public async Task<AttendanceExcelReport> BuildAsync(
        AttendanceExcelExportRequest request,
        CancellationToken cancellationToken = default)
    {
        Validate(request);
        var employeeIds = request.EmployeeIds.Distinct().ToArray();
        var result = await attendanceReviewService.SearchAsync(
            new AttendanceReviewQuery
            {
                StartDate = request.StartDate,
                EndDate = request.EndDate,
                EmployeeIds = employeeIds,
                OnlyAnomalies = request.OnlyAnomalies,
                OnlyPending = request.OnlyPending,
                QuickFilter = request.QuickFilter,
                SortBy = AttendanceReviewSortField.Employee,
                SortDirection = AttendanceReviewSortDirection.Ascending
            }, cancellationToken);

        var details = result.Items
            .OrderBy(item => item.EmployeeNumber, StringComparer.Ordinal)
            .ThenBy(item => item.WorkDate)
            .Select(ToDetail)
            .ToArray();
        var summaries = result.Items
            .GroupBy(item => new
            {
                Month = new DateOnly(item.WorkDate.Year, item.WorkDate.Month, 1),
                item.EmployeeId,
                item.EmployeeNumber,
                item.EmployeeName,
                item.DepartmentName
            })
            .Select(group => ToMonthly(group.Key.Month, group.Key.EmployeeNumber,
                group.Key.EmployeeName, group.Key.DepartmentName, group.ToArray()))
            .OrderBy(item => item.Month)
            .ThenBy(item => item.DepartmentName, StringComparer.Ordinal)
            .ThenBy(item => item.EmployeeNumber, StringComparer.Ordinal)
            .ToArray();

        return new AttendanceExcelReport(
            CopyRequest(request, employeeIds),
            timeProvider.GetUtcNow(),
            details,
            summaries);
    }

    private static AttendanceExcelExportRequest CopyRequest(
        AttendanceExcelExportRequest request,
        IReadOnlyCollection<Guid> employeeIds) => new()
    {
        StartDate = request.StartDate,
        EndDate = request.EndDate,
        DepartmentId = request.DepartmentId,
        EmployeeIds = employeeIds,
        OnlyAnomalies = request.OnlyAnomalies,
        OnlyPending = request.OnlyPending,
        QuickFilter = request.QuickFilter
    };

    private static AttendanceExcelDetailRow ToDetail(AttendanceReviewRowDto item) =>
        new(
            item.WorkDate,
            Weekday(item.WorkDate),
            item.EmployeeNumber,
            item.EmployeeName,
            item.DepartmentName,
            item.ShiftName ?? item.ShiftCode ?? "未設定",
            item.ScheduledStartTime,
            item.ScheduledEndTime,
            item.MissingClockIn ? null : item.EffectiveClockInLocalTime,
            item.MissingClockOut ? null : item.EffectiveClockOutLocalTime,
            item.LateMinutes,
            item.EarlyLeaveMinutes,
            MissingPunchStatus(item),
            item.OverstayMinutes,
            AttendanceStatus(item),
            LeaveStatus(item),
            OvertimeRequestStatus(item.OvertimeRequest),
            item.OvertimeRequest.ApprovedCoveredMinutes,
            MyAttendanceSummaryCalculator.ValidRecognizedMinutes(item.OvertimeRequest),
            RecognitionStatus(item.OvertimeRequest),
            CorrectionStatus(item));

    private static AttendanceExcelMonthlyRow ToMonthly(
        DateOnly month,
        string employeeNumber,
        string employeeName,
        string departmentName,
        IReadOnlyCollection<AttendanceReviewRowDto> items)
    {
        var metrics = MyAttendanceSummaryCalculator.BuildMonthly(items);
        var leaveItems = items.SelectMany(item => item.LeaveItems).ToArray();
        var leaveSummary = leaveItems.Length == 0
            ? "無"
            : string.Join("；", leaveItems
                .GroupBy(item => item.LeaveTypeName)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .Select(group => $"{group.Key} {group.Sum(item => item.CoveredMinutes)} 分鐘"));
        return new AttendanceExcelMonthlyRow(
            month,
            employeeNumber,
            employeeName,
            departmentName,
            metrics.WorkdayCount,
            metrics.NormalCount,
            metrics.AnomalyCount,
            metrics.LateCount,
            metrics.LateMinutes,
            metrics.EarlyLeaveCount,
            metrics.EarlyLeaveMinutes,
            metrics.MissingPunchCount,
            metrics.ExtendedStayCount,
            metrics.PotentialUnreportedOvertimeCount,
            metrics.ApprovedOvertimeMinutes,
            metrics.ValidRecognizedMinutes,
            leaveItems.Select(item => item.LeaveRequestId).Distinct().Count(),
            leaveSummary);
    }

    private static string AttendanceStatus(AttendanceReviewRowDto item)
    {
        var statuses = new List<string>();
        if (!item.IsRequiredWorkday) statuses.Add("非工作日");
        if (item.IsEmploymentSuspended) statuses.Add("留職停薪");
        if (item.IsAttendanceExempted) statuses.Add("出勤豁免");
        if (item.LeaveCoverageStatus == "Full") statuses.Add("全日請假");
        if (item.LeaveCoverageStatus == "Partial") statuses.Add("部分請假");
        if (item.IsLate) statuses.Add("遲到");
        if (item.IsEarlyLeave) statuses.Add("早退");
        if (item.MissingClockIn || item.MissingClockOut) statuses.Add(MissingPunchStatus(item));
        if (item.MissingMinutes > 0) statuses.Add($"未覆蓋缺勤 {item.MissingMinutes} 分鐘");
        if (item.WorkedDuringApprovedLeaveMinutes > 0) statuses.Add("核准請假期間有出勤");
        if (item.OverstayLevel == AttendanceReviewOverstayLevel.ExtendedStay)
            statuses.Add("延後下班");
        if (item.OverstayLevel == AttendanceReviewOverstayLevel.PotentialUnreportedOvertime)
            statuses.Add("疑似未申報加班");
        if (item.Status == "NoShift") statuses.Add("無班別");
        if (item.Status == "AmbiguousPunch") statuses.Add("打卡不明");
        return statuses.Count == 0 ? "正常" : string.Join("；", statuses.Distinct());
    }

    private static string LeaveStatus(AttendanceReviewRowDto item)
    {
        if (item.LeaveItems.Count == 0) return "無";
        var prefix = item.LeaveCoverageStatus switch
        {
            "Full" => "全日請假",
            "Partial" => "部分請假",
            _ => "核准請假"
        };
        return prefix + "：" + string.Join("；", item.LeaveItems.Select(leave =>
            $"{leave.LeaveTypeName} {leave.CoveredMinutes} 分鐘"));
    }

    private static string OvertimeRequestStatus(AttendanceReviewOvertimeRequestDto value) =>
        value.State switch
        {
            AttendanceReviewOvertimeRequestState.NoRequest => "無申請",
            AttendanceReviewOvertimeRequestState.DraftRequest => "草稿",
            AttendanceReviewOvertimeRequestState.PendingRequest => "待審核",
            AttendanceReviewOvertimeRequestState.ApprovedCovered => "已核准（完整覆蓋）",
            AttendanceReviewOvertimeRequestState.ApprovedPartiallyCovered => "已核准（部分覆蓋）",
            AttendanceReviewOvertimeRequestState.ApprovedPendingRecognition => "已核准，待實際認列",
            AttendanceReviewOvertimeRequestState.RecognitionConfirmed => "認列完成",
            AttendanceReviewOvertimeRequestState.RecognitionNeedsReview => "需重新確認",
            AttendanceReviewOvertimeRequestState.RecognitionReopened => "認列已重新開啟",
            AttendanceReviewOvertimeRequestState.RejectedRequest => "已駁回",
            AttendanceReviewOvertimeRequestState.WithdrawnRequest => "已撤回",
            _ => "未知"
        };

    private static string RecognitionStatus(AttendanceReviewOvertimeRequestDto value)
    {
        if (value.RecognitionIsStale)
            return "出勤資料已變更，待重新確認";
        return value.State switch
        {
            AttendanceReviewOvertimeRequestState.RecognitionConfirmed => "已確認認列",
            AttendanceReviewOvertimeRequestState.RecognitionNeedsReview => "待重新確認",
            AttendanceReviewOvertimeRequestState.RecognitionReopened => "已重新開啟",
            AttendanceReviewOvertimeRequestState.ApprovedPendingRecognition => "待實際認列",
            _ => "無認列"
        };
    }

    private static string CorrectionStatus(AttendanceReviewRowDto item) =>
        item.CorrectionRequests.Count == 0
            ? "無"
            : string.Join("；", item.CorrectionRequests
                .Select(request => request.PublicStatus)
                .Distinct(StringComparer.Ordinal));

    private static string MissingPunchStatus(AttendanceReviewRowDto item) =>
        (item.MissingClockIn, item.MissingClockOut) switch
        {
            (true, true) => "上下班缺卡",
            (true, false) => "上班缺卡",
            (false, true) => "下班缺卡",
            _ => "無"
        };

    private static string Weekday(DateOnly date) => date.DayOfWeek switch
    {
        DayOfWeek.Monday => "星期一",
        DayOfWeek.Tuesday => "星期二",
        DayOfWeek.Wednesday => "星期三",
        DayOfWeek.Thursday => "星期四",
        DayOfWeek.Friday => "星期五",
        DayOfWeek.Saturday => "星期六",
        _ => "星期日"
    };

    private static void Validate(AttendanceExcelExportRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.EmployeeIds.Count == 0 ||
            request.EmployeeIds.Count > MaximumEmployeeCount ||
            request.EmployeeIds.Any(item => item == Guid.Empty))
            throw new ApplicationValidationException("匯出必須選擇 1 至 500 位員工。");
        if (request.EndDate < request.StartDate)
            throw new ApplicationValidationException("結束日期不可早於開始日期。");
        if (request.EndDate.DayNumber - request.StartDate.DayNumber + 1 > 92)
            throw new ApplicationValidationException("匯出日期範圍不可超過 92 天。");
        if (!Enum.IsDefined(request.QuickFilter))
            throw new ApplicationValidationException("匯出篩選條件不合法。");
    }
}
