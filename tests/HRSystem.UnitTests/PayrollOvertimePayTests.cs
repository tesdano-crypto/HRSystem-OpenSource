using HRSystem.Domain.Attendance;
using HRSystem.Domain.Overtime;
using HRSystem.Domain.Payroll;

namespace HRSystem.UnitTests;

public sealed class PayrollOvertimePayTests
{
    private static readonly DateOnly Start = new(2026, 7, 1);
    private static readonly DateOnly End = new(2026, 7, 31);

    [Theory]
    [InlineData("BASE_SALARY", true)]
    [InlineData("PERFORMANCE", true)]
    [InlineData("MEAL_ALLOWANCE", true)]
    [InlineData("JOB_ALLOWANCE", true)]
    [InlineData("ATTENDANCE_ALLOWANCE", true)]
    [InlineData("CERTIFICATE_ALLOWANCE", false)]
    [InlineData("CASE_BONUS", false)]
    [InlineData("OTHER_EARNING", false)]
    public void Hourly_Base_Uses_Metadata_Not_Component_Code(string code, bool include)
    {
        var result = Calculate([Component(code, 1000, include)], []);
        Assert.Equal(include ? 1000m : 0m, result.MonthlyOvertimeBase);
    }

    [Fact]
    public void Confirmed_Base_Example_Excludes_Certificate()
    {
        OvertimeBaseComponentInput[] components =
        [
            Component("BASE_SALARY", 29500, true),
            Component("PERFORMANCE", 4200, true),
            Component("MEAL_ALLOWANCE", 2000, true),
            Component("JOB_ALLOWANCE", 3000, true),
            Component("ATTENDANCE_ALLOWANCE", 2000, true),
            Component("CERTIFICATE_ALLOWANCE", 2000, false)
        ];
        var result = Calculate(components, [Recognition(60)]);
        Assert.Equal(40700, result.MonthlyOvertimeBase);
        Assert.Equal(40700m / 30m / 8m, result.HourlyBase);
    }

    [Theory]
    [InlineData("BASE_SALARY", 29500, 18683)]
    [InlineData("MEAL_ALLOWANCE", 2000, 1267)]
    [InlineData("ATTENDANCE_ALLOWANCE", 2000, 1933)]
    public void Hourly_Base_Uses_Full_Month_Not_Payable_Amount(
        string code, decimal fullMonth, decimal payable)
    {
        _ = payable;
        var result = Calculate([Component(code, fullMonth, true)], [Recognition(60)]);
        Assert.Equal(fullMonth / 30m / 8m, result.HourlyBase);
    }

    [Theory]
    [InlineData(60, 60, 0, 0)]
    [InlineData(120, 120, 0, 0)]
    [InlineData(121, 120, 1, 0)]
    [InlineData(180, 120, 60, 0)]
    [InlineData(480, 120, 360, 0)]
    [InlineData(481, 120, 360, 1)]
    [InlineData(600, 120, 360, 120)]
    public void Allocates_Daily_Minute_Boundaries(int total, int first,
        int afterTwo, int afterEight)
    {
        var day = Assert.Single(Calculate(Base(), [Recognition(total)]).Days);
        Assert.Equal((first, afterTwo, afterEight),
            (day.FirstTwoHoursMinutes, day.AfterTwoHoursMinutes,
                day.AfterEightHoursMinutes));
    }

    [Fact]
    public void Multiple_Segments_On_Same_WorkDate_Share_Cumulative_Buckets()
    {
        var first = Recognition(60, 18, 0);
        var second = Recognition(90, 19, 0);
        var result = Calculate(Base(), [first, second]);
        var day = Assert.Single(result.Days);
        Assert.Equal(150, day.RecognizedMinutes);
        Assert.Equal(120, day.FirstTwoHoursMinutes);
        Assert.Equal(30, day.AfterTwoHoursMinutes);
    }

    [Fact]
    public void Cross_Midnight_Remains_On_Recognition_WorkDate()
    {
        var item = Recognition(180, 23, 0);
        var result = Calculate(Base(), [item]);
        var day = Assert.Single(result.Days);
        Assert.Equal(Start.AddDays(7), day.WorkDate);
        Assert.Equal(120, day.FirstTwoHoursMinutes);
        Assert.Equal(60, day.AfterTwoHoursMinutes);
    }

    [Fact]
    public void Minute_Precision_Is_Not_Rounded_To_Thirty()
    {
        var day = Assert.Single(Calculate(Base(), [Recognition(137)]).Days);
        Assert.Equal(137, day.RecognizedMinutes);
        Assert.Equal(17, day.AfterTwoHoursMinutes);
    }

    [Theory]
    [InlineData(OvertimeRecognitionStatus.Reopened)]
    [InlineData(OvertimeRecognitionStatus.NeedsReview)]
    [InlineData(OvertimeRecognitionStatus.Pending)]
    public void Non_Confirmed_Recognition_Is_Not_Paid(OvertimeRecognitionStatus status)
    {
        var result = Calculate(Base(), [Recognition(60) with { Status = status }]);
        Assert.Equal(PayrollCalculationStatus.NeedsReview, result.CalculationStatus);
        Assert.Null(result.TotalOvertimePay);
    }

    [Fact]
    public void Stale_Recognition_Is_Not_Paid() =>
        Assert.Equal(PayrollCalculationStatus.NeedsReview,
            Calculate(Base(), [Recognition(60) with { IsCurrent = false }])
                .CalculationStatus);

    [Fact]
    public void Confirmed_Zero_Is_A_Resolved_Zero()
    {
        var result = Calculate(Base(), [Recognition(0) with
        {
            RecognizedStartAt = null, RecognizedEndAt = null
        }]);
        Assert.Equal(PayrollCalculationStatus.Resolved, result.CalculationStatus);
        Assert.Equal(0, result.TotalOvertimePay);
    }

    [Fact]
    public void Approved_Request_Without_Recognition_Is_Not_Paid()
    {
        var result = PayrollOvertimePayCalculator.Calculate(Base(), [], Rates(),
            Start, End, true);
        Assert.Equal(PayrollCalculationStatus.NeedsReview, result.CalculationStatus);
    }

    [Fact]
    public void Missing_Full_Month_Base_Requires_Setup()
    {
        var result = Calculate([Component("PERFORMANCE", null, true)],
            [Recognition(60)]);
        Assert.Equal(PayrollCalculationStatus.NeedsSetup, result.CalculationStatus);
    }

    [Fact]
    public void Overlap_Is_Needs_Review_And_Not_Double_Paid()
    {
        var result = Calculate(Base(),
            [Recognition(90, 18, 0), Recognition(90, 19, 0)]);
        Assert.Equal(PayrollCalculationStatus.NeedsReview, result.CalculationStatus);
    }

    [Fact]
    public void Recognition_Outside_Employment_Is_Needs_Review() =>
        Assert.Equal(PayrollCalculationStatus.NeedsReview,
            Calculate(Base(), [Recognition(60) with { IsWithinEmployment = false }])
                .CalculationStatus);

    [Theory]
    [InlineData(AttendanceCalendarClassification.Weekend)]
    [InlineData(AttendanceCalendarClassification.Holiday)]
    public void Unsupported_Special_Day_Is_Policy_Pending(
        AttendanceCalendarClassification classification)
    {
        var result = Calculate(Base(),
            [Recognition(60) with { CalendarClassification = classification }]);
        Assert.Equal(PayrollCalculationStatus.PolicyPending, result.CalculationStatus);
        Assert.Null(result.TotalOvertimePay);
    }

    [Fact]
    public void Rates_And_Bucket_Aggregation_Use_Centralized_Rounding()
    {
        var result = Calculate([Component("BASE_SALARY", 36000, true)],
            [Recognition(90), Recognition(90, 18, 0, Start.AddDays(1))]);
        var first = result.Buckets.Single(x => x.Bucket == OvertimePayBucket.FirstTwoHours);
        Assert.Equal(180, first.Minutes);
        Assert.Equal(1.34m, first.Multiplier);
        Assert.Equal(603m, first.RawPay);
        Assert.Equal(PayrollMoneyRoundingPolicy.RoundNtd(first.RawPay), first.FinalPay);
    }

    [Fact]
    public void Fingerprint_Changes_With_Recognition_Or_Rate()
    {
        var first = Calculate(Base(), [Recognition(60)]).SourceFingerprint;
        var minutes = Calculate(Base(), [Recognition(61)]).SourceFingerprint;
        var changedRates = Rates();
        changedRates[0] = new(Guid.NewGuid(), "2027-v2",
            OvertimePayBucket.FirstTwoHours, 1.35m, Start);
        var rate = PayrollOvertimePayCalculator.Calculate(Base(), [Recognition(60)],
            changedRates, Start, End).SourceFingerprint;
        Assert.False(first.SequenceEqual(minutes));
        Assert.False(first.SequenceEqual(rate));
    }

    [Fact]
    public void Snapshot_Copies_Monthly_Base_Rates_Buckets_Days_And_Recognition_Evidence()
    {
        var result = Calculate(
            [Component("BASE_SALARY", 36000, true),
             Component("CERTIFICATE_ALLOWANCE", 2000, false)],
            [Recognition(180)]);

        var snapshot = new PayrollOvertimePaySnapshot(
            Guid.NewGuid(), Guid.NewGuid(), result);

        Assert.Equal(36000m, snapshot.MonthlyOvertimeBase);
        Assert.Equal(150m, snapshot.HourlyBase);
        Assert.Equal(180, snapshot.TotalRecognizedMinutes);
        Assert.Single(snapshot.IncludedComponents,
            x => x.ComponentCode == "BASE_SALARY" && x.FullMonthlyAmount == 36000m);
        Assert.DoesNotContain(snapshot.IncludedComponents,
            x => x.ComponentCode == "CERTIFICATE_ALLOWANCE");
        Assert.Equal(3, snapshot.Buckets.Count);
        Assert.All(snapshot.Buckets,
            x => Assert.Equal("2026-v1", x.RatePolicyVersion));
        Assert.Single(snapshot.Days, x => x.RecognizedMinutes == 180);
        Assert.Single(snapshot.Recognitions, x => x.RecognizedMinutes == 180);
        Assert.Equal(PayrollOvertimeSourceFingerprintV1.Version,
            snapshot.SourceFingerprintVersion);
        Assert.Equal(32, snapshot.SourceFingerprint.Length);
    }

    [Fact]
    public void Recognition_Evidence_Exposes_No_Free_Text_Actor_Or_Raw_Punch_Fields()
    {
        var names = typeof(PayrollOvertimeRecognitionEvidence)
            .GetProperties()
            .Select(x => x.Name)
            .ToArray();

        Assert.DoesNotContain(names, x =>
            x.Contains("Note", StringComparison.OrdinalIgnoreCase) ||
            x.Contains("Actor", StringComparison.OrdinalIgnoreCase) ||
            x.Contains("Punch", StringComparison.OrdinalIgnoreCase) ||
            x.Contains("Reason", StringComparison.OrdinalIgnoreCase));
    }

    private static PayrollOvertimePayResult Calculate(
        IReadOnlyList<OvertimeBaseComponentInput> components,
        IReadOnlyList<PayrollOvertimeRecognitionInput> recognitions) =>
        PayrollOvertimePayCalculator.Calculate(components, recognitions, Rates(),
            Start, End);

    private static OvertimeBaseComponentInput[] Base() =>
        [Component("BASE_SALARY", 36000, true)];

    private static OvertimeBaseComponentInput Component(string code,
        decimal? amount, bool include) => new(Guid.NewGuid(), code, amount,
        include, Guid.NewGuid(), Start);

    private static PayrollOvertimeRecognitionInput Recognition(int minutes,
        int hour = 18, int minute = 0, DateOnly? workDate = null)
    {
        var date = workDate ?? Start.AddDays(7);
        var start = date.ToDateTime(new TimeOnly(hour, minute));
        var end = start.AddMinutes(minutes);
        return new(Guid.NewGuid(), Guid.NewGuid(), date, start, end, minutes,
            OvertimeRecognitionStatus.Confirmed, true, new byte[32],
            AttendanceCalendarClassification.WorkingDay, true);
    }

    private static OvertimePayRatePolicy[] Rates() =>
    [
        new(Guid.NewGuid(), "2026-v1", OvertimePayBucket.FirstTwoHours, 1.34m, Start),
        new(Guid.NewGuid(), "2026-v1", OvertimePayBucket.AfterTwoHours, 1.67m, Start),
        new(Guid.NewGuid(), "2026-v1", OvertimePayBucket.AfterEightHours, 2.67m, Start)
    ];
}
