using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using HRSystem.Domain.Common;
using HRSystem.Domain.MasterData;

namespace HRSystem.Domain.Payroll;

public sealed class EmployeeLaborInsuranceEnrollment
{
    private EmployeeLaborInsuranceEnrollment() { }

    public EmployeeLaborInsuranceEnrollment(Guid id, Guid employeeId,
        LaborInsuranceEnrollmentStatus status,
        decimal? monthlyLaborInsuredSalary,
        DateOnly effectiveFrom, DateOnly? effectiveTo = null,
        bool isActive = true)
    {
        if (employeeId == Guid.Empty || !Enum.IsDefined(status))
            throw new DomainValidationException("勞保投保設定不合法。");
        if (effectiveTo < effectiveFrom)
            throw new DomainValidationException("勞保投保期間不合法。");
        if (monthlyLaborInsuredSalary is <= 0)
            throw new DomainValidationException("勞保月投保薪資必須大於 0。");
        if (status == LaborInsuranceEnrollmentStatus.NotEnrolled &&
            monthlyLaborInsuredSalary.HasValue)
            throw new DomainValidationException("明確未投保不得設定勞保投保薪資。");
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        EmployeeId = employeeId;
        Status = status;
        MonthlyLaborInsuredSalary = monthlyLaborInsuredSalary;
        EffectiveFrom = effectiveFrom;
        EffectiveTo = effectiveTo;
        IsActive = isActive;
    }

    public Guid Id { get; private set; }
    public Guid EmployeeId { get; private set; }
    public LaborInsuranceEnrollmentStatus Status { get; private set; }
    public decimal? MonthlyLaborInsuredSalary { get; private set; }
    public decimal? MonthlyInsuredSalary => MonthlyLaborInsuredSalary;
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
            throw new DomainValidationException("勞保投保設定無法在指定日期前結束。");
        EffectiveTo = effectiveTo;
    }

    public void Deactivate()
    {
        if (!IsActive)
            throw new DomainValidationException("勞保投保設定已停用。");
        IsActive = false;
    }
}

public sealed class LaborInsuranceRatePolicy
{
    private LaborInsuranceRatePolicy() { }

    public LaborInsuranceRatePolicy(Guid id, string version,
        LaborInsuranceCoverage coverage, decimal ordinaryAccidentInsuranceRate,
        decimal employmentInsuranceRate, decimal employeeShareRate,
        LaborInsuranceContributionPeriodPolicy contributionPeriodPolicy,
        DateOnly effectiveFrom, DateOnly? effectiveTo = null, bool isActive = true)
    {
        if (coverage == LaborInsuranceCoverage.None ||
            (coverage & ~(LaborInsuranceCoverage.OrdinaryAccident |
                           LaborInsuranceCoverage.Employment)) != 0 ||
            ordinaryAccidentInsuranceRate is < 0 or > 1 ||
            employmentInsuranceRate is < 0 or > 1 ||
            employeeShareRate is <= 0 or > 1 ||
            !Enum.IsDefined(contributionPeriodPolicy))
            throw new DomainValidationException("勞保費率政策不合法。");
        if (coverage.HasFlag(LaborInsuranceCoverage.OrdinaryAccident) &&
            ordinaryAccidentInsuranceRate == 0 ||
            coverage.HasFlag(LaborInsuranceCoverage.Employment) &&
            employmentInsuranceRate == 0)
            throw new DomainValidationException("適用的勞保險別費率必須大於 0。");
        if (effectiveTo < effectiveFrom)
            throw new DomainValidationException("勞保費率政策期間不合法。");
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        Version = PayrollComponentDefinition.RequiredText(version, "勞保政策版本", 50);
        Coverage = coverage;
        OrdinaryAccidentInsuranceRate = ordinaryAccidentInsuranceRate;
        EmploymentInsuranceRate = employmentInsuranceRate;
        EmployeeShareRate = employeeShareRate;
        ContributionPeriodPolicy = contributionPeriodPolicy;
        EffectiveFrom = effectiveFrom;
        EffectiveTo = effectiveTo;
        IsActive = isActive;
    }

    public Guid Id { get; private set; }
    public string Version { get; private set; } = string.Empty;
    public LaborInsuranceCoverage Coverage { get; private set; }
    public decimal OrdinaryAccidentInsuranceRate { get; private set; }
    public decimal EmploymentInsuranceRate { get; private set; }
    public decimal EmployeeShareRate { get; private set; }
    public LaborInsuranceContributionPeriodPolicy ContributionPeriodPolicy { get; private set; }
    public DateOnly EffectiveFrom { get; private set; }
    public DateOnly? EffectiveTo { get; private set; }
    public bool IsActive { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public bool Overlaps(DateOnly from, DateOnly to) => IsActive &&
        EffectiveFrom <= to && (!EffectiveTo.HasValue || EffectiveTo.Value >= from);
}

public sealed record LaborInsuranceContributionResult(
    LaborInsuranceContributionKind Kind, decimal Rate,
    decimal EmployeeShareRate, decimal RawEmployeeAmount,
    decimal RoundedDisplayAmount);

public sealed record LaborInsuranceCalculationResult(
    LaborInsuranceEnrollmentStatus? EnrollmentStatus,
    Guid? EnrollmentId,
    decimal? MonthlyLaborInsuredSalary,
    DateOnly? EnrollmentFrom,
    DateOnly? EnrollmentTo,
    Guid? PolicyId,
    string? PolicyVersion,
    DateOnly? PolicyFrom,
    DateOnly? PolicyTo,
    LaborInsuranceCoverage Coverage,
    decimal? OrdinaryAccidentInsuranceRate,
    decimal? EmploymentInsuranceRate,
    decimal? EmployeeShareRate,
    decimal? FinalEmployeeDeduction,
    PayrollCalculationStatus CalculationStatus,
    IReadOnlyList<LaborInsuranceContributionResult> Contributions,
    byte[] SourceFingerprint)
{
    public decimal? MonthlyInsuredSalary => MonthlyLaborInsuredSalary;
}

public static class LaborInsuranceEmployeeDeductionCalculator
{
    public static LaborInsuranceCalculationResult Calculate(
        IEnumerable<EmployeeLaborInsuranceEnrollment> enrollments,
        IEnumerable<LaborInsuranceRatePolicy> policies,
        DateOnly periodStart, DateOnly periodEnd)
    {
        ArgumentNullException.ThrowIfNull(enrollments);
        ArgumentNullException.ThrowIfNull(policies);
        var applicableEnrollments = enrollments.Where(x => x.Overlaps(periodStart, periodEnd)).ToArray();
        var applicablePolicies = policies.Where(x => x.Overlaps(periodStart, periodEnd)).ToArray();
        var fingerprint = LaborInsuranceSourceFingerprintV2.Calculate(
            applicableEnrollments, applicablePolicies, periodStart, periodEnd);
        if (applicableEnrollments.Length == 0)
            return Empty(PayrollCalculationStatus.NeedsSetup, fingerprint);
        if (applicableEnrollments.Length != 1)
            return Empty(PayrollCalculationStatus.NeedsReview, fingerprint);

        var enrollment = applicableEnrollments[0];
        if (enrollment.Status == LaborInsuranceEnrollmentStatus.NotEnrolled)
            return FromEnrollment(enrollment, null, 0m,
                PayrollCalculationStatus.Resolved, [], fingerprint);
        if (!enrollment.MonthlyLaborInsuredSalary.HasValue)
            return FromEnrollment(enrollment, null, null,
                PayrollCalculationStatus.NeedsSetup, [], fingerprint);
        if (applicablePolicies.Length == 0)
            return FromEnrollment(enrollment, null, null,
                PayrollCalculationStatus.PolicyPending, [], fingerprint);
        if (applicablePolicies.Length != 1)
            return FromEnrollment(enrollment, null, null,
                PayrollCalculationStatus.NeedsReview, [], fingerprint);

        var policy = applicablePolicies[0];
        if (policy.EffectiveFrom > periodStart ||
            policy.EffectiveTo is { } policyEnd && policyEnd < periodEnd)
            return FromEnrollment(enrollment, policy, null,
                PayrollCalculationStatus.PolicyPending, [], fingerprint);
        var coverage = InsuranceCoverageDays.Calculate(periodStart, periodEnd,
            enrollment.EffectiveFrom, enrollment.EffectiveTo);
        var policySupportsCoverage = coverage.CoveredDays ==
                InsuranceCoverageDays.MonthlyDenominator
            ? policy.ContributionPeriodPolicy is
                LaborInsuranceContributionPeriodPolicy.FullPeriodOnly or
                LaborInsuranceContributionPeriodPolicy.ThirtyDayProrated
            : policy.ContributionPeriodPolicy ==
                LaborInsuranceContributionPeriodPolicy.ThirtyDayProrated;
        if (!policySupportsCoverage)
            return FromEnrollment(enrollment, policy, null,
                PayrollCalculationStatus.PolicyPending, [], fingerprint);

        var salary = enrollment.MonthlyLaborInsuredSalary.Value;
        var contributions = new List<LaborInsuranceContributionResult>();
        Add(policy.Coverage.HasFlag(LaborInsuranceCoverage.OrdinaryAccident),
            LaborInsuranceContributionKind.OrdinaryAccident,
            policy.OrdinaryAccidentInsuranceRate);
        Add(policy.Coverage.HasFlag(LaborInsuranceCoverage.Employment),
            LaborInsuranceContributionKind.Employment,
            policy.EmploymentInsuranceRate);
        var final = PayrollMoneyRoundingPolicy.RoundNtd(
            contributions.Sum(x => x.RawEmployeeAmount));
        return FromEnrollment(enrollment, policy, final,
            PayrollCalculationStatus.Resolved, contributions, fingerprint);

        void Add(bool applies, LaborInsuranceContributionKind kind, decimal rate)
        {
            if (!applies) return;
            var raw = decimal.Round(salary * rate * policy.EmployeeShareRate *
                coverage.Factor, 6, MidpointRounding.AwayFromZero);
            contributions.Add(new(kind, rate, policy.EmployeeShareRate, raw,
                PayrollMoneyRoundingPolicy.RoundNtd(raw)));
        }
    }

    private static LaborInsuranceCalculationResult Empty(
        PayrollCalculationStatus status, byte[] fingerprint) =>
        new(null, null, null, null, null, null, null, null, null,
            LaborInsuranceCoverage.None, null, null, null, null,
            status, [], fingerprint);

    private static LaborInsuranceCalculationResult FromEnrollment(
        EmployeeLaborInsuranceEnrollment enrollment,
        LaborInsuranceRatePolicy? policy, decimal? amount,
        PayrollCalculationStatus status,
        IReadOnlyList<LaborInsuranceContributionResult> contributions,
        byte[] fingerprint) => new(enrollment.Status, enrollment.Id,
            enrollment.MonthlyLaborInsuredSalary,
            enrollment.EffectiveFrom,
            enrollment.EffectiveTo, policy?.Id, policy?.Version,
            policy?.EffectiveFrom, policy?.EffectiveTo,
            policy?.Coverage ?? LaborInsuranceCoverage.None,
            policy?.OrdinaryAccidentInsuranceRate,
            policy?.EmploymentInsuranceRate, policy?.EmployeeShareRate,
            amount, status, contributions, fingerprint);
}

public static class LaborInsuranceSourceFingerprintV2
{
    public const short Version = 2;

    public static byte[] Calculate(
        IEnumerable<EmployeeLaborInsuranceEnrollment> enrollments,
        IEnumerable<LaborInsuranceRatePolicy> policies,
        DateOnly periodStart, DateOnly periodEnd)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, "v2");
        Append(hash, periodStart.ToString("yyyy-MM-dd"));
        Append(hash, periodEnd.ToString("yyyy-MM-dd"));
        foreach (var item in enrollments.OrderBy(x => x.Id))
            Append(hash, string.Join('|', item.Id.ToString("D"),
                ((byte)item.Status).ToString(CultureInfo.InvariantCulture),
                item.MonthlyLaborInsuredSalary?.ToString(CultureInfo.InvariantCulture),
                item.EffectiveFrom.ToString("yyyy-MM-dd"),
                item.EffectiveTo?.ToString("yyyy-MM-dd"), item.IsActive ? "1" : "0"));
        foreach (var item in policies.OrderBy(x => x.Id))
            Append(hash, string.Join('|', item.Id.ToString("D"), item.Version,
                ((byte)item.Coverage).ToString(CultureInfo.InvariantCulture),
                item.OrdinaryAccidentInsuranceRate.ToString(CultureInfo.InvariantCulture),
                item.EmploymentInsuranceRate.ToString(CultureInfo.InvariantCulture),
                item.EmployeeShareRate.ToString(CultureInfo.InvariantCulture),
                ((byte)item.ContributionPeriodPolicy).ToString(CultureInfo.InvariantCulture),
                item.EffectiveFrom.ToString("yyyy-MM-dd"),
                item.EffectiveTo?.ToString("yyyy-MM-dd"), item.IsActive ? "1" : "0"));
        return hash.GetHashAndReset();
    }

    public static bool IsCurrent(byte[] storedFingerprint,
        IEnumerable<EmployeeLaborInsuranceEnrollment> enrollments,
        IEnumerable<LaborInsuranceRatePolicy> policies,
        DateOnly periodStart, DateOnly periodEnd)
    {
        if (storedFingerprint is null || storedFingerprint.Length != 32)
            return false;
        return CryptographicOperations.FixedTimeEquals(storedFingerprint,
            Calculate(enrollments, policies, periodStart, periodEnd));
    }

    private static void Append(IncrementalHash hash, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(length, (uint)bytes.Length);
        hash.AppendData(length);
        hash.AppendData(bytes);
    }
}

public sealed class PayrollLaborInsuranceSnapshot
{
    private PayrollLaborInsuranceSnapshot() { }
    public PayrollLaborInsuranceSnapshot(Guid id, Guid componentId,
        LaborInsuranceCalculationResult result)
    {
        if (componentId == Guid.Empty || result.SourceFingerprint.Length != 32)
            throw new DomainValidationException("勞保薪資快照來源不合法。");
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        PayrollEmployeeSnapshotComponentId = componentId;
        EnrollmentStatus = result.EnrollmentStatus;
        EnrollmentId = result.EnrollmentId;
        MonthlyLaborInsuredSalary = result.MonthlyLaborInsuredSalary;
        EnrollmentFrom = result.EnrollmentFrom;
        EnrollmentTo = result.EnrollmentTo;
        PolicyId = result.PolicyId;
        PolicyVersion = result.PolicyVersion;
        PolicyFrom = result.PolicyFrom;
        PolicyTo = result.PolicyTo;
        Coverage = result.Coverage;
        OrdinaryAccidentInsuranceRate = result.OrdinaryAccidentInsuranceRate;
        EmploymentInsuranceRate = result.EmploymentInsuranceRate;
        EmployeeShareRate = result.EmployeeShareRate;
        FinalEmployeeDeduction = result.FinalEmployeeDeduction;
        CalculationStatus = result.CalculationStatus;
        SourceFingerprintVersion = LaborInsuranceSourceFingerprintV2.Version;
        SourceFingerprint = result.SourceFingerprint.ToArray();
    }
    public Guid Id { get; private set; }
    public Guid PayrollEmployeeSnapshotComponentId { get; private set; }
    public LaborInsuranceEnrollmentStatus? EnrollmentStatus { get; private set; }
    public Guid? EnrollmentId { get; private set; }
    public decimal? MonthlyLaborInsuredSalary { get; private set; }
    // Immutable historical evidence only. New occupational authority is held by
    // EmployeeOccupationalInsuranceEnrollment, never by a Labor snapshot.
    public decimal? MonthlyOccupationalInsuredSalary { get; private set; }
    public decimal? MonthlyInsuredSalary => MonthlyLaborInsuredSalary;
    public DateOnly? EnrollmentFrom { get; private set; }
    public DateOnly? EnrollmentTo { get; private set; }
    public Guid? PolicyId { get; private set; }
    public string? PolicyVersion { get; private set; }
    public DateOnly? PolicyFrom { get; private set; }
    public DateOnly? PolicyTo { get; private set; }
    public LaborInsuranceCoverage Coverage { get; private set; }
    public decimal? OrdinaryAccidentInsuranceRate { get; private set; }
    public decimal? EmploymentInsuranceRate { get; private set; }
    public decimal? EmployeeShareRate { get; private set; }
    public decimal? FinalEmployeeDeduction { get; private set; }
    public PayrollCalculationStatus CalculationStatus { get; private set; }
    public short SourceFingerprintVersion { get; private set; }
    public byte[] SourceFingerprint { get; private set; } = [];
    public PayrollEmployeeSnapshotComponent PayrollEmployeeSnapshotComponent { get; private set; } = null!;
    public ICollection<PayrollLaborInsuranceContributionEvidence> Contributions { get; } =
        new List<PayrollLaborInsuranceContributionEvidence>();
}

public sealed class PayrollLaborInsuranceContributionEvidence
{
    private PayrollLaborInsuranceContributionEvidence() { }
    public PayrollLaborInsuranceContributionEvidence(Guid id, Guid snapshotId,
        LaborInsuranceContributionResult result)
    {
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        PayrollLaborInsuranceSnapshotId = snapshotId;
        Kind = result.Kind;
        Rate = result.Rate;
        EmployeeShareRate = result.EmployeeShareRate;
        RawEmployeeAmount = result.RawEmployeeAmount;
        RoundedDisplayAmount = result.RoundedDisplayAmount;
    }
    public Guid Id { get; private set; }
    public Guid PayrollLaborInsuranceSnapshotId { get; private set; }
    public LaborInsuranceContributionKind Kind { get; private set; }
    public decimal Rate { get; private set; }
    public decimal EmployeeShareRate { get; private set; }
    public decimal RawEmployeeAmount { get; private set; }
    public decimal RoundedDisplayAmount { get; private set; }
    public PayrollLaborInsuranceSnapshot PayrollLaborInsuranceSnapshot { get; private set; } = null!;
}
