using HRSystem.Domain.Common;
using HRSystem.Domain.MasterData;

namespace HRSystem.Domain.AnnualLeave;

public sealed class AnnualLeaveEntitlement
{
    private AnnualLeaveEntitlement() { }

    public AnnualLeaveEntitlement(
        Guid id,
        Guid employeeId,
        AnnualLeaveMilestone milestone,
        int completedServiceYears,
        DateOnly grantedDate,
        DateOnly periodStart,
        DateOnly periodEnd,
        decimal grantedDays,
        int grantedMinutes,
        DateTimeOffset nowUtc)
    {
        if (employeeId == Guid.Empty) throw new DomainValidationException("員工為必填欄位。");
        if (completedServiceYears < 0) throw new DomainValidationException("完成年資不可小於零。");
        if (periodEnd < periodStart) throw new DomainValidationException("額度有效期間不正確。");
        if (grantedDays <= 0 || grantedMinutes <= 0) throw new DomainValidationException("核發額度必須大於零。");

        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        EmployeeId = employeeId;
        Milestone = milestone;
        CompletedServiceYears = completedServiceYears;
        GrantedDate = grantedDate;
        PeriodStart = periodStart;
        PeriodEnd = periodEnd;
        GrantedDays = grantedDays;
        GrantedMinutes = grantedMinutes;
        Status = AnnualLeaveEntitlementStatus.Open;
        CreatedAtUtc = nowUtc.ToUniversalTime();
        UpdatedAtUtc = CreatedAtUtc;
    }

    public Guid Id { get; private set; }
    public Guid EmployeeId { get; private set; }
    public AnnualLeaveMilestone Milestone { get; private set; }
    public int CompletedServiceYears { get; private set; }
    public DateOnly GrantedDate { get; private set; }
    public DateOnly PeriodStart { get; private set; }
    public DateOnly PeriodEnd { get; private set; }
    public decimal GrantedDays { get; private set; }
    public int GrantedMinutes { get; private set; }
    public int CarriedInMinutes { get; private set; }
    public int ReservedMinutes { get; private set; }
    public int ConsumedMinutes { get; private set; }
    public int SettledMinutes { get; private set; }
    public AnnualLeaveEntitlementStatus Status { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }
    public byte[] RowVersion { get; private set; } = [];
    public Employee Employee { get; private set; } = null!;
    public ICollection<AnnualLeaveAllocation> Allocations { get; } = new List<AnnualLeaveAllocation>();

    public int AvailableMinutes =>
        GrantedMinutes + CarriedInMinutes - ReservedMinutes - ConsumedMinutes - SettledMinutes;

    public void Reserve(int minutes, DateTimeOffset nowUtc)
    {
        EnsurePositive(minutes);
        if (Status is not (AnnualLeaveEntitlementStatus.Open or AnnualLeaveEntitlementStatus.ExpiredPendingSettlement))
            throw new DomainValidationException("此特休額度不可再配置。");
        if (minutes > AvailableMinutes) throw new DomainValidationException("特休可用額度不足。");
        ReservedMinutes = checked(ReservedMinutes + minutes);
        Touch(nowUtc);
    }

    public void Consume(int minutes, DateTimeOffset nowUtc)
    {
        EnsurePositive(minutes);
        if (minutes > ReservedMinutes) throw new DomainValidationException("保留額度不足，無法核准特休。");
        ReservedMinutes -= minutes;
        ConsumedMinutes = checked(ConsumedMinutes + minutes);
        Touch(nowUtc);
    }

    public void Release(int minutes, DateTimeOffset nowUtc)
    {
        EnsurePositive(minutes);
        if (minutes > ReservedMinutes) throw new DomainValidationException("保留額度不足，無法釋放。");
        ReservedMinutes -= minutes;
        Touch(nowUtc);
    }

    public void Restore(int minutes, DateTimeOffset nowUtc)
    {
        EnsurePositive(minutes);
        if (minutes > ConsumedMinutes) throw new DomainValidationException("已使用額度不足，無法回補。");
        ConsumedMinutes -= minutes;
        Touch(nowUtc);
    }

    public void MarkExpiredPendingSettlement(DateTimeOffset nowUtc)
    {
        if (Status == AnnualLeaveEntitlementStatus.Open)
        {
            Status = AnnualLeaveEntitlementStatus.ExpiredPendingSettlement;
            Touch(nowUtc);
        }
    }

    public void AddCarriedIn(int minutes, DateTimeOffset nowUtc)
    {
        EnsurePositive(minutes);
        CarriedInMinutes = checked(CarriedInMinutes + minutes);
        Touch(nowUtc);
    }

    public void CarryForwardOut(int minutes, DateTimeOffset nowUtc)
    {
        EnsurePositive(minutes);
        if (minutes > AvailableMinutes) throw new DomainValidationException("可遞延特休額度不足。");
        SettledMinutes = checked(SettledMinutes + minutes);
        Status = AnnualLeaveEntitlementStatus.CarriedForward;
        Touch(nowUtc);
    }

    public void Settle(
        int minutes,
        AnnualLeaveEntitlementStatus settlementStatus,
        DateTimeOffset nowUtc)
    {
        EnsurePositive(minutes);
        if (settlementStatus is not (AnnualLeaveEntitlementStatus.PaidOut or AnnualLeaveEntitlementStatus.Closed))
            throw new DomainValidationException("特休結算狀態不正確。");
        if (ReservedMinutes != 0) throw new DomainValidationException("仍有申請中額度，不可結算。");
        if (minutes > AvailableMinutes) throw new DomainValidationException("可結算特休額度不足。");
        SettledMinutes = checked(SettledMinutes + minutes);
        Status = settlementStatus;
        Touch(nowUtc);
    }

    private static void EnsurePositive(int minutes)
    {
        if (minutes <= 0) throw new DomainValidationException("特休分鐘數必須大於零。");
    }

    private void Touch(DateTimeOffset nowUtc) => UpdatedAtUtc = nowUtc.ToUniversalTime();
}
