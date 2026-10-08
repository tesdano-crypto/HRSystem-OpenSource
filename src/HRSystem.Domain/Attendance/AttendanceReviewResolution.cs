using HRSystem.Domain.Common;
using HRSystem.Domain.MasterData;

namespace HRSystem.Domain.Attendance;

public enum AttendanceReviewAnomalyType : byte
{
    Late = 1,
    EarlyLeave = 2,
    MissingClockIn = 3,
    MissingClockOut = 4,
    MissingBoth = 5,
    ExtendedStay = 6,
    PotentialUnreportedOvertime = 7,
    NonWorkingDayPunch = 8
}

public enum AttendanceReviewResolutionStatus : byte
{
    Resolved = 1,
    Reopened = 2
}

public enum AttendanceReviewResolutionReason : byte
{
    ConfirmedAttendance = 1,
    ApprovedLeave = 2,
    AuthorizedAdjustment = 3,
    AttendanceException = 4,
    DataCorrectionRequired = 5,
    ConfirmedOvertimeWork = 10,
    NonWorkActivity = 11,
    PersonalReason = 12,
    WaitingForTransportation = 13,
    ReturnedToOfficeLater = 14,
    ForgotToClockOutEarlier = 15,
    IncorrectPunch = 16,
    Other = 99
}

public enum AttendanceReviewResolutionHistoryAction : byte
{
    Created = 1,
    Resolved = 2,
    Reopened = 3
}

public sealed class AttendanceReviewResolution
{
    private AttendanceReviewResolution() { }

    public AttendanceReviewResolution(
        Guid id,
        Guid employeeId,
        DateOnly workDate,
        Guid? dailyAttendanceResultId,
        AttendanceReviewAnomalyType anomalyType,
        byte[] sourceFingerprint,
        AttendanceReviewResolutionReason reason,
        string? note,
        string actorUserId,
        DateTimeOffset nowUtc)
    {
        if (employeeId == Guid.Empty || dailyAttendanceResultId == Guid.Empty ||
            (dailyAttendanceResultId is null && anomalyType != AttendanceReviewAnomalyType.NonWorkingDayPunch))
            throw new DomainValidationException("出勤檢核來源資料不完整。");
        ValidateAnomaly(anomalyType);
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        EmployeeId = employeeId;
        WorkDate = workDate;
        DailyAttendanceResultId = dailyAttendanceResultId;
        AnomalyType = anomalyType;
        SourceFingerprint = ValidateFingerprint(sourceFingerprint);
        Resolve(reason, note, actorUserId, nowUtc, true);
        CreatedAtUtc = nowUtc.ToUniversalTime();
    }

    public Guid Id { get; private set; }
    public Guid EmployeeId { get; private set; }
    public DateOnly WorkDate { get; private set; }
    public Guid? DailyAttendanceResultId { get; private set; }
    public AttendanceReviewAnomalyType AnomalyType { get; private set; }
    public byte[] SourceFingerprint { get; private set; } = [];
    public AttendanceReviewResolutionStatus Status { get; private set; }
    public AttendanceReviewResolutionReason Reason { get; private set; }
    public string? Note { get; private set; }
    public string LastActionByUserId { get; private set; } = string.Empty;
    public DateTimeOffset LastActionAtUtc { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public byte[] RowVersion { get; private set; } = [];
    public Employee Employee { get; private set; } = null!;
    public DailyAttendanceResult? DailyAttendanceResult { get; private set; }
    public ICollection<AttendanceReviewResolutionHistory> Histories { get; } =
        new List<AttendanceReviewResolutionHistory>();

    public void Resolve(
        AttendanceReviewResolutionReason reason,
        string? note,
        string actorUserId,
        DateTimeOffset nowUtc,
        bool isInitial = false)
    {
        if (!isInitial && Status != AttendanceReviewResolutionStatus.Reopened)
            throw new DomainValidationException("只有重新開啟的異常可以再次結案。");
        ValidateReason(reason, AnomalyType);
        Reason = reason;
        Note = NormalizeNote(note, reason == AttendanceReviewResolutionReason.Other);
        SourceFingerprint = ValidateFingerprint(SourceFingerprint);
        Status = AttendanceReviewResolutionStatus.Resolved;
        Touch(actorUserId, nowUtc);
    }

    public void ResolveAgain(
        byte[] sourceFingerprint,
        AttendanceReviewResolutionReason reason,
        string? note,
        string actorUserId,
        DateTimeOffset nowUtc)
    {
        SourceFingerprint = ValidateFingerprint(sourceFingerprint);
        Resolve(reason, note, actorUserId, nowUtc);
    }

    public void Reopen(string? note, string actorUserId, DateTimeOffset nowUtc)
    {
        if (Status != AttendanceReviewResolutionStatus.Resolved)
            throw new DomainValidationException("只有已結案的異常可以重新開啟。");
        Note = NormalizeNote(note, false);
        Status = AttendanceReviewResolutionStatus.Reopened;
        Touch(actorUserId, nowUtc);
    }

    private void Touch(string actorUserId, DateTimeOffset nowUtc)
    {
        var actor = actorUserId?.Trim();
        if (string.IsNullOrWhiteSpace(actor) || actor.Length > 450)
            throw new DomainValidationException("異動者資料不合法。");
        LastActionByUserId = actor;
        LastActionAtUtc = nowUtc.ToUniversalTime();
    }

    private static void ValidateAnomaly(AttendanceReviewAnomalyType anomalyType)
    {
        if (!Enum.IsDefined(anomalyType))
            throw new DomainValidationException("出勤異常類型不合法。");
    }

    private static void ValidateReason(
        AttendanceReviewResolutionReason reason,
        AttendanceReviewAnomalyType anomalyType)
    {
        if (!Enum.IsDefined(reason))
            throw new DomainValidationException("結案原因不合法。");
        if (anomalyType == AttendanceReviewAnomalyType.NonWorkingDayPunch &&
            reason is not (AttendanceReviewResolutionReason.NonWorkActivity or
                AttendanceReviewResolutionReason.PersonalReason or
                AttendanceReviewResolutionReason.IncorrectPunch or AttendanceReviewResolutionReason.Other))
            throw new DomainValidationException("非工作日打卡只能使用非加班結案原因；加班須走申請與核准流程。");
        var overtime = anomalyType is AttendanceReviewAnomalyType.ExtendedStay or
            AttendanceReviewAnomalyType.PotentialUnreportedOvertime;
        if (overtime && reason is >= AttendanceReviewResolutionReason.ConfirmedAttendance and
            <= AttendanceReviewResolutionReason.DataCorrectionRequired)
            throw new DomainValidationException("超時異常必須選擇適用的結案原因。");
    }

    private static byte[] ValidateFingerprint(byte[]? fingerprint)
    {
        if (fingerprint is null || fingerprint.Length != 32)
            throw new DomainValidationException("出勤異常來源指紋不合法。");
        return fingerprint.ToArray();
    }

    private static string? NormalizeNote(string? value, bool required)
    {
        var note = value?.Trim();
        if (required && string.IsNullOrWhiteSpace(note))
            throw new DomainValidationException("選擇其他時必須填寫說明。");
        if (note?.Length > 1000)
            throw new DomainValidationException("結案說明不可超過 1000 個字元。");
        return string.IsNullOrWhiteSpace(note) ? null : note;
    }
}
