using HRSystem.Domain.Payroll;

namespace HRSystem.UnitTests;

public sealed class PayrollTotalsTests
{
    [Fact]
    public void Single_Earning_Resolves_Gross_And_Net()
    {
        var result = Calculate(Earning("BASE", 40000));
        Assert.Equal(40000, result.KnownGrossPay);
        Assert.Equal(0, result.KnownDeductions);
        Assert.Equal(40000, result.NetPay);
        Assert.Equal(PayrollCalculationStatus.Resolved, result.CalculationStatus);
    }

    [Fact]
    public void Multiple_Earnings_And_Overtime_Buckets_Sum_Exactly_Once()
    {
        var result = Calculate(Earning("BASE", 29500), Earning("MEAL", 2000),
            Earning("OVERTIME_FIRST_2H", 500),
            Earning("OVERTIME_AFTER_2H", 700),
            Earning("OVERTIME_AFTER_8H", 900));
        Assert.Equal(33600, result.KnownGrossPay);
    }

    [Fact]
    public void Attendance_Allowance_Uses_Final_Resolved_Amount() =>
        Assert.Equal(1267, Calculate(Earning("ATTENDANCE_ALLOWANCE", 1267))
            .KnownGrossPay);

    [Fact]
    public void Manual_Earning_And_New_Earning_Are_Component_Driven()
    {
        var result = Calculate(Earning("FUTURE_EARNING", 123),
            Earning("OTHER_EARNING", 456, PayrollSnapshotSourceType.ManualAdjustment));
        Assert.Equal(579, result.KnownGrossPay);
    }

    [Fact]
    public void Deductions_Are_Positive_And_Subtracted_Once()
    {
        var result = Calculate(Earning("BASE", 29500),
            Deduction("LEAVE_DEDUCTION", 983), Deduction("LABOR_INSURANCE", 723),
            Deduction("HEALTH_INSURANCE", 458), Deduction("OTHER_DEDUCTION", 100));
        Assert.Equal(29500, result.KnownGrossPay);
        Assert.Equal(2264, result.KnownDeductions);
        Assert.Equal(27236, result.NetPay);
    }

    [Fact]
    public void Gross_40000_Deductions_3000_Net_37000()
    {
        var result = Calculate(Earning("BASE", 40000), Deduction("DEDUCTION", 3000));
        Assert.Equal(37000, result.NetPay);
    }

    [Fact]
    public void Zero_Gross_And_Zero_Deduction_Can_Resolve()
    {
        var result = Calculate(Earning("BASE", 0), Deduction("INSURANCE", 0));
        Assert.Equal(0, result.NetPay);
        Assert.Equal(PayrollCalculationStatus.Resolved, result.CalculationStatus);
    }

    [Fact]
    public void Negative_Net_Is_Not_Clamped_And_Requires_Review()
    {
        var result = Calculate(Earning("BASE", 100), Deduction("DEDUCTION", 150));
        Assert.Equal(-50, result.NetPay);
        Assert.Equal(PayrollCalculationStatus.NeedsReview, result.CalculationStatus);
        Assert.Contains(result.BlockingItems,
            x => x.Reason == PayrollTotalBlockingReason.NegativeNetPay);
    }

    [Theory]
    [InlineData(PayrollCalculationStatus.NeedsSetup)]
    [InlineData(PayrollCalculationStatus.PolicyPending)]
    [InlineData(PayrollCalculationStatus.NeedsReview)]
    [InlineData(PayrollCalculationStatus.NotCalculated)]
    [InlineData(PayrollCalculationStatus.Pending)]
    [InlineData(PayrollCalculationStatus.SourceChanged)]
    public void Unresolved_Required_Component_Makes_Net_Null(
        PayrollCalculationStatus status)
    {
        var result = Calculate(Earning("BASE", 40000),
            Component("HEALTH_INSURANCE", PayrollComponentCategory.Deduction,
                null, status));
        Assert.Null(result.NetPay);
        Assert.NotEqual(PayrollCalculationStatus.Resolved, result.CalculationStatus);
    }

    [Fact]
    public void Status_Precedence_Is_Deterministic()
    {
        var result = Calculate(
            Component("HEALTH", PayrollComponentCategory.Deduction, null,
                PayrollCalculationStatus.PolicyPending),
            Component("LABOR", PayrollComponentCategory.Deduction, null,
                PayrollCalculationStatus.NeedsSetup),
            Component("ATTENDANCE", PayrollComponentCategory.Earning, null,
                PayrollCalculationStatus.NeedsReview));
        Assert.Equal(PayrollCalculationStatus.NeedsReview, result.CalculationStatus);
        Assert.Equal(3, result.BlockingItems.Count);
    }

    [Fact]
    public void Optional_Component_Absent_Does_Not_Block()
    {
        var result = Calculate(Earning("BASE", 100));
        Assert.Equal(PayrollCalculationStatus.Resolved, result.CalculationStatus);
        Assert.Equal(PayrollTotalComponentRequirement.Optional,
            PayrollTotalReadinessPolicy.Classify(Earning("JOB_ALLOWANCE", 1)));
    }

    [Fact]
    public void Disabled_Component_Is_NotApplicable_And_Does_Not_Block()
    {
        var disabled = Component("LABOR_INSURANCE",
            PayrollComponentCategory.Deduction, null, PayrollCalculationStatus.Disabled);
        var result = Calculate(Earning("BASE", 100), disabled);
        Assert.Equal(100, result.NetPay);
        Assert.Equal(PayrollTotalComponentRequirement.NotApplicable,
            PayrollTotalReadinessPolicy.Classify(disabled));
    }

    [Fact]
    public void Resolved_Zero_NotEnrolled_Insurance_Does_Not_Block()
    {
        var result = Calculate(Earning("BASE", 100),
            Deduction("LABOR_INSURANCE", 0), Deduction("HEALTH_INSURANCE", 0));
        Assert.Equal(100, result.NetPay);
    }

    [Fact]
    public void Informational_Component_Is_Excluded()
    {
        var result = Calculate(Earning("BASE", 100),
            Component("INFO", PayrollComponentCategory.Informational, 999,
                PayrollCalculationStatus.Resolved));
        Assert.Equal(100, result.GrossOrThrow());
    }

    [Fact]
    public void Invalid_Sign_Is_Not_Converted_With_Abs()
    {
        var result = Calculate(Earning("BASE", -10));
        Assert.Equal(0, result.KnownGrossPay);
        Assert.Null(result.NetPay);
        Assert.Contains(result.BlockingItems,
            x => x.Reason == PayrollTotalBlockingReason.InvalidSign);
    }

    [Fact]
    public void Missing_Resolved_Amount_Requires_Review()
    {
        var result = Calculate(Component("BASE", PayrollComponentCategory.Earning,
            null, PayrollCalculationStatus.Resolved));
        Assert.Equal(PayrollCalculationStatus.NeedsReview, result.CalculationStatus);
        Assert.Null(result.NetPay);
    }

    [Fact]
    public void Decimal_Sum_Has_No_Second_Rounding()
    {
        var result = Calculate(Earning("A", 1.11m), Earning("B", 2.22m),
            Deduction("D", .03m));
        Assert.Equal(3.33m, result.KnownGrossPay);
        Assert.Equal(.03m, result.KnownDeductions);
        Assert.Equal(3.30m, result.NetPay);
    }

    [Fact]
    public void Fingerprint_Is_Order_Independent_But_Result_Sensitive()
    {
        var a = Earning("A", 1);
        var b = Deduction("B", 2);
        Assert.Equal(PayrollTotalFingerprintV1.Calculate([a, b]),
            PayrollTotalFingerprintV1.Calculate([b, a]));
        Assert.NotEqual(PayrollTotalFingerprintV1.Calculate([a, b]),
            PayrollTotalFingerprintV1.Calculate([a, b with { ResolvedAmount = 3 }]));
    }

    [Fact]
    public void Snapshot_Persists_Totals_Blockers_And_Fingerprint_Once()
    {
        var snapshot = Snapshot();
        var result = Calculate(Earning("BASE", 100),
            Component("HEALTH", PayrollComponentCategory.Deduction, null,
                PayrollCalculationStatus.PolicyPending));
        snapshot.ApplyTotals(result, DateTimeOffset.Parse("2026-08-27T00:00:00Z"));
        Assert.Equal(100, snapshot.GrossPay);
        Assert.Null(snapshot.NetPay);
        Assert.Single(snapshot.TotalBlockingEvidence);
        Assert.Equal(32, snapshot.TotalSourceFingerprint!.Length);
        Assert.ThrowsAny<Exception>(() => snapshot.ApplyTotals(result,
            DateTimeOffset.Parse("2026-08-28T00:00:00Z")));
    }

    private static PayrollTotalCalculationResult Calculate(
        params PayrollTotalComponentInput[] components) =>
        PayrollTotalCalculator.Calculate(components);

    private static PayrollTotalComponentInput Earning(string code, decimal amount,
        PayrollSnapshotSourceType source = PayrollSnapshotSourceType.PayrollPlan) =>
        Component(code, PayrollComponentCategory.Earning, amount,
            PayrollCalculationStatus.Resolved, source);

    private static PayrollTotalComponentInput Deduction(string code, decimal amount) =>
        Component(code, PayrollComponentCategory.Deduction, amount,
            PayrollCalculationStatus.Resolved);

    private static PayrollTotalComponentInput Component(string code,
        PayrollComponentCategory category, decimal? amount,
        PayrollCalculationStatus status,
        PayrollSnapshotSourceType source = PayrollSnapshotSourceType.PayrollPlan) =>
        new(GuidFrom(code, 1), code, category, source, GuidFrom(code, 2), status, amount);

    private static Guid GuidFrom(string value, int suffix)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(value + suffix));
        return new Guid(bytes[..16]);
    }

    private static PayrollEmployeeSnapshot Snapshot() => new(Guid.NewGuid(),
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "P001", "Employee", null, "Department",
        new DateOnly(2020, 1, 1), null, Guid.NewGuid(), "PLAN",
        DateTimeOffset.Parse("2026-08-27T00:00:00Z"), PayrollEmployeeSetupStatus.Ready);
}

file static class PayrollTotalTestExtensions
{
    public static decimal GrossOrThrow(this PayrollTotalCalculationResult result) =>
        result.KnownGrossPay;
}
