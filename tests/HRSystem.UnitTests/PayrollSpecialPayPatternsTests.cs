using HRSystem.Domain.Common;
using HRSystem.Domain.Payroll;

namespace HRSystem.UnitTests;

public sealed class PayrollSpecialPayPatternsTests
{
    private static readonly Guid EmployeeId = Guid.NewGuid();
    private static readonly DateOnly Anchor = new(2025, 12, 1);

    [Theory]
    [InlineData(2025, 12, true)]
    [InlineData(2026, 1, false)]
    [InlineData(2026, 5, false)]
    [InlineData(2026, 6, true)]
    [InlineData(2026, 7, false)]
    [InlineData(2026, 12, true)]
    [InlineData(2027, 6, true)]
    public void Semiannual_Fixed_Uses_Anchor_Across_Years(
        int year, int month, bool expected)
    {
        var cycle = Semiannual();
        var start = new DateOnly(year, month, 1);
        var end = new DateOnly(year, month,
            DateTime.DaysInMonth(year, month));

        var result = PayrollParticipationPolicy.Evaluate(true,
            new DateOnly(2020, 1, 1), null, start, end, [cycle]);

        Assert.Equal(expected, result.IsEligible);
        Assert.Equal(expected ? 4000m : null, result.PeriodicFixedAmount);
        if (!expected)
            Assert.Equal("本月不發薪", result.Message);
    }

    [Fact]
    public void No_Explicit_Cycle_Defaults_To_Monthly()
    {
        var result = PayrollParticipationPolicy.Evaluate(true,
            new DateOnly(2020, 1, 1), null,
            new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31), []);
        Assert.True(result.IsEligible);
        Assert.Equal(PayrollPayCycleType.Monthly, result.PayCycleType);
    }

    [Fact]
    public void Inactive_Without_Termination_Is_Excluded()
    {
        var result = PayrollParticipationPolicy.Evaluate(false,
            new DateOnly(2020, 1, 1), null,
            new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31), []);
        Assert.Equal(PayrollParticipationStatus.InactiveWithoutTermination,
            result.Status);
    }

    [Fact]
    public void Historical_Terminated_Employee_Is_Included_When_Overlapping()
    {
        var result = PayrollParticipationPolicy.Evaluate(false,
            new DateOnly(2020, 1, 1), new DateOnly(2026, 7, 20),
            new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31), []);
        Assert.True(result.IsEligible);
    }

    [Fact]
    public void Future_Hire_Is_Excluded()
    {
        var result = PayrollParticipationPolicy.Evaluate(true,
            new DateOnly(2026, 8, 1), null,
            new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31), []);
        Assert.Equal(PayrollParticipationStatus.NotEmployed, result.Status);
    }

    [Fact]
    public void Monthly_Rejects_Periodic_Details() =>
        Assert.Throws<DomainValidationException>(() =>
            new EmployeePayrollPayCycle(Guid.NewGuid(), EmployeeId,
                PayrollPayCycleType.Monthly, new DateOnly(2026, 1, 1),
                anchorPayMonth: Anchor, fixedPaymentAmount: 4000));

    [Fact]
    public void Semiannual_Requires_Positive_Amount_And_Month_Anchor() =>
        Assert.Throws<DomainValidationException>(() =>
            new EmployeePayrollPayCycle(Guid.NewGuid(), EmployeeId,
                PayrollPayCycleType.SemiannualFixed,
                new DateOnly(2026, 1, 1), anchorPayMonth: Anchor,
                fixedPaymentAmount: 0));

    private static EmployeePayrollPayCycle Semiannual() => new(
        Guid.NewGuid(), EmployeeId, PayrollPayCycleType.SemiannualFixed,
        new DateOnly(2025, 12, 1), anchorPayMonth: Anchor,
        fixedPaymentAmount: 4000, reason: "test");
}
