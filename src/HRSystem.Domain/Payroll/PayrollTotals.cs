using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using HRSystem.Domain.Common;

namespace HRSystem.Domain.Payroll;

public sealed record PayrollTotalComponentInput(
    Guid ComponentDefinitionId,
    string ComponentCode,
    PayrollComponentCategory Category,
    PayrollSnapshotSourceType SourceType,
    Guid SourceId,
    PayrollCalculationStatus CalculationStatus,
    decimal? ResolvedAmount);

public sealed record PayrollTotalBlockingItem(
    Guid? ComponentDefinitionId,
    string ComponentCode,
    PayrollCalculationStatus ComponentStatus,
    PayrollTotalBlockingReason Reason);

public sealed record PayrollTotalCalculationResult(
    decimal KnownGrossPay,
    decimal KnownDeductions,
    decimal? NetPay,
    PayrollCalculationStatus CalculationStatus,
    IReadOnlyList<PayrollTotalBlockingItem> BlockingItems,
    byte[] SourceFingerprint);

public static class PayrollTotalReadinessPolicy
{
    private static readonly HashSet<string> OptionalCodes = new(StringComparer.Ordinal)
    {
        "JOB_ALLOWANCE", "CERTIFICATE_ALLOWANCE", "CASE_BONUS",
        "OTHER_EARNING", "OTHER_DEDUCTION"
    };

    public static PayrollTotalComponentRequirement Classify(
        PayrollTotalComponentInput component)
    {
        ArgumentNullException.ThrowIfNull(component);
        if (component.Category == PayrollComponentCategory.Informational ||
            component.CalculationStatus == PayrollCalculationStatus.Disabled)
            return PayrollTotalComponentRequirement.NotApplicable;
        return OptionalCodes.Contains(component.ComponentCode)
            ? PayrollTotalComponentRequirement.Optional
            : PayrollTotalComponentRequirement.Required;
    }

    public static bool IsIncludedInTotals(PayrollTotalComponentInput component) =>
        (component.Category is PayrollComponentCategory.Earning or
            PayrollComponentCategory.Deduction) &&
        component.CalculationStatus != PayrollCalculationStatus.Disabled;

    public static PayrollCalculationStatus SelectStatus(
        IEnumerable<PayrollTotalBlockingItem> blockers)
    {
        var statuses = blockers.Select(x => x.ComponentStatus).ToArray();
        if (statuses.Length == 0) return PayrollCalculationStatus.Resolved;
        if (statuses.Contains(PayrollCalculationStatus.NeedsReview))
            return PayrollCalculationStatus.NeedsReview;
        if (statuses.Contains(PayrollCalculationStatus.SourceChanged))
            return PayrollCalculationStatus.SourceChanged;
        if (statuses.Contains(PayrollCalculationStatus.NeedsSetup))
            return PayrollCalculationStatus.NeedsSetup;
        if (statuses.Contains(PayrollCalculationStatus.PolicyPending))
            return PayrollCalculationStatus.PolicyPending;
        return PayrollCalculationStatus.NotCalculated;
    }
}

public static class PayrollTotalCalculator
{
    public static PayrollTotalCalculationResult Calculate(
        IEnumerable<PayrollTotalComponentInput> source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var components = source.ToArray();
        var blockers = new List<PayrollTotalBlockingItem>();
        decimal gross = 0;
        decimal deductions = 0;

        foreach (var component in components.Where(
                     PayrollTotalReadinessPolicy.IsIncludedInTotals))
        {
            if (component.CalculationStatus != PayrollCalculationStatus.Resolved)
            {
                blockers.Add(new(component.ComponentDefinitionId,
                    component.ComponentCode, Normalize(component.CalculationStatus),
                    component.CalculationStatus == PayrollCalculationStatus.SourceChanged
                        ? PayrollTotalBlockingReason.SourceChanged
                        : PayrollTotalBlockingReason.ComponentUnresolved));
                continue;
            }
            if (!component.ResolvedAmount.HasValue)
            {
                blockers.Add(new(component.ComponentDefinitionId,
                    component.ComponentCode, PayrollCalculationStatus.NeedsReview,
                    PayrollTotalBlockingReason.MissingResolvedAmount));
                continue;
            }
            if (component.ResolvedAmount.Value < 0)
            {
                blockers.Add(new(component.ComponentDefinitionId,
                    component.ComponentCode, PayrollCalculationStatus.NeedsReview,
                    PayrollTotalBlockingReason.InvalidSign));
                continue;
            }
            if (component.Category == PayrollComponentCategory.Earning)
                gross += component.ResolvedAmount.Value;
            else if (component.Category == PayrollComponentCategory.Deduction)
                deductions += component.ResolvedAmount.Value;
        }

        decimal? net = blockers.Count == 0 ? gross - deductions : null;
        if (net < 0)
        {
            blockers.Add(new(null, "PAYROLL_TOTAL",
                PayrollCalculationStatus.NeedsReview,
                PayrollTotalBlockingReason.NegativeNetPay));
        }
        var status = PayrollTotalReadinessPolicy.SelectStatus(blockers);
        return new(gross, deductions, net, status, blockers,
            PayrollTotalFingerprintV1.Calculate(components));
    }

    private static PayrollCalculationStatus Normalize(PayrollCalculationStatus status) =>
        status is PayrollCalculationStatus.Pending or PayrollCalculationStatus.Disabled
            ? PayrollCalculationStatus.NotCalculated
            : status;
}

public static class PayrollTotalFingerprintV1
{
    public const short Version = 1;

    public static byte[] Calculate(IEnumerable<PayrollTotalComponentInput> source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var values = source.OrderBy(x => x.ComponentDefinitionId)
            .ThenBy(x => x.SourceType).ThenBy(x => x.SourceId)
            .Select(x => string.Join("|",
                x.ComponentDefinitionId.ToString("D"),
                x.ComponentCode,
                ((int)x.Category).ToString(CultureInfo.InvariantCulture),
                ((int)x.SourceType).ToString(CultureInfo.InvariantCulture),
                x.SourceId.ToString("D"),
                ((int)x.CalculationStatus).ToString(CultureInfo.InvariantCulture),
                x.ResolvedAmount?.ToString("0.############################",
                    CultureInfo.InvariantCulture) ?? "<null>"));
        return SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", values)));
    }

    public static bool Matches(byte[]? expected,
        IEnumerable<PayrollTotalComponentInput> components) =>
        expected is { Length: 32 } &&
        CryptographicOperations.FixedTimeEquals(expected, Calculate(components));
}

public sealed class PayrollTotalBlockingEvidence
{
    private PayrollTotalBlockingEvidence() { }
    public PayrollTotalBlockingEvidence(Guid id, Guid payrollEmployeeSnapshotId,
        Guid? componentDefinitionId, string componentCode,
        PayrollCalculationStatus componentStatus,
        PayrollTotalBlockingReason reason)
    {
        if (payrollEmployeeSnapshotId == Guid.Empty)
            throw new DomainValidationException("員工薪資快照不可空白。");
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        PayrollEmployeeSnapshotId = payrollEmployeeSnapshotId;
        ComponentDefinitionId = componentDefinitionId;
        ComponentCode = PayrollComponentDefinition.RequiredCode(componentCode, 50);
        ComponentStatus = componentStatus;
        Reason = reason;
    }

    public Guid Id { get; private set; }
    public Guid PayrollEmployeeSnapshotId { get; private set; }
    public Guid? ComponentDefinitionId { get; private set; }
    public string ComponentCode { get; private set; } = string.Empty;
    public PayrollCalculationStatus ComponentStatus { get; private set; }
    public PayrollTotalBlockingReason Reason { get; private set; }
    public PayrollEmployeeSnapshot PayrollEmployeeSnapshot { get; private set; } = null!;
    public PayrollComponentDefinition? ComponentDefinition { get; private set; }
}
