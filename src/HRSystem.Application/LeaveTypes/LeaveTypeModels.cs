using System.ComponentModel.DataAnnotations;
using HRSystem.Domain.MasterData;

namespace HRSystem.Application.LeaveTypes;

public sealed record LeaveTypeDto(
    Guid Id,
    string Code,
    string Name,
    LeaveUnit Unit,
    decimal MinimumUnit,
    bool RequiresReason,
    bool IsPaid,
    bool IsActive,
    int SortOrder,
    LeaveCategory Category,
    LeaveCalculationMode CalculationMode,
    bool AllowHourlyRequest,
    int? MinimumRequestMinutes,
    bool RequiresAttachment,
    bool IsEmployeeRequestEnabled,
    string? Description,
    string RowVersion);

public sealed class LeaveTypeQuery
{
    public bool? IsActive { get; set; }
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 10;
}

public class CreateLeaveTypeRequest : IValidatableObject
{
    [Required(ErrorMessage = "假別代碼為必填欄位。")]
    [StringLength(LeaveType.CodeMaxLength, ErrorMessage = "假別代碼不可超過 50 個字元。")]
    public string Code { get; set; } = string.Empty;

    [Required(ErrorMessage = "假別名稱為必填欄位。")]
    [StringLength(100, ErrorMessage = "假別名稱不可超過 100 個字元。")]
    public string Name { get; set; } = string.Empty;

    public LeaveUnit Unit { get; set; } = LeaveUnit.Hour;

    [Range(typeof(decimal), "0.01", "999999", ErrorMessage = "最小請假單位必須大於 0。")]
    public decimal MinimumUnit { get; set; } = 0.5m;

    public bool RequiresReason { get; set; }
    public bool IsPaid { get; set; }

    public LeaveCategory Category { get; set; } = LeaveCategory.General;

    public LeaveCalculationMode CalculationMode { get; set; } = LeaveCalculationMode.WorkingSchedule;

    public bool AllowHourlyRequest { get; set; } = true;

    [Range(1, int.MaxValue, ErrorMessage = "最小申請分鐘必須大於 0。")]
    public int? MinimumRequestMinutes { get; set; } = 30;

    public bool RequiresAttachment { get; set; }

    public bool IsEmployeeRequestEnabled { get; set; } = true;

    [StringLength(LeaveType.DescriptionMaxLength, ErrorMessage = "說明不可超過 1000 個字元。")]
    public string? Description { get; set; }

    [Range(0, int.MaxValue, ErrorMessage = "排序不可小於 0。")]
    public int SortOrder { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        var valid = Unit switch
        {
            LeaveUnit.Day => MinimumUnit == 0.5m ||
                (MinimumUnit >= 1m && decimal.Truncate(MinimumUnit) == MinimumUnit),
            LeaveUnit.Hour => decimal.Truncate(MinimumUnit * 2m) == MinimumUnit * 2m,
            _ => false
        };
        if (!valid)
        {
            yield return new ValidationResult(
                Unit == LeaveUnit.Day ? "日單位僅允許 0.5 或正整數。" : "小時單位必須以 0.5 遞增。",
                [nameof(MinimumUnit)]);
        }

        if (Category == LeaveCategory.General && CalculationMode != LeaveCalculationMode.WorkingSchedule ||
            Category == LeaveCategory.SpecialCalendarLeave && CalculationMode != LeaveCalculationMode.CalendarDays ||
            Category == LeaveCategory.LeaveOfAbsence && CalculationMode != LeaveCalculationMode.LeaveOfAbsence)
        {
            yield return new ValidationResult(
                "假別分類與計算模式不相容。",
                [nameof(Category), nameof(CalculationMode)]);
        }

        if (CalculationMode == LeaveCalculationMode.WorkingSchedule && !MinimumRequestMinutes.HasValue)
        {
            yield return new ValidationResult(
                "班表計算假別必須設定最小申請分鐘。",
                [nameof(MinimumRequestMinutes)]);
        }

        if (CalculationMode != LeaveCalculationMode.WorkingSchedule && AllowHourlyRequest)
        {
            yield return new ValidationResult(
                "特殊曆日假或留職停薪不得開放按小時申請。",
                [nameof(AllowHourlyRequest)]);
        }

        if (CalculationMode == LeaveCalculationMode.LeaveOfAbsence && IsEmployeeRequestEnabled)
        {
            yield return new ValidationResult(
                "尚未支援的特殊假別不得開放一般員工申請。",
                [nameof(IsEmployeeRequestEnabled)]);
        }
    }
}

public sealed class UpdateLeaveTypeRequest : CreateLeaveTypeRequest
{
    public Guid Id { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}
