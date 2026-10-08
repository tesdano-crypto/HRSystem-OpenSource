using System.ComponentModel.DataAnnotations;

namespace HRSystem.Application.Attendance;

public sealed record BioWebAttendanceSourceRecord(
    long ExternalEventId,
    string SourcePersonPin,
    string? DeviceSerialNumber,
    DateTime EventLocalDateTime,
    int? StatusCode,
    int? VerifyCode,
    DateTime? SourceCreatedTime);

public sealed record BioWebPersonMappingDto(
    Guid Id,
    Guid EmployeeId,
    string EmployeeNumber,
    string EmployeeName,
    string BioWebPin,
    DateTime EffectiveFrom,
    DateTime? EffectiveTo,
    bool IsActive,
    string RowVersion);

public sealed record UnmappedBioWebPinDto(
    string BioWebPin,
    long EventCount,
    DateTime FirstEventLocalDateTime,
    DateTime LastEventLocalDateTime);

public sealed record BioWebRelinkPreviewDto(
    Guid MappingId,
    long RelinkCandidateCount,
    long UnableToRelinkCount);

public sealed record BioWebRelinkResultDto(
    Guid MappingId,
    int RelinkedCount,
    int RecalculatedKeyCount,
    long UnableToRelinkCount,
    bool PunchRecordsRefreshRecommended);

public sealed record AttendanceImportStatusDto(
    string SourceSystem,
    long LastExternalEventId,
    DateTimeOffset? LastAttemptAtUtc,
    DateTimeOffset? LastSuccessfulSyncAtUtc,
    int LastImportedCount,
    string? LastErrorSummary,
    long UnmappedEventCount);

public sealed record AttendanceSyncResultDto(
    string SourceSystem,
    long PreviousExternalEventId,
    long LastExternalEventId,
    int ImportedCount,
    int UnmappedImportedCount,
    long TotalUnmappedEventCount,
    DateTimeOffset CompletedAtUtc);

public enum AttendancePunchMappingStatus
{
    All = 0,
    Mapped = 1,
    Unmapped = 2
}

public sealed record AttendancePunchRecordDto(
    DateTime EventLocalDateTime,
    string? EmployeeNumber,
    string? EmployeeName,
    string SourcePersonPin,
    bool IsMapped,
    string? DeviceSerialNumber,
    int? StatusCode,
    int? VerifyCode,
    long ExternalEventId,
    DateTimeOffset ImportedAtUtc);

public sealed record AttendancePunchEmployeeOptionDto(
    Guid EmployeeId,
    string EmployeeNumber,
    string EmployeeName);

public sealed class AttendancePunchRecordQuery
{
    public DateOnly? DateFrom { get; set; }
    public DateOnly? DateTo { get; set; }
    public Guid? EmployeeId { get; set; }
    public string? EmployeeKeyword { get; set; }
    public string? BioWebPin { get; set; }
    public AttendancePunchMappingStatus MappingStatus { get; set; }
    public string? DeviceSerialNumber { get; set; }
    public int? StatusCode { get; set; }
    public int? VerifyCode { get; set; }
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}

public sealed class CreateBioWebPersonMappingRequest : IValidatableObject
{
    public Guid EmployeeId { get; set; }

    [Required(ErrorMessage = "BioWeb PIN 為必填欄位。")]
    [StringLength(20, ErrorMessage = "BioWeb PIN 不可超過 20 個字元。")]
    public string BioWebPin { get; set; } = string.Empty;

    public DateTime EffectiveFrom { get; set; }
    public DateTime? EffectiveTo { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (EmployeeId == Guid.Empty)
        {
            yield return new ValidationResult(
                "必須指定 HRSystem 員工。",
                [nameof(EmployeeId)]);
        }

        if (EffectiveFrom == default)
        {
            yield return new ValidationResult(
                "生效起始時間為必填欄位。",
                [nameof(EffectiveFrom)]);
        }

        if (EffectiveTo.HasValue && EffectiveTo.Value <= EffectiveFrom)
        {
            yield return new ValidationResult(
                "生效結束時間必須晚於生效起始時間。",
                [nameof(EffectiveTo)]);
        }
    }
}

public sealed class UpdateBioWebPersonMappingRequest : IValidatableObject
{
    public Guid Id { get; set; }
    public DateTime EffectiveFrom { get; set; }
    public DateTime? EffectiveTo { get; set; }
    public string RowVersion { get; set; } = string.Empty;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Id == Guid.Empty)
        {
            yield return new ValidationResult("對照識別碼不可為空值。", [nameof(Id)]);
        }

        if (EffectiveFrom == default)
        {
            yield return new ValidationResult(
                "生效起始時間為必填欄位。",
                [nameof(EffectiveFrom)]);
        }

        if (EffectiveTo.HasValue && EffectiveTo.Value <= EffectiveFrom)
        {
            yield return new ValidationResult(
                "生效結束時間必須晚於生效起始時間。",
                [nameof(EffectiveTo)]);
        }
    }
}

public sealed class AttendanceImportSettings
{
    public AttendanceImportSettings(int batchSize)
    {
        if (batchSize is < 1 or > 1000)
        {
            throw new ArgumentOutOfRangeException(
                nameof(batchSize),
                "BioWebTA 匯入批次大小必須介於 1 與 1000。");
        }

        BatchSize = batchSize;
    }

    public int BatchSize { get; }
}
