using HRSystem.Domain.Common;

namespace HRSystem.Domain.Payroll;

public sealed class PayrollComponentDefinition
{
    private PayrollComponentDefinition() { }

    public PayrollComponentDefinition(Guid id, string code, string name,
        PayrollComponentCategory category, PayrollCalculationKind calculationKind,
        bool isRecurring, int sortOrder, DateOnly effectiveFrom,
        DateOnly? effectiveTo = null, bool isActive = true,
        bool includeInOvertimeHourlyBase = false)
    {
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        Code = RequiredCode(code, 50);
        Name = RequiredText(name, "薪資項目名稱", 100);
        Category = category;
        CalculationKind = calculationKind;
        IsRecurring = isRecurring;
        IncludeInOvertimeHourlyBase = includeInOvertimeHourlyBase;
        SortOrder = sortOrder;
        EffectiveFrom = effectiveFrom;
        EffectiveTo = ValidateRange(effectiveFrom, effectiveTo);
        IsActive = isActive;
    }

    public Guid Id { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public PayrollComponentCategory Category { get; private set; }
    public PayrollCalculationKind CalculationKind { get; private set; }
    public bool IsRecurring { get; private set; }
    public bool IncludeInOvertimeHourlyBase { get; private set; }
    public bool IsActive { get; private set; }
    public int SortOrder { get; private set; }
    public DateOnly EffectiveFrom { get; private set; }
    public DateOnly? EffectiveTo { get; private set; }
    public byte[] RowVersion { get; private set; } = [];
    public ICollection<PayrollPlanComponent> PlanComponents { get; } = new List<PayrollPlanComponent>();

    public bool IsEffectiveOn(DateOnly date) => IsActive && EffectiveFrom <= date &&
        (!EffectiveTo.HasValue || EffectiveTo.Value >= date);

    internal static DateOnly? ValidateRange(DateOnly from, DateOnly? to)
    {
        if (to < from) throw new DomainValidationException("生效結束日不可早於開始日。");
        return to;
    }

    internal static decimal? ValidateAmount(decimal? value, string label)
    {
        if (value < 0) throw new DomainValidationException($"{label}不可小於 0。");
        return value;
    }

    internal static string RequiredText(string? value, string label, int maximum)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed)) throw new DomainValidationException($"{label}為必填欄位。");
        if (trimmed.Length > maximum) throw new DomainValidationException($"{label}不可超過 {maximum} 個字元。");
        return trimmed;
    }

    internal static string RequiredCode(string? value, int maximum)
    {
        var code = RequiredText(value, "代碼", maximum).ToUpperInvariant();
        if (code.Any(character => !(char.IsLetterOrDigit(character) || character == '_')))
            throw new DomainValidationException("代碼只能包含英文字母、數字與底線。");
        return code;
    }
}
