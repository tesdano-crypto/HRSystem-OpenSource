using HRSystem.Domain.Common;
using HRSystem.Domain.MasterData;

namespace HRSystem.Domain.Payroll;

public sealed class PayrollPeriod
{
    private PayrollPeriod() { }
    public PayrollPeriod(Guid id, int year, int month)
    {
        if (year is < 2000 or > 2200 || month is < 1 or > 12)
            throw new DomainValidationException("薪資年月不合法。");
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        Year = year; Month = month;
        PeriodStart = new DateOnly(year, month, 1);
        PeriodEnd = PeriodStart.AddMonths(1).AddDays(-1);
        Status = PayrollPeriodStatus.Open;
    }
    public Guid Id { get; private set; }
    public int Year { get; private set; }
    public int Month { get; private set; }
    public DateOnly PeriodStart { get; private set; }
    public DateOnly PeriodEnd { get; private set; }
    public PayrollPeriodStatus Status { get; private set; }
    public byte[] RowVersion { get; private set; } = [];
    public ICollection<PayrollRun> Runs { get; } = new List<PayrollRun>();
    public ICollection<PayrollAdjustment> Adjustments { get; } = new List<PayrollAdjustment>();
    public ICollection<PayrollPeriodEmployeeCurrentSnapshot> CurrentSnapshots { get; } =
        new List<PayrollPeriodEmployeeCurrentSnapshot>();
    public PayrollFinalization? Finalization { get; private set; }
    public void MarkDraftCreated()
    {
        if (Status == PayrollPeriodStatus.DraftCreated) return;
        if (Status != PayrollPeriodStatus.Open) throw new DomainValidationException("已結算的薪資月份不可建立 Draft。");
        Status = PayrollPeriodStatus.DraftCreated;
    }
    public void FinalizePeriod()
    {
        if (Status == PayrollPeriodStatus.Finalized)
            throw new DomainValidationException("此薪資月份已完成正式結算。");
        if (Status != PayrollPeriodStatus.DraftCreated)
            throw new DomainValidationException("薪資月份尚未建立完整試算，無法正式結算。");
        Status = PayrollPeriodStatus.Finalized;
    }
}
public sealed class PayrollRun
{
    private PayrollRun() { }
    public PayrollRun(Guid id, Guid payrollPeriodId, int revisionNumber,
        PayrollRunTrigger trigger, string createdBy, DateTimeOffset createdAtUtc)
    {
        if (payrollPeriodId == Guid.Empty) throw new DomainValidationException("薪資月份不可空白。");
        if (revisionNumber <= 0) throw new DomainValidationException("薪資試算版本必須大於 0。");
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        PayrollPeriodId = payrollPeriodId;
        RevisionNumber = revisionNumber;
        Trigger = trigger;
        CreatedBy = PayrollComponentDefinition.RequiredText(createdBy, "建立者", 450);
        CreatedAtUtc = createdAtUtc;
        Status = PayrollRunStatus.Draft;
    }
    public Guid Id { get; private set; }
    public Guid PayrollPeriodId { get; private set; }
    public int RevisionNumber { get; private set; }
    public PayrollRunTrigger Trigger { get; private set; }
    public PayrollRunStatus Status { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public string CreatedBy { get; private set; } = string.Empty;
    public DateTimeOffset? FinalizedAtUtc { get; private set; }
    public byte[] RowVersion { get; private set; } = [];
    public PayrollPeriod PayrollPeriod { get; private set; } = null!;
    public ICollection<PayrollEmployeeSnapshot> EmployeeSnapshots { get; } = new List<PayrollEmployeeSnapshot>();
}

public sealed class PayrollPeriodEmployeeCurrentSnapshot
{
    private PayrollPeriodEmployeeCurrentSnapshot() { }

    public PayrollPeriodEmployeeCurrentSnapshot(Guid payrollPeriodId, Guid employeeId,
        Guid payrollEmployeeSnapshotId, DateTimeOffset updatedAtUtc)
    {
        if (payrollPeriodId == Guid.Empty || employeeId == Guid.Empty ||
            payrollEmployeeSnapshotId == Guid.Empty)
            throw new DomainValidationException("薪資月份、員工與當前快照不可空白。");
        PayrollPeriodId = payrollPeriodId;
        EmployeeId = employeeId;
        PayrollEmployeeSnapshotId = payrollEmployeeSnapshotId;
        UpdatedAtUtc = updatedAtUtc;
        IsSourceChanged = false;
    }

    public Guid PayrollPeriodId { get; private set; }
    public Guid EmployeeId { get; private set; }
    public Guid PayrollEmployeeSnapshotId { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }
    public bool IsSourceChanged { get; private set; }
    public byte[] RowVersion { get; private set; } = [];
    public PayrollPeriod PayrollPeriod { get; private set; } = null!;
    public Employee Employee { get; private set; } = null!;
    public PayrollEmployeeSnapshot PayrollEmployeeSnapshot { get; private set; } = null!;

    public void SwitchTo(PayrollEmployeeSnapshot snapshot, DateTimeOffset updatedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.PayrollPeriodId != PayrollPeriodId || snapshot.EmployeeId != EmployeeId)
            throw new DomainValidationException("當前快照必須屬於同一薪資月份與員工。");
        PayrollEmployeeSnapshotId = snapshot.Id;
        UpdatedAtUtc = updatedAtUtc;
        IsSourceChanged = false;
    }

    public void MarkSourceChanged(DateTimeOffset updatedAtUtc)
    {
        IsSourceChanged = true;
        UpdatedAtUtc = updatedAtUtc;
    }

    public void ConfirmSourceCurrent(DateTimeOffset updatedAtUtc)
    {
        IsSourceChanged = false;
        UpdatedAtUtc = updatedAtUtc;
    }
}

public sealed class PayrollAdjustment
{
    private PayrollAdjustment() { }
    public PayrollAdjustment(Guid id, Guid payrollPeriodId, Guid employeeId,
        Guid componentDefinitionId, decimal amount, PayrollAdjustmentDirection direction,
        string reason, string createdBy, DateTimeOffset createdAtUtc)
    {
        if (payrollPeriodId == Guid.Empty || employeeId == Guid.Empty || componentDefinitionId == Guid.Empty)
            throw new DomainValidationException("薪資月份、員工與薪資項目不可空白。");
        if (amount <= 0) throw new DomainValidationException("臨時項目金額必須大於 0。");
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        PayrollPeriodId = payrollPeriodId; EmployeeId = employeeId;
        PayrollComponentDefinitionId = componentDefinitionId;
        Amount = amount; Direction = direction;
        Reason = PayrollComponentDefinition.RequiredText(reason, "調整原因", 500);
        CreatedBy = PayrollComponentDefinition.RequiredText(createdBy, "建立者", 450);
        CreatedAtUtc = createdAtUtc;
    }
    public Guid Id { get; private set; }
    public Guid PayrollPeriodId { get; private set; }
    public Guid EmployeeId { get; private set; }
    public Guid PayrollComponentDefinitionId { get; private set; }
    public decimal Amount { get; private set; }
    public PayrollAdjustmentDirection Direction { get; private set; }
    public string Reason { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public string CreatedBy { get; private set; } = string.Empty;
    public byte[] RowVersion { get; private set; } = [];
    public PayrollPeriod PayrollPeriod { get; private set; } = null!;
    public Employee Employee { get; private set; } = null!;
    public PayrollComponentDefinition ComponentDefinition { get; private set; } = null!;
    public void Update(decimal amount, PayrollAdjustmentDirection direction, string reason)
    {
        if (amount <= 0) throw new DomainValidationException("臨時項目金額必須大於 0。");
        Amount = amount; Direction = direction;
        Reason = PayrollComponentDefinition.RequiredText(reason, "調整原因", 500);
    }
}
