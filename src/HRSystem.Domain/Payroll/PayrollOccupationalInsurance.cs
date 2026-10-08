using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using HRSystem.Domain.Common;
using HRSystem.Domain.MasterData;

namespace HRSystem.Domain.Payroll;

public sealed class EmployeeOccupationalInsuranceEnrollment
{
    private EmployeeOccupationalInsuranceEnrollment() { }

    public EmployeeOccupationalInsuranceEnrollment(Guid id, Guid employeeId,
        OccupationalInsuranceEnrollmentStatus status,
        decimal? monthlyInsuredSalary, DateOnly effectiveFrom,
        DateOnly? effectiveTo = null, bool isActive = true)
    {
        if (employeeId == Guid.Empty || !Enum.IsDefined(status))
            throw new DomainValidationException("職業災害保險投保設定不合法。");
        if (effectiveTo < effectiveFrom)
            throw new DomainValidationException("職業災害保險投保期間不合法。");
        if (monthlyInsuredSalary is <= 0)
            throw new DomainValidationException("災保月投保薪資必須大於 0。");
        if (status == OccupationalInsuranceEnrollmentStatus.Enrolled &&
            !monthlyInsuredSalary.HasValue)
            throw new DomainValidationException("已投保災保必須設定月投保薪資。");
        if (status == OccupationalInsuranceEnrollmentStatus.NotEnrolled &&
            monthlyInsuredSalary.HasValue)
            throw new DomainValidationException("明確未投保不得設定災保投保薪資。");

        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        EmployeeId = employeeId;
        Status = status;
        MonthlyInsuredSalary = monthlyInsuredSalary;
        EffectiveFrom = effectiveFrom;
        EffectiveTo = effectiveTo;
        IsActive = isActive;
    }

    public Guid Id { get; private set; }
    public Guid EmployeeId { get; private set; }
    public OccupationalInsuranceEnrollmentStatus Status { get; private set; }
    public decimal? MonthlyInsuredSalary { get; private set; }
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
            throw new DomainValidationException(
                "職業災害保險投保設定無法在指定日期前結束。");
        EffectiveTo = effectiveTo;
    }
}

public sealed record OccupationalInsuranceReadinessResult(
    OccupationalInsuranceEnrollmentStatus? EnrollmentStatus,
    Guid? EnrollmentId,
    decimal? MonthlyInsuredSalary,
    DateOnly? EnrollmentFrom,
    DateOnly? EnrollmentTo,
    int CoveredDays,
    decimal CoverageFactor,
    PayrollCalculationStatus CalculationStatus,
    byte[] SourceFingerprint);

public static class OccupationalInsuranceReadinessEvaluator
{
    public static OccupationalInsuranceReadinessResult Evaluate(
        IEnumerable<EmployeeOccupationalInsuranceEnrollment> enrollments,
        DateOnly periodStart, DateOnly periodEnd)
    {
        ArgumentNullException.ThrowIfNull(enrollments);
        _ = InsuranceCoverageDays.Calculate(periodStart, periodEnd,
            periodStart, periodEnd);
        var source = enrollments.Where(x => x.Overlaps(periodStart, periodEnd))
            .ToArray();
        var fingerprint = OccupationalInsuranceSourceFingerprintV1.Calculate(
            source, periodStart, periodEnd);
        var applicable = source.Where(x => x.Overlaps(periodStart, periodEnd))
            .ToArray();
        if (applicable.Length == 0)
            return Empty(PayrollCalculationStatus.NeedsSetup, fingerprint);
        if (applicable.Length != 1)
            return Empty(PayrollCalculationStatus.NeedsReview, fingerprint);

        var enrollment = applicable[0];
        var coverage = InsuranceCoverageDays.Calculate(periodStart, periodEnd,
            enrollment.EffectiveFrom, enrollment.EffectiveTo);
        if (enrollment.Status == OccupationalInsuranceEnrollmentStatus.NotEnrolled)
            return From(enrollment, new(0, 0m), PayrollCalculationStatus.Resolved,
                fingerprint);
        return From(enrollment, coverage,
            enrollment.MonthlyInsuredSalary.HasValue
                ? PayrollCalculationStatus.PolicyPending
                : PayrollCalculationStatus.NeedsSetup,
            fingerprint);
    }

    private static OccupationalInsuranceReadinessResult Empty(
        PayrollCalculationStatus status, byte[] fingerprint) =>
        new(null, null, null, null, null, 0, 0m, status, fingerprint);

    private static OccupationalInsuranceReadinessResult From(
        EmployeeOccupationalInsuranceEnrollment enrollment,
        InsuranceThirtyDayCoverage coverage, PayrollCalculationStatus status,
        byte[] fingerprint) => new(enrollment.Status, enrollment.Id,
            enrollment.MonthlyInsuredSalary, enrollment.EffectiveFrom,
            enrollment.EffectiveTo, coverage.CoveredDays, coverage.Factor,
            status, fingerprint);
}

public static class OccupationalInsuranceSourceFingerprintV1
{
    public const short Version = 1;

    public static byte[] Calculate(
        IEnumerable<EmployeeOccupationalInsuranceEnrollment> enrollments,
        DateOnly periodStart, DateOnly periodEnd)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, "v1");
        Append(hash, periodStart.ToString("yyyy-MM-dd"));
        Append(hash, periodEnd.ToString("yyyy-MM-dd"));
        foreach (var item in enrollments.OrderBy(x => x.Id))
            Append(hash, string.Join('|', item.Id.ToString("D"),
                ((byte)item.Status).ToString(CultureInfo.InvariantCulture),
                item.MonthlyInsuredSalary?.ToString(CultureInfo.InvariantCulture),
                item.EffectiveFrom.ToString("yyyy-MM-dd"),
                item.EffectiveTo?.ToString("yyyy-MM-dd"),
                item.IsActive ? "1" : "0"));
        return hash.GetHashAndReset();
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
