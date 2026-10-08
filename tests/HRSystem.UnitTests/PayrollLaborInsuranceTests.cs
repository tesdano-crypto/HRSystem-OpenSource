using HRSystem.Domain.Payroll;

namespace HRSystem.UnitTests;

public sealed class PayrollLaborInsuranceTests
{
    private static readonly DateOnly Start = new(2026, 8, 1);
    private static readonly DateOnly End = new(2026, 8, 31);

    [Fact]
    public void Enrolled_Employee_Uses_Explicit_Insured_Salary_And_Employee_Share()
    {
        var result = Calculate([Enrollment(30000)], [Policy()]);

        Assert.Equal(PayrollCalculationStatus.Resolved, result.CalculationStatus);
        Assert.Equal(660, result.FinalEmployeeDeduction);
        Assert.Equal(2, result.Contributions.Count);
        Assert.Equal(600, Assert.Single(result.Contributions,
            x => x.Kind == LaborInsuranceContributionKind.OrdinaryAccident).RawEmployeeAmount);
        Assert.Equal(60, Assert.Single(result.Contributions,
            x => x.Kind == LaborInsuranceContributionKind.Employment).RawEmployeeAmount);
    }

    [Fact]
    public void Explicit_Not_Enrolled_Is_Resolved_Zero()
    {
        var item = new EmployeeLaborInsuranceEnrollment(Guid.NewGuid(), Guid.NewGuid(),
            LaborInsuranceEnrollmentStatus.NotEnrolled, null, Start);
        var result = Calculate([item], []);
        Assert.Equal(PayrollCalculationStatus.Resolved, result.CalculationStatus);
        Assert.Equal(0, result.FinalEmployeeDeduction);
        Assert.Empty(result.Contributions);
    }

    [Fact]
    public void Missing_Enrollment_Needs_Setup() =>
        Assert.Equal(PayrollCalculationStatus.NeedsSetup,
            Calculate([], [Policy()]).CalculationStatus);

    [Fact]
    public void Missing_Insured_Salary_Needs_Setup() =>
        Assert.Equal(PayrollCalculationStatus.NeedsSetup,
            Calculate([Enrollment(null)], [Policy()]).CalculationStatus);

    [Fact]
    public void Missing_Policy_Is_Policy_Pending() =>
        Assert.Equal(PayrollCalculationStatus.PolicyPending,
            Calculate([Enrollment(30000)], []).CalculationStatus);

    [Fact]
    public void Overlapping_Policies_Need_Review() =>
        Assert.Equal(PayrollCalculationStatus.NeedsReview,
            Calculate([Enrollment(30000)], [Policy(), Policy("synthetic-v2")]).CalculationStatus);

    [Fact]
    public void Overlapping_Enrollments_Need_Review() =>
        Assert.Equal(PayrollCalculationStatus.NeedsReview,
            Calculate([Enrollment(30000), Enrollment(32000)], [Policy()]).CalculationStatus);

    [Fact]
    public void Partial_Enrollment_Requires_Explicit_Thirty_Day_Policy()
    {
        var enrollment = new EmployeeLaborInsuranceEnrollment(Guid.NewGuid(), Guid.NewGuid(),
            LaborInsuranceEnrollmentStatus.Enrolled, 30000, new DateOnly(2026, 8, 15));
        var result = Calculate([enrollment], [Policy()]);
        Assert.Equal(PayrollCalculationStatus.PolicyPending, result.CalculationStatus);
        Assert.Null(result.FinalEmployeeDeduction);
    }

    [Fact]
    public void MidMonth_Enrollment_Uses_Thirty_Day_Factor_For_Labor_And_Employment()
    {
        var enrollment = new EmployeeLaborInsuranceEnrollment(Guid.NewGuid(), Guid.NewGuid(),
            LaborInsuranceEnrollmentStatus.Enrolled, 30000, new DateOnly(2026, 8, 13));
        var result = Calculate([enrollment], [Policy(
            contributionPeriodPolicy: LaborInsuranceContributionPeriodPolicy.ThirtyDayProrated)]);

        Assert.Equal(PayrollCalculationStatus.Resolved, result.CalculationStatus);
        Assert.Equal(18m / 30m, InsuranceCoverageDays.Calculate(
            Start, End, enrollment.EffectiveFrom, enrollment.EffectiveTo).Factor);
        Assert.Equal(360, Assert.Single(result.Contributions,
            x => x.Kind == LaborInsuranceContributionKind.OrdinaryAccident).RawEmployeeAmount);
        Assert.Equal(36, Assert.Single(result.Contributions,
            x => x.Kind == LaborInsuranceContributionKind.Employment).RawEmployeeAmount);
        Assert.Equal(396, result.FinalEmployeeDeduction);
    }

    [Fact]
    public void MidMonth_Withdrawal_Uses_Inclusive_Stored_EffectiveTo()
    {
        var enrollment = new EmployeeLaborInsuranceEnrollment(Guid.NewGuid(), Guid.NewGuid(),
            LaborInsuranceEnrollmentStatus.Enrolled, 30000, Start,
            new DateOnly(2026, 8, 20));
        var result = Calculate([enrollment], [Policy(
            contributionPeriodPolicy: LaborInsuranceContributionPeriodPolicy.ThirtyDayProrated)]);
        Assert.Equal(PayrollCalculationStatus.Resolved, result.CalculationStatus);
        Assert.Equal(20, InsuranceCoverageDays.Calculate(
            Start, End, enrollment.EffectiveFrom, enrollment.EffectiveTo).CoveredDays);
        Assert.Equal(440, result.FinalEmployeeDeduction);
    }

    [Fact]
    public void Historical_Period_Uses_Historical_Policy()
    {
        var oldPolicy = Policy("old", new DateOnly(2026, 1, 1), End);
        var future = Policy("future", new DateOnly(2026, 9, 1));
        var result = Calculate([Enrollment(30000)], [oldPolicy, future]);
        Assert.Equal("old", result.PolicyVersion);
        Assert.Equal(PayrollCalculationStatus.Resolved, result.CalculationStatus);
    }

    [Fact]
    public void Aggregate_Raw_Amount_Is_Rounded_Once()
    {
        var policy = new LaborInsuranceRatePolicy(Guid.NewGuid(), "rounding",
            LaborInsuranceCoverage.OrdinaryAccident | LaborInsuranceCoverage.Employment,
            .03333333m, .01666667m, .2m,
            LaborInsuranceContributionPeriodPolicy.FullPeriodOnly, Start);
        var result = Calculate([Enrollment(101)], [policy]);
        var raw = result.Contributions.Sum(x => x.RawEmployeeAmount);
        Assert.Equal(PayrollMoneyRoundingPolicy.RoundNtd(raw), result.FinalEmployeeDeduction);
    }

    [Fact]
    public void Fingerprint_Changes_When_Insured_Salary_Changes()
    {
        var enrollmentId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var policy = Policy();
        var original = new EmployeeLaborInsuranceEnrollment(enrollmentId,
            employeeId, LaborInsuranceEnrollmentStatus.Enrolled, 30000, Start);
        var changed = new EmployeeLaborInsuranceEnrollment(enrollmentId,
            employeeId, LaborInsuranceEnrollmentStatus.Enrolled, 32000, Start);
        var first = Calculate([original], [policy]);
        var second = Calculate([changed], [policy]);
        Assert.NotEqual(Convert.ToHexString(first.SourceFingerprint),
            Convert.ToHexString(second.SourceFingerprint));
        Assert.False(LaborInsuranceSourceFingerprintV2.IsCurrent(
            first.SourceFingerprint, [changed], [policy], Start, End));
    }

    [Fact]
    public void Snapshot_Is_Immutable_Copy_Of_Source_Evidence()
    {
        var result = Calculate([Enrollment(30000)], [Policy()]);
        var snapshot = new PayrollLaborInsuranceSnapshot(Guid.NewGuid(), Guid.NewGuid(), result);
        Assert.Equal(30000, snapshot.MonthlyInsuredSalary);
        Assert.Equal("synthetic-v1", snapshot.PolicyVersion);
        Assert.Equal(660, snapshot.FinalEmployeeDeduction);
        Assert.Equal(32, snapshot.SourceFingerprint.Length);
    }

    [Fact]
    public void Evidence_Model_Contains_No_Unrelated_Sensitive_Fields()
    {
        var names = typeof(PayrollLaborInsuranceSnapshot).GetProperties()
            .Select(x => x.Name).ToArray();
        Assert.DoesNotContain(names, x => x.Contains("NationalId", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(names, x => x.Contains("Bank", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(names, x => x.Contains("Medical", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(29500)]
    [InlineData(50000)]
    [InlineData(2000)]
    [InlineData(41000)]
    public void Earnings_Do_Not_Change_Explicit_Insured_Salary(decimal unrelatedEarning)
    {
        _ = unrelatedEarning;
        var result = Calculate([Enrollment(30000)], [Policy()]);
        Assert.Equal(30000, result.MonthlyInsuredSalary);
        Assert.Equal(660, result.FinalEmployeeDeduction);
    }

    private static LaborInsuranceCalculationResult Calculate(
        IEnumerable<EmployeeLaborInsuranceEnrollment> enrollments,
        IEnumerable<LaborInsuranceRatePolicy> policies) =>
        LaborInsuranceEmployeeDeductionCalculator.Calculate(enrollments,
            policies, Start, End);

    private static EmployeeLaborInsuranceEnrollment Enrollment(decimal? salary) =>
        new(Guid.NewGuid(), Guid.NewGuid(), LaborInsuranceEnrollmentStatus.Enrolled,
            salary, Start);

    private static LaborInsuranceRatePolicy Policy(string version = "synthetic-v1",
        DateOnly? from = null, DateOnly? to = null,
        LaborInsuranceContributionPeriodPolicy contributionPeriodPolicy =
            LaborInsuranceContributionPeriodPolicy.FullPeriodOnly) => new(Guid.NewGuid(), version,
            LaborInsuranceCoverage.OrdinaryAccident | LaborInsuranceCoverage.Employment,
            .10m, .01m, .20m,
            contributionPeriodPolicy,
            from ?? Start, to);
}
