using HRSystem.Domain.Common;
using HRSystem.Domain.MasterData;

namespace HRSystem.Domain.Payroll;

public sealed class EmployeePayrollPayCycle
{
    private EmployeePayrollPayCycle() { }

    public EmployeePayrollPayCycle(Guid id, Guid employeeId,
        PayrollPayCycleType type, DateOnly effectiveFrom,
        DateOnly? effectiveTo = null, DateOnly? anchorPayMonth = null,
        decimal? fixedPaymentAmount = null, string? reason = null,
        bool isActive = true, decimal? monthlyFixedAmount = null,
        int? cycleMonths = null,
        PayrollPeriodicPaymentTiming? paymentTiming = null)
    {
        if (employeeId == Guid.Empty)
            throw new DomainValidationException("員工不可空白。");
        if (!Enum.IsDefined(type))
            throw new DomainValidationException("發薪方式不合法。");
        if (effectiveFrom.Day != 1 || effectiveTo is { } end &&
            end.Day != DateTime.DaysInMonth(end.Year, end.Month))
            throw new DomainValidationException("發薪方式生效期間必須使用完整月份邊界。");
        if (effectiveTo < effectiveFrom)
            throw new DomainValidationException("發薪方式生效期間不合法。");

        if (type == PayrollPayCycleType.SemiannualFixed)
        {
            if (anchorPayMonth is null || anchorPayMonth.Value.Day != 1)
                throw new DomainValidationException("每 6 個月固定給付必須設定基準發薪月份。");
            if (fixedPaymentAmount is null or <= 0)
                throw new DomainValidationException("每 6 個月固定給付金額必須大於 0。");
            if (monthlyFixedAmount.HasValue || cycleMonths.HasValue ||
                paymentTiming.HasValue)
                throw new DomainValidationException("舊版每 6 個月固定給付不可混用週期累積設定。");
        }
        else if (type == PayrollPayCycleType.PeriodicAccruedFixed)
        {
            if (anchorPayMonth is null || anchorPayMonth.Value.Day != 1)
                throw new DomainValidationException("週期集中發薪必須設定基準支付月份。");
            if (fixedPaymentAmount.HasValue)
                throw new DomainValidationException("週期累積薪資不可使用舊版單期固定金額。");
            if (monthlyFixedAmount is null or <= 0)
                throw new DomainValidationException("每月固定薪資必須大於 0。");
            if (cycleMonths is < 2 or > 120)
                throw new DomainValidationException("集中支付週期必須介於 2 與 120 個月。");
            if (paymentTiming != PayrollPeriodicPaymentTiming.CycleStart)
                throw new DomainValidationException("目前僅支援週期開始月支付。");
        }
        else if (anchorPayMonth.HasValue || fixedPaymentAmount.HasValue ||
                 monthlyFixedAmount.HasValue || cycleMonths.HasValue ||
                 paymentTiming.HasValue)
        {
            throw new DomainValidationException("每月發薪不得重複設定週期固定金額或基準月份。");
        }

        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        EmployeeId = employeeId;
        Type = type;
        EffectiveFrom = effectiveFrom;
        EffectiveTo = effectiveTo;
        AnchorPayMonth = anchorPayMonth;
        FixedPaymentAmount = fixedPaymentAmount;
        MonthlyFixedAmount = monthlyFixedAmount;
        CycleMonths = cycleMonths;
        PaymentTiming = paymentTiming;
        Reason = string.IsNullOrWhiteSpace(reason) ? null :
            reason.Trim() is { Length: <= 500 } value ? value :
            throw new DomainValidationException("設定原因不可超過 500 字元。");
        IsActive = isActive;
    }

    public Guid Id { get; private set; }
    public Guid EmployeeId { get; private set; }
    public PayrollPayCycleType Type { get; private set; }
    public DateOnly EffectiveFrom { get; private set; }
    public DateOnly? EffectiveTo { get; private set; }
    public DateOnly? AnchorPayMonth { get; private set; }
    public decimal? FixedPaymentAmount { get; private set; }
    public decimal? MonthlyFixedAmount { get; private set; }
    public int? CycleMonths { get; private set; }
    public PayrollPeriodicPaymentTiming? PaymentTiming { get; private set; }
    public string? Reason { get; private set; }
    public bool IsActive { get; private set; }
    public byte[] RowVersion { get; private set; } = [];
    public Employee Employee { get; private set; } = null!;

    public bool IsEffectiveFor(DateOnly month) => IsActive &&
        EffectiveFrom <= month && (!EffectiveTo.HasValue || EffectiveTo >= month);
}

public sealed record PayrollParticipationDecision(
    PayrollParticipationStatus Status,
    PayrollPayCycleType PayCycleType,
    decimal? PeriodicFixedAmount = null)
{
    public bool IsEligible => Status == PayrollParticipationStatus.Eligible;
    public string Message => Status switch
    {
        PayrollParticipationStatus.Eligible => "本月發薪",
        PayrollParticipationStatus.NotEmployed => "不在本月任職範圍",
        PayrollParticipationStatus.InactiveWithoutTermination => "停用且無歷史離職日，不列入薪資人口",
        PayrollParticipationStatus.NonPayMonth => "本月不發薪",
        PayrollParticipationStatus.AmbiguousConfiguration => "發薪方式設定重疊，需先處理",
        _ => "不列入薪資人口"
    };
}

public static class PayrollParticipationPolicy
{
    public static PayrollParticipationDecision Evaluate(bool isActive,
        DateOnly hireDate, DateOnly? terminationDate,
        DateOnly periodStart, DateOnly periodEnd,
        IReadOnlyList<EmployeePayrollPayCycle> effectivePayCycles)
    {
        if (hireDate > periodEnd || terminationDate < periodStart)
            return new(PayrollParticipationStatus.NotEmployed,
                PayrollPayCycleType.Monthly);
        if (!isActive && !terminationDate.HasValue)
            return new(PayrollParticipationStatus.InactiveWithoutTermination,
                PayrollPayCycleType.Monthly);
        if (effectivePayCycles.Count > 1)
            return new(PayrollParticipationStatus.AmbiguousConfiguration,
                PayrollPayCycleType.Monthly);
        if (effectivePayCycles.Count == 0 ||
            effectivePayCycles[0].Type == PayrollPayCycleType.Monthly)
            return new(PayrollParticipationStatus.Eligible,
                PayrollPayCycleType.Monthly);

        var cycle = effectivePayCycles[0];
        var anchor = cycle.AnchorPayMonth!.Value;
        var months = (periodStart.Year - anchor.Year) * 12 +
            periodStart.Month - anchor.Month;
        var cycleMonths = cycle.Type == PayrollPayCycleType.SemiannualFixed
            ? 6
            : cycle.CycleMonths!.Value;
        var isPayMonth = months >= 0 && months % cycleMonths == 0;
        if (!isPayMonth)
            return new(PayrollParticipationStatus.NonPayMonth, cycle.Type);
        return cycle.Type == PayrollPayCycleType.SemiannualFixed
            ? new(PayrollParticipationStatus.Eligible, cycle.Type,
                cycle.FixedPaymentAmount)
            : new(PayrollParticipationStatus.Eligible, cycle.Type);
    }
}
