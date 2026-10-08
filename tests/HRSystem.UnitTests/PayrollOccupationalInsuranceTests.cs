using HRSystem.Domain.Common;
using HRSystem.Domain.Payroll;

namespace HRSystem.UnitTests;

public sealed class PayrollOccupationalInsuranceTests
{
    private static readonly DateOnly Start = new(2026, 8, 1);
    private static readonly DateOnly End = new(2026, 8, 31);

    [Fact]
    public void Occupational_Only_Employee_Is_Valid_And_Policy_Pending()
    {
        var item = Enrollment(72800, Start);
        var result = OccupationalInsuranceReadinessEvaluator.Evaluate(
            [item], Start, End);

        Assert.Equal(72800, result.MonthlyInsuredSalary);
        Assert.Equal(PayrollCalculationStatus.PolicyPending,
            result.CalculationStatus);
        Assert.Equal(30, result.CoveredDays);
    }

    [Fact]
    public void Missing_Occupational_Enrollment_Needs_Setup()
    {
        var result = OccupationalInsuranceReadinessEvaluator.Evaluate(
            [], Start, End);
        Assert.Equal(PayrollCalculationStatus.NeedsSetup,
            result.CalculationStatus);
    }

    [Fact]
    public void Labor_And_Occupational_Are_Independent_With_Different_Salaries()
    {
        var employeeId = Guid.NewGuid();
        var labor = new EmployeeLaborInsuranceEnrollment(Guid.NewGuid(),
            employeeId, LaborInsuranceEnrollmentStatus.Enrolled, 45800, Start);
        var occupational = new EmployeeOccupationalInsuranceEnrollment(
            Guid.NewGuid(), employeeId,
            OccupationalInsuranceEnrollmentStatus.Enrolled, 72800, Start);

        Assert.Equal(45800, labor.MonthlyLaborInsuredSalary);
        Assert.Equal(72800, occupational.MonthlyInsuredSalary);
    }

    [Fact]
    public void Same_Salary_Still_Uses_Independent_Enrollment_Identity()
    {
        var employeeId = Guid.NewGuid();
        var labor = new EmployeeLaborInsuranceEnrollment(Guid.NewGuid(),
            employeeId, LaborInsuranceEnrollmentStatus.Enrolled, 45800, Start);
        var occupational = new EmployeeOccupationalInsuranceEnrollment(
            Guid.NewGuid(), employeeId,
            OccupationalInsuranceEnrollmentStatus.Enrolled, 45800, Start);
        Assert.NotEqual(labor.Id, occupational.Id);
    }

    [Fact]
    public void Independent_Dates_Use_Thirty_Day_Coverage()
    {
        var item = Enrollment(72800, new DateOnly(2026, 8, 13),
            new DateOnly(2026, 8, 20));
        var result = OccupationalInsuranceReadinessEvaluator.Evaluate(
            [item], Start, End);
        Assert.Equal(8, result.CoveredDays);
        Assert.Equal(8m / 30m, result.CoverageFactor);
    }

    [Fact]
    public void Overlap_Needs_Review_And_Gap_Is_Allowed()
    {
        Assert.Equal(PayrollCalculationStatus.NeedsReview,
            OccupationalInsuranceReadinessEvaluator.Evaluate(
                [Enrollment(45800, Start), Enrollment(72800, Start)],
                Start, End).CalculationStatus);
        var historical = Enrollment(45800, new DateOnly(2026, 6, 1),
            new DateOnly(2026, 6, 30));
        var current = Enrollment(72800, new DateOnly(2026, 8, 1));
        Assert.Equal(PayrollCalculationStatus.PolicyPending,
            OccupationalInsuranceReadinessEvaluator.Evaluate(
                [historical, current], Start, End).CalculationStatus);
    }

    [Fact]
    public void Fingerprint_Changes_With_Occupational_Source()
    {
        var id = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var first = new EmployeeOccupationalInsuranceEnrollment(id, employeeId,
            OccupationalInsuranceEnrollmentStatus.Enrolled, 45800, Start);
        var second = new EmployeeOccupationalInsuranceEnrollment(id, employeeId,
            OccupationalInsuranceEnrollmentStatus.Enrolled, 72800, Start);
        var a = OccupationalInsuranceSourceFingerprintV1.Calculate(
            [first], Start, End);
        var b = OccupationalInsuranceSourceFingerprintV1.Calculate(
            [second], Start, End);
        Assert.NotEqual(Convert.ToHexString(a), Convert.ToHexString(b));
    }

    [Fact]
    public void Invalid_Salary_And_Period_Are_Rejected()
    {
        Assert.Throws<DomainValidationException>(() => Enrollment(0, Start));
        Assert.Throws<DomainValidationException>(() => Enrollment(45800,
            new DateOnly(2026, 8, 2), new DateOnly(2026, 8, 1)));
    }

    private static EmployeeOccupationalInsuranceEnrollment Enrollment(
        decimal salary, DateOnly from, DateOnly? to = null) => new(
            Guid.NewGuid(), Guid.NewGuid(),
            OccupationalInsuranceEnrollmentStatus.Enrolled, salary, from, to);

    [Fact]
    public void Explicit_Uninsured_Period_Has_Zero_Coverage()
    {
        var item = new EmployeeOccupationalInsuranceEnrollment(Guid.NewGuid(), Guid.NewGuid(),
            OccupationalInsuranceEnrollmentStatus.NotEnrolled, null, Start);
        var result = OccupationalInsuranceReadinessEvaluator.Evaluate([item], Start, End);
        Assert.Equal(PayrollCalculationStatus.Resolved, result.CalculationStatus);
        Assert.Equal(0, result.CoveredDays);
        Assert.Equal(0m, result.CoverageFactor);
    }

    [Fact]
    public void OutOfPeriod_Source_Does_Not_Change_Current_Fingerprint()
    {
        var current = Enrollment(45800, Start, End);
        var future = Enrollment(72800, new DateOnly(2027, 1, 1));
        var first = OccupationalInsuranceReadinessEvaluator.Evaluate([current], Start, End);
        var second = OccupationalInsuranceReadinessEvaluator.Evaluate([current, future], Start, End);
        Assert.Equal(first.SourceFingerprint, second.SourceFingerprint);
    }

    [Fact]
    public void Labor_Only_Remains_Valid_Without_Occupational_Enrollment()
    {
        var labor = new EmployeeLaborInsuranceEnrollment(Guid.NewGuid(), Guid.NewGuid(),
            LaborInsuranceEnrollmentStatus.Enrolled, 45800, Start);
        var result = LaborInsuranceEmployeeDeductionCalculator.Calculate([labor], [], Start, End);
        Assert.Equal(45800, result.MonthlyLaborInsuredSalary);
        Assert.Equal(PayrollCalculationStatus.PolicyPending, result.CalculationStatus);
        Assert.Equal(2, LaborInsuranceSourceFingerprintV2.Version);
    }

    [Fact]
    public void Invalid_Period_Is_Rejected_Even_Without_Enrollment()
    {
        Assert.Throws<DomainValidationException>(() =>
            OccupationalInsuranceReadinessEvaluator.Evaluate([], Start.AddDays(1), End));
    }
}
