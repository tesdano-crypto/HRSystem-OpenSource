using HRSystem.Domain.Common;
using HRSystem.Domain.MasterData;

namespace HRSystem.Domain.Payroll;

public sealed class PayrollEmployeeSnapshot
{
    private PayrollEmployeeSnapshot() { }
    public PayrollEmployeeSnapshot(Guid id, Guid payrollPeriodId, Guid payrollRunId, Guid employeeId,
        string employeeCode, string employeeName, Guid? departmentId,
        string departmentName, DateOnly? employmentStart, DateOnly? employmentEnd,
        Guid? payrollPlanId, string? payrollPlanCode, DateTimeOffset snapshotAtUtc,
        PayrollEmployeeSetupStatus setupStatus)
    {
        if (payrollPeriodId == Guid.Empty || payrollRunId == Guid.Empty || employeeId == Guid.Empty)
            throw new DomainValidationException("薪資月份、批次與員工不可空白。");
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        PayrollPeriodId = payrollPeriodId; PayrollRunId = payrollRunId; EmployeeId = employeeId;
        EmployeeCode = PayrollComponentDefinition.RequiredText(employeeCode, "員工編號", 20);
        EmployeeName = PayrollComponentDefinition.RequiredText(employeeName, "員工姓名", 100);
        DepartmentId = departmentId;
        DepartmentName = PayrollComponentDefinition.RequiredText(departmentName, "部門名稱", 100);
        EmploymentStart = employmentStart; EmploymentEnd = employmentEnd;
        PayrollPlanId = payrollPlanId; PayrollPlanCode = PayrollPlan.Optional(payrollPlanCode, 50);
        SnapshotAtUtc = snapshotAtUtc; SetupStatus = setupStatus;
    }
    public Guid Id { get; private set; }
    public Guid PayrollPeriodId { get; private set; }
    public Guid PayrollRunId { get; private set; }
    public Guid EmployeeId { get; private set; }
    public string EmployeeCode { get; private set; } = string.Empty;
    public string EmployeeName { get; private set; } = string.Empty;
    public Guid? DepartmentId { get; private set; }
    public string DepartmentName { get; private set; } = string.Empty;
    public DateOnly? EmploymentStart { get; private set; }
    public DateOnly? EmploymentEnd { get; private set; }
    public Guid? PayrollPlanId { get; private set; }
    public string? PayrollPlanCode { get; private set; }
    public DateTimeOffset SnapshotAtUtc { get; private set; }
    public PayrollEmployeeSetupStatus SetupStatus { get; private set; }
    public decimal GrossPay { get; private set; }
    public decimal TotalDeductions { get; private set; }
    public decimal? NetPay { get; private set; }
    public PayrollCalculationStatus TotalCalculationStatus { get; private set; } =
        PayrollCalculationStatus.NotCalculated;
    public DateTimeOffset? TotalsCalculatedAtUtc { get; private set; }
    public int BlockingComponentCount { get; private set; }
    public short? TotalSourceFingerprintVersion { get; private set; }
    public byte[]? TotalSourceFingerprint { get; private set; }
    public byte[] RowVersion { get; private set; } = [];
    public PayrollRun PayrollRun { get; private set; } = null!;
    public PayrollPeriod PayrollPeriod { get; private set; } = null!;
    public Employee Employee { get; private set; } = null!;
    public ICollection<PayrollEmployeeSnapshotComponent> Components { get; } = new List<PayrollEmployeeSnapshotComponent>();
    public ICollection<PayrollTotalBlockingEvidence> TotalBlockingEvidence { get; } =
        new List<PayrollTotalBlockingEvidence>();
    public PayrollOvertimePaySnapshot? OvertimePaySnapshot { get; private set; }

    public void AttachOvertimePay(PayrollOvertimePaySnapshot snapshot)
    {
        if (snapshot.PayrollEmployeeSnapshotId != Id)
            throw new DomainValidationException("加班費證據必須屬於此員工薪資快照。");
        OvertimePaySnapshot = snapshot;
    }

    public void ApplyTotals(PayrollTotalCalculationResult result,
        DateTimeOffset calculatedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (TotalsCalculatedAtUtc.HasValue)
            throw new DomainValidationException("薪資總額快照不可覆寫。");
        GrossPay = result.KnownGrossPay;
        TotalDeductions = result.KnownDeductions;
        NetPay = result.NetPay;
        TotalCalculationStatus = result.CalculationStatus;
        TotalsCalculatedAtUtc = calculatedAtUtc;
        BlockingComponentCount = result.BlockingItems.Count;
        TotalSourceFingerprintVersion = PayrollTotalFingerprintV1.Version;
        TotalSourceFingerprint = result.SourceFingerprint.ToArray();
        foreach (var item in result.BlockingItems)
            TotalBlockingEvidence.Add(new PayrollTotalBlockingEvidence(
                Guid.NewGuid(), Id, item.ComponentDefinitionId,
                item.ComponentCode, item.ComponentStatus, item.Reason));
    }
}

public sealed class PayrollEmployeeSnapshotComponent
{
    private PayrollEmployeeSnapshotComponent() { }
    public PayrollEmployeeSnapshotComponent(Guid id, Guid payrollEmployeeSnapshotId,
        Guid componentDefinitionId, string componentCode, string componentName,
        PayrollComponentCategory category, PayrollSnapshotSourceType sourceType,
        Guid sourceId, decimal? standardAmount, decimal? overrideAmount,
        decimal? resolvedAmount, PayrollCalculationStatus calculationStatus,
        DateOnly? effectiveSourceDate,
        PayrollProrationKind prorationKind = PayrollProrationKind.None,
        decimal? fullMonthlyAmount = null, int? payableDays = null,
        decimal? prorationFactor = null, decimal? rawProratedAmount = null)
    {
        if (payrollEmployeeSnapshotId == Guid.Empty || componentDefinitionId == Guid.Empty || sourceId == Guid.Empty)
            throw new DomainValidationException("Snapshot、薪資項目與來源不可空白。");
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        PayrollEmployeeSnapshotId = payrollEmployeeSnapshotId;
        PayrollComponentDefinitionId = componentDefinitionId;
        ComponentCode = PayrollComponentDefinition.RequiredCode(componentCode, 50);
        ComponentName = PayrollComponentDefinition.RequiredText(componentName, "薪資項目名稱", 100);
        Category = category; SourceType = sourceType; SourceId = sourceId;
        StandardAmount = PayrollComponentDefinition.ValidateAmount(standardAmount, "標準金額");
        OverrideAmount = PayrollComponentDefinition.ValidateAmount(overrideAmount, "覆蓋金額");
        ResolvedAmount = PayrollComponentDefinition.ValidateAmount(resolvedAmount, "解析金額");
        FullMonthlyAmount = PayrollComponentDefinition.ValidateAmount(
            fullMonthlyAmount, "完整月額");
        RawProratedAmount = PayrollComponentDefinition.ValidateAmount(
            rawProratedAmount, "比例原始金額");
        if (payableDays is < 0 or > 31)
            throw new DomainValidationException("計薪日數必須介於 0 與 31 之間。");
        if (prorationFactor is < 0 or > 1)
            throw new DomainValidationException("計薪比例必須介於 0 與 1 之間。");
        ProrationKind = prorationKind;
        PayableDays = payableDays;
        ProrationFactor = prorationFactor;
        CalculationStatus = calculationStatus; EffectiveSourceDate = effectiveSourceDate;
    }
    public Guid Id { get; private set; }
    public Guid PayrollEmployeeSnapshotId { get; private set; }
    public Guid PayrollComponentDefinitionId { get; private set; }
    public string ComponentCode { get; private set; } = string.Empty;
    public string ComponentName { get; private set; } = string.Empty;
    public PayrollComponentCategory Category { get; private set; }
    public PayrollSnapshotSourceType SourceType { get; private set; }
    public Guid SourceId { get; private set; }
    public decimal? StandardAmount { get; private set; }
    public decimal? OverrideAmount { get; private set; }
    public decimal? ResolvedAmount { get; private set; }
    public PayrollProrationKind ProrationKind { get; private set; }
    public decimal? FullMonthlyAmount { get; private set; }
    public int? PayableDays { get; private set; }
    public decimal? ProrationFactor { get; private set; }
    public decimal? RawProratedAmount { get; private set; }
    public PayrollCalculationStatus CalculationStatus { get; private set; }
    public DateOnly? EffectiveSourceDate { get; private set; }
    public PayrollEmployeeSnapshot PayrollEmployeeSnapshot { get; private set; } = null!;
    public PayrollComponentDefinition ComponentDefinition { get; private set; } = null!;
    public PayrollAttendanceAllowanceSnapshot? AttendanceAllowanceSnapshot { get; private set; }
    public PayrollLeaveDeductionSnapshot? LeaveDeductionSnapshot { get; private set; }
    public PayrollLaborInsuranceSnapshot? LaborInsuranceSnapshot { get; private set; }
    public PayrollHealthInsuranceSnapshot? HealthInsuranceSnapshot { get; private set; }
    public PayrollPeriodicAccrualSnapshot? PeriodicAccrualSnapshot { get; private set; }

    public void AttachAttendanceAllowance(PayrollAttendanceAllowanceSnapshot snapshot)
    {
        if (snapshot.PayrollEmployeeSnapshotComponentId != Id)
            throw new DomainValidationException("出席補貼證據必須屬於此薪資項目。");
        AttendanceAllowanceSnapshot = snapshot;
    }

    public void AttachLeaveDeduction(PayrollLeaveDeductionSnapshot snapshot)
    {
        if (snapshot.PayrollEmployeeSnapshotComponentId != Id)
            throw new DomainValidationException("請假扣薪證據必須屬於此薪資項目。");
        LeaveDeductionSnapshot = snapshot;
    }

    public void AttachLaborInsurance(PayrollLaborInsuranceSnapshot snapshot)
    {
        if (snapshot.PayrollEmployeeSnapshotComponentId != Id)
            throw new DomainValidationException("勞保證據必須屬於此薪資項目。");
        LaborInsuranceSnapshot = snapshot;
    }

    public void AttachHealthInsurance(PayrollHealthInsuranceSnapshot snapshot)
    {
        if (snapshot.PayrollEmployeeSnapshotComponentId != Id)
            throw new DomainValidationException("健保證據必須屬於此薪資項目。");
        HealthInsuranceSnapshot = snapshot;
    }

    public void AttachPeriodicAccrual(PayrollPeriodicAccrualSnapshot snapshot)
    {
        if (snapshot.PayrollEmployeeSnapshotComponentId != Id)
            throw new DomainValidationException("週期累積薪資證據必須屬於此薪資項目。");
        PeriodicAccrualSnapshot = snapshot;
    }
}
