using HRSystem.Domain.Payroll;

namespace HRSystem.UnitTests;

public sealed class InsuranceDateSemanticsTests
{
    [Fact]
    public void Full_January_Normalizes_To_Thirty_Days() =>
        AssertCoverage(2026, 1, new(2026, 1, 1), null, 30);

    [Fact]
    public void Full_February_Normalizes_To_Thirty_Days() =>
        AssertCoverage(2026, 2, new(2026, 2, 1), null, 30);

    [Fact]
    public void Full_ThirtyOne_Day_Month_Normalizes_To_Thirty_Days() =>
        AssertCoverage(2026, 7, new(2026, 7, 1), null, 30);

    [Fact]
    public void July_Thirteenth_Enrollment_Covers_Eighteen_Days() =>
        AssertCoverage(2026, 7, new(2026, 7, 13), null, 18);

    [Fact]
    public void Withdrawal_Effective_July_TwentyFirst_Stores_Twentieth_And_Covers_Twenty_Days() =>
        AssertCoverage(2026, 7, new(2026, 7, 1), new(2026, 7, 20), 20);

    [Fact]
    public void July_Thirteenth_Through_Twentieth_Covers_Eight_Days() =>
        AssertCoverage(2026, 7, new(2026, 7, 13), new(2026, 7, 20), 8);

    [Fact]
    public void Coverage_Crossing_Month_Boundary_Uses_Current_Month_Window() =>
        AssertCoverage(2026, 8, new(2026, 7, 20), new(2026, 8, 10), 10);

    [Fact]
    public void Health_FirstDay_Enrollment_With_MonthEnd_Authority_Is_Full_Month()
    {
        var enrollment = Health(new(2026, 7, 1));
        Assert.Equal(HealthInsuranceMonthlyCoverageDecision.CoveredFullMonth,
            Evaluate([enrollment], 2026, 7).Decision);
    }

    [Fact]
    public void Health_MidMonth_Enrollment_With_MonthEnd_Authority_Is_Full_Month()
    {
        var enrollment = Health(new(2026, 7, 13));
        Assert.Equal(HealthInsuranceMonthlyCoverageDecision.CoveredFullMonth,
            Evaluate([enrollment], 2026, 7).Decision);
    }

    [Fact]
    public void Health_September_Is_Not_Covered_After_September_First_Withdrawal()
    {
        var enrollment = Health(new(2026, 7, 1), new(2026, 8, 31));
        Assert.Equal(HealthInsuranceMonthlyCoverageDecision.NotCovered,
            Evaluate([enrollment], 2026, 9).Decision);
    }

    [Fact]
    public void Health_July_Is_Covered_When_Withdrawal_Is_Effective_August_First()
    {
        var enrollment = Health(new(2026, 7, 1), new(2026, 7, 31));
        Assert.Equal(HealthInsuranceMonthlyCoverageDecision.CoveredFullMonth,
            Evaluate([enrollment], 2026, 7).Decision);
    }

    [Fact]
    public void Health_MidMonth_TransferOut_Without_Authority_Needs_Review()
    {
        var enrollment = Health(new(2026, 7, 1), new(2026, 7, 20));
        var result = Evaluate([enrollment], 2026, 7);
        Assert.Equal(HealthInsuranceMonthlyCoverageDecision.NeedsReview,
            result.Decision);
        Assert.Equal(HealthInsuranceMonthlyCoverage.MissingMonthEndAuthorityReason,
            result.ReviewReason);
    }

    private static void AssertCoverage(int year, int month, DateOnly from,
        DateOnly? to, int expectedDays)
    {
        var periodStart = new DateOnly(year, month, 1);
        var periodEnd = new DateOnly(year, month,
            DateTime.DaysInMonth(year, month));
        var result = InsuranceCoverageDays.Calculate(periodStart, periodEnd, from, to);
        Assert.Equal(expectedDays, result.CoveredDays);
        Assert.Equal(expectedDays / 30m, result.Factor);
    }

    private static EmployeeHealthInsuranceEnrollment Health(DateOnly from,
        DateOnly? to = null) => new(Guid.NewGuid(), Guid.NewGuid(),
            HealthInsuranceEnrollmentStatus.Enrolled, 30000, 0, from, to);

    private static HealthInsuranceMonthlyCoverageResult Evaluate(
        IEnumerable<EmployeeHealthInsuranceEnrollment> enrollments,
        int year, int month)
    {
        var periodStart = new DateOnly(year, month, 1);
        var periodEnd = new DateOnly(year, month,
            DateTime.DaysInMonth(year, month));
        return HealthInsuranceMonthlyCoverage.Evaluate(enrollments,
            periodStart, periodEnd);
    }
}
