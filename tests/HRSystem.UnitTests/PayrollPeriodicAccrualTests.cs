using HRSystem.Domain.Common;
using HRSystem.Domain.Payroll;

namespace HRSystem.UnitTests;

public sealed class PayrollPeriodicAccrualTests
{
    private static readonly Guid EmployeeId = Guid.NewGuid();
    private static readonly DateOnly Anchor = new(2025, 12, 1);

    [Theory]
    [InlineData(2026, 7, false)]
    [InlineData(2026, 12, true)]
    [InlineData(2027, 6, true)]
    public void Cycle_Eligibility_Uses_Anchor_And_Configured_Months(
        int year, int month, bool expected)
    {
        var setting = Setting(new DateOnly(2026, 1, 1), null, 4000);
        var start = new DateOnly(year, month, 1);
        var end = new DateOnly(year, month,
            DateTime.DaysInMonth(year, month));

        var result = PayrollParticipationPolicy.Evaluate(true,
            new DateOnly(2020, 1, 1), null, start, end, [setting]);

        Assert.Equal(expected, result.IsEligible);
        Assert.Equal(PayrollPayCycleType.PeriodicAccruedFixed,
            result.PayCycleType);
        if (!expected)
            Assert.Equal("本月不發薪", result.Message);
    }

    [Fact]
    public void Six_Equal_Months_Aggregate_To_24000()
    {
        var setting = Setting(new DateOnly(2026, 12, 1),
            new DateOnly(2027, 5, 31), 4000);

        var result = PayrollPeriodicAccrualCalculator.Calculate(setting,
            [setting], new DateOnly(2026, 12, 1));

        Assert.Equal(PayrollCalculationStatus.Resolved,
            result.CalculationStatus);
        Assert.Equal(24000, result.TotalAmount);
        Assert.Equal(new DateOnly(2026, 12, 1), result.CoveredFrom);
        Assert.Equal(new DateOnly(2027, 5, 31), result.CoveredTo);
        Assert.Equal(6, result.Months.Count);
    }

    [Fact]
    public void Cross_Year_Effective_Changes_Are_Summed_Per_Month()
    {
        var first = Setting(new DateOnly(2026, 12, 1),
            new DateOnly(2027, 1, 31), 4000);
        var second = Setting(new DateOnly(2027, 2, 1),
            new DateOnly(2027, 5, 31), 5000);

        var result = PayrollPeriodicAccrualCalculator.Calculate(first,
            [first, second], new DateOnly(2026, 12, 1));

        Assert.Equal(28000, result.TotalAmount);
        Assert.Equal([4000m, 4000m, 5000m, 5000m, 5000m, 5000m],
            result.Months.Select(x => x.MonthlyFixedAmount));
    }

    [Fact]
    public void Missing_Covered_Month_Is_NeedsSetup_Not_Guessed()
    {
        var partial = Setting(new DateOnly(2026, 12, 1),
            new DateOnly(2027, 3, 31), 4000);

        var result = PayrollPeriodicAccrualCalculator.Calculate(partial,
            [partial], new DateOnly(2026, 12, 1));

        Assert.Equal(PayrollCalculationStatus.NeedsSetup,
            result.CalculationStatus);
        Assert.Null(result.TotalAmount);
        Assert.Equal(2, result.Months.Count(x =>
            x.CalculationStatus == PayrollCalculationStatus.NeedsSetup));
    }

    [Fact]
    public void Retroactive_Amount_Change_Changes_Source_Fingerprint()
    {
        var original = Setting(new DateOnly(2026, 12, 1),
            new DateOnly(2027, 5, 31), 4000);
        var changed = Setting(new DateOnly(2026, 12, 1),
            new DateOnly(2027, 5, 31), 5000, original.Id);
        var first = PayrollPeriodicAccrualCalculator.Calculate(original,
            [original], new DateOnly(2026, 12, 1));
        var second = PayrollPeriodicAccrualCalculator.Calculate(changed,
            [changed], new DateOnly(2026, 12, 1));

        Assert.NotEqual(Convert.ToHexString(first.SourceFingerprint),
            Convert.ToHexString(second.SourceFingerprint));
        Assert.False(PayrollPeriodicAccrualSourceFingerprintV1.IsCurrent(
            first.SourceFingerprint, changed, second.CoveredFrom,
            second.CoveredTo, second.Months));
    }

    [Fact]
    public void Snapshot_Retains_Covered_Month_Evidence()
    {
        var setting = Setting(new DateOnly(2026, 12, 1),
            new DateOnly(2027, 5, 31), 4000);
        var result = PayrollPeriodicAccrualCalculator.Calculate(setting,
            [setting], new DateOnly(2026, 12, 1));

        var snapshot = new PayrollPeriodicAccrualSnapshot(Guid.NewGuid(),
            Guid.NewGuid(), result);

        Assert.Equal(24000, snapshot.TotalAmount);
        Assert.Equal(6, snapshot.Months.Count);
        Assert.Equal(32, snapshot.SourceFingerprint.Length);
    }

    [Fact]
    public void Periodic_Accrual_Requires_Complete_Typed_Settings() =>
        Assert.Throws<DomainValidationException>(() =>
            new EmployeePayrollPayCycle(Guid.NewGuid(), EmployeeId,
                PayrollPayCycleType.PeriodicAccruedFixed,
                new DateOnly(2026, 1, 1), anchorPayMonth: Anchor,
                monthlyFixedAmount: 4000, cycleMonths: 6));

    private static EmployeePayrollPayCycle Setting(DateOnly from,
        DateOnly? to, decimal amount, Guid? id = null) => new(
            id ?? Guid.NewGuid(), EmployeeId,
            PayrollPayCycleType.PeriodicAccruedFixed, from, to,
            anchorPayMonth: Anchor, reason: "test",
            monthlyFixedAmount: amount, cycleMonths: 6,
            paymentTiming: PayrollPeriodicPaymentTiming.CycleStart);
}
