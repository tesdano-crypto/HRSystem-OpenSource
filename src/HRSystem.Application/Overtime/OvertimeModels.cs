using System.ComponentModel.DataAnnotations;
using HRSystem.Domain.Overtime;

namespace HRSystem.Application.Overtime;

public sealed record OvertimeRequestHistoryDto(
    OvertimeRequestHistoryAction Action,
    OvertimeRequestStatus? FromStatus,
    OvertimeRequestStatus ToStatus,
    string? Note,
    DateTimeOffset OccurredAtUtc);

public sealed record OvertimeAttendanceContextDto(
    TimeOnly? ScheduledEnd,
    DateTime? ActualClockOut,
    int OverstayMinutes,
    string? OverstayLevel,
    bool MissingPunch,
    int ApprovedLeaveMinutes,
    bool IsRequiredWorkday);

public sealed record OvertimeRequestDto(
    Guid Id,
    Guid EmployeeId,
    string EmployeeNumber,
    string EmployeeName,
    Guid DepartmentId,
    string DepartmentName,
    DateOnly OvertimeDate,
    DateTime PlannedStartAt,
    DateTime PlannedEndAt,
    int RequestedMinutes,
    string Reason,
    OvertimeRequestStatus Status,
    OvertimeReviewReason? ReviewReason,
    string? ReviewNote,
    DateTimeOffset? SubmittedAtUtc,
    DateTimeOffset? ApprovedAtUtc,
    DateTimeOffset? RejectedAtUtc,
    DateTimeOffset? WithdrawnAtUtc,
    DateTimeOffset CreatedAtUtc,
    string RowVersion,
    IReadOnlyList<OvertimeRequestHistoryDto> Histories,
    OvertimeAttendanceContextDto? AttendanceContext = null)
{
    public OvertimeRecognitionDto? Recognition { get; init; }
}

public sealed class OvertimeRequestQuery
{
    public Guid? RequestId { get; set; }
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public Guid? DepartmentId { get; set; }
    public Guid? EmployeeId { get; set; }
    public OvertimeRequestStatus? Status { get; set; }
    public bool PendingOnly { get; set; }
    public bool Descending { get; set; } = true;
}

public class CreateOvertimeDraftRequest : IValidatableObject
{
    public DateOnly? NonWorkingDayEvidenceDate { get; set; }
    public DateTime PlannedStartAt { get; set; }
    public DateTime PlannedEndAt { get; set; }

    [Required(ErrorMessage = "加班原因為必填欄位。")]
    [StringLength(500, ErrorMessage = "加班原因不可超過 500 個字元。")]
    public string Reason { get; set; } = string.Empty;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (PlannedStartAt.Kind != DateTimeKind.Unspecified ||
            PlannedEndAt.Kind != DateTimeKind.Unspecified)
            yield return new ValidationResult("加班時間必須使用台北本地時間。");
        if (PlannedEndAt <= PlannedStartAt)
            yield return new ValidationResult("預計結束時間必須晚於開始時間。");
        else if (PlannedEndAt - PlannedStartAt > TimeSpan.FromHours(12))
            yield return new ValidationResult("單張加班申請不得超過 12 小時。");
        else if ((PlannedEndAt - PlannedStartAt).TotalMinutes % 30 != 0)
            yield return new ValidationResult("加班申請必須以 30 分鐘為單位。");
    }
}

public sealed class OvertimeDraftOverlapException(Guid existingRequestId) : Exception("此時段已有加班申請")
{
    public Guid ExistingRequestId { get; } = existingRequestId;
}

public enum OvertimeRecognitionFilter
{
    All = 0,
    Pending = 1,
    Confirmed = 2,
    NeedsReview = 3,
    ExcessBeyondApproval = 4,
    MissingClockOut = 5
}

public sealed class OvertimeRecognitionQuery
{
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public Guid? DepartmentId { get; set; }
    public Guid? EmployeeId { get; set; }
    public OvertimeRecognitionFilter Filter { get; set; }
}

public sealed record OvertimeRecognitionHistoryDto(
    OvertimeRecognitionHistoryAction Action,
    OvertimeRecognitionStatus? FromStatus,
    OvertimeRecognitionStatus ToStatus,
    OvertimeRecognitionReason? Reason,
    string? Note,
    int? PreviousRecognizedMinutes,
    int? NewRecognizedMinutes,
    DateTimeOffset OccurredAtUtc);

public sealed record OvertimeRecognitionDto(
    Guid? RecognitionId,
    Guid OvertimeRequestId,
    Guid EmployeeId,
    string EmployeeNumber,
    string EmployeeName,
    string DepartmentName,
    DateOnly WorkDate,
    DateTime ApprovedPlannedStartAt,
    DateTime ApprovedPlannedEndAt,
    int ApprovedMinutes,
    DateTime? ScheduledEndAt,
    DateTime? ObservedClockOutAt,
    int ObservedOvertimeMinutes,
    DateTime? SuggestedStartAt,
    DateTime? SuggestedEndAt,
    int? SuggestedMinutes,
    int ExcessBeyondApprovalMinutes,
    DateTime? RecognizedStartAt,
    DateTime? RecognizedEndAt,
    int? RecognizedMinutes,
    OvertimeRecognitionStatus Status,
    OvertimeRecognitionReason? Reason,
    string? Note,
    bool IsSourceStale,
    bool MissingClockOut,
    string SourceFingerprint,
    string? RowVersion,
    IReadOnlyList<OvertimeRecognitionHistoryDto> Histories);

public sealed class ConfirmOvertimeRecognitionRequest : IValidatableObject
{
    public Guid OvertimeRequestId { get; set; }
    public Guid? RecognitionId { get; set; }
    public string? RowVersion { get; set; }
    public string SourceFingerprint { get; set; } = string.Empty;
    public DateTime? RecognizedStartAt { get; set; }
    public DateTime? RecognizedEndAt { get; set; }
    public OvertimeRecognitionReason Reason { get; set; }

    [StringLength(1000, ErrorMessage = "認列說明不可超過 1000 個字元。")]
    public string? Note { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (OvertimeRequestId == Guid.Empty)
            yield return new ValidationResult("加班申請不可為空。");
        if (!Enum.IsDefined(Reason))
            yield return new ValidationResult("請選擇有效的加班認列原因。");
        if (Reason == OvertimeRecognitionReason.Other &&
            string.IsNullOrWhiteSpace(Note))
            yield return new ValidationResult("選擇其他時必須填寫認列說明。");
        if (Reason == OvertimeRecognitionReason.NoActualOvertime)
        {
            if (RecognizedStartAt.HasValue || RecognizedEndAt.HasValue)
                yield return new ValidationResult("未實際加班時不可填寫認列時段。");
            yield break;
        }
        if (Reason == OvertimeRecognitionReason.NonWorkActivityExcluded &&
            !RecognizedStartAt.HasValue && !RecognizedEndAt.HasValue)
            yield break;
        if (!RecognizedStartAt.HasValue || !RecognizedEndAt.HasValue)
        {
            yield return new ValidationResult("請填寫完整的實際加班認列時段。");
            yield break;
        }
        if (RecognizedStartAt.Value.Kind != DateTimeKind.Unspecified ||
            RecognizedEndAt.Value.Kind != DateTimeKind.Unspecified)
            yield return new ValidationResult("認列時間必須使用台北本地時間。");
        if (RecognizedEndAt <= RecognizedStartAt)
            yield return new ValidationResult("認列結束時間必須晚於開始時間。");
        else if (RecognizedEndAt - RecognizedStartAt >
            OvertimeRecognition.MaximumRecognitionDuration)
            yield return new ValidationResult("單次實際加班認列不得超過 24 小時。");
    }
}

public sealed class ReopenOvertimeRecognitionRequest
{
    public Guid RecognitionId { get; set; }
    public string RowVersion { get; set; } = string.Empty;
    public string SourceFingerprint { get; set; } = string.Empty;

    [StringLength(1000, ErrorMessage = "重新開啟說明不可超過 1000 個字元。")]
    public string? Note { get; set; }
}

public sealed class UpdateOvertimeDraftRequest : CreateOvertimeDraftRequest
{
    public Guid Id { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public class OvertimeActionRequest
{
    public Guid Id { get; set; }
    public string RowVersion { get; set; } = string.Empty;

    [StringLength(1000, ErrorMessage = "說明不可超過 1000 個字元。")]
    public string? Note { get; set; }
}

public sealed class ReviewOvertimeRequest : OvertimeActionRequest
{
    public OvertimeReviewReason Reason { get; set; }
}
