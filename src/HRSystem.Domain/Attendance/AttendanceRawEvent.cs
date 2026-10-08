using HRSystem.Domain.Common;
using HRSystem.Domain.MasterData;

namespace HRSystem.Domain.Attendance;

public sealed class AttendanceRawEvent
{
    private AttendanceRawEvent()
    {
    }

    public AttendanceRawEvent(
        Guid id,
        string sourceSystem,
        long externalEventId,
        Guid? employeeId,
        string sourcePersonPin,
        string? deviceSerialNumber,
        DateTime eventLocalDateTime,
        int? statusCode,
        int? verifyCode,
        DateTime? sourceCreatedTime,
        DateTimeOffset importedAtUtc)
    {
        if (externalEventId <= 0)
        {
            throw new DomainValidationException("來源事件 Id 必須大於零。");
        }

        if (employeeId == Guid.Empty)
        {
            throw new DomainValidationException("員工識別碼不可為空值。");
        }

        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        SourceSystem = AttendanceRules.Required(sourceSystem, "來源系統", 50);
        ExternalEventId = externalEventId;
        EmployeeId = employeeId;
        SourcePersonPin = AttendanceRules.Required(sourcePersonPin, "來源人員 PIN", 20);
        DeviceSerialNumber = NormalizeDeviceSerialNumber(
            deviceSerialNumber, "裝置序號", 20);
        EventLocalDateTime = AttendanceRules.SourceLocalTime(
            eventLocalDateTime, "來源事件時間");
        StatusCode = statusCode;
        VerifyCode = verifyCode;
        SourceFingerprintVersion = AttendanceRawEventFingerprintV1.Version;
        SourceFingerprint = AttendanceRawEventFingerprintV1.Compute(
            SourceSystem,
            SourcePersonPin,
            DeviceSerialNumber,
            EventLocalDateTime,
            StatusCode,
            VerifyCode);
        SourceCreatedTime = AttendanceRules.OptionalSourceLocalTime(
            sourceCreatedTime, "來源建立時間");
        ImportedAtUtc = importedAtUtc.ToUniversalTime();
        IsSourceMissing = false;
    }

    public Guid Id { get; private set; }
    public string SourceSystem { get; private set; } = string.Empty;
    public long ExternalEventId { get; private set; }
    public Guid? EmployeeId { get; private set; }
    public string SourcePersonPin { get; private set; } = string.Empty;
    public string? DeviceSerialNumber { get; private set; }
    public DateTime EventLocalDateTime { get; private set; }
    public int? StatusCode { get; private set; }
    public int? VerifyCode { get; private set; }
    public short SourceFingerprintVersion { get; private set; }
    public byte[] SourceFingerprint { get; private set; } = [];
    public DateTime? SourceCreatedTime { get; private set; }
    public DateTimeOffset ImportedAtUtc { get; private set; }
    public bool IsSourceMissing { get; private set; }
    public byte[] RowVersion { get; private set; } = [];
    public Employee? Employee { get; private set; }

    public void Relink(Guid employeeId)
    {
        if (employeeId == Guid.Empty)
        {
            throw new DomainValidationException("員工識別碼不可為空值。");
        }

        if (EmployeeId.HasValue && EmployeeId.Value != employeeId)
        {
            throw new DomainValidationException("已連結的原始事件不可由重新連結流程改派其他員工。");
        }

        EmployeeId = employeeId;
    }

    private static string? NormalizeDeviceSerialNumber(
        string? value,
        string field,
        int maxLength)
    {
        if (value is null)
        {
            return null;
        }

        var normalized = value.Trim();
        if (normalized.Length > maxLength)
        {
            throw new DomainValidationException(
                $"{field} cannot exceed {maxLength} characters.");
        }

        return normalized;
    }
}
