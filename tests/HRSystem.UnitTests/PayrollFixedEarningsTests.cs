using HRSystem.Domain.Payroll;

namespace HRSystem.UnitTests;

public sealed class PayrollFixedEarningsTests
{
    [Theory]
    [InlineData(2026, 2, 28)]
    [InlineData(2028, 2, 29)]
    [InlineData(2026, 4, 30)]
    [InlineData(2026, 7, 31)]
    public void Full_Month_Always_Uses_Factor_One(int year, int month, int expectedDays)
    {
        var start = new DateOnly(year, month, 1);
        var end = new DateOnly(year, month, expectedDays);

        var result = Monthly30DayProrationPolicy.Calculate(29500, start, end, start, end);

        Assert.True(result.IsFullMonth);
        Assert.Equal(expectedDays, result.PayableDays);
        Assert.Equal(1, result.PayableFactor);
        Assert.Equal(29500, result.RoundedAmount);
    }

    [Theory]
    [InlineData(2026, 7, 13, 2026, 7, 31, 19)]
    [InlineData(2026, 7, 1, 2026, 7, 20, 20)]
    [InlineData(2026, 7, 10, 2026, 7, 20, 11)]
    public void Partial_Month_Uses_Inclusive_Calendar_Overlap(
        int startYear, int startMonth, int startDay,
        int endYear, int endMonth, int endDay,
        int expectedDays)
    {
        var result = Monthly30DayProrationPolicy.Calculate(
            30000,
            new DateOnly(2026, 7, 1),
            new DateOnly(2026, 7, 31),
            new DateOnly(startYear, startMonth, startDay),
            new DateOnly(endYear, endMonth, endDay));

        Assert.False(result.IsFullMonth);
        Assert.Equal(expectedDays, result.PayableDays);
        Assert.Equal(1000 * expectedDays, result.RoundedAmount);
    }

    [Fact]
    public void July_Mid_Month_Base_Salary_Matches_Approved_Regression()
    {
        var result = JulyPartial(29500);

        Assert.Equal(19, result.PayableDays);
        Assert.Equal(18683.333333m, result.RawProratedAmount);
        Assert.Equal(18683, result.RoundedAmount);
    }

    [Fact]
    public void July_Mid_Month_Meal_Allowance_Matches_Approved_Regression()
    {
        var result = JulyPartial(2000);

        Assert.Equal(1266.666667m, result.RawProratedAmount);
        Assert.Equal(1267, result.RoundedAmount);
    }

    [Theory]
    [InlineData(1.5, 2)]
    [InlineData(2.5, 3)]
    [InlineData(1.49, 1)]
    public void Ntd_Rounding_Is_Explicitly_Away_From_Zero(decimal amount, decimal expected) =>
        Assert.Equal(expected, PayrollMoneyRoundingPolicy.RoundNtd(amount));

    [Fact]
    public void Seniority_Evaluation_Date_Is_Centralized_Period_End_Policy()
    {
        var periodEnd = new DateOnly(2026, 7, 31);

        Assert.Equal(periodEnd, PayrollSeniorityEvaluationPolicies.ResolveDate(
            SeniorityEvaluationPolicy.PeriodEnd, periodEnd));
    }

    [Theory]
    [InlineData(2025, 7, 31, 12)]
    [InlineData(2025, 8, 1, 11)]
    [InlineData(2026, 7, 31, 0)]
    public void Full_Calendar_Months_Uses_Anniversary_Boundary(
        int year, int month, int day, int expected)
    {
        Assert.Equal(expected, PayrollSeniorityEvaluationPolicies.FullCalendarMonths(
            new DateOnly(year, month, day), new DateOnly(2026, 7, 31)));
    }

    [Theory]
    [InlineData(2026, 5, 31, 3, 2100)]
    [InlineData(2025, 8, 31, 12, 4200)]
    [InlineData(2023, 8, 31, 36, 6000)]
    [InlineData(2020, 8, 31, 72, 7500)]
    public void Exact_Seniority_Anniversaries_Select_Expected_Tier(
        int hireYear, int hireMonth, int hireDay, int expectedMonths,
        decimal expectedAmount)
    {
        var months = PayrollSeniorityEvaluationPolicies.FullCalendarMonths(
            new DateOnly(hireYear, hireMonth, hireDay), new DateOnly(2026, 8, 31));
        var componentId = Guid.NewGuid();
        PayrollSeniorityTier[] tiers =
        [
            new(Guid.NewGuid(), componentId, 0, 3, 0),
            new(Guid.NewGuid(), componentId, 3, 12, 2100),
            new(Guid.NewGuid(), componentId, 12, 36, 4200),
            new(Guid.NewGuid(), componentId, 36, 72, 6000),
            new(Guid.NewGuid(), componentId, 72, null, 7500)
        ];

        Assert.Equal(expectedMonths, months);
        Assert.Equal(expectedAmount, Assert.Single(tiers, x => x.Contains(months)).Amount);
    }

    private static Monthly30DayProrationResult JulyPartial(decimal amount) =>
        Monthly30DayProrationPolicy.Calculate(
            amount,
            new DateOnly(2026, 7, 1),
            new DateOnly(2026, 7, 31),
            new DateOnly(2026, 7, 13),
            null);
}
