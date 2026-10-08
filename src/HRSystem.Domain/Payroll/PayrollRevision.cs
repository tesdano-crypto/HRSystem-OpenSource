using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace HRSystem.Domain.Payroll;

public static class PayrollEmployeeCalculationFingerprintV1
{
    public const short Version = 1;

    public static byte[] Calculate(PayrollEmployeeSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Add(hash, "payroll-employee-calculation-v1");
        Add(hash, snapshot.PayrollPeriodId);
        Add(hash, snapshot.EmployeeId);
        Add(hash, snapshot.PayrollPlanId);
        Add(hash, snapshot.PayrollPlanCode);
        Add(hash, snapshot.EmploymentStart);
        Add(hash, snapshot.EmploymentEnd);
        Add(hash, snapshot.SetupStatus.ToString());

        foreach (var component in snapshot.Components
                     .OrderBy(x => x.ComponentCode, StringComparer.Ordinal)
                     .ThenBy(x => x.SourceType)
                     .ThenBy(x => x.SourceId))
        {
            Add(hash, component.PayrollComponentDefinitionId);
            Add(hash, component.ComponentCode);
            Add(hash, component.Category.ToString());
            Add(hash, component.SourceType.ToString());
            Add(hash, component.SourceId);
            Add(hash, component.StandardAmount);
            Add(hash, component.OverrideAmount);
            Add(hash, component.ResolvedAmount);
            Add(hash, component.CalculationStatus.ToString());
            Add(hash, component.ProrationKind.ToString());
            Add(hash, component.FullMonthlyAmount);
            Add(hash, component.PayableDays);
            Add(hash, component.ProrationFactor);
            Add(hash, component.RawProratedAmount);
            Add(hash, component.EffectiveSourceDate);
            Add(hash, component.AttendanceAllowanceSnapshot?.SourceFingerprint);
            Add(hash, component.LeaveDeductionSnapshot?.SourceFingerprint);
            Add(hash, component.LaborInsuranceSnapshot?.SourceFingerprint);
            Add(hash, component.HealthInsuranceSnapshot?.SourceFingerprint);
            Add(hash, component.PeriodicAccrualSnapshot?.SourceFingerprint);
        }

        Add(hash, snapshot.OvertimePaySnapshot?.SourceFingerprint);
        Add(hash, snapshot.TotalCalculationStatus.ToString());
        Add(hash, snapshot.GrossPay);
        Add(hash, snapshot.TotalDeductions);
        Add(hash, snapshot.NetPay);
        Add(hash, snapshot.TotalSourceFingerprint);
        return hash.GetHashAndReset();
    }

    private static void Add(IncrementalHash hash, Guid value) => Add(hash, value.ToString("D"));
    private static void Add(IncrementalHash hash, Guid? value) => Add(hash, value?.ToString("D"));
    private static void Add(IncrementalHash hash, DateOnly? value) =>
        Add(hash, value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
    private static void Add(IncrementalHash hash, decimal? value) =>
        Add(hash, value?.ToString("0.############################", CultureInfo.InvariantCulture));
    private static void Add(IncrementalHash hash, int? value) =>
        Add(hash, value?.ToString(CultureInfo.InvariantCulture));
    private static void Add(IncrementalHash hash, byte[]? value) =>
        AddBytes(hash, value);
    private static void Add(IncrementalHash hash, string? value) =>
        AddBytes(hash, value is null ? null : Encoding.UTF8.GetBytes(value));

    private static void AddBytes(IncrementalHash hash, byte[]? value)
    {
        Span<byte> length = stackalloc byte[4];
        if (value is null)
        {
            BinaryPrimitives.WriteInt32BigEndian(length, -1);
            hash.AppendData(length);
            return;
        }
        BinaryPrimitives.WriteInt32BigEndian(length, value.Length);
        hash.AppendData(length);
        hash.AppendData(value);
    }
}

public sealed record PayrollMonthFingerprintItem(
    Guid EmployeeId,
    Guid PayrollEmployeeSnapshotId,
    byte[]? TotalSourceFingerprint,
    decimal GrossPay,
    decimal TotalDeductions,
    decimal? NetPay,
    PayrollCalculationStatus Status,
    bool IsSourceChanged);

public static class PayrollMonthFingerprintV1
{
    public const short Version = 1;

    public static byte[] Calculate(Guid payrollPeriodId,
        IEnumerable<PayrollMonthFingerprintItem> items)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, "payroll-month-current-set-v1");
        Append(hash, payrollPeriodId.ToString("D"));
        foreach (var item in items.OrderBy(x => x.EmployeeId))
        {
            Append(hash, item.EmployeeId.ToString("D"));
            Append(hash, item.PayrollEmployeeSnapshotId.ToString("D"));
            Append(hash, item.TotalSourceFingerprint is null
                ? null : Convert.ToHexString(item.TotalSourceFingerprint));
            Append(hash, item.GrossPay.ToString(CultureInfo.InvariantCulture));
            Append(hash, item.TotalDeductions.ToString(CultureInfo.InvariantCulture));
            Append(hash, item.NetPay?.ToString(CultureInfo.InvariantCulture));
            Append(hash, item.Status.ToString());
            Append(hash, item.IsSourceChanged ? "1" : "0");
        }
        return hash.GetHashAndReset();
    }

    private static void Append(IncrementalHash hash, string? value)
    {
        var bytes = value is null ? null : Encoding.UTF8.GetBytes(value);
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, bytes?.Length ?? -1);
        hash.AppendData(length);
        if (bytes is not null) hash.AppendData(bytes);
    }
}
