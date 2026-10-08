using HRSystem.Domain.Common;
using HRSystem.Domain.MasterData;

namespace HRSystem.Domain.Attendance;

public sealed class AttendanceCorrectionRequest
{
    private AttendanceCorrectionRequest() { }

    public AttendanceCorrectionRequest(
        Guid id,
        Guid employeeId,
        DateOnly workDate,
        Guid? attendanceResultId,
        AttendanceCorrectionRequestType requestType,
        DateTime? originalClockInAt,
        DateTime? originalClockOutAt,
        DateTime? proposedClockInAt,
        DateTime? proposedClockOutAt,
        AttendanceCorrectionReason reason,
        string employeeReason,
        byte[] sourceFingerprint,
        DateTimeOffset nowUtc)
    {
        if (employeeId == Guid.Empty)
            throw new DomainValidationException("無法識別申請員工。");
        if (attendanceResultId == Guid.Empty)
            throw new DomainValidationException("出勤結果識別碼不合法。");
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        EmployeeId = employeeId;
        WorkDate = workDate;
        AttendanceResultId = attendanceResultId;
        Status = AttendanceCorrectionRequestStatus.Draft;
        CreatedAtUtc = nowUtc.ToUniversalTime();
        SetDetails(requestType, originalClockInAt, originalClockOutAt,
            proposedClockInAt, proposedClockOutAt, reason, employeeReason,
            sourceFingerprint, nowUtc);
    }

    public Guid Id { get; private set; }
    public Guid EmployeeId { get; private set; }
    public DateOnly WorkDate { get; private set; }
    public Guid? AttendanceResultId { get; private set; }
    public AttendanceCorrectionRequestType RequestType { get; private set; }
    public AttendanceCorrectionRequestStatus Status { get; private set; }
    public DateTime? OriginalClockInAt { get; private set; }
    public DateTime? OriginalClockOutAt { get; private set; }
    public DateTime? ProposedClockInAt { get; private set; }
    public DateTime? ProposedClockOutAt { get; private set; }
    public AttendanceCorrectionReason Reason { get; private set; }
    public string EmployeeReason { get; private set; } = string.Empty;
    public byte[] SourceFingerprint { get; private set; } = [];
    public string? ReviewerNote { get; private set; }
    public DateTimeOffset? SubmittedAtUtc { get; private set; }
    public DateTimeOffset? ApprovedAtUtc { get; private set; }
    public DateTimeOffset? RejectedAtUtc { get; private set; }
    public DateTimeOffset? WithdrawnAtUtc { get; private set; }
    public Guid? AppliedAttendanceAdjustmentId { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }
    public byte[] RowVersion { get; private set; } = [];
    public Employee Employee { get; private set; } = null!;
    public DailyAttendanceResult? AttendanceResult { get; private set; }
    public AttendanceAdjustment? AppliedAttendanceAdjustment { get; private set; }
    public ICollection<AttendanceCorrectionRequestHistory> Histories { get; } =
        new List<AttendanceCorrectionRequestHistory>();

    public bool IsTimeCorrection => RequestType is
        AttendanceCorrectionRequestType.MissingClockIn or
        AttendanceCorrectionRequestType.MissingClockOut or
        AttendanceCorrectionRequestType.MissingBoth or
        AttendanceCorrectionRequestType.ClockInCorrection or
        AttendanceCorrectionRequestType.ClockOutCorrection;

    public void UpdateDraft(
        AttendanceCorrectionRequestType requestType,
        DateTime? proposedClockInAt,
        DateTime? proposedClockOutAt,
        AttendanceCorrectionReason reason,
        string employeeReason,
        byte[] sourceFingerprint,
        DateTimeOffset nowUtc)
    {
        EnsureStatus(AttendanceCorrectionRequestStatus.Draft,
            "只有草稿可以修改。");
        SetDetails(requestType, OriginalClockInAt, OriginalClockOutAt,
            proposedClockInAt, proposedClockOutAt, reason, employeeReason,
            sourceFingerprint, nowUtc);
    }

    public void Submit(DateTimeOffset nowUtc)
    {
        EnsureStatus(AttendanceCorrectionRequestStatus.Draft,
            "只有草稿可以送出。");
        ValidateDetails(RequestType, ProposedClockInAt, ProposedClockOutAt,
            Reason, EmployeeReason, SourceFingerprint);
        Status = AttendanceCorrectionRequestStatus.Submitted;
        SubmittedAtUtc = nowUtc.ToUniversalTime();
        Touch(nowUtc);
    }

    public void Approve(
        Guid? attendanceAdjustmentId,
        string? reviewerNote,
        DateTimeOffset nowUtc)
    {
        EnsureStatus(AttendanceCorrectionRequestStatus.Submitted,
            "只有待審核申請可以核准。");
        if (IsTimeCorrection &&
            (!attendanceAdjustmentId.HasValue ||
             attendanceAdjustmentId.Value == Guid.Empty))
            throw new DomainValidationException("時間更正核准必須建立出勤調整。");
        if (!IsTimeCorrection && attendanceAdjustmentId.HasValue)
            throw new DomainValidationException("說明類申請不得建立出勤調整。");
        ReviewerNote = NormalizeOptional(reviewerNote, 1000, "審核說明");
        AppliedAttendanceAdjustmentId = attendanceAdjustmentId;
        Status = AttendanceCorrectionRequestStatus.Approved;
        ApprovedAtUtc = nowUtc.ToUniversalTime();
        Touch(nowUtc);
    }

    public void Reject(string reviewerNote, DateTimeOffset nowUtc)
    {
        EnsureStatus(AttendanceCorrectionRequestStatus.Submitted,
            "只有待審核申請可以駁回。");
        ReviewerNote = Required(reviewerNote, 1000, "駁回說明");
        Status = AttendanceCorrectionRequestStatus.Rejected;
        RejectedAtUtc = nowUtc.ToUniversalTime();
        Touch(nowUtc);
    }

    public void Withdraw(string? note, DateTimeOffset nowUtc)
    {
        EnsureStatus(AttendanceCorrectionRequestStatus.Submitted,
            "只有待審核申請可以撤回。");
        ReviewerNote = NormalizeOptional(note, 1000, "撤回說明");
        Status = AttendanceCorrectionRequestStatus.Withdrawn;
        WithdrawnAtUtc = nowUtc.ToUniversalTime();
        Touch(nowUtc);
    }

    private void SetDetails(
        AttendanceCorrectionRequestType requestType,
        DateTime? originalClockInAt,
        DateTime? originalClockOutAt,
        DateTime? proposedClockInAt,
        DateTime? proposedClockOutAt,
        AttendanceCorrectionReason reason,
        string employeeReason,
        byte[] sourceFingerprint,
        DateTimeOffset nowUtc)
    {
        ValidateDetails(requestType, proposedClockInAt, proposedClockOutAt,
            reason, employeeReason, sourceFingerprint);
        RequestType = requestType;
        OriginalClockInAt = Local(originalClockInAt);
        OriginalClockOutAt = Local(originalClockOutAt);
        ProposedClockInAt = Local(proposedClockInAt);
        ProposedClockOutAt = Local(proposedClockOutAt);
        Reason = reason;
        EmployeeReason = Required(employeeReason, 1000, "申請說明");
        SourceFingerprint = sourceFingerprint.ToArray();
        Touch(nowUtc);
    }

    private static void ValidateDetails(
        AttendanceCorrectionRequestType requestType,
        DateTime? proposedClockInAt,
        DateTime? proposedClockOutAt,
        AttendanceCorrectionReason reason,
        string? employeeReason,
        byte[]? sourceFingerprint)
    {
        if (!Enum.IsDefined(requestType))
            throw new DomainValidationException("更正申請類型不合法。");
        if (!Enum.IsDefined(reason))
            throw new DomainValidationException("請選擇有效的申請原因。");
        _ = Required(employeeReason, 1000, "申請說明");
        if (sourceFingerprint is not { Length: 32 })
            throw new DomainValidationException("來源出勤指紋不合法。");

        var validTimes = requestType switch
        {
            AttendanceCorrectionRequestType.MissingClockIn or
            AttendanceCorrectionRequestType.ClockInCorrection =>
                proposedClockInAt.HasValue && !proposedClockOutAt.HasValue,
            AttendanceCorrectionRequestType.MissingClockOut or
            AttendanceCorrectionRequestType.ClockOutCorrection =>
                !proposedClockInAt.HasValue && proposedClockOutAt.HasValue,
            AttendanceCorrectionRequestType.MissingBoth =>
                proposedClockInAt.HasValue && proposedClockOutAt.HasValue,
            AttendanceCorrectionRequestType.LateExplanation or
            AttendanceCorrectionRequestType.EarlyLeaveExplanation =>
                !proposedClockInAt.HasValue && !proposedClockOutAt.HasValue,
            _ => false
        };
        if (!validTimes)
            throw new DomainValidationException("建議出勤時間與申請類型不一致。");
        if (proposedClockInAt.HasValue &&
            proposedClockInAt.Value.Kind != DateTimeKind.Unspecified ||
            proposedClockOutAt.HasValue &&
            proposedClockOutAt.Value.Kind != DateTimeKind.Unspecified)
            throw new DomainValidationException("建議時間必須使用台北本地時間。");
    }

    private void EnsureStatus(
        AttendanceCorrectionRequestStatus expected,
        string message)
    {
        if (Status != expected) throw new DomainValidationException(message);
    }

    private void Touch(DateTimeOffset nowUtc) =>
        UpdatedAtUtc = nowUtc.ToUniversalTime();

    private static DateTime? Local(DateTime? value) => value.HasValue
        ? DateTime.SpecifyKind(value.Value, DateTimeKind.Unspecified)
        : null;

    private static string Required(string? value, int maxLength, string field)
    {
        var text = value?.Trim();
        if (string.IsNullOrWhiteSpace(text))
            throw new DomainValidationException($"{field}為必填欄位。");
        if (text.Length > maxLength)
            throw new DomainValidationException($"{field}不可超過 {maxLength} 個字元。");
        return text;
    }

    private static string? NormalizeOptional(
        string? value,
        int maxLength,
        string field)
    {
        var text = value?.Trim();
        if (text?.Length > maxLength)
            throw new DomainValidationException($"{field}不可超過 {maxLength} 個字元。");
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }
}
