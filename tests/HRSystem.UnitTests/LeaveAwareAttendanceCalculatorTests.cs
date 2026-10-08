using HRSystem.Domain.Attendance;

namespace HRSystem.UnitTests;

public sealed class LeaveAwareAttendanceCalculatorTests
{
    private static readonly DateOnly WorkDate = new(2026, 8, 3);
    private static readonly Guid LeaveTypeId = Guid.NewGuid();
    private static readonly AttendanceShiftSnapshot Shift = new(
        Guid.NewGuid(),
        "正常班",
        new TimeOnly(8, 0),
        new TimeOnly(8, 1),
        new TimeOnly(12, 0),
        new TimeOnly(13, 30),
        new TimeOnly(17, 30),
        480,
        false,
        false);

    [Fact]
    public void Full_day_leave_without_punches_has_no_attendance_exception()
    {
        var result = Calculate(
            [],
            Leave(new TimeOnly(8, 0), new TimeOnly(17, 30)));

        Assert.Equal(480, result.ApprovedLeaveMinutes);
        Assert.Equal(0, result.RequiredAttendanceMinutes);
        Assert.Equal(0, result.MissingMinutes);
        Assert.Equal(LeaveCoverageStatus.Full, result.LeaveCoverageStatus);
        Assert.Equal(AttendanceDailyStatus.Normal, result.Status);
        Assert.False(result.IsLate);
        Assert.False(result.IsEarlyLeave);
        Assert.False(result.MissingClockIn);
        Assert.False(result.MissingClockOut);
    }

    [Fact]
    public void Morning_leave_allows_normal_afternoon_punches()
    {
        var result = Calculate(
            Punches(new TimeOnly(13, 25), new TimeOnly(17, 35)),
            Leave(new TimeOnly(8, 0), new TimeOnly(12, 0)));

        Assert.Equal(240, result.ApprovedLeaveMinutes);
        Assert.Equal(240, result.RequiredAttendanceMinutes);
        Assert.Equal(240, result.RecognizedWorkMinutes);
        Assert.Equal(0, result.MissingMinutes);
        Assert.Equal(LeaveCoverageStatus.Partial, result.LeaveCoverageStatus);
        Assert.Equal(AttendanceDailyStatus.Normal, result.Status);
    }

    [Fact]
    public void Afternoon_leave_allows_normal_morning_punches()
    {
        var result = Calculate(
            Punches(new TimeOnly(7, 55), new TimeOnly(12, 5)),
            Leave(new TimeOnly(13, 30), new TimeOnly(17, 30)));

        Assert.Equal(240, result.ApprovedLeaveMinutes);
        Assert.Equal(0, result.MissingMinutes);
        Assert.False(result.IsEarlyLeave);
        Assert.False(result.MissingClockOut);
        Assert.Equal(AttendanceDailyStatus.Normal, result.Status);
    }

    [Fact]
    public void Afternoon_leave_does_not_hide_uncovered_morning_early_leave()
    {
        var result = Calculate(
            Punches(new TimeOnly(8, 0), new TimeOnly(11, 0)),
            Leave(new TimeOnly(13, 30), new TimeOnly(17, 30)));

        Assert.Equal(60, result.MissingMinutes);
        Assert.Equal(3600, result.EarlyLeaveSeconds);
        Assert.True(result.IsEarlyLeave);
        Assert.Equal(AttendanceDailyStatus.EarlyLeave, result.Status);
    }

    [Fact]
    public void Morning_leave_uses_afternoon_required_start_for_lateness()
    {
        var result = Calculate(
            Punches(new TimeOnly(14, 0), new TimeOnly(17, 30)),
            Leave(new TimeOnly(8, 0), new TimeOnly(12, 0)));

        Assert.Equal(30, result.MissingMinutes);
        Assert.Equal(1800, result.LateSeconds);
        Assert.Equal(AttendanceDailyStatus.Late, result.Status);
    }

    [Fact]
    public void Hour_leave_can_fully_cover_late_arrival()
    {
        var result = Calculate(
            Punches(new TimeOnly(9, 0), new TimeOnly(17, 30)),
            Leave(new TimeOnly(8, 0), new TimeOnly(9, 0)));

        Assert.Equal(60, result.ApprovedLeaveMinutes);
        Assert.Equal(0, result.LateSeconds);
        Assert.Equal(0, result.MissingMinutes);
        Assert.Equal(AttendanceDailyStatus.Normal, result.Status);
    }

    [Fact]
    public void Partial_hour_leave_preserves_uncovered_lateness()
    {
        var result = Calculate(
            Punches(new TimeOnly(9, 0), new TimeOnly(17, 30)),
            Leave(new TimeOnly(8, 0), new TimeOnly(8, 30)));

        Assert.Equal(30, result.ApprovedLeaveMinutes);
        Assert.Equal(1800, result.LateSeconds);
        Assert.Equal(30, result.MissingMinutes);
        Assert.Equal(AttendanceDailyStatus.Late, result.Status);
    }

    [Fact]
    public void Lunch_only_leave_does_not_create_a_leave_segment()
    {
        var result = Calculate(
            Punches(new TimeOnly(8, 0), new TimeOnly(17, 30)),
            Leave(new TimeOnly(12, 0), new TimeOnly(13, 30)));

        Assert.Equal(0, result.ApprovedLeaveMinutes);
        Assert.Empty(result.LeaveSegments);
        Assert.Equal(LeaveCoverageStatus.None, result.LeaveCoverageStatus);
    }

    [Fact]
    public void Non_workday_does_not_create_leave_coverage_or_exceptions()
    {
        var result = AttendanceDailyCalculator.Calculate(
            WorkDate,
            false,
            Shift,
            [],
            [Leave(new TimeOnly(8, 0), new TimeOnly(17, 30))]);

        Assert.Equal(0, result.ApprovedLeaveMinutes);
        Assert.Equal(0, result.RequiredAttendanceMinutes);
        Assert.Equal(0, result.MissingMinutes);
        Assert.Empty(result.LeaveSegments);
        Assert.Equal(LeaveCoverageStatus.None, result.LeaveCoverageStatus);
        Assert.Equal(AttendanceDailyStatus.RestDay, result.Status);
    }

    [Fact]
    public void Missing_shift_does_not_guess_work_or_leave_intervals()
    {
        var result = AttendanceDailyCalculator.Calculate(
            WorkDate,
            true,
            null,
            [],
            [Leave(new TimeOnly(8, 0), new TimeOnly(17, 30))]);

        Assert.Equal(0, result.ApprovedLeaveMinutes);
        Assert.Equal(0, result.RequiredAttendanceMinutes);
        Assert.Equal(0, result.MissingMinutes);
        Assert.Empty(result.LeaveSegments);
        Assert.Equal(LeaveCoverageStatus.None, result.LeaveCoverageStatus);
        Assert.Equal(AttendanceDailyStatus.NoShift, result.Status);
    }

    [Fact]
    public void Attendance_during_approved_leave_is_preserved_as_warning_minutes()
    {
        var result = Calculate(
            Punches(new TimeOnly(8, 0), new TimeOnly(17, 30)),
            Leave(new TimeOnly(13, 30), new TimeOnly(17, 30)));

        Assert.Equal(240, result.ApprovedLeaveMinutes);
        Assert.Equal(240, result.RecognizedWorkMinutes);
        Assert.Equal(240, result.WorkedDuringApprovedLeaveMinutes);
        Assert.Equal(0, result.MissingMinutes);
        Assert.Equal(480,
            result.RecognizedWorkMinutes +
            result.WorkedDuringApprovedLeaveMinutes);
    }

    [Fact]
    public void Overlapping_approved_leave_intervals_are_unioned_without_double_counting()
    {
        var first = Leave(
            new TimeOnly(8, 0),
            new TimeOnly(9, 0),
            Guid.NewGuid());
        var second = Leave(
            new TimeOnly(8, 30),
            new TimeOnly(10, 0),
            Guid.NewGuid());

        var result = Calculate(
            Punches(new TimeOnly(10, 0), new TimeOnly(17, 30)),
            first,
            second);

        Assert.Equal(120, result.ApprovedLeaveMinutes);
        Assert.Equal(360, result.RequiredAttendanceMinutes);
        Assert.Equal(120, result.LeaveSegments.Sum(item => item.CoveredMinutes));
    }

    [Fact]
    public void Cross_day_leave_is_clipped_to_each_daily_work_interval()
    {
        var leaveId = Guid.NewGuid();
        var leave = new AttendanceApprovedLeaveInterval(
            leaveId,
            LeaveTypeId,
            "ANNUAL",
            "特休",
            WorkDate.ToDateTime(new TimeOnly(16, 30)),
            WorkDate.AddDays(1).ToDateTime(new TimeOnly(9, 30)));

        var first = AttendanceDailyCalculator.Calculate(
            WorkDate,
            true,
            Shift,
            [],
            [leave]);
        var second = AttendanceDailyCalculator.Calculate(
            WorkDate.AddDays(1),
            true,
            Shift,
            [],
            [leave]);

        Assert.Equal(60, first.ApprovedLeaveMinutes);
        Assert.Equal(90, second.ApprovedLeaveMinutes);
        Assert.Equal(150,
            first.ApprovedLeaveMinutes + second.ApprovedLeaveMinutes);
    }

    [Fact]
    public void Current_adjustment_uses_the_same_leave_aware_intervals()
    {
        var raw = Calculate(
            [],
            Leave(new TimeOnly(8, 0), new TimeOnly(12, 0)));
        var adjusted = AttendanceDailyCalculator.ApplyEffectiveTimes(
            WorkDate,
            Shift,
            raw,
            WorkDate.ToDateTime(new TimeOnly(14, 0)),
            WorkDate.ToDateTime(new TimeOnly(17, 30)));

        Assert.Equal(240, adjusted.ApprovedLeaveMinutes);
        Assert.Equal(30, adjusted.MissingMinutes);
        Assert.Equal(1800, adjusted.LateSeconds);
        Assert.Equal(AttendanceDailyStatus.Late, adjusted.Status);
    }

    private static AttendanceDailyCalculation Calculate(
        IReadOnlyCollection<AttendancePunchCandidate> punches,
        params AttendanceApprovedLeaveInterval[] leaves) =>
        AttendanceDailyCalculator.Calculate(
            WorkDate,
            true,
            Shift,
            punches,
            leaves);

    private static AttendanceApprovedLeaveInterval Leave(
        TimeOnly start,
        TimeOnly end,
        Guid? requestId = null) =>
        new(
            requestId ?? Guid.NewGuid(),
            LeaveTypeId,
            "ANNUAL",
            "特休",
            WorkDate.ToDateTime(start),
            WorkDate.ToDateTime(end));

    private static IReadOnlyCollection<AttendancePunchCandidate> Punches(
        TimeOnly clockIn,
        TimeOnly clockOut) =>
    [
        new(Guid.NewGuid(), WorkDate.ToDateTime(clockIn)),
        new(Guid.NewGuid(), WorkDate.ToDateTime(clockOut))
    ];
}
