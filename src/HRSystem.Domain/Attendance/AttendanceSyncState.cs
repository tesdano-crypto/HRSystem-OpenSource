using HRSystem.Domain.Common;

namespace HRSystem.Domain.Attendance;

public sealed class AttendanceSyncState
{
    private AttendanceSyncState()
    {
    }

    public AttendanceSyncState(string sourceSystem, DateTimeOffset attemptedAtUtc)
    {
        SourceSystem = AttendanceRules.Required(sourceSystem, "來源系統", 50);
        LastExternalEventId = 0;
        LastAttemptAtUtc = attemptedAtUtc.ToUniversalTime();
        LastImportedCount = 0;
    }

    public string SourceSystem { get; private set; } = string.Empty;
    public long LastExternalEventId { get; private set; }
    public DateTimeOffset LastAttemptAtUtc { get; private set; }
    public DateTimeOffset? LastSuccessfulSyncAtUtc { get; private set; }
    public int LastImportedCount { get; private set; }
    public string? LastErrorSummary { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public void MarkAttempt(DateTimeOffset attemptedAtUtc)
    {
        LastAttemptAtUtc = attemptedAtUtc.ToUniversalTime();
        LastErrorSummary = null;
    }

    public void MarkSucceeded(
        long lastExternalEventId,
        int importedCount,
        DateTimeOffset completedAtUtc)
    {
        if (lastExternalEventId < LastExternalEventId)
        {
            throw new DomainValidationException("出勤來源游標不可倒退。");
        }

        if (importedCount < 0)
        {
            throw new DomainValidationException("匯入筆數不可小於零。");
        }

        LastExternalEventId = lastExternalEventId;
        LastSuccessfulSyncAtUtc = completedAtUtc.ToUniversalTime();
        LastImportedCount = importedCount;
        LastErrorSummary = null;
    }

    public void MarkFailed(string safeErrorSummary, DateTimeOffset failedAtUtc)
    {
        LastAttemptAtUtc = failedAtUtc.ToUniversalTime();
        LastImportedCount = 0;
        LastErrorSummary = AttendanceRules.Required(
            safeErrorSummary, "安全錯誤摘要", 500);
    }
}
