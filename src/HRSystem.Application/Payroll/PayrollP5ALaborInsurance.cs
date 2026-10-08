using HRSystem.Domain.Payroll;

namespace HRSystem.Application.Payroll;

internal sealed record PayrollP5AEmployeeResolution(
    IReadOnlyList<PayrollComponentResolutionResult> Components,
    LaborInsuranceCalculationResult LaborInsurance);

internal static class PayrollP5ALaborInsurance
{
    public const string ComponentCode = "LABOR_INSURANCE";

    public static PayrollP5AEmployeeResolution Resolve(
        IReadOnlyList<PayrollComponentResolutionResult> components,
        IEnumerable<EmployeeLaborInsuranceEnrollment> enrollments,
        IEnumerable<LaborInsuranceRatePolicy> policies,
        DateOnly periodStart, DateOnly periodEnd)
    {
        var calculation = LaborInsuranceEmployeeDeductionCalculator.Calculate(
            enrollments, policies, periodStart, periodEnd);
        var output = components.ToList();
        var index = output.FindIndex(x => x.ComponentCode == ComponentCode);
        if (index >= 0)
        {
            var original = output[index];
            output[index] = original with
            {
                SourceType = calculation.CalculationStatus == PayrollCalculationStatus.Resolved
                    ? PayrollSnapshotSourceType.PayrollPlan
                    : PayrollSnapshotSourceType.ExternalPending,
                ResolvedAmount = calculation.FinalEmployeeDeduction,
                RawProratedAmount = calculation.CalculationStatus == PayrollCalculationStatus.Resolved
                    ? calculation.Contributions.Sum(x => x.RawEmployeeAmount)
                    : null,
                CalculationStatus = calculation.CalculationStatus
            };
        }
        return new(output, calculation);
    }
}
