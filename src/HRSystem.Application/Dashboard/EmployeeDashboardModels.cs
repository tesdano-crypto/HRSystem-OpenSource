using HRSystem.Application.Attendance;

namespace HRSystem.Application.Dashboard;

public sealed record EmployeeDashboardDto(
    bool HasEmployeeBinding,
    string? EmployeeName,
    DateOnly Today,
    EmployeeDashboardTodayDto? TodayAttendance,
    IReadOnlyList<EmployeeDashboardTodayStatusDto> TodayStatuses,
    IReadOnlyList<EmployeeDashboardTaskDto> NeedsAction,
    IReadOnlyList<EmployeeDashboardTaskDto> WaitingReview,
    IReadOnlyList<MyAttendanceRowDto> RecentAttendance,
    EmployeeDashboardMonthSummaryDto MonthSummary,
    EmployeeDashboardWorkflowSummaryDto WorkflowSummary,
    IReadOnlyList<EmployeeDashboardUpcomingDto> Upcoming,
    IReadOnlyList<EmployeeDashboardQuickActionDto> QuickActions)
{
    public int NonWorkingDayPendingCount { get; init; }
}

public sealed record EmployeeDashboardTodayDto(
    Guid DailyAttendanceResultId,
    string Schedule,
    DateTime? ClockIn,
    DateTime? ClockOut,
    string Status,
    string? LeaveStatus,
    string? OvertimeStatus,
    bool IsInProgress,
    bool IsAnomaly,
    string DetailUrl);

public sealed record EmployeeDashboardTodayStatusDto(
    string Category,
    string Text,
    string Url);

public sealed record EmployeeDashboardTaskDto(
    string Category,
    string Title,
    string Status,
    DateOnly? WorkDate,
    string Url);

public sealed record EmployeeDashboardMonthSummaryDto(
    int Workdays,
    int NormalDays,
    int AnomalyDays,
    int LateDays,
    int EarlyLeaveDays,
    int MissingPunchDays);

public sealed record EmployeeDashboardWorkflowSummaryDto(
    int OvertimeDraft,
    int OvertimeSubmitted,
    int OvertimeApproved,
    int OvertimeApprovedPendingRecognition,
    int OvertimeNeedsReview,
    int OvertimeRecognizedMinutes,
    int CorrectionDraft,
    int CorrectionSubmitted,
    int CorrectionRejected,
    int LeaveDraftOrRejected,
    int LeaveSubmitted,
    int LeaveApprovedThisMonth);

public sealed record EmployeeDashboardUpcomingDto(
    DateOnly Date,
    string Title,
    string Status,
    string Url);

public sealed record EmployeeDashboardQuickActionDto(string Text, string Url);
