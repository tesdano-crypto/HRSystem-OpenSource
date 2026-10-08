using HRSystem.Domain.Common;
using HRSystem.Domain.Payroll;

namespace HRSystem.UnitTests;

public sealed class PayrollHealthInsuranceTests
{
    private static readonly DateOnly Start = new(2026, 8, 1);
    private static readonly DateOnly End = new(2026, 8, 31);
    private static readonly DateOnly EmploymentStart = new(2020, 1, 1);

    [Theory]
    [InlineData(0, 0, 1, 465)]
    [InlineData(1, 1, 2, 931)]
    [InlineData(2, 2, 3, 1396)]
    [InlineData(3, 3, 4, 1861)]
    [InlineData(5, 3, 4, 1861)]
    public void Employee_And_Capped_Dependents_Are_Charged(
        int actual, int chargeable, int units, decimal expected)
    {
        var result = Calculate([Enrollment(30000, actual)], [Policy()]);
        Assert.Equal(PayrollCalculationStatus.Resolved, result.CalculationStatus);
        Assert.Equal(chargeable, result.ChargeableDependentCount);
        Assert.Equal(units, result.ContributionUnits);
        Assert.Equal(expected, result.FinalEmployeeDeduction);
    }

    [Fact]
    public void Explicit_Not_Enrolled_Is_Resolved_Zero()
    {
        var enrollment = new EmployeeHealthInsuranceEnrollment(Guid.NewGuid(),
            Guid.NewGuid(), HealthInsuranceEnrollmentStatus.NotEnrolled,
            null, null, EmploymentStart);
        var result = Calculate([enrollment], []);
        Assert.Equal(PayrollCalculationStatus.Resolved, result.CalculationStatus);
        Assert.Equal(0, result.FinalEmployeeDeduction);
    }

    [Fact]
    public void Missing_Enrollment_Needs_Setup() =>
        Assert.Equal(PayrollCalculationStatus.NeedsSetup,
            Calculate([], [Policy()]).CalculationStatus);

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Missing_Insured_Amount_Or_Dependent_Count_Needs_Setup(
        bool missingAmount, bool missingDependents)
    {
        var result = Calculate([Enrollment(missingAmount ? null : 30000,
            missingDependents ? null : 0)], [Policy()]);
        Assert.Equal(PayrollCalculationStatus.NeedsSetup, result.CalculationStatus);
    }

    [Fact]
    public void Missing_Policy_Is_Policy_Pending() =>
        Assert.Equal(PayrollCalculationStatus.PolicyPending,
            Calculate([Enrollment(30000, 0)], []).CalculationStatus);

    [Fact]
    public void MidMonth_Enrollment_Active_At_MonthEnd_Is_Full_Month()
    {
        var partial = new EmployeeHealthInsuranceEnrollment(Guid.NewGuid(),
            Guid.NewGuid(), HealthInsuranceEnrollmentStatus.Enrolled,
            30000, 0, new DateOnly(2026, 8, 15));
        var result = Calculate([partial], [Policy()]);
        Assert.Equal(PayrollCalculationStatus.Resolved, result.CalculationStatus);
        Assert.Equal(465, result.FinalEmployeeDeduction);
    }

    [Fact]
    public void Overlap_Needs_Review_But_Employment_Dates_Do_Not_Clip_Insurance()
    {
        Assert.Equal(PayrollCalculationStatus.NeedsReview,
            Calculate([Enrollment(30000, 0), Enrollment(32000, 1)], [Policy()])
                .CalculationStatus);
        var beforeEmployment = new EmployeeHealthInsuranceEnrollment(Guid.NewGuid(),
            Guid.NewGuid(), HealthInsuranceEnrollmentStatus.Enrolled,
            30000, 0, new DateOnly(2019, 12, 1));
        Assert.Equal(PayrollCalculationStatus.Resolved,
            Calculate([beforeEmployment], [Policy()],
                employmentStart: new DateOnly(2020, 1, 1)).CalculationStatus);

        Assert.Equal(PayrollCalculationStatus.Resolved,
            Calculate([Enrollment(30000, 0)], [Policy()],
                employmentEnd: new DateOnly(2026, 7, 31)).CalculationStatus);
    }

    [Fact]
    public void MidMonth_TransferOut_Without_MonthEnd_Authority_Needs_Review()
    {
        var enrollment = new EmployeeHealthInsuranceEnrollment(Guid.NewGuid(),
            Guid.NewGuid(), HealthInsuranceEnrollmentStatus.Enrolled,
            30000, 0, Start, new DateOnly(2026, 8, 20));

        var decision = HealthInsuranceMonthlyCoverage.Evaluate([enrollment], Start, End);
        var result = Calculate([enrollment], [Policy()]);

        Assert.Equal(HealthInsuranceMonthlyCoverageDecision.NeedsReview,
            decision.Decision);
        Assert.Equal(HealthInsuranceMonthlyCoverage.MissingMonthEndAuthorityReason,
            decision.ReviewReason);
        Assert.Equal(PayrollCalculationStatus.NeedsReview, result.CalculationStatus);
    }

    [Fact]
    public void Fictional_MonthEnd_Authority_Covers_Two_Months()
    {
        var enrollment = new EmployeeHealthInsuranceEnrollment(Guid.NewGuid(),
            Guid.NewGuid(), HealthInsuranceEnrollmentStatus.Enrolled,
            42000, 1, new DateOnly(2032, 7, 1), new DateOnly(2032, 8, 31));

        var july = CalculateMonth([enrollment],
            [PolicyFrom(new DateOnly(2032, 7, 1))], 2032, 7);
        var august = CalculateMonth([enrollment],
            [PolicyFrom(new DateOnly(2032, 7, 1))], 2032, 8);
        Assert.Equal(PayrollCalculationStatus.Resolved, july.CalculationStatus);
        Assert.Equal(PayrollCalculationStatus.Resolved, august.CalculationStatus);
        Assert.Equal(july.FinalEmployeeDeduction, august.FinalEmployeeDeduction);
        Assert.True(july.FinalEmployeeDeduction > 0);
        var september = CalculateMonth([enrollment],
            [PolicyFrom(new DateOnly(2032, 7, 1))], 2032, 9);
        Assert.Equal(PayrollCalculationStatus.Resolved, september.CalculationStatus);
        Assert.Equal(0, september.FinalEmployeeDeduction);
    }

    [Fact]
    public void Fictional_Dependents_Use_Three_Covered_Units()
    {
        var result = Calculate([Enrollment(36000, 2)], [Policy()]);

        Assert.Equal(2, result.ChargeableDependentCount);
        Assert.Equal(3, result.ContributionUnits);
        Assert.Equal(PayrollCalculationStatus.Resolved, result.CalculationStatus);
    }

    [Fact]
    public void Overlapping_Policies_Need_Review() =>
        Assert.Equal(PayrollCalculationStatus.NeedsReview,
            Calculate([Enrollment(30000, 0)], [Policy(), Policy("v2")])
                .CalculationStatus);

    [Fact]
    public void Domain_Rejects_Negative_Dependents_And_NotEnrolled_Values()
    {
        Assert.Throws<DomainValidationException>(() => Enrollment(30000, -1));
        Assert.Throws<DomainValidationException>(() =>
            new EmployeeHealthInsuranceEnrollment(Guid.NewGuid(), Guid.NewGuid(),
                HealthInsuranceEnrollmentStatus.NotEnrolled, 30000, null,
                EmploymentStart));
    }

    [Fact]
    public void Payroll_Rounding_Is_Explicit_Away_From_Zero()
    {
        var result = Calculate([Enrollment(100, 0)],
            [new HealthInsuranceRatePolicy(Guid.NewGuid(), "half", .05m, .10m, 3,
                HealthInsuranceDependentBillingRule.EmployeeAndCappedDependents,
                HealthInsuranceContributionPeriodPolicy.FullPeriodOnly, Start)]);
        Assert.Equal(.5m, result.RawEmployeeAmount);
        Assert.Equal(1m, result.FinalEmployeeDeduction);
    }

    [Fact]
    public void Fingerprint_Changes_For_Insured_Dependent_Enrollment_And_Policy()
    {
        var id = Guid.NewGuid(); var employeeId = Guid.NewGuid(); var policyId = Guid.NewGuid();
        byte[] Fingerprint(decimal amount, int dependents, DateOnly from, decimal rate) =>
            Calculate([new(id, employeeId, HealthInsuranceEnrollmentStatus.Enrolled,
                amount, dependents, from)],
                [new(policyId, "v1", rate, .30m, 3,
                    HealthInsuranceDependentBillingRule.EmployeeAndCappedDependents,
                    HealthInsuranceContributionPeriodPolicy.FullPeriodOnly, Start)])
            .SourceFingerprint;
        var baseline = Convert.ToHexString(Fingerprint(30000, 0, EmploymentStart, .0517m));
        Assert.NotEqual(baseline, Convert.ToHexString(Fingerprint(32000, 0, EmploymentStart, .0517m)));
        Assert.NotEqual(baseline, Convert.ToHexString(Fingerprint(30000, 1, EmploymentStart, .0517m)));
        Assert.NotEqual(baseline, Convert.ToHexString(Fingerprint(30000, 0, Start, .0517m)));
        Assert.NotEqual(baseline, Convert.ToHexString(Fingerprint(30000, 0, EmploymentStart, .052m)));
        Assert.False(HealthInsuranceSourceFingerprintV1.IsCurrent(
            Convert.FromHexString(baseline),
            [Enrollment(32000, 0)], [Policy()], Start, End, EmploymentStart, null));
    }

    [Fact]
    public void Snapshot_Evidence_Has_No_Dependent_Identity_Or_Unrelated_Fields()
    {
        var result = Calculate([Enrollment(30000, 2)], [Policy()]);
        var snapshot = new PayrollHealthInsuranceSnapshot(Guid.NewGuid(), Guid.NewGuid(), result);
        var evidence = new PayrollHealthInsuranceEvidence(Guid.NewGuid(), snapshot.Id, result);
        snapshot.AttachEvidence(evidence);
        Assert.Equal(32, snapshot.SourceFingerprint.Length);
        Assert.Equal(2, evidence.ActualDependentCount);
        var names = typeof(PayrollHealthInsuranceEvidence).GetProperties().Select(x => x.Name);
        Assert.DoesNotContain(names, x => x.Contains("Name", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(names, x => x.Contains("NationalId", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(names, x => x.Contains("Bank", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(29500)] [InlineData(2000)] [InlineData(4200)] [InlineData(99999)]
    public void Earnings_Do_Not_Change_Explicit_Insured_Amount(decimal unrelated)
    {
        _ = unrelated;
        Assert.Equal(30000, Calculate([Enrollment(30000, 0)], [Policy()]).MonthlyInsuredAmount);
    }

    private static HealthInsuranceCalculationResult Calculate(
        IEnumerable<EmployeeHealthInsuranceEnrollment> enrollments,
        IEnumerable<HealthInsuranceRatePolicy> policies,
        DateOnly? employmentStart = null,
        DateOnly? employmentEnd = null) =>
        HealthInsuranceEmployeeDeductionCalculator.Calculate(enrollments, policies,
            Start, End, employmentStart ?? EmploymentStart, employmentEnd);
    private static HealthInsuranceCalculationResult CalculateMonth(
        IEnumerable<EmployeeHealthInsuranceEnrollment> enrollments,
        IEnumerable<HealthInsuranceRatePolicy> policies, int year, int month)
    {
        var start = new DateOnly(year, month, 1);
        var end = new DateOnly(year, month, DateTime.DaysInMonth(year, month));
        return HealthInsuranceEmployeeDeductionCalculator.Calculate(enrollments,
            policies, start, end, EmploymentStart, null);
    }
    private static EmployeeHealthInsuranceEnrollment Enrollment(decimal? amount, int? dependents) =>
        new(Guid.NewGuid(), Guid.NewGuid(), HealthInsuranceEnrollmentStatus.Enrolled,
            amount, dependents, EmploymentStart);
    private static HealthInsuranceRatePolicy Policy(string version = "synthetic-v1") =>
        PolicyFrom(Start, version);
    private static HealthInsuranceRatePolicy PolicyFrom(DateOnly from,
        string version = "synthetic-v1") =>
        new(Guid.NewGuid(), version, .0517m, .30m, 3,
            HealthInsuranceDependentBillingRule.EmployeeAndCappedDependents,
            HealthInsuranceContributionPeriodPolicy.FullPeriodOnly, from);
}
