using HRSystem.Domain.Payroll;

namespace HRSystem.UnitTests;

public sealed class PayrollAttendanceAndLeaveTests
{
    [Theory]
    [InlineData(2026, 2)]
    [InlineData(2028, 2)]
    [InlineData(2026, 7)]
    public void Full_Month_Without_Ineligible_Day_Is_Always_2000(int year, int month)
    {
        var start = new DateOnly(year, month, 1);
        var end = new DateOnly(year, month, DateTime.DaysInMonth(year, month));
        var result = AttendanceAllowanceCalculator.Calculate(2000, start, end,
            start, null, [], true);
        Assert.Equal(2000, result.ResolvedAmount);
        Assert.Equal(30, result.EligibleDays);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(8)]
    public void Each_Final_Attendance_Anomaly_Loses_One_Day(int expectedReason)
    {
        var day = Day((AttendanceAllowanceIneligibilityReason)expectedReason);
        var result = FullMonth([day]);
        Assert.Equal(1933, result.ResolvedAmount);
        Assert.Equal((AttendanceAllowanceIneligibilityReason)expectedReason,
            Assert.Single(result.Evidence).Reasons);
    }

    [Fact]
    public void Same_Day_Reasons_Are_Deduplicated_With_All_Flags()
    {
        var date = new DateOnly(2026, 7, 10);
        PayrollAttendanceDayOutcome[] outcomes =
        [
            new(date, true, true, true, true, true),
            new(date, true, false, false, false, false)
        ];
        var result = FullMonth(outcomes);
        Assert.Equal(1, result.IneligibleDays);
        Assert.Equal(1933, result.ResolvedAmount);
        Assert.Equal(
            AttendanceAllowanceIneligibilityReason.Late |
            AttendanceAllowanceIneligibilityReason.EarlyLeave |
            AttendanceAllowanceIneligibilityReason.MissingClockIn |
            AttendanceAllowanceIneligibilityReason.MissingClockOut |
            AttendanceAllowanceIneligibilityReason.ApprovedLeave,
            Assert.Single(result.Evidence).Reasons);
    }

    [Theory]
    [InlineData(60)]
    [InlineData(240)]
    [InlineData(480)]
    public void Any_Approved_Leave_Duration_Loses_Whole_Allowance_Day(int minutes)
    {
        _ = minutes;
        var result = FullMonth([new(new DateOnly(2026, 7, 10), false, false,
            false, false, true)]);
        Assert.Equal(1, result.IneligibleDays);
        Assert.Equal(1933, result.ResolvedAmount);
    }

    [Fact]
    public void Partial_Employment_Uses_Shared_30_Day_Basis_Without_Rounding_Drift()
    {
        var start = new DateOnly(2026, 7, 1);
        var end = new DateOnly(2026, 7, 31);
        var hire = new DateOnly(2026, 7, 13);
        var normal = AttendanceAllowanceCalculator.Calculate(2000, start, end,
            hire, null, [], true);
        var late = AttendanceAllowanceCalculator.Calculate(2000, start, end,
            hire, null, [new(new DateOnly(2026, 7, 20), true, false, false, false, false)], true);
        Assert.Equal(19, normal.EmploymentPayableDays);
        Assert.Equal(1267, normal.EmploymentProratedMaximum);
        Assert.Equal(1267, normal.ResolvedAmount);
        Assert.Equal(1200, late.ResolvedAmount);
    }

    [Fact]
    public void Allowance_Never_Becomes_Negative() =>
        Assert.Equal(0, FullMonth(Enumerable.Range(1, 31).Select(day =>
            new PayrollAttendanceDayOutcome(new DateOnly(2026, 7, day), true,
                false, false, false, false))).ResolvedAmount);

    [Fact]
    public void Missing_Authoritative_Source_Requires_Review_Not_Full_Allowance()
    {
        var result = AttendanceAllowanceCalculator.Calculate(2000,
            new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31),
            new DateOnly(2026, 7, 1), null, [], false);
        Assert.Equal(PayrollCalculationStatus.NeedsReview, result.CalculationStatus);
        Assert.Null(result.ResolvedAmount);
    }

    [Theory]
    [InlineData(480, 983)]
    [InlineData(240, 492)]
    [InlineData(120, 246)]
    public void Personal_Leave_Uses_Full_Monthly_Base_At_100_Percent(
        int minutes, decimal expected)
    {
        var result = Leave([Segment("PERSONAL", minutes)], Rules());
        Assert.Equal(expected, result.ResolvedAmount);
        Assert.Equal(minutes, result.PersonalLeaveMinutes);
    }

    [Theory]
    [InlineData(480, 492)]
    [InlineData(240, 246)]
    [InlineData(120, 123)]
    public void Ordinary_Sick_Leave_Uses_50_Percent(int minutes, decimal expected)
    {
        var result = Leave([Segment("SICK", minutes)], Rules());
        Assert.Equal(expected, result.ResolvedAmount);
        Assert.Equal(minutes, result.SickLeaveMinutes);
    }

    [Fact]
    public void Mixed_Leave_Aggregates_Raw_Then_Rounds_Once()
    {
        var result = Leave([
            Segment("PERSONAL", 240),
            new(new DateOnly(2026, 7, 11), "SICK", 240, 480)
        ], Rules());
        Assert.Equal(738, result.ResolvedAmount);
        Assert.Equal(737.5m, result.TotalRawAmount);
        Assert.Equal(2, result.Evidence.Count);
    }

    [Fact]
    public void Partial_Employee_Deduction_Still_Uses_Full_Monthly_Base()
    {
        var result = Leave([Segment("PERSONAL", 480)], Rules());
        Assert.Equal(29500, result.FullMonthlyBaseAmount);
        Assert.Equal(983, result.ResolvedAmount);
        Assert.NotEqual(PayrollMoneyRoundingPolicy.RoundNtd(18683m / 30m),
            result.ResolvedAmount);
    }

    [Fact]
    public void Performance_Meal_Job_And_Attendance_Are_Not_Deduction_Bases()
    {
        var result = LeaveDeductionCalculator.Calculate(29500,
            [Segment("PERSONAL", 480)], Rules());
        Assert.Equal(983, result.ResolvedAmount);
    }

    [Fact]
    public void Unsupported_Leave_Is_Policy_Pending_And_Never_Guessed_As_Zero()
    {
        var result = Leave([Segment("ANNUAL", 480)], Rules());
        Assert.Equal(PayrollCalculationStatus.PolicyPending, result.CalculationStatus);
        Assert.Equal(480, result.UnsupportedLeaveMinutes);
        Assert.Null(result.ResolvedAmount);
        Assert.Null(Assert.Single(result.Evidence).DeductionRate);
    }

    [Fact]
    public void Comp_Time_Is_Paid_Leave_With_Zero_Deduction()
    {
        var result = Leave([Segment("COMP_TIME", 480)], Rules());

        Assert.Equal(PayrollCalculationStatus.Resolved, result.CalculationStatus);
        Assert.Equal(0m, result.ResolvedAmount);
        Assert.Equal(0, result.UnsupportedLeaveMinutes);
        var evidence = Assert.Single(result.Evidence);
        Assert.Equal(0m, evidence.DeductionRate);
        Assert.Equal(0m, evidence.RawAmount);
    }

    [Fact]
    public void Invalid_Scheduled_Minutes_Requires_Review()
    {
        var result = Leave([new(new DateOnly(2026, 7, 10), "PERSONAL", 60, 0)], Rules());
        Assert.Equal(PayrollCalculationStatus.PolicyPending, result.CalculationStatus);
        Assert.Equal(PayrollCalculationStatus.NeedsReview,
            Assert.Single(result.Evidence).CalculationStatus);
    }

    private static AttendanceAllowanceCalculationResult FullMonth(
        IEnumerable<PayrollAttendanceDayOutcome> outcomes) =>
        AttendanceAllowanceCalculator.Calculate(2000,
            new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31),
            new DateOnly(2020, 1, 1), null, outcomes, true);

    private static PayrollAttendanceDayOutcome Day(
        AttendanceAllowanceIneligibilityReason reason) => new(
            new DateOnly(2026, 7, 10),
            reason.HasFlag(AttendanceAllowanceIneligibilityReason.Late),
            reason.HasFlag(AttendanceAllowanceIneligibilityReason.EarlyLeave),
            reason.HasFlag(AttendanceAllowanceIneligibilityReason.MissingClockIn),
            reason.HasFlag(AttendanceAllowanceIneligibilityReason.MissingClockOut),
            reason.HasFlag(AttendanceAllowanceIneligibilityReason.ApprovedLeave));

    private static PayrollLeaveSegmentInput Segment(string code, int minutes) =>
        new(new DateOnly(2026, 7, 10), code, minutes, 480);
    private static LeaveDeductionCalculationResult Leave(
        IEnumerable<PayrollLeaveSegmentInput> segments,
        IEnumerable<PayrollLeaveDeductionRule> rules) =>
        LeaveDeductionCalculator.Calculate(29500, segments, rules);
    private static PayrollLeaveDeductionRule[] Rules() =>
    [
        new("PERSONAL", 1m, PayrollLeaveDeductionBasis.BaseSalaryOnly,
            new DateOnly(2026, 1, 1), null),
        new("SICK", .5m, PayrollLeaveDeductionBasis.BaseSalaryOnly,
            new DateOnly(2026, 1, 1), null)
    ];
}
