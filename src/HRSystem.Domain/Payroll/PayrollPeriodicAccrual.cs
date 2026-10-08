using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using HRSystem.Domain.Common;

namespace HRSystem.Domain.Payroll;

public sealed record PayrollPeriodicAccrualMonthResult(
    DateOnly CoveredMonth,
    Guid? PayCycleId,
    decimal? MonthlyFixedAmount,
    PayrollCalculationStatus CalculationStatus);

public sealed record PayrollPeriodicAccrualResult(
    DateOnly CoveredFrom,
    DateOnly CoveredTo,
    int CycleMonths,
    PayrollPeriodicPaymentTiming PaymentTiming,
    decimal? TotalAmount,
    PayrollCalculationStatus CalculationStatus,
    IReadOnlyList<PayrollPeriodicAccrualMonthResult> Months,
    byte[] SourceFingerprint);

public static class PayrollPeriodicAccrualCalculator
{
    public static PayrollPeriodicAccrualResult Calculate(
        EmployeePayrollPayCycle schedule,
        IEnumerable<EmployeePayrollPayCycle> settings,
        DateOnly paymentMonth)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        ArgumentNullException.ThrowIfNull(settings);
        if (schedule.Type != PayrollPayCycleType.PeriodicAccruedFixed ||
            schedule.CycleMonths is not { } cycleMonths ||
            schedule.PaymentTiming != PayrollPeriodicPaymentTiming.CycleStart)
            throw new DomainValidationException("週期累積薪資設定不完整。");

        var firstMonth = new DateOnly(paymentMonth.Year, paymentMonth.Month, 1);
        var finalMonth = firstMonth.AddMonths(cycleMonths - 1);
        var coveredTo = new DateOnly(finalMonth.Year, finalMonth.Month,
            DateTime.DaysInMonth(finalMonth.Year, finalMonth.Month));
        var all = settings.Where(x => x.Type ==
                PayrollPayCycleType.PeriodicAccruedFixed && x.IsActive)
            .ToArray();
        var months = new List<PayrollPeriodicAccrualMonthResult>(cycleMonths);
        for (var index = 0; index < cycleMonths; index++)
        {
            var month = firstMonth.AddMonths(index);
            var matches = all.Where(x => x.IsEffectiveFor(month)).ToArray();
            if (matches.Length == 0)
            {
                months.Add(new(month, null, null,
                    PayrollCalculationStatus.NeedsSetup));
                continue;
            }

            if (matches.Length != 1 || matches[0].AnchorPayMonth !=
                    schedule.AnchorPayMonth ||
                matches[0].CycleMonths != cycleMonths ||
                matches[0].PaymentTiming != schedule.PaymentTiming)
            {
                months.Add(new(month, matches.Length == 1 ? matches[0].Id : null,
                    matches.Length == 1 ? matches[0].MonthlyFixedAmount : null,
                    PayrollCalculationStatus.NeedsReview));
                continue;
            }

            months.Add(new(month, matches[0].Id,
                matches[0].MonthlyFixedAmount,
                matches[0].MonthlyFixedAmount.HasValue
                    ? PayrollCalculationStatus.Resolved
                    : PayrollCalculationStatus.NeedsSetup));
        }

        var status = months.Any(x => x.CalculationStatus ==
                PayrollCalculationStatus.NeedsReview)
            ? PayrollCalculationStatus.NeedsReview
            : months.Any(x => x.CalculationStatus != PayrollCalculationStatus.Resolved)
                ? PayrollCalculationStatus.NeedsSetup
                : PayrollCalculationStatus.Resolved;
        decimal? total = status == PayrollCalculationStatus.Resolved
            ? PayrollMoneyRoundingPolicy.RoundNtd(
                months.Sum(x => x.MonthlyFixedAmount!.Value))
            : null;
        var fingerprint = PayrollPeriodicAccrualSourceFingerprintV1.Calculate(
            schedule, firstMonth, coveredTo, months);
        return new(firstMonth, coveredTo, cycleMonths,
            schedule.PaymentTiming.Value, total, status, months, fingerprint);
    }
}

public static class PayrollPeriodicAccrualSourceFingerprintV1
{
    public const short Version = 1;

    public static byte[] Calculate(EmployeePayrollPayCycle schedule,
        DateOnly coveredFrom, DateOnly coveredTo,
        IEnumerable<PayrollPeriodicAccrualMonthResult> months)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Add(hash, "periodic-accrual-v1");
        Add(hash, schedule.EmployeeId.ToString("D"));
        Add(hash, schedule.AnchorPayMonth?.ToString("yyyy-MM-dd",
            CultureInfo.InvariantCulture));
        Add(hash, schedule.CycleMonths?.ToString(CultureInfo.InvariantCulture));
        Add(hash, schedule.PaymentTiming?.ToString());
        Add(hash, coveredFrom.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        Add(hash, coveredTo.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        foreach (var month in months.OrderBy(x => x.CoveredMonth))
        {
            Add(hash, month.CoveredMonth.ToString("yyyy-MM-dd",
                CultureInfo.InvariantCulture));
            Add(hash, month.PayCycleId?.ToString("D"));
            Add(hash, month.MonthlyFixedAmount?.ToString(
                "0.############################", CultureInfo.InvariantCulture));
            Add(hash, month.CalculationStatus.ToString());
        }
        return hash.GetHashAndReset();
    }

    public static bool IsCurrent(byte[] stored, EmployeePayrollPayCycle schedule,
        DateOnly coveredFrom, DateOnly coveredTo,
        IEnumerable<PayrollPeriodicAccrualMonthResult> months) =>
        stored is { Length: 32 } && CryptographicOperations.FixedTimeEquals(
            stored, Calculate(schedule, coveredFrom, coveredTo, months));

    private static void Add(IncrementalHash hash, string? value)
    {
        Span<byte> length = stackalloc byte[4];
        if (value is null)
        {
            BinaryPrimitives.WriteInt32BigEndian(length, -1);
            hash.AppendData(length);
            return;
        }
        var bytes = Encoding.UTF8.GetBytes(value);
        BinaryPrimitives.WriteInt32BigEndian(length, bytes.Length);
        hash.AppendData(length);
        hash.AppendData(bytes);
    }
}

public sealed class PayrollPeriodicAccrualSnapshot
{
    private PayrollPeriodicAccrualSnapshot() { }

    public PayrollPeriodicAccrualSnapshot(Guid id, Guid componentId,
        PayrollPeriodicAccrualResult result)
    {
        if (componentId == Guid.Empty || result.SourceFingerprint.Length != 32)
            throw new DomainValidationException("週期累積薪資快照來源不合法。");
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        PayrollEmployeeSnapshotComponentId = componentId;
        CoveredFrom = result.CoveredFrom;
        CoveredTo = result.CoveredTo;
        CycleMonths = result.CycleMonths;
        PaymentTiming = result.PaymentTiming;
        TotalAmount = result.TotalAmount;
        CalculationStatus = result.CalculationStatus;
        SourceFingerprintVersion = PayrollPeriodicAccrualSourceFingerprintV1.Version;
        SourceFingerprint = result.SourceFingerprint.ToArray();
        foreach (var item in result.Months)
            Months.Add(new PayrollPeriodicAccrualMonthEvidence(Guid.NewGuid(),
                Id, item));
    }

    public Guid Id { get; private set; }
    public Guid PayrollEmployeeSnapshotComponentId { get; private set; }
    public DateOnly CoveredFrom { get; private set; }
    public DateOnly CoveredTo { get; private set; }
    public int CycleMonths { get; private set; }
    public PayrollPeriodicPaymentTiming PaymentTiming { get; private set; }
    public decimal? TotalAmount { get; private set; }
    public PayrollCalculationStatus CalculationStatus { get; private set; }
    public short SourceFingerprintVersion { get; private set; }
    public byte[] SourceFingerprint { get; private set; } = [];
    public PayrollEmployeeSnapshotComponent PayrollEmployeeSnapshotComponent { get; private set; } = null!;
    public ICollection<PayrollPeriodicAccrualMonthEvidence> Months { get; } =
        new List<PayrollPeriodicAccrualMonthEvidence>();
}

public sealed class PayrollPeriodicAccrualMonthEvidence
{
    private PayrollPeriodicAccrualMonthEvidence() { }

    public PayrollPeriodicAccrualMonthEvidence(Guid id, Guid snapshotId,
        PayrollPeriodicAccrualMonthResult result)
    {
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        PayrollPeriodicAccrualSnapshotId = snapshotId;
        CoveredMonth = result.CoveredMonth;
        PayCycleId = result.PayCycleId;
        MonthlyFixedAmount = result.MonthlyFixedAmount;
        CalculationStatus = result.CalculationStatus;
    }

    public Guid Id { get; private set; }
    public Guid PayrollPeriodicAccrualSnapshotId { get; private set; }
    public DateOnly CoveredMonth { get; private set; }
    public Guid? PayCycleId { get; private set; }
    public decimal? MonthlyFixedAmount { get; private set; }
    public PayrollCalculationStatus CalculationStatus { get; private set; }
    public PayrollPeriodicAccrualSnapshot PayrollPeriodicAccrualSnapshot { get; private set; } = null!;
}
