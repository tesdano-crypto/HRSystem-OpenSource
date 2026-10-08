using HRSystem.Domain.Common;

namespace HRSystem.Domain.Payroll;

public readonly record struct InsuranceThirtyDayCoverage(
    int CoveredDays,
    decimal Factor)
{
    public bool HasCoverage => CoveredDays > 0;
}

public static class InsuranceCoverageDays
{
    public const int MonthlyDenominator = 30;

    public static InsuranceThirtyDayCoverage Calculate(
        DateOnly periodStart,
        DateOnly periodEnd,
        DateOnly coverageFrom,
        DateOnly? coverageTo)
    {
        ValidatePayrollMonth(periodStart, periodEnd);

        var from = coverageFrom > periodStart ? coverageFrom : periodStart;
        var to = coverageTo is { } end && end < periodEnd ? end : periodEnd;
        if (from > to)
            return new(0, 0m);

        var normalizedFromDay = Math.Min(from.Day, MonthlyDenominator);
        var normalizedToDay = to == periodEnd
            ? MonthlyDenominator
            : Math.Min(to.Day, MonthlyDenominator);
        var days = Math.Clamp(
            normalizedToDay - normalizedFromDay + 1,
            0,
            MonthlyDenominator);
        return new(days, days / (decimal)MonthlyDenominator);
    }

    private static void ValidatePayrollMonth(DateOnly periodStart, DateOnly periodEnd)
    {
        var expectedEnd = new DateOnly(periodStart.Year, periodStart.Month,
            DateTime.DaysInMonth(periodStart.Year, periodStart.Month));
        if (periodStart.Day != 1 || periodEnd != expectedEnd)
            throw new DomainValidationException("保險計費期間必須是完整曆月。");
    }
}

public enum HealthInsuranceMonthlyCoverageDecision : byte
{
    CoveredFullMonth = 1,
    NotCovered = 2,
    NeedsReview = 3
}

public sealed record HealthInsuranceMonthlyCoverageResult(
    HealthInsuranceMonthlyCoverageDecision Decision,
    Guid? AuthorityEnrollmentId,
    string? ReviewReason);

public static class HealthInsuranceMonthlyCoverage
{
    public const string MissingMonthEndAuthorityReason =
        "Health monthly billing authority requires month-end coverage / transfer information";

    public static HealthInsuranceMonthlyCoverageResult Evaluate(
        IEnumerable<EmployeeHealthInsuranceEnrollment> enrollments,
        DateOnly periodStart,
        DateOnly periodEnd)
    {
        ArgumentNullException.ThrowIfNull(enrollments);
        _ = InsuranceCoverageDays.Calculate(periodStart, periodEnd,
            periodStart, periodEnd);

        var active = enrollments.Where(x => x.IsActive).ToArray();
        var monthEndRows = active.Where(x => x.Overlaps(periodEnd, periodEnd))
            .ToArray();
        if (monthEndRows.Length > 1)
            return new(HealthInsuranceMonthlyCoverageDecision.NeedsReview,
                null, "Multiple health enrollments cover the payroll month end");

        if (monthEndRows.Length == 1)
        {
            var authority = monthEndRows[0];
            return authority.Status == HealthInsuranceEnrollmentStatus.Enrolled
                ? new(HealthInsuranceMonthlyCoverageDecision.CoveredFullMonth,
                    authority.Id, null)
                : new(HealthInsuranceMonthlyCoverageDecision.NotCovered,
                    authority.Id, null);
        }

        var earlierCovered = active.Where(x =>
                x.Status == HealthInsuranceEnrollmentStatus.Enrolled)
            .Where(x => x.Overlaps(periodStart, periodEnd))
            .OrderByDescending(x => x.EffectiveTo)
            .FirstOrDefault();
        return earlierCovered is not null
            ? new(HealthInsuranceMonthlyCoverageDecision.NeedsReview,
                earlierCovered.Id, MissingMonthEndAuthorityReason)
            : new(HealthInsuranceMonthlyCoverageDecision.NotCovered, null, null);
    }
}
