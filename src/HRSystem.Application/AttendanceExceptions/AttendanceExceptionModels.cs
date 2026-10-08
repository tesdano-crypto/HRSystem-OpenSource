using System.ComponentModel.DataAnnotations;
using HRSystem.Domain.AttendanceExceptions;

namespace HRSystem.Application.AttendanceExceptions;

public sealed record AttendanceExceptionEmployeeOptionDto(Guid Id, string EmployeeNumber, string EmployeeName);
public sealed record AttendanceExceptionReviewContextDto(string ShiftSummary, int RawPunchCount,
    string RawPunchSummary, string ProjectedOutcomeSummary);

public sealed record AttendanceExceptionHistoryDto(Guid Id, AttendanceExceptionHistoryAction Action,
    string ActionByDisplayName, string? Comment, DateTimeOffset ActionAtUtc,
    AttendanceExceptionStatus FromStatus, AttendanceExceptionStatus ToStatus);

public sealed record AttendanceExceptionDto(Guid Id, string RequestNumber, Guid EmployeeId,
    string EmployeeNumber, string EmployeeName, Guid DepartmentId, string DepartmentName,
    DateOnly WorkDate, AttendanceExceptionType ExceptionType, NaturalDisasterReasonType ReasonType,
    AttendanceExceptionImpactType ImpactType, TimeOnly? ExemptFromTime, TimeOnly? ExemptToTime,
    string? Reason, AttendanceExceptionStatus Status, string? CancellationReason,
    DateTimeOffset CreatedAtUtc, string RowVersion, IReadOnlyList<AttendanceExceptionHistoryDto> Histories);

public sealed class AttendanceExceptionQuery
{
    public AttendanceExceptionStatus? Status { get; set; }
    public string? Keyword { get; set; }
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

public class CreateAttendanceExceptionDraftRequest : IValidatableObject
{
    public Guid? EmployeeId { get; set; }
    public DateOnly WorkDate { get; set; }
    public NaturalDisasterReasonType ReasonType { get; set; } = NaturalDisasterReasonType.WorkplaceClosure;
    public AttendanceExceptionImpactType ImpactType { get; set; } = AttendanceExceptionImpactType.FullDay;
    public TimeOnly? ExemptFromTime { get; set; }
    public TimeOnly? ExemptToTime { get; set; }
    [StringLength(1000, ErrorMessage = "補充說明不可超過 1000 個字元。")]
    public string? Reason { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (WorkDate == default) yield return new ValidationResult("出勤日期為必填欄位。", [nameof(WorkDate)]);
        if (ImpactType == AttendanceExceptionImpactType.FullDay && (ExemptFromTime.HasValue || ExemptToTime.HasValue))
            yield return new ValidationResult("全日豁免不可填寫起訖時間。", [nameof(ExemptFromTime), nameof(ExemptToTime)]);
        if (ImpactType == AttendanceExceptionImpactType.LateArrival && (!ExemptToTime.HasValue || ExemptFromTime.HasValue))
            yield return new ValidationResult("延後到班必須只填寫豁免截止時間。", [nameof(ExemptFromTime), nameof(ExemptToTime)]);
        if (ImpactType == AttendanceExceptionImpactType.EarlyDeparture && (!ExemptFromTime.HasValue || ExemptToTime.HasValue))
            yield return new ValidationResult("提前離開必須只填寫豁免開始時間。", [nameof(ExemptFromTime), nameof(ExemptToTime)]);
    }
}

public sealed class UpdateAttendanceExceptionDraftRequest : CreateAttendanceExceptionDraftRequest
{
    public Guid Id { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public class AttendanceExceptionActionRequest
{
    public Guid Id { get; set; }
    public string RowVersion { get; set; } = string.Empty;
    [StringLength(1000, ErrorMessage = "意見不可超過 1000 個字元。")]
    public string? Comment { get; set; }
}

public sealed class AttendanceExceptionReasonActionRequest : AttendanceExceptionActionRequest, IValidatableObject
{
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (string.IsNullOrWhiteSpace(Comment))
            yield return new ValidationResult("原因為必填欄位。", [nameof(Comment)]);
    }
}
