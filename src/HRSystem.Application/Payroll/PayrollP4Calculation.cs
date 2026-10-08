using HRSystem.Application.Overtime;
using HRSystem.Domain.Attendance;
using HRSystem.Domain.Overtime;
using HRSystem.Domain.Payroll;

namespace HRSystem.Application.Payroll;

internal sealed record PayrollP4EmployeeResolution(
    IReadOnlyList<PayrollComponentResolutionResult> Components,
    PayrollOvertimePayResult OvertimePay);

internal static class PayrollP4Calculation
{
    public const string FirstTwoCode = "OVERTIME_FIRST_2H";
    public const string AfterTwoCode = "OVERTIME_AFTER_2H";
    public const string AfterEightCode = "OVERTIME_AFTER_8H";

    public static PayrollP4EmployeeResolution Resolve(
        IReadOnlyList<PayrollComponentResolutionResult> components,
        IReadOnlyDictionary<Guid, bool> overtimeBaseFlags,
        IReadOnlyList<OvertimeRequest> approvedRequests,
        IReadOnlyDictionary<(Guid EmployeeId, DateOnly WorkDate), DailyAttendanceResult> attendance,
        IReadOnlyList<OvertimePayRatePolicy> rates,
        DateOnly periodStart,
        DateOnly periodEnd,
        DateOnly employmentStart,
        DateOnly? employmentEnd)
    {
        var baseInputs = components.Select(x => new OvertimeBaseComponentInput(
            x.ComponentDefinitionId, x.ComponentCode, x.FullMonthlyAmount,
            overtimeBaseFlags.GetValueOrDefault(x.ComponentDefinitionId),
            x.SourceId, x.EffectiveSourceDate)).ToArray();
        var recognitionInputs = approvedRequests.Where(x => x.Recognition is not null)
            .Select(request => ToInput(request, request.Recognition!,
                attendance.GetValueOrDefault((request.EmployeeId, request.OvertimeDate)),
                employmentStart, employmentEnd)).ToArray();
        var missingRecognition = approvedRequests.Any(x => x.Recognition is null);
        var result = PayrollOvertimePayCalculator.Calculate(baseInputs,
            recognitionInputs, rates, periodStart, periodEnd, missingRecognition);

        var output = components.ToList();
        ApplyBucket(output, FirstTwoCode, OvertimePayBucket.FirstTwoHours, result);
        ApplyBucket(output, AfterTwoCode, OvertimePayBucket.AfterTwoHours, result);
        ApplyBucket(output, AfterEightCode, OvertimePayBucket.AfterEightHours, result);
        return new(output, result);
    }

    private static PayrollOvertimeRecognitionInput ToInput(
        OvertimeRequest request,
        OvertimeRecognition recognition,
        DailyAttendanceResult? attendance,
        DateOnly employmentStart,
        DateOnly? employmentEnd)
    {
        var source = OvertimeRecognitionPolicy.Build(
            request.Id, request.EmployeeId, request.OvertimeDate,
            request.Status, request.RowVersion, request.PlannedStartAt,
            request.PlannedEndAt, attendance?.Id, attendance?.RowVersion,
            attendance?.ScheduledEndTimeSnapshot,
            attendance?.IsOvernightShiftSnapshot == true,
            attendance?.EffectiveClockOutLocalTime,
            attendance?.MissingClockIn ?? true,
            attendance?.MissingClockOut ?? true);
        var effective = OvertimeRecognitionPolicy.EffectiveStatus(
            recognition, source, out var stale);
        return new(recognition.Id, request.Id, recognition.WorkDate,
            recognition.RecognizedStartAt, recognition.RecognizedEndAt,
            recognition.RecognizedMinutes, effective, !stale,
            recognition.SourceFingerprint.ToArray(),
            attendance?.CalendarClassification,
            recognition.WorkDate >= employmentStart &&
            (!employmentEnd.HasValue || recognition.WorkDate <= employmentEnd.Value));
    }

    private static void ApplyBucket(List<PayrollComponentResolutionResult> components,
        string code, OvertimePayBucket bucket, PayrollOvertimePayResult result)
    {
        var index = components.FindIndex(x => x.ComponentCode == code);
        if (index < 0) return;
        var original = components[index];
        var bucketResult = result.Buckets.SingleOrDefault(x => x.Bucket == bucket);
        components[index] = original with
        {
            SourceType = result.CalculationStatus == PayrollCalculationStatus.Resolved
                ? PayrollSnapshotSourceType.PayrollPlan
                : PayrollSnapshotSourceType.ExternalPending,
            RawProratedAmount = bucketResult?.RawPay,
            ResolvedAmount = bucketResult?.FinalPay,
            CalculationStatus = result.CalculationStatus
        };
    }
}
