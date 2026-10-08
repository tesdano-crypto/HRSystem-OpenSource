namespace HRSystem.Application.Attendance;

public enum AttendanceReviewOverstayLevel
{
    None = 0,
    ExtendedStay = 1,
    PotentialUnreportedOvertime = 2
}

public readonly record struct AttendanceReviewOverstay(
    int Minutes,
    AttendanceReviewOverstayLevel Level);

public static class AttendanceReviewOverstayPolicy
{
    public const int NoticeThresholdMinutes = 30;
    public const int CriticalThresholdMinutes = 120;

    public static AttendanceReviewOverstay Evaluate(
        DateOnly workDate,
        TimeOnly? scheduledEndTime,
        bool? isOvernightShift,
        DateTime? effectiveClockOutLocalTime,
        bool isRequiredWorkday,
        bool missingClockOut,
        bool isEmploymentSuspended,
        string leaveCoverageStatus)
    {
        if (!isRequiredWorkday ||
            missingClockOut ||
            isEmploymentSuspended ||
            string.Equals(
                leaveCoverageStatus,
                "Full",
                StringComparison.Ordinal) ||
            !scheduledEndTime.HasValue ||
            !isOvernightShift.HasValue ||
            !effectiveClockOutLocalTime.HasValue)
        {
            return default;
        }

        var scheduledEnd = DateTime.SpecifyKind(
            workDate.ToDateTime(scheduledEndTime.Value),
            DateTimeKind.Unspecified);
        if (isOvernightShift.Value)
        {
            scheduledEnd = scheduledEnd.AddDays(1);
        }

        var clockOut = DateTime.SpecifyKind(
            effectiveClockOutLocalTime.Value,
            DateTimeKind.Unspecified);
        var minutes = checked((int)Math.Floor(
            Math.Max(0, (clockOut - scheduledEnd).TotalMinutes)));
        var level = minutes >= CriticalThresholdMinutes
            ? AttendanceReviewOverstayLevel.PotentialUnreportedOvertime
            : minutes >= NoticeThresholdMinutes
                ? AttendanceReviewOverstayLevel.ExtendedStay
                : AttendanceReviewOverstayLevel.None;
        return new AttendanceReviewOverstay(minutes, level);
    }
}
