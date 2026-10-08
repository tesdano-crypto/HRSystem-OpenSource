using System.Security.Cryptography;
using System.Text;
using HRSystem.Domain.Common;
using HRSystem.Domain.CompTime;

namespace HRSystem.Domain.Payroll;

public sealed record PayrollAttendanceDayOutcome(
    DateOnly WorkDate,
    bool IsLate,
    bool IsEarlyLeave,
    bool MissingClockIn,
    bool MissingClockOut,
    bool HasApprovedLeave,
    bool HasUnsupportedAbsence = false);

public sealed record AttendanceAllowanceEvidenceResult(
    DateOnly WorkDate,
    AttendanceAllowanceIneligibilityReason Reasons);

public sealed record AttendanceAllowanceCalculationResult(
    decimal FullMonthlyAmount,
    decimal EmploymentProratedMaximum,
    int EmploymentPayableDays,
    int EligibleDays,
    int IneligibleDays,
    decimal RawCalculatedAmount,
    decimal? ResolvedAmount,
    PayrollCalculationStatus CalculationStatus,
    IReadOnlyList<AttendanceAllowanceEvidenceResult> Evidence);

public static class AttendanceAllowanceEligibilityPolicy
{
    public const string Code = "ANY_FINAL_ANOMALY_OR_APPROVED_LEAVE_V1";

    public static AttendanceAllowanceIneligibilityReason Evaluate(
        PayrollAttendanceDayOutcome outcome)
    {
        var reasons = AttendanceAllowanceIneligibilityReason.None;
        if (outcome.IsLate) reasons |= AttendanceAllowanceIneligibilityReason.Late;
        if (outcome.IsEarlyLeave) reasons |= AttendanceAllowanceIneligibilityReason.EarlyLeave;
        if (outcome.MissingClockIn) reasons |= AttendanceAllowanceIneligibilityReason.MissingClockIn;
        if (outcome.MissingClockOut) reasons |= AttendanceAllowanceIneligibilityReason.MissingClockOut;
        if (outcome.HasApprovedLeave) reasons |= AttendanceAllowanceIneligibilityReason.ApprovedLeave;
        if (outcome.HasUnsupportedAbsence) reasons |= AttendanceAllowanceIneligibilityReason.AbsencePolicyPending;
        return reasons;
    }
}

public static class AttendanceAllowanceCalculator
{
    public static AttendanceAllowanceCalculationResult Calculate(
        decimal fullMonthlyAmount,
        DateOnly periodStart,
        DateOnly periodEnd,
        DateOnly employmentStart,
        DateOnly? employmentEnd,
        IEnumerable<PayrollAttendanceDayOutcome> outcomes,
        bool sourceComplete)
    {
        ArgumentNullException.ThrowIfNull(outcomes);
        var employment = Monthly30DayProrationPolicy.Calculate(
            fullMonthlyAmount, periodStart, periodEnd, employmentStart, employmentEnd);
        var overlapStart = employmentStart > periodStart ? employmentStart : periodStart;
        var overlapEnd = employmentEnd is { } end && end < periodEnd ? end : periodEnd;
        var evidence = outcomes
            .Where(x => x.WorkDate >= overlapStart && x.WorkDate <= overlapEnd)
            .GroupBy(x => x.WorkDate)
            .Select(group => new AttendanceAllowanceEvidenceResult(
                group.Key,
                group.Aggregate(
                    AttendanceAllowanceIneligibilityReason.None,
                    (flags, item) => flags | AttendanceAllowanceEligibilityPolicy.Evaluate(item))))
            .Where(x => x.Reasons != AttendanceAllowanceIneligibilityReason.None)
            .OrderBy(x => x.WorkDate)
            .ToArray();

        var basisDays = employment.IsFullMonth ? 30 : Math.Min(employment.PayableDays, 30);
        var ineligibleDays = Math.Min(evidence.Length, basisDays);
        var eligibleDays = Math.Max(0, basisDays - ineligibleDays);
        var raw = decimal.Round(
            fullMonthlyAmount * eligibleDays / 30m,
            6,
            MidpointRounding.AwayFromZero);
        return new(
            fullMonthlyAmount,
            employment.RoundedAmount,
            employment.PayableDays,
            eligibleDays,
            ineligibleDays,
            raw,
            sourceComplete ? PayrollMoneyRoundingPolicy.RoundNtd(raw) : null,
            sourceComplete
                ? PayrollCalculationStatus.Resolved
                : PayrollCalculationStatus.NeedsReview,
            evidence);
    }
}

public sealed record PayrollLeaveSegmentInput(
    DateOnly WorkDate,
    string LeaveTypeCode,
    int LeaveMinutes,
    int ScheduledMinutes);

public sealed record PayrollLeaveDeductionRule(
    string LeaveTypeCode,
    decimal DeductionRate,
    PayrollLeaveDeductionBasis CalculationBasis,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo)
{
    public bool AppliesOn(DateOnly date) => EffectiveFrom <= date &&
        (!EffectiveTo.HasValue || EffectiveTo.Value >= date);
}

public sealed record LeaveDeductionEvidenceResult(
    DateOnly WorkDate,
    string LeaveTypeCode,
    int LeaveMinutes,
    int ScheduledMinutes,
    decimal? DeductionRate,
    decimal? RawAmount,
    PayrollCalculationStatus CalculationStatus);

public sealed record LeaveDeductionCalculationResult(
    decimal FullMonthlyBaseAmount,
    int PersonalLeaveMinutes,
    int SickLeaveMinutes,
    int UnsupportedLeaveMinutes,
    decimal PersonalLeaveRawAmount,
    decimal SickLeaveRawAmount,
    decimal TotalRawAmount,
    decimal? ResolvedAmount,
    PayrollCalculationStatus CalculationStatus,
    IReadOnlyList<LeaveDeductionEvidenceResult> Evidence);

public static class LeaveDeductionCalculator
{
    public const string PersonalLeaveCode = "PERSONAL";
    public const string OrdinarySickLeaveCode = "SICK";

    public static LeaveDeductionCalculationResult Calculate(
        decimal fullMonthlyBaseAmount,
        IEnumerable<PayrollLeaveSegmentInput> segments,
        IEnumerable<PayrollLeaveDeductionRule> rules)
    {
        if (fullMonthlyBaseAmount < 0)
            throw new DomainValidationException("完整月薪扣款基礎不可小於 0。");
        ArgumentNullException.ThrowIfNull(segments);
        ArgumentNullException.ThrowIfNull(rules);
        var availableRules = rules.ToArray();
        var evidence = new List<LeaveDeductionEvidenceResult>();
        decimal personalRaw = 0;
        decimal sickRaw = 0;
        var personalMinutes = 0;
        var sickMinutes = 0;
        var unsupportedMinutes = 0;
        var hasPending = false;

        foreach (var segment in segments.OrderBy(x => x.WorkDate).ThenBy(x => x.LeaveTypeCode))
        {
            if (segment.LeaveMinutes <= 0 || segment.ScheduledMinutes <= 0 ||
                segment.LeaveMinutes > segment.ScheduledMinutes)
            {
                hasPending = true;
                unsupportedMinutes += Math.Max(0, segment.LeaveMinutes);
                evidence.Add(new(segment.WorkDate, segment.LeaveTypeCode,
                    segment.LeaveMinutes, segment.ScheduledMinutes, null, null,
                    PayrollCalculationStatus.NeedsReview));
                continue;
            }
            if (CompTimePolicy.IsCompTime(segment.LeaveTypeCode))
            {
                evidence.Add(new(
                    segment.WorkDate,
                    segment.LeaveTypeCode,
                    segment.LeaveMinutes,
                    segment.ScheduledMinutes,
                    0m,
                    0m,
                    PayrollCalculationStatus.Resolved));
                continue;
            }
            var matches = availableRules.Where(rule =>
                string.Equals(rule.LeaveTypeCode, segment.LeaveTypeCode,
                    StringComparison.Ordinal) && rule.AppliesOn(segment.WorkDate)).ToArray();
            if (matches.Length != 1 ||
                matches[0].CalculationBasis != PayrollLeaveDeductionBasis.BaseSalaryOnly)
            {
                hasPending = true;
                unsupportedMinutes += segment.LeaveMinutes;
                evidence.Add(new(segment.WorkDate, segment.LeaveTypeCode,
                    segment.LeaveMinutes, segment.ScheduledMinutes, null, null,
                    PayrollCalculationStatus.PolicyPending));
                continue;
            }
            var rule = matches[0];
            var raw = decimal.Round(
                fullMonthlyBaseAmount / 30m * segment.LeaveMinutes /
                segment.ScheduledMinutes * rule.DeductionRate,
                6,
                MidpointRounding.AwayFromZero);
            if (string.Equals(segment.LeaveTypeCode, PersonalLeaveCode, StringComparison.Ordinal))
            {
                personalMinutes += segment.LeaveMinutes;
                personalRaw += raw;
            }
            else if (string.Equals(segment.LeaveTypeCode, OrdinarySickLeaveCode, StringComparison.Ordinal))
            {
                sickMinutes += segment.LeaveMinutes;
                sickRaw += raw;
            }
            evidence.Add(new(segment.WorkDate, segment.LeaveTypeCode,
                segment.LeaveMinutes, segment.ScheduledMinutes,
                rule.DeductionRate, raw, PayrollCalculationStatus.Resolved));
        }

        personalRaw = decimal.Round(personalRaw, 6, MidpointRounding.AwayFromZero);
        sickRaw = decimal.Round(sickRaw, 6, MidpointRounding.AwayFromZero);
        var totalRaw = decimal.Round(personalRaw + sickRaw, 6, MidpointRounding.AwayFromZero);
        return new(
            fullMonthlyBaseAmount,
            personalMinutes,
            sickMinutes,
            unsupportedMinutes,
            personalRaw,
            sickRaw,
            totalRaw,
            hasPending ? null : PayrollMoneyRoundingPolicy.RoundNtd(totalRaw),
            hasPending ? PayrollCalculationStatus.PolicyPending : PayrollCalculationStatus.Resolved,
            evidence);
    }
}

public static class PayrollSourceFingerprintV1
{
    public const short Version = 1;

    public static byte[] Calculate(IEnumerable<string> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var payload = string.Join("\n", values.Order(StringComparer.Ordinal));
        return SHA256.HashData(Encoding.UTF8.GetBytes(payload));
    }
}
