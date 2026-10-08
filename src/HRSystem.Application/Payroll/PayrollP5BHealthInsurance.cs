using HRSystem.Domain.Payroll;

namespace HRSystem.Application.Payroll;

internal sealed record PayrollP5BEmployeeResolution(
    IReadOnlyList<PayrollComponentResolutionResult> Components,
    HealthInsuranceCalculationResult HealthInsurance);

internal static class PayrollP5BHealthInsurance
{
    public const string ComponentCode = "HEALTH_INSURANCE";
    public static PayrollP5BEmployeeResolution Resolve(
        IReadOnlyList<PayrollComponentResolutionResult> components,
        IEnumerable<EmployeeHealthInsuranceEnrollment> enrollments,
        IEnumerable<HealthInsuranceRatePolicy> policies,
        DateOnly periodStart, DateOnly periodEnd,
        DateOnly employmentStart, DateOnly? employmentEnd)
    {
        var calculation = HealthInsuranceEmployeeDeductionCalculator.Calculate(
            enrollments, policies, periodStart, periodEnd, employmentStart, employmentEnd);
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
                RawProratedAmount = calculation.RawEmployeeAmount,
                CalculationStatus = calculation.CalculationStatus
            };
        }
        return new(output, calculation);
    }
}
