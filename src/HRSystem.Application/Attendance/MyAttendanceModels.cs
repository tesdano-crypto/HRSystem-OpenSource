using HRSystem.Domain.Attendance;

namespace HRSystem.Application.Attendance;

public enum MyAttendanceQuickFilter
{
    All = 0,
    Normal = 1,
    Anomalies = 2,
    Late = 3,
    EarlyLeave = 4,
    MissingPunch = 5,
    ExtendedStay = 6,
    PotentialUnreportedOvertime = 7,
    HasOvertimeRequest = 8,
    RecognizedOvertime = 9,
    NonWorkingDayPunch = 10
}

public sealed class MyAttendanceQuery
{
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public MyAttendanceQuickFilter QuickFilter { get; set; }
    public AttendanceReviewSortDirection SortDirection { get; set; } =
        AttendanceReviewSortDirection.Descending;
    public int? Limit { get; set; }
}

public sealed record MyAttendanceReviewItemDto(
    AttendanceReviewAnomalyType AnomalyType,
    int Minutes,
    AttendanceReviewState State,
    string PublicStatus);

public sealed record MyAttendanceRowDto(
    Guid DailyAttendanceResultId,
    DateOnly WorkDate,
    bool IsRequiredWorkday,
    string CalendarClassification,
    string? ShiftName,
    TimeOnly? ScheduledStartTime,
    TimeOnly? ScheduledEndTime,
    DateTime? EffectiveClockInLocalTime,
    DateTime? EffectiveClockOutLocalTime,
    string Status,
    bool IsLate,
    bool IsEarlyLeave,
    bool MissingClockIn,
    bool MissingClockOut,
    int LateMinutes,
    int EarlyLeaveMinutes,
    int ApprovedLeaveMinutes,
    int RequiredAttendanceMinutes,
    int RecognizedWorkMinutes,
    int MissingMinutes,
    int WorkedDuringApprovedLeaveMinutes,
    string LeaveCoverageStatus,
    bool IsEmploymentSuspended,
    bool IsAttendanceExempted,
    int AttendanceExceptionMinutes,
    IReadOnlyList<AttendanceReviewLeaveDto> LeaveItems,
    AttendanceReviewOvertimeRequestDto OvertimeRequest,
    IReadOnlyList<MyAttendanceReviewItemDto> ReviewItems,
    int OverstayMinutes,
    AttendanceReviewOverstayLevel OverstayLevel,
    bool IsAnomaly)
{
    public NonWorkingDayPunchEvidence? PunchEvidence { get; init; }
    public IReadOnlyList<AttendanceOvertimeLink> OvertimeLinks { get; init; } = [];
    public bool NonWorkingPunchPending => PunchEvidence?.HasNonWorkingPunch == true && ReviewItems.Any(x =>
        x.AnomalyType == AttendanceReviewAnomalyType.NonWorkingDayPunch && x.State != AttendanceReviewState.Resolved);
    public IReadOnlyList<AttendanceCorrectionPublicStatusDto>
        CorrectionRequests { get; init; } = [];
}

public sealed record MyAttendanceSummary(
    int ResultCount,
    int WorkdayCount,
    int NormalCount,
    int AnomalyCount,
    int LateCount,
    int EarlyLeaveCount,
    int MissingPunchCount,
    int ExtendedStayCount,
    int PotentialUnreportedOvertimeCount,
    int RecognizedOvertimeCount);

public sealed record MyAttendanceResult(
    bool HasEmployeeBinding,
    IReadOnlyList<MyAttendanceRowDto> Items,
    MyAttendanceSummary Summary);

public sealed record MyAttendanceDetailResult(
    bool HasEmployeeBinding,
    MyAttendanceRowDto? Item);
