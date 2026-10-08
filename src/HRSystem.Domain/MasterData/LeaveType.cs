using HRSystem.Domain.Common;

namespace HRSystem.Domain.MasterData;

public sealed class LeaveType
{
    public const int CodeMaxLength = 50;
    public const int DescriptionMaxLength = 1000;

    private LeaveType()
    {
    }

    public LeaveType(
        Guid id,
        string code,
        string name,
        LeaveUnit unit,
        decimal minimumUnit,
        bool requiresReason,
        bool isPaid,
        int sortOrder,
        DateTimeOffset nowUtc)
        : this(
            id,
            code,
            name,
            unit,
            minimumUnit,
            requiresReason,
            isPaid,
            sortOrder,
            LeaveCategory.General,
            LeaveCalculationMode.WorkingSchedule,
            unit == LeaveUnit.Hour,
            ResolveLegacyMinimumRequestMinutes(unit, minimumUnit),
            requiresAttachment: false,
            isEmployeeRequestEnabled: true,
            description: null,
            nowUtc)
    {
    }

    public LeaveType(
        Guid id,
        string code,
        string name,
        LeaveUnit unit,
        decimal minimumUnit,
        bool requiresReason,
        bool isPaid,
        int sortOrder,
        LeaveCategory category,
        LeaveCalculationMode calculationMode,
        bool allowHourlyRequest,
        int? minimumRequestMinutes,
        bool requiresAttachment,
        bool isEmployeeRequestEnabled,
        string? description,
        DateTimeOffset nowUtc)
    {
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CreatedAtUtc = nowUtc;
        IsActive = true;
        Code = MasterDataRules.RequiredCode(code, "假別代碼", CodeMaxLength);
        SetDetails(
            name,
            unit,
            minimumUnit,
            requiresReason,
            isPaid,
            sortOrder,
            category,
            calculationMode,
            allowHourlyRequest,
            minimumRequestMinutes,
            requiresAttachment,
            isEmployeeRequestEnabled,
            description,
            nowUtc);
    }

    public Guid Id { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public LeaveUnit Unit { get; private set; }
    public decimal MinimumUnit { get; private set; }
    public bool RequiresReason { get; private set; }
    public bool IsPaid { get; private set; }
    public bool IsActive { get; private set; }
    public int SortOrder { get; private set; }
    public LeaveCategory Category { get; private set; }
    public LeaveCalculationMode CalculationMode { get; private set; }
    public bool AllowHourlyRequest { get; private set; }
    public int? MinimumRequestMinutes { get; private set; }
    public bool RequiresAttachment { get; private set; }
    public bool IsEmployeeRequestEnabled { get; private set; }
    public string? Description { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public void Update(
        string code,
        string name,
        LeaveUnit unit,
        decimal minimumUnit,
        bool requiresReason,
        bool isPaid,
        int sortOrder,
        DateTimeOffset nowUtc)
    {
        var normalizedCode = MasterDataRules.RequiredCode(code, "假別代碼", CodeMaxLength);
        if (!string.Equals(Code, normalizedCode, StringComparison.Ordinal))
        {
            throw new DomainValidationException("假別代碼建立後不可修改。");
        }

        Update(
            name,
            unit,
            minimumUnit,
            requiresReason,
            isPaid,
            sortOrder,
            Category,
            CalculationMode,
            AllowHourlyRequest,
            MinimumRequestMinutes,
            RequiresAttachment,
            IsEmployeeRequestEnabled,
            Description,
            nowUtc);
    }

    public void Update(
        string name,
        LeaveUnit unit,
        decimal minimumUnit,
        bool requiresReason,
        bool isPaid,
        int sortOrder,
        LeaveCategory category,
        LeaveCalculationMode calculationMode,
        bool allowHourlyRequest,
        int? minimumRequestMinutes,
        bool requiresAttachment,
        bool isEmployeeRequestEnabled,
        string? description,
        DateTimeOffset nowUtc) =>
        SetDetails(
            name,
            unit,
            minimumUnit,
            requiresReason,
            isPaid,
            sortOrder,
            category,
            calculationMode,
            allowHourlyRequest,
            minimumRequestMinutes,
            requiresAttachment,
            isEmployeeRequestEnabled,
            description,
            nowUtc);

    public void Activate(DateTimeOffset nowUtc)
    {
        IsActive = true;
        UpdatedAtUtc = nowUtc;
    }

    public void Deactivate(DateTimeOffset nowUtc)
    {
        IsActive = false;
        UpdatedAtUtc = nowUtc;
    }

    private void SetDetails(
        string name,
        LeaveUnit unit,
        decimal minimumUnit,
        bool requiresReason,
        bool isPaid,
        int sortOrder,
        LeaveCategory category,
        LeaveCalculationMode calculationMode,
        bool allowHourlyRequest,
        int? minimumRequestMinutes,
        bool requiresAttachment,
        bool isEmployeeRequestEnabled,
        string? description,
        DateTimeOffset nowUtc)
    {
        if (minimumUnit <= 0)
        {
            throw new DomainValidationException("最小請假單位必須大於 0。");
        }

        var isValidIncrement = unit switch
        {
            LeaveUnit.Day => minimumUnit == 0.5m || (minimumUnit >= 1m && decimal.Truncate(minimumUnit) == minimumUnit),
            LeaveUnit.Hour => decimal.Truncate(minimumUnit * 2m) == minimumUnit * 2m,
            _ => false
        };

        if (!isValidIncrement)
        {
            throw new DomainValidationException(unit == LeaveUnit.Day
                ? "日單位僅允許 0.5 或正整數。"
                : "小時單位必須以 0.5 遞增。");
        }

        if (sortOrder < 0)
        {
            throw new DomainValidationException("排序不可小於 0。");
        }

        if (!Enum.IsDefined(category))
        {
            throw new DomainValidationException("請選擇有效的假別分類。");
        }

        if (!Enum.IsDefined(calculationMode))
        {
            throw new DomainValidationException("請選擇有效的計算模式。");
        }

        var expectedMode = category switch
        {
            LeaveCategory.General => LeaveCalculationMode.WorkingSchedule,
            LeaveCategory.SpecialCalendarLeave => LeaveCalculationMode.CalendarDays,
            LeaveCategory.LeaveOfAbsence => LeaveCalculationMode.LeaveOfAbsence,
            _ => throw new DomainValidationException("假別分類不支援此計算模式。")
        };
        if (calculationMode != expectedMode)
        {
            throw new DomainValidationException("假別分類與計算模式不相容。");
        }

        if (minimumRequestMinutes is <= 0)
        {
            throw new DomainValidationException("最小申請分鐘必須大於 0。");
        }

        if (calculationMode == LeaveCalculationMode.WorkingSchedule && !minimumRequestMinutes.HasValue)
        {
            throw new DomainValidationException("班表計算假別必須設定最小申請分鐘。");
        }

        if (calculationMode != LeaveCalculationMode.WorkingSchedule && allowHourlyRequest)
        {
            throw new DomainValidationException("特殊曆日假或留職停薪不得開放按小時申請。");
        }

        if (calculationMode == LeaveCalculationMode.LeaveOfAbsence && isEmployeeRequestEnabled)
        {
            throw new DomainValidationException("尚未支援的特殊假別不得開放一般員工申請。");
        }

        Name = MasterDataRules.RequiredText(name, "假別名稱", 100);
        Unit = unit;
        MinimumUnit = minimumUnit;
        RequiresReason = requiresReason;
        IsPaid = isPaid;
        SortOrder = sortOrder;
        Category = category;
        CalculationMode = calculationMode;
        AllowHourlyRequest = allowHourlyRequest;
        MinimumRequestMinutes = minimumRequestMinutes;
        RequiresAttachment = requiresAttachment;
        IsEmployeeRequestEnabled = isEmployeeRequestEnabled;
        Description = MasterDataRules.OptionalText(description, "說明", DescriptionMaxLength);
        UpdatedAtUtc = nowUtc;
    }

    public void SetEmployeeRequestEnabled(
        bool isEnabled,
        DateTimeOffset nowUtc)
    {
        if (isEnabled && CalculationMode == LeaveCalculationMode.LeaveOfAbsence)
        {
            throw new DomainValidationException("留職停薪尚未開放員工直接申請。");
        }

        IsEmployeeRequestEnabled = isEnabled;
        UpdatedAtUtc = nowUtc;
    }

    private static int ResolveLegacyMinimumRequestMinutes(LeaveUnit unit, decimal minimumUnit) =>
        checked((int)(minimumUnit * (unit == LeaveUnit.Hour ? 60m : 480m)));
}
