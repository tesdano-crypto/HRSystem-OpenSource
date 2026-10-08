using HRSystem.Domain.Common;

namespace HRSystem.Domain.Payroll;

public sealed record Monthly30DayProrationResult(
    bool IsFullMonth,
    int PayableDays,
    decimal RawProratedAmount,
    decimal RoundedAmount,
    decimal PayableFactor);

public static class PayrollMoneyRoundingPolicy
{
    public static decimal RoundNtd(decimal amount) =>
        decimal.Round(amount, 0, MidpointRounding.AwayFromZero);
}

public static class Monthly30DayProrationPolicy
{
    public static Monthly30DayProrationResult Calculate(
        decimal fullMonthlyAmount,
        DateOnly periodStart,
        DateOnly periodEnd,
        DateOnly employmentStart,
        DateOnly? employmentEnd)
    {
        if (fullMonthlyAmount < 0)
            throw new DomainValidationException("完整月額不可小於 0。");
        if (periodEnd < periodStart)
            throw new DomainValidationException("薪資期間不合法。");
        if (employmentEnd < employmentStart)
            throw new DomainValidationException("任職期間不合法。");

        var overlapStart = employmentStart > periodStart
            ? employmentStart
            : periodStart;
        var overlapEnd = employmentEnd is { } end && end < periodEnd
            ? end
            : periodEnd;
        if (overlapEnd < overlapStart)
            return new(false, 0, 0, 0, 0);

        var payableDays = overlapEnd.DayNumber - overlapStart.DayNumber + 1;
        var isFullMonth = employmentStart <= periodStart &&
            (!employmentEnd.HasValue || employmentEnd.Value >= periodEnd);
        if (isFullMonth)
        {
            return new(
                true,
                payableDays,
                fullMonthlyAmount,
                PayrollMoneyRoundingPolicy.RoundNtd(fullMonthlyAmount),
                1m);
        }

        var prorationDays = Math.Min(payableDays, 30);
        var factor = decimal.Round(
            prorationDays / 30m,
            6,
            MidpointRounding.AwayFromZero);
        var raw = decimal.Round(
            fullMonthlyAmount * prorationDays / 30m,
            6,
            MidpointRounding.AwayFromZero);
        var rounded = Math.Min(
            PayrollMoneyRoundingPolicy.RoundNtd(raw),
            PayrollMoneyRoundingPolicy.RoundNtd(fullMonthlyAmount));
        return new(false, payableDays, raw, rounded, factor);
    }
}

public static class PayrollSeniorityEvaluationPolicies
{
    public static DateOnly ResolveDate(
        SeniorityEvaluationPolicy policy,
        DateOnly periodEnd) => policy switch
        {
            SeniorityEvaluationPolicy.PeriodEnd => periodEnd,
            _ => throw new DomainValidationException("未知的年資評估政策。")
        };

    public static int FullCalendarMonths(DateOnly employmentStart, DateOnly evaluationDate)
    {
        var months = (evaluationDate.Year - employmentStart.Year) * 12 +
            evaluationDate.Month - employmentStart.Month;
        if (evaluationDate.Day < employmentStart.Day)
            months--;
        return Math.Max(0, months);
    }
}

public sealed record PayrollComponentResolutionResult(
    Guid ComponentDefinitionId,
    string ComponentCode,
    string ComponentName,
    PayrollComponentCategory Category,
    PayrollSnapshotSourceType SourceType,
    Guid SourceId,
    decimal? StandardAmount,
    decimal? OverrideAmount,
    PayrollProrationKind ProrationKind,
    decimal? FullMonthlyAmount,
    int? PayableDays,
    decimal? ProrationFactor,
    decimal? RawProratedAmount,
    decimal? ResolvedAmount,
    PayrollCalculationStatus CalculationStatus,
    DateOnly EffectiveSourceDate,
    PayrollPeriodicAccrualResult? PeriodicAccrual = null);

public static class FixedEarningsCalculator
{
    private static readonly HashSet<string> FixedComponentCodes =
        new(StringComparer.Ordinal)
        {
            "BASE_SALARY",
            "PERFORMANCE",
            "JOB_ALLOWANCE",
            "CERTIFICATE_ALLOWANCE",
            "MEAL_ALLOWANCE",
            "PERIODIC_FIXED_PAY"
        };

    public static PayrollComponentResolutionResult ResolvePeriodicFixed(
        PayrollComponentDefinition definition, Guid sourceId,
        decimal amount, DateOnly effectiveSourceDate) => new(
            definition.Id, definition.Code, definition.Name,
            definition.Category, PayrollSnapshotSourceType.PayrollPlan,
            sourceId, amount, null, PayrollProrationKind.None, amount,
            null, 1m, amount, PayrollMoneyRoundingPolicy.RoundNtd(amount),
            PayrollCalculationStatus.Resolved, effectiveSourceDate);

    public static PayrollComponentResolutionResult ResolvePeriodicAccrued(
        PayrollComponentDefinition definition, Guid sourceId,
        PayrollPeriodicAccrualResult accrual,
        DateOnly effectiveSourceDate) => new(
            definition.Id, definition.Code, definition.Name,
            definition.Category, PayrollSnapshotSourceType.PayrollPlan,
            sourceId, accrual.TotalAmount, null, PayrollProrationKind.None,
            null, null, 1m, accrual.TotalAmount, accrual.TotalAmount,
            accrual.CalculationStatus, effectiveSourceDate, accrual);

    public static PayrollComponentResolutionResult ResolvePlanComponent(
        PayrollPlanComponent planComponent,
        EmployeePayrollComponentOverride? itemOverride,
        DateOnly employmentStart,
        DateOnly? employmentEnd,
        DateOnly periodStart,
        DateOnly periodEnd,
        DateOnly effectiveSourceDate,
        SeniorityEvaluationPolicy seniorityPolicy =
            SeniorityEvaluationPolicy.PeriodEnd)
    {
        var definition = planComponent.ComponentDefinition;
        var standard = planComponent.DefaultAmount;
        var fullMonthlyAmount = standard;
        var status = PayrollCalculationStatus.Resolved;
        var source = PayrollSnapshotSourceType.PayrollPlan;
        var resolvedSourceDate = effectiveSourceDate;

        if (planComponent.RuleKind == PayrollRuleKind.SeniorityTier)
        {
            var evaluationDate = PayrollSeniorityEvaluationPolicies.ResolveDate(
                seniorityPolicy,
                periodEnd);
            resolvedSourceDate = evaluationDate;
            var months = PayrollSeniorityEvaluationPolicies.FullCalendarMonths(
                employmentStart,
                evaluationDate);
            var tiers = planComponent.SeniorityTiers
                .Where(item => item.Contains(months))
                .ToArray();
            if (tiers.Length != 1)
            {
                status = PayrollCalculationStatus.NeedsSetup;
                fullMonthlyAmount = null;
            }
            else
            {
                standard = fullMonthlyAmount = tiers[0].Amount;
            }
        }
        else if (planComponent.RuleKind is
                 PayrollRuleKind.AttendanceProrated or
                 PayrollRuleKind.ExternalPending)
        {
            status = PayrollCalculationStatus.NotCalculated;
            fullMonthlyAmount = null;
            source = planComponent.RuleKind == PayrollRuleKind.AttendanceProrated
                ? PayrollSnapshotSourceType.RulePending
                : PayrollSnapshotSourceType.ExternalPending;
        }
        else if (!standard.HasValue)
        {
            status = PayrollCalculationStatus.NeedsSetup;
        }

        if (itemOverride is not null)
        {
            source = PayrollSnapshotSourceType.EmployeeOverride;
            switch (itemOverride.OverrideMode)
            {
                case PayrollOverrideMode.Replace:
                    fullMonthlyAmount = itemOverride.OverrideAmount;
                    status = PayrollCalculationStatus.Resolved;
                    break;
                case PayrollOverrideMode.Add when fullMonthlyAmount.HasValue:
                    fullMonthlyAmount += itemOverride.OverrideAmount;
                    status = PayrollCalculationStatus.Resolved;
                    break;
                case PayrollOverrideMode.Add:
                    status = PayrollCalculationStatus.NeedsSetup;
                    break;
                case PayrollOverrideMode.Disable:
                    fullMonthlyAmount = null;
                    status = PayrollCalculationStatus.Disabled;
                    break;
            }
        }

        return Finish(
            definition,
            itemOverride?.Id ?? planComponent.Id,
            source,
            standard,
            itemOverride?.OverrideAmount,
            planComponent.ProrationKind,
            fullMonthlyAmount,
            status,
            employmentStart,
            employmentEnd,
            periodStart,
            periodEnd,
            resolvedSourceDate);
    }

    public static PayrollComponentResolutionResult ResolveDirectOverride(
        EmployeePayrollComponentOverride itemOverride,
        DateOnly employmentStart,
        DateOnly? employmentEnd,
        DateOnly periodStart,
        DateOnly periodEnd)
    {
        var definition = itemOverride.ComponentDefinition;
        var prorationKind = DirectOverrideProration(definition.Code);
        var status = itemOverride.OverrideMode switch
        {
            PayrollOverrideMode.Disable => PayrollCalculationStatus.Disabled,
            PayrollOverrideMode.Replace => PayrollCalculationStatus.Resolved,
            _ => PayrollCalculationStatus.NotCalculated
        };
        return Finish(
            definition,
            itemOverride.Id,
            PayrollSnapshotSourceType.EmployeeOverride,
            null,
            itemOverride.OverrideAmount,
            prorationKind,
            status == PayrollCalculationStatus.Resolved
                ? itemOverride.OverrideAmount
                : null,
            status,
            employmentStart,
            employmentEnd,
            periodStart,
            periodEnd,
            itemOverride.EffectiveFrom);
    }

    private static PayrollComponentResolutionResult Finish(
        PayrollComponentDefinition definition,
        Guid sourceId,
        PayrollSnapshotSourceType sourceType,
        decimal? standardAmount,
        decimal? overrideAmount,
        PayrollProrationKind prorationKind,
        decimal? fullMonthlyAmount,
        PayrollCalculationStatus status,
        DateOnly employmentStart,
        DateOnly? employmentEnd,
        DateOnly periodStart,
        DateOnly periodEnd,
        DateOnly effectiveSourceDate)
    {
        int? payableDays = null;
        decimal? factor = null;
        decimal? raw = null;
        decimal? resolved = null;

        if (status == PayrollCalculationStatus.Resolved && fullMonthlyAmount.HasValue)
        {
            var proration = Monthly30DayProrationPolicy.Calculate(
                fullMonthlyAmount.Value,
                periodStart,
                periodEnd,
                employmentStart,
                employmentEnd);
            payableDays = proration.PayableDays;
            factor = proration.PayableFactor;
            if (prorationKind == PayrollProrationKind.Monthly30Day)
            {
                raw = proration.RawProratedAmount;
                resolved = proration.RoundedAmount;
            }
            else if (prorationKind == PayrollProrationKind.PendingPolicy &&
                     !proration.IsFullMonth)
            {
                status = PayrollCalculationStatus.PolicyPending;
            }
            else
            {
                raw = fullMonthlyAmount.Value;
                resolved = PayrollMoneyRoundingPolicy.RoundNtd(
                    fullMonthlyAmount.Value);
                factor = 1m;
            }
        }

        return new(
            definition.Id,
            definition.Code,
            definition.Name,
            definition.Category,
            sourceType,
            sourceId,
            standardAmount,
            overrideAmount,
            prorationKind,
            fullMonthlyAmount,
            payableDays,
            factor,
            raw,
            resolved,
            status,
            effectiveSourceDate);
    }

    private static PayrollProrationKind DirectOverrideProration(string code) =>
        code switch
        {
            "BASE_SALARY" or "MEAL_ALLOWANCE" =>
                PayrollProrationKind.Monthly30Day,
            "PERFORMANCE" or "JOB_ALLOWANCE" or "CERTIFICATE_ALLOWANCE" =>
                PayrollProrationKind.PendingPolicy,
            _ => PayrollProrationKind.None
        };

    public static bool SupportsComponent(string code) =>
        FixedComponentCodes.Contains(code);
}
