using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using HRSystem.Domain.Attendance;
using HRSystem.Domain.Common;
using HRSystem.Domain.Overtime;

namespace HRSystem.Domain.Payroll;

public sealed class OvertimePayRatePolicy
{
    private OvertimePayRatePolicy() { }

    public OvertimePayRatePolicy(Guid id, string version, OvertimePayBucket bucket,
        decimal multiplier, DateOnly effectiveFrom, DateOnly? effectiveTo = null,
        bool isActive = true)
    {
        if (!Enum.IsDefined(bucket) || multiplier <= 0)
            throw new DomainValidationException("加班費倍率政策不合法。");
        if (effectiveTo < effectiveFrom)
            throw new DomainValidationException("加班費倍率政策期間不合法。");
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        Version = PayrollComponentDefinition.RequiredText(version, "倍率版本", 50);
        Bucket = bucket;
        Multiplier = multiplier;
        EffectiveFrom = effectiveFrom;
        EffectiveTo = effectiveTo;
        IsActive = isActive;
    }

    public Guid Id { get; private set; }
    public string Version { get; private set; } = string.Empty;
    public OvertimePayBucket Bucket { get; private set; }
    public decimal Multiplier { get; private set; }
    public DateOnly EffectiveFrom { get; private set; }
    public DateOnly? EffectiveTo { get; private set; }
    public bool IsActive { get; private set; }
    public byte[] RowVersion { get; private set; } = [];
    public bool IsEffectiveOn(DateOnly date) => IsActive && EffectiveFrom <= date &&
        (!EffectiveTo.HasValue || EffectiveTo.Value >= date);
}

public sealed record OvertimeBaseComponentInput(
    Guid ComponentDefinitionId,
    string Code,
    decimal? FullMonthlyAmount,
    bool IncludeInOvertimeHourlyBase,
    Guid SourceId,
    DateOnly EffectiveSourceDate);

public sealed record PayrollOvertimeRecognitionInput(
    Guid RecognitionId,
    Guid OvertimeRequestId,
    DateOnly WorkDate,
    DateTime? RecognizedStartAt,
    DateTime? RecognizedEndAt,
    int? RecognizedMinutes,
    OvertimeRecognitionStatus Status,
    bool IsCurrent,
    byte[] SourceFingerprint,
    AttendanceCalendarClassification? CalendarClassification,
    bool IsWithinEmployment);

public sealed record PayrollOvertimeDayResult(
    DateOnly WorkDate,
    int RecognizedMinutes,
    int FirstTwoHoursMinutes,
    int AfterTwoHoursMinutes,
    int AfterEightHoursMinutes);

public sealed record PayrollOvertimeBucketResult(
    OvertimePayBucket Bucket,
    int Minutes,
    decimal Multiplier,
    decimal RawPay,
    decimal FinalPay,
    string RatePolicyVersion,
    Guid RatePolicyId);

public sealed record PayrollOvertimePayResult(
    decimal? MonthlyOvertimeBase,
    decimal? HourlyBase,
    int TotalRecognizedMinutes,
    decimal? TotalOvertimePay,
    PayrollCalculationStatus CalculationStatus,
    IReadOnlyList<OvertimeBaseComponentInput> IncludedComponents,
    IReadOnlyList<PayrollOvertimeBucketResult> Buckets,
    IReadOnlyList<PayrollOvertimeDayResult> Days,
    IReadOnlyList<PayrollOvertimeRecognitionInput> Recognitions,
    byte[] SourceFingerprint);

public static class PayrollOvertimePayCalculator
{
    public static PayrollOvertimePayResult Calculate(
        IReadOnlyList<OvertimeBaseComponentInput> components,
        IReadOnlyList<PayrollOvertimeRecognitionInput> recognitions,
        IReadOnlyList<OvertimePayRatePolicy> ratePolicies,
        DateOnly periodStart,
        DateOnly periodEnd,
        bool hasApprovedRequestWithoutRecognition = false)
    {
        ArgumentNullException.ThrowIfNull(components);
        ArgumentNullException.ThrowIfNull(recognitions);
        ArgumentNullException.ThrowIfNull(ratePolicies);

        var included = components.Where(x => x.IncludeInOvertimeHourlyBase)
            .OrderBy(x => x.Code, StringComparer.Ordinal).ToArray();
        var fingerprint = PayrollOvertimeSourceFingerprintV1.Calculate(
            included, recognitions, ratePolicies);
        if (included.Any(x => !x.FullMonthlyAmount.HasValue))
            return Empty(PayrollCalculationStatus.NeedsSetup, included,
                recognitions, fingerprint);

        var relevant = recognitions.Where(x => x.WorkDate >= periodStart &&
            x.WorkDate <= periodEnd).OrderBy(x => x.WorkDate)
            .ThenBy(x => x.RecognizedStartAt).ToArray();
        var invalid = relevant.Any(x => x.Status != OvertimeRecognitionStatus.Confirmed ||
            !x.IsCurrent || !x.IsWithinEmployment || x.RecognizedMinutes is null);
        if (invalid || hasApprovedRequestWithoutRecognition || HasOverlap(relevant))
            return Empty(PayrollCalculationStatus.NeedsReview, included,
                relevant, fingerprint);
        if (relevant.Any(x => x.CalendarClassification is not
            (AttendanceCalendarClassification.WorkingDay or
             AttendanceCalendarClassification.ExceptionalWorkingDay or
             AttendanceCalendarClassification.FallbackWorkingDay)))
            return Empty(PayrollCalculationStatus.PolicyPending, included,
                relevant, fingerprint);

        var rates = ratePolicies.Where(x => x.IsActive &&
                x.EffectiveFrom <= periodEnd &&
                (!x.EffectiveTo.HasValue || x.EffectiveTo >= periodStart))
            .GroupBy(x => x.Bucket).ToDictionary(x => x.Key, x => x.ToArray());
        if (Enum.GetValues<OvertimePayBucket>().Any(bucket =>
                !rates.TryGetValue(bucket, out var matches) || matches.Length != 1))
            return Empty(PayrollCalculationStatus.PolicyPending, included,
                relevant, fingerprint);

        var monthlyBase = included.Sum(x => x.FullMonthlyAmount!.Value);
        var hourlyBase = monthlyBase / 30m / 8m;
        var days = relevant.GroupBy(x => x.WorkDate).Select(group =>
        {
            var total = group.Sum(x => x.RecognizedMinutes ?? 0);
            var first = Math.Min(total, 120);
            var afterTwo = Math.Min(Math.Max(total - 120, 0), 360);
            var afterEight = Math.Max(total - 480, 0);
            return new PayrollOvertimeDayResult(group.Key, total, first,
                afterTwo, afterEight);
        }).OrderBy(x => x.WorkDate).ToArray();

        var minutes = new Dictionary<OvertimePayBucket, int>
        {
            [OvertimePayBucket.FirstTwoHours] = days.Sum(x => x.FirstTwoHoursMinutes),
            [OvertimePayBucket.AfterTwoHours] = days.Sum(x => x.AfterTwoHoursMinutes),
            [OvertimePayBucket.AfterEightHours] = days.Sum(x => x.AfterEightHoursMinutes)
        };
        var buckets = Enum.GetValues<OvertimePayBucket>().Select(bucket =>
        {
            var policy = rates[bucket][0];
            var raw = hourlyBase * policy.Multiplier * minutes[bucket] / 60m;
            return new PayrollOvertimeBucketResult(bucket, minutes[bucket],
                policy.Multiplier, raw, PayrollMoneyRoundingPolicy.RoundNtd(raw),
                policy.Version, policy.Id);
        }).ToArray();
        return new(monthlyBase, hourlyBase, days.Sum(x => x.RecognizedMinutes),
            buckets.Sum(x => x.FinalPay), PayrollCalculationStatus.Resolved,
            included, buckets, days, relevant, fingerprint);
    }

    private static PayrollOvertimePayResult Empty(PayrollCalculationStatus status,
        IReadOnlyList<OvertimeBaseComponentInput> components,
        IReadOnlyList<PayrollOvertimeRecognitionInput> recognitions,
        byte[] fingerprint) => new(null, null, 0, null, status, components,
            [], [], recognitions, fingerprint);

    private static bool HasOverlap(IReadOnlyList<PayrollOvertimeRecognitionInput> items)
    {
        foreach (var group in items.Where(x => x.RecognizedMinutes > 0)
                     .GroupBy(x => x.WorkDate))
        {
            var ordered = group.OrderBy(x => x.RecognizedStartAt).ToArray();
            for (var index = 1; index < ordered.Length; index++)
                if (ordered[index - 1].RecognizedEndAt > ordered[index].RecognizedStartAt)
                    return true;
        }
        return false;
    }
}

public static class PayrollOvertimeSourceFingerprintV1
{
    public const short Version = 1;

    public static byte[] Calculate(
        IEnumerable<OvertimeBaseComponentInput> components,
        IEnumerable<PayrollOvertimeRecognitionInput> recognitions,
        IEnumerable<OvertimePayRatePolicy> policies)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, "v1");
        foreach (var item in components.OrderBy(x => x.Code, StringComparer.Ordinal))
            Append(hash, string.Join('|', item.ComponentDefinitionId.ToString("D"),
                item.Code, item.FullMonthlyAmount?.ToString(CultureInfo.InvariantCulture),
                item.SourceId.ToString("D"), item.EffectiveSourceDate.ToString("yyyy-MM-dd")));
        foreach (var item in recognitions.OrderBy(x => x.WorkDate)
                     .ThenBy(x => x.RecognitionId))
            Append(hash, string.Join('|', item.RecognitionId.ToString("D"),
                item.OvertimeRequestId.ToString("D"), item.WorkDate.ToString("yyyy-MM-dd"),
                item.RecognizedStartAt?.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff"),
                item.RecognizedEndAt?.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff"),
                item.RecognizedMinutes?.ToString(CultureInfo.InvariantCulture),
                ((byte)item.Status).ToString(CultureInfo.InvariantCulture),
                item.IsCurrent ? "1" : "0", Convert.ToHexString(item.SourceFingerprint)));
        foreach (var item in policies.OrderBy(x => x.Bucket))
            Append(hash, string.Join('|', item.Id.ToString("D"), item.Version,
                ((byte)item.Bucket).ToString(CultureInfo.InvariantCulture),
                item.Multiplier.ToString(CultureInfo.InvariantCulture),
                item.EffectiveFrom.ToString("yyyy-MM-dd"), item.EffectiveTo?.ToString("yyyy-MM-dd")));
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
