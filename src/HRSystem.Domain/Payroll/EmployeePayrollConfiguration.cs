using HRSystem.Domain.Common;
using HRSystem.Domain.MasterData;

namespace HRSystem.Domain.Payroll;

public sealed class EmployeePayrollAssignment
{
    private EmployeePayrollAssignment() { }
    public EmployeePayrollAssignment(Guid id, Guid employeeId, Guid payrollPlanId,
        DateOnly effectiveFrom, DateOnly? effectiveTo = null, bool isActive = true)
    {
        if (employeeId == Guid.Empty || payrollPlanId == Guid.Empty)
            throw new DomainValidationException("員工與薪資方案不可空白。");
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        EmployeeId = employeeId;
        PayrollPlanId = payrollPlanId;
        EffectiveFrom = effectiveFrom;
        EffectiveTo = PayrollComponentDefinition.ValidateRange(effectiveFrom, effectiveTo);
        IsActive = isActive;
    }
    public Guid Id { get; private set; }
    public Guid EmployeeId { get; private set; }
    public Guid PayrollPlanId { get; private set; }
    public DateOnly EffectiveFrom { get; private set; }
    public DateOnly? EffectiveTo { get; private set; }
    public bool IsActive { get; private set; }
    public byte[] RowVersion { get; private set; } = [];
    public Employee Employee { get; private set; } = null!;
    public PayrollPlan PayrollPlan { get; private set; } = null!;
    public bool IsEffectiveOn(DateOnly date) => IsActive && EffectiveFrom <= date && (!EffectiveTo.HasValue || EffectiveTo.Value >= date);
}

public sealed class EmployeePayrollComponentOverride
{
    private EmployeePayrollComponentOverride() { }
    public EmployeePayrollComponentOverride(Guid id, Guid employeeId,
        Guid componentDefinitionId, decimal? overrideAmount,
        PayrollOverrideMode overrideMode, DateOnly effectiveFrom,
        DateOnly? effectiveTo = null, string? reasonCode = null)
    {
        if (employeeId == Guid.Empty || componentDefinitionId == Guid.Empty)
            throw new DomainValidationException("員工與薪資項目不可空白。");
        if ((overrideMode is PayrollOverrideMode.Replace or PayrollOverrideMode.Add) && !overrideAmount.HasValue)
            throw new DomainValidationException("Replace 或 Add 覆蓋必須提供金額。");
        if (overrideMode == PayrollOverrideMode.Disable && overrideAmount.HasValue)
            throw new DomainValidationException("Disable 覆蓋不可提供金額。");
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        EmployeeId = employeeId;
        PayrollComponentDefinitionId = componentDefinitionId;
        OverrideAmount = PayrollComponentDefinition.ValidateAmount(overrideAmount, "覆蓋金額");
        OverrideMode = overrideMode;
        EffectiveFrom = effectiveFrom;
        EffectiveTo = PayrollComponentDefinition.ValidateRange(effectiveFrom, effectiveTo);
        ReasonCode = PayrollPlan.Optional(reasonCode, 100);
    }
    public Guid Id { get; private set; }
    public Guid EmployeeId { get; private set; }
    public Guid PayrollComponentDefinitionId { get; private set; }
    public decimal? OverrideAmount { get; private set; }
    public PayrollOverrideMode OverrideMode { get; private set; }
    public DateOnly EffectiveFrom { get; private set; }
    public DateOnly? EffectiveTo { get; private set; }
    public string? ReasonCode { get; private set; }
    public byte[] RowVersion { get; private set; } = [];
    public Employee Employee { get; private set; } = null!;
    public PayrollComponentDefinition ComponentDefinition { get; private set; } = null!;
    public bool IsEffectiveOn(DateOnly date) => EffectiveFrom <= date && (!EffectiveTo.HasValue || EffectiveTo.Value >= date);
}
