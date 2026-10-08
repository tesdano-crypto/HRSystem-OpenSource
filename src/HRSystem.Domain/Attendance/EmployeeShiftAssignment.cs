using HRSystem.Domain.Common;
using HRSystem.Domain.MasterData;

namespace HRSystem.Domain.Attendance;

public sealed class EmployeeShiftAssignment
{
    private EmployeeShiftAssignment()
    {
    }

    public EmployeeShiftAssignment(
        Guid id,
        Guid employeeId,
        Guid shiftId,
        DateOnly effectiveFrom,
        DateOnly? effectiveTo,
        DateTimeOffset nowUtc)
    {
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        EmployeeId = RequiredId(employeeId, "員工");
        ShiftId = RequiredId(shiftId, "班別");
        CreatedAtUtc = nowUtc.ToUniversalTime();
        IsActive = true;
        SetPeriod(effectiveFrom, effectiveTo, nowUtc);
    }

    public Guid Id { get; private set; }
    public Guid EmployeeId { get; private set; }
    public Guid ShiftId { get; private set; }
    public DateOnly EffectiveFrom { get; private set; }
    public DateOnly? EffectiveTo { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }
    public byte[] RowVersion { get; private set; } = [];
    public Employee Employee { get; private set; } = null!;
    public AttendanceShift Shift { get; private set; } = null!;

    public bool AppliesOn(DateOnly date) =>
        IsActive &&
        EffectiveFrom <= date &&
        (!EffectiveTo.HasValue || date <= EffectiveTo.Value);

    public void UpdatePeriod(
        DateOnly effectiveFrom,
        DateOnly? effectiveTo,
        DateTimeOffset nowUtc) =>
        SetPeriod(effectiveFrom, effectiveTo, nowUtc);

    public void Update(
        Guid shiftId,
        DateOnly effectiveFrom,
        DateOnly? effectiveTo,
        DateTimeOffset nowUtc)
    {
        ShiftId = RequiredId(shiftId, "Shift");
        SetPeriod(effectiveFrom, effectiveTo, nowUtc);
    }

    public void Activate(DateTimeOffset nowUtc)
    {
        IsActive = true;
        UpdatedAtUtc = nowUtc.ToUniversalTime();
    }

    public void Deactivate(DateTimeOffset nowUtc)
    {
        IsActive = false;
        UpdatedAtUtc = nowUtc.ToUniversalTime();
    }

    private void SetPeriod(
        DateOnly effectiveFrom,
        DateOnly? effectiveTo,
        DateTimeOffset nowUtc)
    {
        if (effectiveTo.HasValue && effectiveTo.Value < effectiveFrom)
        {
            throw new DomainValidationException("班別指派結束日期不可早於開始日期。");
        }

        EffectiveFrom = effectiveFrom;
        EffectiveTo = effectiveTo;
        UpdatedAtUtc = nowUtc.ToUniversalTime();
    }

    private static Guid RequiredId(Guid value, string field)
    {
        if (value == Guid.Empty)
        {
            throw new DomainValidationException($"必須指定{field}。");
        }

        return value;
    }
}
