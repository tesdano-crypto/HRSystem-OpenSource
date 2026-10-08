using System.ComponentModel.DataAnnotations;
using HRSystem.Domain.ParentalLeave;

namespace HRSystem.Application.ParentalLeave;

public sealed record ParentalLeaveApprovalHistoryDto(
    Guid Id,
    ParentalLeaveApprovalAction Action,
    string ActionByDisplayName,
    string? Comment,
    DateTimeOffset ActionAtUtc,
    ParentalLeaveStatus FromStatus,
    ParentalLeaveStatus ToStatus);

public sealed record ParentalLeaveRequestDto(
    Guid Id,
    string RequestNumber,
    Guid EmployeeId,
    string EmployeeNumber,
    string EmployeeName,
    Guid DepartmentId,
    string DepartmentName,
    Guid ChildReferenceId,
    DateOnly ChildBirthDate,
    string? ChildDisplayName,
    DateOnly StartDate,
    DateOnly EndDate,
    DateOnly EffectiveEndDate,
    int CalendarDayCount,
    ParentalLeaveApplicationType ApplicationType,
    ParentalLeaveNoticeType NoticeType,
    string? Reason,
    string ContactAddress,
    string ContactPhone,
    bool ContinueSocialInsurance,
    string? EmergencyCareReason,
    ParentalLeaveStatus Status,
    ParentalLeaveStatus EffectiveStatus,
    DateTimeOffset? RequestedAtUtc,
    DateTimeOffset? ApprovedAtUtc,
    string? CancellationReason,
    DateOnly? EarlyReturnDate,
    string? EarlyReturnReason,
    bool IsEarlyReturnApproved,
    DateTimeOffset CreatedAtUtc,
    string RowVersion,
    IReadOnlyList<ParentalLeaveApprovalHistoryDto> ApprovalHistories);

public sealed record ParentalLeaveChildOptionDto(
    Guid ChildReferenceId,
    DateOnly ChildBirthDate,
    string? ChildDisplayName,
    int UsedDailyDays,
    int UsedShortTermCount,
    int UsedTotalDays);

public sealed record ParentalLeaveEstimateDto(
    int CalendarDays,
    ParentalLeaveApplicationType ApplicationType,
    int RequiredNoticeDays,
    int ActualNoticeDays,
    bool MeetsNotice,
    int UsedDailyDays,
    int RemainingDailyDays,
    int UsedShortTermCount,
    int RemainingShortTermCount,
    int UsedTotalDays,
    int RemainingTotalDays,
    IReadOnlyList<string> Messages);

public sealed class ParentalLeaveQuery
{
    public ParentalLeaveStatus? Status { get; set; }
    public string? Keyword { get; set; }
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

public class CreateParentalLeaveDraftRequest : IValidatableObject
{
    public Guid? ChildReferenceId { get; set; }
    public DateOnly ChildBirthDate { get; set; }

    [StringLength(100, ErrorMessage = "子女識別名稱不可超過 100 個字元。")]
    public string? ChildDisplayName { get; set; }

    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }

    [Required(ErrorMessage = "聯絡地址為必填欄位。")]
    [StringLength(500, ErrorMessage = "聯絡地址不可超過 500 個字元。")]
    public string ContactAddress { get; set; } = string.Empty;

    [Required(ErrorMessage = "聯絡電話為必填欄位。")]
    [StringLength(30, ErrorMessage = "聯絡電話不可超過 30 個字元。")]
    public string ContactPhone { get; set; } = string.Empty;

    public bool ContinueSocialInsurance { get; set; } = true;
    public ParentalLeaveNoticeType NoticeType { get; set; } =
        ParentalLeaveNoticeType.Standard;

    [StringLength(1000, ErrorMessage = "原因／說明不可超過 1000 個字元。")]
    public string? Reason { get; set; }

    [StringLength(1000, ErrorMessage = "緊急照顧原因不可超過 1000 個字元。")]
    public string? EmergencyCareReason { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (ChildBirthDate == default)
        {
            yield return new ValidationResult("請填寫子女出生日期。", [nameof(ChildBirthDate)]);
        }

        if (StartDate == default || EndDate < StartDate)
        {
            yield return new ValidationResult(
                "申請結束日期不得早於開始日期。",
                [nameof(StartDate), nameof(EndDate)]);
        }

        if (NoticeType == ParentalLeaveNoticeType.EmergencyCare &&
            string.IsNullOrWhiteSpace(EmergencyCareReason))
        {
            yield return new ValidationResult(
                "緊急照顧申請必須填寫原因。",
                [nameof(EmergencyCareReason)]);
        }
    }
}

public sealed class UpdateParentalLeaveDraftRequest :
    CreateParentalLeaveDraftRequest
{
    public Guid Id { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public class ParentalLeaveActionRequest
{
    public Guid Id { get; set; }
    public string RowVersion { get; set; } = string.Empty;

    [StringLength(1000, ErrorMessage = "意見不可超過 1000 個字元。")]
    public string? Comment { get; set; }
}

public class ParentalLeaveReasonActionRequest : ParentalLeaveActionRequest
{
    [Required(ErrorMessage = "原因為必填欄位。")]
    public new string? Comment { get; set; }
}

public sealed class RequestParentalLeaveEarlyReturnRequest :
    ParentalLeaveReasonActionRequest
{
    public DateOnly ReturnDate { get; set; }
}
