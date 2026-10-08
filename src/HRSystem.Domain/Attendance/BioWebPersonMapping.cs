using HRSystem.Domain.Common;
using HRSystem.Domain.MasterData;

namespace HRSystem.Domain.Attendance;

public sealed class BioWebPersonMapping
{
    private BioWebPersonMapping()
    {
    }

    public BioWebPersonMapping(
        Guid id,
        Guid employeeId,
        string bioWebPin,
        DateTime effectiveFrom,
        DateTime? effectiveTo,
        DateTimeOffset nowUtc)
    {
        if (employeeId == Guid.Empty)
        {
            throw new DomainValidationException("必須指定 HRSystem 員工。");
        }

        AttendanceRules.ValidateEffectivePeriod(effectiveFrom, effectiveTo);
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        EmployeeId = employeeId;
        BioWebPin = AttendanceRules.Required(bioWebPin, "BioWeb PIN", 20);
        EffectiveFrom = effectiveFrom;
        EffectiveTo = effectiveTo;
        IsActive = true;
        CreatedAtUtc = nowUtc.ToUniversalTime();
        UpdatedAtUtc = CreatedAtUtc;
    }

    public Guid Id { get; private set; }
    public Guid EmployeeId { get; private set; }
    public string BioWebPin { get; private set; } = string.Empty;
    public DateTime EffectiveFrom { get; private set; }
    public DateTime? EffectiveTo { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }
    public byte[] RowVersion { get; private set; } = [];
    public Employee Employee { get; private set; } = null!;

    public void UpdateEffectivePeriod(
        DateTime effectiveFrom,
        DateTime? effectiveTo,
        DateTimeOffset nowUtc)
    {
        AttendanceRules.ValidateEffectivePeriod(effectiveFrom, effectiveTo);
        EffectiveFrom = effectiveFrom;
        EffectiveTo = effectiveTo;
        UpdatedAtUtc = nowUtc.ToUniversalTime();
    }

    public void Deactivate(DateTimeOffset nowUtc)
    {
        IsActive = false;
        UpdatedAtUtc = nowUtc.ToUniversalTime();
    }

    public bool IsEffectiveAt(DateTime sourceLocalDateTime) =>
        IsActive &&
        sourceLocalDateTime >= EffectiveFrom &&
        (!EffectiveTo.HasValue || sourceLocalDateTime < EffectiveTo.Value);
}
