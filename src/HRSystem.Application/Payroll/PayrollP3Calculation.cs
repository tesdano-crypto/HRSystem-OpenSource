using HRSystem.Domain.Attendance;
using HRSystem.Domain.Payroll;

namespace HRSystem.Application.Payroll;

internal sealed record PayrollP3EmployeeResolution(
    IReadOnlyList<PayrollComponentResolutionResult> Components,
    AttendanceAllowanceCalculationResult? AttendanceAllowance,
    LeaveDeductionCalculationResult? LeaveDeduction,
    byte[] AttendanceSourceFingerprint,
    byte[] LeaveSourceFingerprint);

internal static class PayrollP3Calculation
{
    public const string AttendanceAllowanceCode = "ATTENDANCE_ALLOWANCE";
    public const string LeaveDeductionCode = "LEAVE_DEDUCTION";
    public const decimal StandardAttendanceAllowance = 2000m;

    public static PayrollP3EmployeeResolution Resolve(
        IReadOnlyList<PayrollComponentResolutionResult> fixedComponents,
        IReadOnlyList<DailyAttendanceResult> attendanceResults,
        IReadOnlyList<PayrollLeaveDeductionPolicy> policies,
        DateOnly periodStart,
        DateOnly periodEnd,
        DateOnly employmentStart,
        DateOnly? employmentEnd)
    {
        var components = fixedComponents.ToList();
        var attendanceIndex = components.FindIndex(x => x.ComponentCode == AttendanceAllowanceCode);
        var leaveIndex = components.FindIndex(x => x.ComponentCode == LeaveDeductionCode);
        var overlapStart = employmentStart > periodStart ? employmentStart : periodStart;
        var overlapEnd = employmentEnd is { } end && end < periodEnd ? end : periodEnd;
        var relevant = attendanceResults
            .Where(x => x.WorkDate >= overlapStart && x.WorkDate <= overlapEnd)
            .OrderBy(x => x.WorkDate)
            .ToArray();
        var expectedDays = overlapEnd < overlapStart
            ? 0
            : overlapEnd.DayNumber - overlapStart.DayNumber + 1;
        var sourceComplete = relevant.Select(x => x.WorkDate).Distinct().Count() == expectedDays;
        var attendanceFingerprint = PayrollSourceFingerprintV1.Calculate(
            relevant.Select(AttendanceFingerprintLine));
        var leaveFingerprint = PayrollSourceFingerprintV1.Calculate(
            relevant.SelectMany(x => x.LeaveSegments.Select(segment =>
                $"{x.WorkDate:yyyy-MM-dd}|{x.ExpectedWorkMinutesSnapshot}|{segment.LeaveTypeCodeSnapshot}|{segment.CoveredMinutes}|{segment.StartAtUtc:O}|{segment.EndAtUtc:O}")));

        AttendanceAllowanceCalculationResult? allowance = null;
        if (attendanceIndex >= 0)
        {
            var original = components[attendanceIndex];
            var standard = original.StandardAmount ?? StandardAttendanceAllowance;
            allowance = AttendanceAllowanceCalculator.Calculate(
                standard, periodStart, periodEnd, employmentStart, employmentEnd,
                relevant.Select(x => new PayrollAttendanceDayOutcome(
                    x.WorkDate, x.IsLate, x.IsEarlyLeave, x.MissingClockIn,
                    x.MissingClockOut, x.LeaveSegments.Count > 0)),
                sourceComplete);
            components[attendanceIndex] = original with
            {
                SourceType = PayrollSnapshotSourceType.PayrollPlan,
                ProrationKind = PayrollProrationKind.Monthly30Day,
                FullMonthlyAmount = standard,
                PayableDays = allowance.EmploymentPayableDays,
                ProrationFactor = allowance.EligibleDays / 30m,
                RawProratedAmount = allowance.RawCalculatedAmount,
                ResolvedAmount = allowance.ResolvedAmount,
                CalculationStatus = allowance.CalculationStatus
            };
        }

        LeaveDeductionCalculationResult? leave = null;
        if (leaveIndex >= 0)
        {
            var original = components[leaveIndex];
            var baseSalary = components.SingleOrDefault(x => x.ComponentCode == "BASE_SALARY");
            if (baseSalary?.FullMonthlyAmount is not { } fullBase)
            {
                components[leaveIndex] = original with
                {
                    SourceType = PayrollSnapshotSourceType.RulePending,
                    CalculationStatus = PayrollCalculationStatus.NeedsReview
                };
            }
            else
            {
                var inputs = relevant.SelectMany(result => result.LeaveSegments
                    .GroupBy(segment => segment.LeaveTypeCodeSnapshot, StringComparer.Ordinal)
                    .Select(group => new PayrollLeaveSegmentInput(
                        result.WorkDate,
                        group.Key,
                        group.Sum(x => x.CoveredMinutes),
                        result.ExpectedWorkMinutesSnapshot ?? 0)))
                    .ToArray();
                var rules = policies
                    .Select(policy => new PayrollLeaveDeductionRule(
                        policy.LeaveTypeCode, policy.DeductionRate,
                        policy.CalculationBasis, policy.EffectiveFrom,
                        policy.EffectiveTo))
                    .ToArray();
                leave = LeaveDeductionCalculator.Calculate(fullBase, inputs, rules);
                if (!sourceComplete)
                {
                    leave = leave with
                    {
                        ResolvedAmount = null,
                        CalculationStatus = PayrollCalculationStatus.NeedsReview
                    };
                }
                components[leaveIndex] = original with
                {
                    SourceType = PayrollSnapshotSourceType.PayrollPlan,
                    FullMonthlyAmount = fullBase,
                    RawProratedAmount = leave.TotalRawAmount,
                    ResolvedAmount = leave.ResolvedAmount,
                    CalculationStatus = leave.CalculationStatus
                };
            }
        }

        return new(components, allowance, leave,
            attendanceFingerprint, leaveFingerprint);
    }

    private static string AttendanceFingerprintLine(DailyAttendanceResult result) =>
        $"{result.WorkDate:yyyy-MM-dd}|{result.IsRequiredWorkday}|{result.Status}|{result.IsLate}|{result.IsEarlyLeave}|{result.MissingClockIn}|{result.MissingClockOut}|{result.ApprovedLeaveMinutes}|{result.ExpectedWorkMinutesSnapshot}|{result.CalculationVersion}|{result.UpdatedAtUtc:O}";
}
