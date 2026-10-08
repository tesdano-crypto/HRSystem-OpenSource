using System.ComponentModel.DataAnnotations;
using HRSystem.Domain.LeaveRequests;
using HRSystem.Domain.MasterData;

namespace HRSystem.Application.LeaveRequests;

public sealed record LeaveApprovalHistoryDto(
    Guid Id,
    ApprovalAction Action,
    string ActionByDisplayName,
    string? Comment,
    DateTimeOffset ActionAtUtc,
    LeaveRequestStatus FromStatus,
    LeaveRequestStatus ToStatus);

public sealed record LeaveRequestDto(
    Guid Id,
    string RequestNumber,
    Guid EmployeeId,
    string EmployeeNumber,
    string EmployeeName,
    Guid DepartmentId,
    string DepartmentName,
    Guid LeaveTypeId,
    string LeaveTypeCode,
    string LeaveTypeName,
    DateTimeOffset StartAt,
    DateTimeOffset EndAt,
    decimal DurationHours,
    string Reason,
    LeaveRequestStatus Status,
    DateTimeOffset? SubmittedAtUtc,
    DateTimeOffset? ApprovedAtUtc,
    DateTimeOffset? RejectedAtUtc,
    DateTimeOffset? WithdrawnAtUtc,
    DateTimeOffset? CancellationRequestedAtUtc,
    string? CancellationReason,
    string? CancellationRequestedByUserId,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    string RowVersion,
    IReadOnlyList<LeaveApprovalHistoryDto> ApprovalHistories,
    LeaveCalculationMode CalculationMode = LeaveCalculationMode.WorkingSchedule,
    DateOnly? CalendarStartDate = null,
    DateOnly? CalendarEndDate = null,
    int? CalendarDayCount = null,
    PregnancyDurationCategory? PregnancyDurationCategory = null);

public sealed record LeaveTypeOptionDto(
    Guid Id,
    string Code,
    string Name,
    bool AllowHourlyRequest = true,
    int? MinimumRequestMinutes = null,
    string? Description = null,
    LeaveCalculationMode CalculationMode = LeaveCalculationMode.WorkingSchedule,
    bool RequiresAttachment = false);

public sealed record LeaveDurationEstimateDto(
    decimal DurationHours,
    bool CanSubmit,
    IReadOnlyList<string> Messages,
    int? CalendarDayCount = null,
    DateOnly? CalendarEndDate = null);

public sealed class LeaveDurationEstimateRequest
{
    public Guid LeaveTypeId { get; set; }
    public DateTimeOffset StartAt { get; set; }
    public DateTimeOffset EndAt { get; set; }
    public DateOnly? CalendarStartDate { get; set; }
    public DateOnly? CalendarEndDate { get; set; }
    public PregnancyDurationCategory? PregnancyDurationCategory { get; set; }
}

public sealed class LeaveRequestQuery
{
    public string? Keyword { get; set; }
    public LeaveRequestStatus? Status { get; set; }
    public Guid? EmployeeId { get; set; }
    public Guid? DepartmentId { get; set; }
    public Guid? LeaveTypeId { get; set; }
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 10;
}

public class CreateLeaveDraftRequest : IValidatableObject
{
    public Guid LeaveTypeId { get; set; }

    public DateTimeOffset StartAt { get; set; }

    public DateTimeOffset EndAt { get; set; }

    public DateOnly? CalendarStartDate { get; set; }

    public DateOnly? CalendarEndDate { get; set; }

    public PregnancyDurationCategory? PregnancyDurationCategory { get; set; }

    [Required(ErrorMessage = "請假原因為必填欄位。")]
    [StringLength(500, ErrorMessage = "請假原因不可超過 500 個字元。")]
    public string Reason { get; set; } = string.Empty;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (LeaveTypeId == Guid.Empty)
        {
            yield return new ValidationResult("請選擇假別。", [nameof(LeaveTypeId)]);
        }

        if (!CalendarStartDate.HasValue && StartAt >= EndAt)
        {
            yield return new ValidationResult("開始時間必須早於結束時間。", [nameof(StartAt), nameof(EndAt)]);
        }
        else if (!CalendarStartDate.HasValue &&
            EndAt - StartAt > TimeSpan.FromDays(31))
        {
            yield return new ValidationResult("單張請假申請不得超過 31 天。", [nameof(EndAt)]);
        }
    }
}

public sealed class UpdateLeaveDraftRequest : CreateLeaveDraftRequest
{
    public Guid Id { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public class LeaveRequestActionRequest
{
    public Guid Id { get; set; }
    public string RowVersion { get; set; } = string.Empty;

    [StringLength(1000, ErrorMessage = "意見不可超過 1000 個字元。")]
    public string? Comment { get; set; }
}

public sealed class RejectLeaveRequestRequest : LeaveRequestActionRequest
{
    [Required(ErrorMessage = "退回原因為必填欄位。")]
    public new string? Comment { get; set; }
}

public sealed class RequestLeaveCancellationRequest : LeaveRequestActionRequest
{
    [Required(ErrorMessage = "撤簽原因為必填欄位。")]
    public new string? Comment { get; set; }
}

public sealed class RejectLeaveCancellationRequest : LeaveRequestActionRequest
{
    [Required(ErrorMessage = "撤簽駁回原因為必填欄位。")]
    public new string? Comment { get; set; }
}
