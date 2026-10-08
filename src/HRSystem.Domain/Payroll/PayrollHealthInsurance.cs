using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using HRSystem.Domain.Common;
using HRSystem.Domain.MasterData;

namespace HRSystem.Domain.Payroll;

public sealed class EmployeeHealthInsuranceEnrollment
{
    private EmployeeHealthInsuranceEnrollment() { }
    public EmployeeHealthInsuranceEnrollment(Guid id, Guid employeeId,
        HealthInsuranceEnrollmentStatus status, decimal? monthlyInsuredAmount,
        int? dependentCount, DateOnly effectiveFrom, DateOnly? effectiveTo = null,
        bool isActive = true)
    {
        if (employeeId == Guid.Empty || !Enum.IsDefined(status))
            throw new DomainValidationException("健保投保設定不合法。");
        if (effectiveTo < effectiveFrom)
            throw new DomainValidationException("健保投保期間不合法。");
        if (monthlyInsuredAmount is <= 0)
            throw new DomainValidationException("健保月投保金額必須大於 0。");
        if (dependentCount is < 0)
            throw new DomainValidationException("健保眷屬人數不得小於 0。");
        if (status == HealthInsuranceEnrollmentStatus.NotEnrolled &&
            (monthlyInsuredAmount.HasValue || dependentCount.HasValue))
            throw new DomainValidationException("明確未投保不得設定投保金額或眷屬人數。");
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        EmployeeId = employeeId;
        Status = status;
        MonthlyInsuredAmount = monthlyInsuredAmount;
        DependentCount = dependentCount;
        EffectiveFrom = effectiveFrom;
        EffectiveTo = effectiveTo;
        IsActive = isActive;
    }
    public Guid Id { get; private set; }
    public Guid EmployeeId { get; private set; }
    public HealthInsuranceEnrollmentStatus Status { get; private set; }
    public decimal? MonthlyInsuredAmount { get; private set; }
    public int? DependentCount { get; private set; }
    public DateOnly EffectiveFrom { get; private set; }
    public DateOnly? EffectiveTo { get; private set; }
    public bool IsActive { get; private set; }
    public byte[] RowVersion { get; private set; } = [];
    public Employee Employee { get; private set; } = null!;
    public bool Overlaps(DateOnly from, DateOnly to) => IsActive &&
        EffectiveFrom <= to && (!EffectiveTo.HasValue || EffectiveTo.Value >= from);

    public void CloseBefore(DateOnly nextEffectiveFrom)
    {
        var effectiveTo = nextEffectiveFrom.AddDays(-1);
        if (!IsActive || effectiveTo < EffectiveFrom ||
            EffectiveTo.HasValue && EffectiveTo.Value < nextEffectiveFrom)
            throw new DomainValidationException("健保投保設定無法在指定日期前結束。");
        EffectiveTo = effectiveTo;
    }
}

public sealed class HealthInsuranceRatePolicy
{
    private HealthInsuranceRatePolicy() { }
    public HealthInsuranceRatePolicy(Guid id, string version,
        decimal generalPremiumRate, decimal employeeShareRate, int dependentCap,
        HealthInsuranceDependentBillingRule dependentBillingRule,
        HealthInsuranceContributionPeriodPolicy contributionPeriodPolicy,
        DateOnly effectiveFrom, DateOnly? effectiveTo = null, bool isActive = true)
    {
        if (generalPremiumRate is <= 0 or > 1 || employeeShareRate is <= 0 or > 1 ||
            dependentCap < 0 || !Enum.IsDefined(dependentBillingRule) ||
            !Enum.IsDefined(contributionPeriodPolicy))
            throw new DomainValidationException("健保費率政策不合法。");
        if (effectiveTo < effectiveFrom)
            throw new DomainValidationException("健保費率政策期間不合法。");
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        Version = PayrollComponentDefinition.RequiredText(version, "健保政策版本", 50);
        GeneralPremiumRate = generalPremiumRate;
        EmployeeShareRate = employeeShareRate;
        DependentCap = dependentCap;
        DependentBillingRule = dependentBillingRule;
        ContributionPeriodPolicy = contributionPeriodPolicy;
        EffectiveFrom = effectiveFrom;
        EffectiveTo = effectiveTo;
        IsActive = isActive;
    }
    public Guid Id { get; private set; }
    public string Version { get; private set; } = string.Empty;
    public decimal GeneralPremiumRate { get; private set; }
    public decimal EmployeeShareRate { get; private set; }
    public int DependentCap { get; private set; }
    public HealthInsuranceDependentBillingRule DependentBillingRule { get; private set; }
    public HealthInsuranceContributionPeriodPolicy ContributionPeriodPolicy { get; private set; }
    public DateOnly EffectiveFrom { get; private set; }
    public DateOnly? EffectiveTo { get; private set; }
    public bool IsActive { get; private set; }
    public byte[] RowVersion { get; private set; } = [];
    public bool Overlaps(DateOnly from, DateOnly to) => IsActive &&
        EffectiveFrom <= to && (!EffectiveTo.HasValue || EffectiveTo.Value >= from);
}

public sealed record HealthInsuranceCalculationResult(
    HealthInsuranceEnrollmentStatus? EnrollmentStatus, Guid? EnrollmentId,
    decimal? MonthlyInsuredAmount, int? ActualDependentCount,
    DateOnly? EnrollmentFrom, DateOnly? EnrollmentTo,
    Guid? PolicyId, string? PolicyVersion, DateOnly? PolicyFrom, DateOnly? PolicyTo,
    decimal? GeneralPremiumRate, decimal? EmployeeShareRate, int? DependentCap,
    int? ChargeableDependentCount, int? ContributionUnits,
    decimal? RawEmployeeAmount, decimal? FinalEmployeeDeduction,
    PayrollCalculationStatus CalculationStatus, byte[] SourceFingerprint);

public static class HealthInsuranceEmployeeDeductionCalculator
{
    public static HealthInsuranceCalculationResult Calculate(
        IEnumerable<EmployeeHealthInsuranceEnrollment> enrollments,
        IEnumerable<HealthInsuranceRatePolicy> policies,
        DateOnly periodStart, DateOnly periodEnd,
        DateOnly employmentStart, DateOnly? employmentEnd)
    {
        var sourceEnrollments = enrollments.Where(x => x.IsActive).ToArray();
        var applicablePolicies = policies.Where(x => x.Overlaps(periodStart, periodEnd)).ToArray();
        var fingerprint = HealthInsuranceSourceFingerprintV1.Calculate(
            sourceEnrollments, applicablePolicies, periodStart, periodEnd,
            employmentStart, employmentEnd);
        if (sourceEnrollments.Length == 0)
            return Empty(PayrollCalculationStatus.NeedsSetup, fingerprint);
        var coverage = HealthInsuranceMonthlyCoverage.Evaluate(sourceEnrollments,
            periodStart, periodEnd);
        var authority = coverage.AuthorityEnrollmentId is { } authorityId
            ? sourceEnrollments.Single(x => x.Id == authorityId)
            : null;
        if (coverage.Decision == HealthInsuranceMonthlyCoverageDecision.NeedsReview)
            return authority is null
                ? Empty(PayrollCalculationStatus.NeedsReview, fingerprint)
                : From(authority, null, null, null, null,
                    PayrollCalculationStatus.NeedsReview, fingerprint);
        if (coverage.Decision == HealthInsuranceMonthlyCoverageDecision.NotCovered)
            return authority is null
                ? NotCovered(fingerprint)
                : From(authority, null, 0, 0, 0,
                    PayrollCalculationStatus.Resolved, fingerprint);

        var enrollment = authority!;
        if (enrollment.Status == HealthInsuranceEnrollmentStatus.NotEnrolled)
            return From(enrollment, null, 0, 0, 0,
                PayrollCalculationStatus.Resolved, fingerprint);
        if (!enrollment.MonthlyInsuredAmount.HasValue || !enrollment.DependentCount.HasValue)
            return From(enrollment, null, null, null, null,
                PayrollCalculationStatus.NeedsSetup, fingerprint);
        if (applicablePolicies.Length == 0)
            return From(enrollment, null, null, null, null,
                PayrollCalculationStatus.PolicyPending, fingerprint);
        if (applicablePolicies.Length != 1)
            return From(enrollment, null, null, null, null,
                PayrollCalculationStatus.NeedsReview, fingerprint);
        var policy = applicablePolicies[0];
        var fullyCoveredPolicy = policy.EffectiveFrom <= periodStart &&
            (!policy.EffectiveTo.HasValue || policy.EffectiveTo.Value >= periodEnd);
        if (!fullyCoveredPolicy || policy.ContributionPeriodPolicy !=
            HealthInsuranceContributionPeriodPolicy.FullPeriodOnly)
            return From(enrollment, policy, null, null, null,
                PayrollCalculationStatus.PolicyPending, fingerprint);
        var chargeable = Math.Min(enrollment.DependentCount.Value, policy.DependentCap);
        var units = 1 + chargeable;
        var raw = decimal.Round(enrollment.MonthlyInsuredAmount.Value *
            policy.GeneralPremiumRate * policy.EmployeeShareRate * units,
            6, MidpointRounding.AwayFromZero);
        return From(enrollment, policy, chargeable, units, raw,
            PayrollCalculationStatus.Resolved, fingerprint);
    }

    private static HealthInsuranceCalculationResult Empty(
        PayrollCalculationStatus status, byte[] fingerprint) =>
        new(null, null, null, null, null, null, null, null, null, null,
            null, null, null, null, null, null, null, status, fingerprint);

    private static HealthInsuranceCalculationResult NotCovered(byte[] fingerprint) =>
        new(null, null, null, null, null, null, null, null, null, null,
            null, null, null, null, null, 0, 0,
            PayrollCalculationStatus.Resolved, fingerprint);

    private static HealthInsuranceCalculationResult From(
        EmployeeHealthInsuranceEnrollment enrollment, HealthInsuranceRatePolicy? policy,
        int? chargeable, int? units, decimal? raw,
        PayrollCalculationStatus status, byte[] fingerprint) =>
        new(enrollment.Status, enrollment.Id, enrollment.MonthlyInsuredAmount,
            enrollment.DependentCount, enrollment.EffectiveFrom, enrollment.EffectiveTo,
            policy?.Id, policy?.Version, policy?.EffectiveFrom, policy?.EffectiveTo,
            policy?.GeneralPremiumRate, policy?.EmployeeShareRate, policy?.DependentCap,
            chargeable, units, raw,
            status == PayrollCalculationStatus.Resolved && raw.HasValue
                ? PayrollMoneyRoundingPolicy.RoundNtd(raw.Value) : raw,
            status, fingerprint);
}

public static class HealthInsuranceSourceFingerprintV1
{
    public const short Version = 1;
    public static byte[] Calculate(IEnumerable<EmployeeHealthInsuranceEnrollment> enrollments,
        IEnumerable<HealthInsuranceRatePolicy> policies, DateOnly periodStart,
        DateOnly periodEnd, DateOnly employmentStart, DateOnly? employmentEnd)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, "v1"); Append(hash, periodStart.ToString("yyyy-MM-dd"));
        Append(hash, periodEnd.ToString("yyyy-MM-dd"));
        Append(hash, employmentStart.ToString("yyyy-MM-dd"));
        Append(hash, employmentEnd?.ToString("yyyy-MM-dd") ?? string.Empty);
        foreach (var item in enrollments.OrderBy(x => x.Id))
            Append(hash, string.Join('|', item.Id.ToString("D"),
                ((byte)item.Status).ToString(CultureInfo.InvariantCulture),
                item.MonthlyInsuredAmount?.ToString(CultureInfo.InvariantCulture),
                item.DependentCount?.ToString(CultureInfo.InvariantCulture),
                item.EffectiveFrom.ToString("yyyy-MM-dd"),
                item.EffectiveTo?.ToString("yyyy-MM-dd"), item.IsActive ? "1" : "0"));
        foreach (var item in policies.OrderBy(x => x.Id))
            Append(hash, string.Join('|', item.Id.ToString("D"), item.Version,
                item.GeneralPremiumRate.ToString(CultureInfo.InvariantCulture),
                item.EmployeeShareRate.ToString(CultureInfo.InvariantCulture),
                item.DependentCap.ToString(CultureInfo.InvariantCulture),
                ((byte)item.DependentBillingRule).ToString(CultureInfo.InvariantCulture),
                ((byte)item.ContributionPeriodPolicy).ToString(CultureInfo.InvariantCulture),
                item.EffectiveFrom.ToString("yyyy-MM-dd"),
                item.EffectiveTo?.ToString("yyyy-MM-dd"), item.IsActive ? "1" : "0"));
        return hash.GetHashAndReset();
    }
    public static bool IsCurrent(byte[] storedFingerprint,
        IEnumerable<EmployeeHealthInsuranceEnrollment> enrollments,
        IEnumerable<HealthInsuranceRatePolicy> policies,
        DateOnly periodStart, DateOnly periodEnd,
        DateOnly employmentStart, DateOnly? employmentEnd)
    {
        if (storedFingerprint is null || storedFingerprint.Length != 32)
            return false;
        return CryptographicOperations.FixedTimeEquals(storedFingerprint,
            Calculate(enrollments, policies, periodStart, periodEnd,
                employmentStart, employmentEnd));
    }
    private static void Append(IncrementalHash hash, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(length, (uint)bytes.Length);
        hash.AppendData(length); hash.AppendData(bytes);
    }
}

public sealed class PayrollHealthInsuranceSnapshot
{
    private PayrollHealthInsuranceSnapshot() { }
    public PayrollHealthInsuranceSnapshot(Guid id, Guid componentId,
        HealthInsuranceCalculationResult result)
    {
        if (componentId == Guid.Empty || result.SourceFingerprint.Length != 32)
            throw new DomainValidationException("健保薪資快照來源不合法。");
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        PayrollEmployeeSnapshotComponentId = componentId;
        EnrollmentStatus = result.EnrollmentStatus; EnrollmentId = result.EnrollmentId;
        MonthlyInsuredAmount = result.MonthlyInsuredAmount;
        ActualDependentCount = result.ActualDependentCount;
        EnrollmentFrom = result.EnrollmentFrom; EnrollmentTo = result.EnrollmentTo;
        PolicyId = result.PolicyId; PolicyVersion = result.PolicyVersion;
        PolicyFrom = result.PolicyFrom; PolicyTo = result.PolicyTo;
        GeneralPremiumRate = result.GeneralPremiumRate;
        EmployeeShareRate = result.EmployeeShareRate; DependentCap = result.DependentCap;
        ChargeableDependentCount = result.ChargeableDependentCount;
        ContributionUnits = result.ContributionUnits; RawEmployeeAmount = result.RawEmployeeAmount;
        FinalEmployeeDeduction = result.FinalEmployeeDeduction;
        CalculationStatus = result.CalculationStatus;
        SourceFingerprintVersion = HealthInsuranceSourceFingerprintV1.Version;
        SourceFingerprint = result.SourceFingerprint.ToArray();
    }
    public Guid Id { get; private set; }
    public Guid PayrollEmployeeSnapshotComponentId { get; private set; }
    public HealthInsuranceEnrollmentStatus? EnrollmentStatus { get; private set; }
    public Guid? EnrollmentId { get; private set; }
    public decimal? MonthlyInsuredAmount { get; private set; }
    public int? ActualDependentCount { get; private set; }
    public DateOnly? EnrollmentFrom { get; private set; }
    public DateOnly? EnrollmentTo { get; private set; }
    public Guid? PolicyId { get; private set; }
    public string? PolicyVersion { get; private set; }
    public DateOnly? PolicyFrom { get; private set; }
    public DateOnly? PolicyTo { get; private set; }
    public decimal? GeneralPremiumRate { get; private set; }
    public decimal? EmployeeShareRate { get; private set; }
    public int? DependentCap { get; private set; }
    public int? ChargeableDependentCount { get; private set; }
    public int? ContributionUnits { get; private set; }
    public decimal? RawEmployeeAmount { get; private set; }
    public decimal? FinalEmployeeDeduction { get; private set; }
    public PayrollCalculationStatus CalculationStatus { get; private set; }
    public short SourceFingerprintVersion { get; private set; }
    public byte[] SourceFingerprint { get; private set; } = [];
    public PayrollEmployeeSnapshotComponent PayrollEmployeeSnapshotComponent { get; private set; } = null!;
    public PayrollHealthInsuranceEvidence? Evidence { get; private set; }
    public void AttachEvidence(PayrollHealthInsuranceEvidence evidence)
    {
        if (evidence.PayrollHealthInsuranceSnapshotId != Id)
            throw new DomainValidationException("健保證據必須屬於此快照。");
        Evidence = evidence;
    }
}

public sealed class PayrollHealthInsuranceEvidence
{
    private PayrollHealthInsuranceEvidence() { }
    public PayrollHealthInsuranceEvidence(Guid id, Guid snapshotId,
        HealthInsuranceCalculationResult result)
    {
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        PayrollHealthInsuranceSnapshotId = snapshotId;
        MonthlyInsuredAmount = result.MonthlyInsuredAmount;
        ActualDependentCount = result.ActualDependentCount;
        DependentCap = result.DependentCap;
        ChargeableDependentCount = result.ChargeableDependentCount;
        ContributionUnits = result.ContributionUnits;
        GeneralPremiumRate = result.GeneralPremiumRate;
        EmployeeShareRate = result.EmployeeShareRate;
        RawEmployeeAmount = result.RawEmployeeAmount;
        FinalEmployeeDeduction = result.FinalEmployeeDeduction;
    }
    public Guid Id { get; private set; }
    public Guid PayrollHealthInsuranceSnapshotId { get; private set; }
    public decimal? MonthlyInsuredAmount { get; private set; }
    public int? ActualDependentCount { get; private set; }
    public int? DependentCap { get; private set; }
    public int? ChargeableDependentCount { get; private set; }
    public int? ContributionUnits { get; private set; }
    public decimal? GeneralPremiumRate { get; private set; }
    public decimal? EmployeeShareRate { get; private set; }
    public decimal? RawEmployeeAmount { get; private set; }
    public decimal? FinalEmployeeDeduction { get; private set; }
    public PayrollHealthInsuranceSnapshot PayrollHealthInsuranceSnapshot { get; private set; } = null!;
}
