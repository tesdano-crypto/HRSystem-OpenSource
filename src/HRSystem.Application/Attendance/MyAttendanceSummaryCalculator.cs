namespace HRSystem.Application.Attendance;

internal static class MyAttendanceSummaryCalculator
{
    public static MyAttendanceSummary Build(
        IReadOnlyCollection<MyAttendanceRowDto> items) => new(
        items.Count,
        items.Count(item => item.IsRequiredWorkday),
        items.Count(item => !item.IsAnomaly && item.PunchEvidence?.HasNonWorkingPunch != true),
        items.Count(item => item.IsAnomaly),
        items.Count(item => item.IsLate),
        items.Count(item => item.IsEarlyLeave),
        items.Count(item => item.MissingClockIn || item.MissingClockOut),
        items.Count(item => item.OverstayLevel ==
            AttendanceReviewOverstayLevel.ExtendedStay),
        items.Count(item => item.OverstayLevel ==
            AttendanceReviewOverstayLevel.PotentialUnreportedOvertime),
        items.Count(item => HasValidRecognition(item.OvertimeRequest)));

    public static int ValidRecognizedMinutes(
        AttendanceReviewOvertimeRequestDto overtime) =>
        HasValidRecognition(overtime) ? overtime.RecognizedMinutes ?? 0 : 0;

    public static AttendanceExcelMonthlyMetrics BuildMonthly(
        IReadOnlyCollection<AttendanceReviewRowDto> items) => new(
        items.Count(item => item.IsRequiredWorkday),
        items.Count(item => !item.IsAnomaly && !item.HasNonWorkingPunch),
        items.Count(item => item.IsAnomaly),
        items.Count(item => item.IsLate),
        items.Sum(item => item.LateMinutes),
        items.Count(item => item.IsEarlyLeave),
        items.Sum(item => item.EarlyLeaveMinutes),
        items.Count(item => item.MissingClockIn || item.MissingClockOut),
        items.Count(item => item.OverstayLevel ==
            AttendanceReviewOverstayLevel.ExtendedStay),
        items.Count(item => item.OverstayLevel ==
            AttendanceReviewOverstayLevel.PotentialUnreportedOvertime),
        items.Sum(item => item.OvertimeRequest.ApprovedCoveredMinutes),
        items.Sum(item => ValidRecognizedMinutes(item.OvertimeRequest)));

    private static bool HasValidRecognition(
        AttendanceReviewOvertimeRequestDto overtime) =>
        overtime.State == AttendanceReviewOvertimeRequestState.RecognitionConfirmed &&
        !overtime.RecognitionIsStale;
}

internal sealed record AttendanceExcelMonthlyMetrics(
    int WorkdayCount,
    int NormalCount,
    int AnomalyCount,
    int LateCount,
    int LateMinutes,
    int EarlyLeaveCount,
    int EarlyLeaveMinutes,
    int MissingPunchCount,
    int ExtendedStayCount,
    int PotentialUnreportedOvertimeCount,
    int ApprovedOvertimeMinutes,
    int ValidRecognizedMinutes);
