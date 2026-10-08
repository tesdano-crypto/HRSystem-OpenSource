using System.ComponentModel.DataAnnotations;
using HRSystem.Domain.Attendance;

namespace HRSystem.Application.Attendance;

public sealed record AttendanceCorrectionHistoryDto(
    AttendanceCorrectionRequestHistoryAction Action,
    AttendanceCorrectionRequestStatus? FromStatus,
    AttendanceCorrectionRequestStatus ToStatus,
    string? Note,
    DateTimeOffset OccurredAtUtc);

public sealed record AttendanceCorrectionContextDto(
    Guid AttendanceResultId,
    DateOnly WorkDate,
    TimeOnly? ScheduledStart,
    TimeOnly? ScheduledEnd,
    DateTime? CurrentClockIn,
    DateTime? CurrentClockOut,
    bool MissingClockIn,
    bool MissingClockOut,
    int LateMinutes,
    int EarlyLeaveMinutes,
    string AttendanceStatus,
    bool HasCurrentAdjustment,
    int ExistingAdjustmentCount,
    int ApprovedLeaveMinutes,
    string OvertimeSummary,
    string RawPunchSummary);

public sealed record AttendanceCorrectionRequestDto(
    Guid Id,
    Guid EmployeeId,
    string EmployeeNumber,
    string EmployeeName,
    Guid DepartmentId,
    string DepartmentName,
    DateOnly WorkDate,
    Guid? AttendanceResultId,
    AttendanceCorrectionRequestType RequestType,
    AttendanceCorrectionRequestStatus Status,
    DateTime? OriginalClockInAt,
    DateTime? OriginalClockOutAt,
    DateTime? ProposedClockInAt,
    DateTime? ProposedClockOutAt,
    AttendanceCorrectionReason Reason,
    string EmployeeReason,
    string? ReviewerNote,
    bool IsSourceStale,
    string SourceFingerprint,
    Guid? AppliedAttendanceAdjustmentId,
    DateTimeOffset? SubmittedAtUtc,
    DateTimeOffset? ApprovedAtUtc,
    DateTimeOffset? RejectedAtUtc,
    DateTimeOffset? WithdrawnAtUtc,
    string RowVersion,
    AttendanceCorrectionContextDto? AttendanceContext,
    IReadOnlyList<AttendanceCorrectionHistoryDto> Histories);

public sealed class AttendanceCorrectionQuery
{
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public Guid? DepartmentId { get; set; }
    public Guid? EmployeeId { get; set; }
    public AttendanceCorrectionRequestStatus? Status { get; set; }
    public AttendanceCorrectionRequestType? RequestType { get; set; }
    public bool PendingOnly { get; set; }
    public bool Descending { get; set; } = true;
}

public class CreateAttendanceCorrectionDraftRequest : IValidatableObject
{
    public Guid AttendanceResultId { get; set; }
    public AttendanceCorrectionRequestType RequestType { get; set; }
    public DateTime? ProposedClockInAt { get; set; }
    public DateTime? ProposedClockOutAt { get; set; }
    public AttendanceCorrectionReason Reason { get; set; }

    [Required(ErrorMessage = "申請說明為必填欄位。")]
    [StringLength(1000, ErrorMessage = "申請說明不可超過 1000 個字元。")]
    public string EmployeeReason { get; set; } = string.Empty;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (AttendanceResultId == Guid.Empty)
            yield return new ValidationResult("出勤結果不可為空。");
        if (!Enum.IsDefined(RequestType))
            yield return new ValidationResult("請選擇有效的申請類型。");
        if (!Enum.IsDefined(Reason))
            yield return new ValidationResult("請選擇有效的申請原因。");
    }
}

public sealed class UpdateAttendanceCorrectionDraftRequest :
    CreateAttendanceCorrectionDraftRequest
{
    public Guid Id { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public class AttendanceCorrectionActionRequest
{
    public Guid Id { get; set; }
    public string RowVersion { get; set; } = string.Empty;

    [StringLength(1000, ErrorMessage = "說明不可超過 1000 個字元。")]
    public string? Note { get; set; }
}

public sealed class ReviewAttendanceCorrectionRequest :
    AttendanceCorrectionActionRequest
{
    public string SourceFingerprint { get; set; } = string.Empty;
}

public sealed record AttendanceCorrectionFilterOptions(
    IReadOnlyList<AttendanceReviewDepartmentOption> Departments,
    IReadOnlyList<AttendanceReviewEmployeeOption> Employees);

public sealed record AttendanceCorrectionAdjustmentResult(
    Guid AdjustmentId,
    Guid DailyAttendanceResultId,
    Guid EmployeeId,
    DateOnly WorkDate);
