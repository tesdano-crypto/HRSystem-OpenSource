using HRSystem.Domain.Approvals;
using HRSystem.Domain.Common;

namespace HRSystem.Domain.Payroll;

public sealed class PayrollFinalization
{
    private PayrollFinalization() { }

    public PayrollFinalization(Guid id, Guid payrollPeriodId, Guid approvalId,
        string approvedByUserId, string approvedByDisplayName,
        DateTimeOffset approvedAtUtc, ApprovalChannel approvalChannel,
        string finalizedByUserId, string finalizedByDisplayName,
        DateTimeOffset finalizedAtUtc, short monthFingerprintVersion,
        byte[] monthFingerprint)
    {
        if (payrollPeriodId == Guid.Empty || approvalId == Guid.Empty)
            throw new DomainValidationException("薪資月份與核准紀錄不可空白。");
        if (monthFingerprintVersion <= 0 || monthFingerprint is not { Length: 32 })
            throw new DomainValidationException("薪資月份指紋不合法。");
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        PayrollPeriodId = payrollPeriodId;
        ApprovalId = approvalId;
        ApprovedByUserId = Required(approvedByUserId, 450, "核准人");
        ApprovedByDisplayName = Required(approvedByDisplayName, 100, "核准人名稱");
        ApprovedAtUtc = approvedAtUtc.ToUniversalTime();
        ApprovalChannel = approvalChannel;
        FinalizedByUserId = Required(finalizedByUserId, 450, "結算人");
        FinalizedByDisplayName = Required(finalizedByDisplayName, 100, "結算人名稱");
        FinalizedAtUtc = finalizedAtUtc.ToUniversalTime();
        MonthFingerprintVersion = monthFingerprintVersion;
        MonthFingerprint = monthFingerprint.ToArray();
        FinalVersionNumber = 1;
        Status = PayrollFinalizationStatus.Completed;
    }

    public Guid Id { get; private set; }
    public Guid PayrollPeriodId { get; private set; }
    public int FinalVersionNumber { get; private set; }
    public Guid ApprovalId { get; private set; }
    public string ApprovedByUserId { get; private set; } = string.Empty;
    public string ApprovedByDisplayName { get; private set; } = string.Empty;
    public DateTimeOffset ApprovedAtUtc { get; private set; }
    public ApprovalChannel ApprovalChannel { get; private set; }
    public string FinalizedByUserId { get; private set; } = string.Empty;
    public string FinalizedByDisplayName { get; private set; } = string.Empty;
    public DateTimeOffset FinalizedAtUtc { get; private set; }
    public short MonthFingerprintVersion { get; private set; }
    public byte[] MonthFingerprint { get; private set; } = [];
    public int EmployeeCount { get; private set; }
    public decimal GrossPay { get; private set; }
    public decimal TotalDeductions { get; private set; }
    public decimal NetPay { get; private set; }
    public PayrollFinalizationStatus Status { get; private set; }
    public byte[] RowVersion { get; private set; } = [];
    public PayrollPeriod PayrollPeriod { get; private set; } = null!;
    public Approval Approval { get; private set; } = null!;
    public ICollection<PayrollFinalEmployeeSnapshot> EmployeeSnapshots { get; } =
        new List<PayrollFinalEmployeeSnapshot>();

    public void AddEmployee(PayrollFinalEmployeeSnapshot item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item.PayrollFinalizationId != Id)
            throw new DomainValidationException("正式薪資快照不屬於此結算。");
        if (EmployeeSnapshots.Any(x => x.EmployeeId == item.EmployeeId))
            throw new DomainValidationException("同一員工不可重複加入正式結算。");
        EmployeeSnapshots.Add(item);
        EmployeeCount++;
        GrossPay += item.GrossPay;
        TotalDeductions += item.TotalDeductions;
        NetPay += item.NetPay;
    }

    private static string Required(string? value, int maxLength, string label)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            throw new DomainValidationException($"{label}不可空白。");
        if (normalized.Length > maxLength)
            throw new DomainValidationException($"{label}不可超過 {maxLength} 個字元。");
        return normalized;
    }
}

public sealed class PayrollFinalEmployeeSnapshot
{
    private PayrollFinalEmployeeSnapshot() { }

    public PayrollFinalEmployeeSnapshot(Guid id, Guid payrollFinalizationId,
        PayrollEmployeeSnapshot source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (payrollFinalizationId == Guid.Empty || source.NetPay is null ||
            source.NetPay < 0 || source.TotalCalculationStatus != PayrollCalculationStatus.Resolved ||
            source.TotalSourceFingerprintVersion is null ||
            source.TotalSourceFingerprint is not { Length: 32 })
            throw new DomainValidationException("員工薪資快照尚未完整解析，無法正式結算。");
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        PayrollFinalizationId = payrollFinalizationId;
        EmployeeId = source.EmployeeId;
        PayrollEmployeeSnapshotId = source.Id;
        EmployeeNumber = source.EmployeeCode;
        EmployeeName = source.EmployeeName;
        DepartmentName = source.DepartmentName;
        GrossPay = source.GrossPay;
        TotalDeductions = source.TotalDeductions;
        NetPay = source.NetPay.Value;
        TotalCalculationStatus = source.TotalCalculationStatus;
        SnapshotFingerprintVersion = source.TotalSourceFingerprintVersion.Value;
        SnapshotFingerprint = source.TotalSourceFingerprint.ToArray();
    }

    public Guid Id { get; private set; }
    public Guid PayrollFinalizationId { get; private set; }
    public Guid EmployeeId { get; private set; }
    public Guid PayrollEmployeeSnapshotId { get; private set; }
    public string EmployeeNumber { get; private set; } = string.Empty;
    public string EmployeeName { get; private set; } = string.Empty;
    public string DepartmentName { get; private set; } = string.Empty;
    public decimal GrossPay { get; private set; }
    public decimal TotalDeductions { get; private set; }
    public decimal NetPay { get; private set; }
    public PayrollCalculationStatus TotalCalculationStatus { get; private set; }
    public short SnapshotFingerprintVersion { get; private set; }
    public byte[] SnapshotFingerprint { get; private set; } = [];
    public PayrollFinalization PayrollFinalization { get; private set; } = null!;
    public PayrollEmployeeSnapshot PayrollEmployeeSnapshot { get; private set; } = null!;
}
