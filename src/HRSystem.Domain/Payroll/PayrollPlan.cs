using HRSystem.Domain.Common;

namespace HRSystem.Domain.Payroll;

public sealed class PayrollPlan
{
    private PayrollPlan() { }
    public PayrollPlan(Guid id, string code, string name, string? description,
        DateOnly effectiveFrom, DateOnly? effectiveTo = null, bool isActive = true)
    {
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        Code = PayrollComponentDefinition.RequiredCode(code, 50);
        Name = PayrollComponentDefinition.RequiredText(name, "薪資方案名稱", 100);
        Description = Optional(description, 500);
        EffectiveFrom = effectiveFrom;
        EffectiveTo = PayrollComponentDefinition.ValidateRange(effectiveFrom, effectiveTo);
        IsActive = isActive;
    }
    public Guid Id { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public DateOnly EffectiveFrom { get; private set; }
    public DateOnly? EffectiveTo { get; private set; }
    public bool IsActive { get; private set; }
    public byte[] RowVersion { get; private set; } = [];
    public ICollection<PayrollPlanComponent> Components { get; } = new List<PayrollPlanComponent>();
    public ICollection<EmployeePayrollAssignment> EmployeeAssignments { get; } = new List<EmployeePayrollAssignment>();
    public bool IsEffectiveOn(DateOnly date) => IsActive && EffectiveFrom <= date && (!EffectiveTo.HasValue || EffectiveTo.Value >= date);
    internal static string? Optional(string? value, int maximum)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed)) return null;
        if (trimmed.Length > maximum) throw new DomainValidationException($"文字不可超過 {maximum} 個字元。");
        return trimmed;
    }
}

public sealed class PayrollPlanComponent
{
    private PayrollPlanComponent() { }
    public PayrollPlanComponent(Guid id, Guid payrollPlanId, Guid componentDefinitionId,
        decimal? defaultAmount, PayrollRuleKind ruleKind, DateOnly effectiveFrom,
        DateOnly? effectiveTo = null,
        PayrollProrationKind prorationKind = PayrollProrationKind.None)
    {
        if (payrollPlanId == Guid.Empty || componentDefinitionId == Guid.Empty)
            throw new DomainValidationException("薪資方案與項目不可空白。");
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        PayrollPlanId = payrollPlanId;
        PayrollComponentDefinitionId = componentDefinitionId;
        DefaultAmount = PayrollComponentDefinition.ValidateAmount(defaultAmount, "預設金額");
        RuleKind = ruleKind;
        ProrationKind = prorationKind;
        EffectiveFrom = effectiveFrom;
        EffectiveTo = PayrollComponentDefinition.ValidateRange(effectiveFrom, effectiveTo);
    }
    public Guid Id { get; private set; }
    public Guid PayrollPlanId { get; private set; }
    public Guid PayrollComponentDefinitionId { get; private set; }
    public decimal? DefaultAmount { get; private set; }
    public PayrollRuleKind RuleKind { get; private set; }
    public PayrollProrationKind ProrationKind { get; private set; }
    public DateOnly EffectiveFrom { get; private set; }
    public DateOnly? EffectiveTo { get; private set; }
    public byte[] RowVersion { get; private set; } = [];
    public PayrollPlan PayrollPlan { get; private set; } = null!;
    public PayrollComponentDefinition ComponentDefinition { get; private set; } = null!;
    public ICollection<PayrollSeniorityTier> SeniorityTiers { get; } = new List<PayrollSeniorityTier>();
    public bool IsEffectiveOn(DateOnly date) => EffectiveFrom <= date && (!EffectiveTo.HasValue || EffectiveTo.Value >= date);
}

public sealed class PayrollSeniorityTier
{
    private PayrollSeniorityTier() { }
    public PayrollSeniorityTier(Guid id, Guid payrollPlanComponentId,
        int minMonthsInclusive, int? maxMonthsExclusive, decimal amount)
    {
        if (payrollPlanComponentId == Guid.Empty) throw new DomainValidationException("薪資方案項目不可空白。");
        if (minMonthsInclusive < 0 || maxMonthsExclusive <= minMonthsInclusive)
            throw new DomainValidationException("年資級距不合法。");
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        PayrollPlanComponentId = payrollPlanComponentId;
        MinMonthsInclusive = minMonthsInclusive;
        MaxMonthsExclusive = maxMonthsExclusive;
        Amount = PayrollComponentDefinition.ValidateAmount(amount, "級距金額")!.Value;
    }
    public Guid Id { get; private set; }
    public Guid PayrollPlanComponentId { get; private set; }
    public int MinMonthsInclusive { get; private set; }
    public int? MaxMonthsExclusive { get; private set; }
    public decimal Amount { get; private set; }
    public PayrollPlanComponent PayrollPlanComponent { get; private set; } = null!;
    public bool Contains(int months) => months >= MinMonthsInclusive && (!MaxMonthsExclusive.HasValue || months < MaxMonthsExclusive.Value);

    public static void ValidateNoOverlap(IEnumerable<PayrollSeniorityTier> tiers)
    {
        var ordered = tiers.OrderBy(x => x.MinMonthsInclusive).ToArray();
        for (var index = 1; index < ordered.Length; index++)
        {
            var previousEnd = ordered[index - 1].MaxMonthsExclusive;
            if (!previousEnd.HasValue || previousEnd.Value > ordered[index].MinMonthsInclusive)
                throw new DomainValidationException("年資績效級距不可重疊。");
        }
    }
}
