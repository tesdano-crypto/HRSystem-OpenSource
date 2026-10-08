using HRSystem.Domain.Common;

namespace HRSystem.Domain.Attendance;

public sealed class BioWebTaImportBatch
{
    private BioWebTaImportBatch()
    {
    }

    public BioWebTaImportBatch(
        Guid id,
        BioWebTaImportTriggerType triggerType,
        DateTime queryFromLocal,
        DateTime queryToLocal,
        string timeZoneId,
        DateTimeOffset startedAtUtc,
        string hostName,
        int processId,
        string jobVersion)
    {
        if (!Enum.IsDefined(triggerType))
        {
            throw new DomainValidationException("Import trigger type is invalid.");
        }

        ValidateQueryWindow(queryFromLocal, queryToLocal);
        if (processId <= 0)
        {
            throw new DomainValidationException("Import process id must be positive.");
        }

        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        TriggerType = triggerType;
        QueryFromLocal = queryFromLocal;
        QueryToLocal = queryToLocal;
        TimeZoneId = Required(timeZoneId, "Time zone", 64);
        StartedAtUtc = startedAtUtc.ToUniversalTime();
        HostName = Required(hostName, "Host name", 255);
        ProcessId = processId;
        JobVersion = Required(jobVersion, "Job version", 100);
        Status = BioWebTaImportBatchStatus.Running;
    }

    public Guid Id { get; private set; }
    public BioWebTaImportTriggerType TriggerType { get; private set; }
    public DateTime QueryFromLocal { get; private set; }
    public DateTime QueryToLocal { get; private set; }
    public string TimeZoneId { get; private set; } = string.Empty;
    public DateTimeOffset StartedAtUtc { get; private set; }
    public DateTimeOffset? CompletedAtUtc { get; private set; }
    public int SourceRowCount { get; private set; }
    public int InsertedCount { get; private set; }
    public int DuplicateCount { get; private set; }
    public int ConflictCount { get; private set; }
    public int FailedCount { get; private set; }
    public BioWebTaImportBatchStatus Status { get; private set; }
    public string? ErrorSummary { get; private set; }
    public string HostName { get; private set; } = string.Empty;
    public int ProcessId { get; private set; }
    public string JobVersion { get; private set; } = string.Empty;
    public byte[] RowVersion { get; private set; } = [];
    public ICollection<BioWebTaImportBatchIssue> Issues { get; } =
        new List<BioWebTaImportBatchIssue>();

    public void Complete(
        int sourceRowCount,
        int insertedCount,
        int duplicateCount,
        bool completedWithWarnings,
        DateTimeOffset completedAtUtc)
    {
        EnsureRunning();
        ValidateCounts(
            sourceRowCount,
            insertedCount,
            duplicateCount,
            conflictCount: 0,
            failedCount: 0);
        EnsureCompletionTime(completedAtUtc);

        SourceRowCount = sourceRowCount;
        InsertedCount = insertedCount;
        DuplicateCount = duplicateCount;
        ConflictCount = 0;
        FailedCount = 0;
        CompletedAtUtc = completedAtUtc.ToUniversalTime();
        Status = completedWithWarnings
            ? BioWebTaImportBatchStatus.CompletedWithWarnings
            : BioWebTaImportBatchStatus.Completed;
        ErrorSummary = null;
    }

    public void Fail(
        int sourceRowCount,
        int insertedCount,
        int duplicateCount,
        int conflictCount,
        int failedCount,
        string safeErrorSummary,
        DateTimeOffset completedAtUtc)
    {
        EnsureRunning();
        ValidateCounts(
            sourceRowCount,
            insertedCount,
            duplicateCount,
            conflictCount,
            failedCount);
        EnsureCompletionTime(completedAtUtc);
        if (conflictCount == 0 && failedCount == 0)
        {
            throw new DomainValidationException(
                "A failed import batch must record a conflict or failed row.");
        }

        SourceRowCount = sourceRowCount;
        InsertedCount = insertedCount;
        DuplicateCount = duplicateCount;
        ConflictCount = conflictCount;
        FailedCount = failedCount;
        CompletedAtUtc = completedAtUtc.ToUniversalTime();
        Status = BioWebTaImportBatchStatus.Failed;
        ErrorSummary = Required(safeErrorSummary, "Error summary", 1000);
    }

    public void FailBeforeSourceRead(
        string safeErrorSummary,
        DateTimeOffset completedAtUtc)
    {
        EnsureRunning();
        EnsureCompletionTime(completedAtUtc);
        SourceRowCount = 0;
        InsertedCount = 0;
        DuplicateCount = 0;
        ConflictCount = 0;
        FailedCount = 0;
        CompletedAtUtc = completedAtUtc.ToUniversalTime();
        Status = BioWebTaImportBatchStatus.Failed;
        ErrorSummary = Required(safeErrorSummary, "Error summary", 1000);
    }

    public void Abandon(
        string safeErrorSummary,
        DateTimeOffset completedAtUtc)
    {
        EnsureRunning();
        EnsureCompletionTime(completedAtUtc);
        CompletedAtUtc = completedAtUtc.ToUniversalTime();
        Status = BioWebTaImportBatchStatus.Abandoned;
        ErrorSummary = Required(safeErrorSummary, "Error summary", 1000);
    }

    private static void ValidateQueryWindow(
        DateTime queryFromLocal,
        DateTime queryToLocal)
    {
        if (queryFromLocal.Kind != DateTimeKind.Unspecified ||
            queryToLocal.Kind != DateTimeKind.Unspecified ||
            queryToLocal <= queryFromLocal)
        {
            throw new DomainValidationException(
                "Import query window must be a non-empty local half-open interval.");
        }
    }

    private static void ValidateCounts(
        int sourceRowCount,
        int insertedCount,
        int duplicateCount,
        int conflictCount,
        int failedCount)
    {
        if (sourceRowCount < 0 || insertedCount < 0 || duplicateCount < 0 ||
            conflictCount < 0 || failedCount < 0 ||
            insertedCount + duplicateCount + conflictCount + failedCount !=
            sourceRowCount)
        {
            throw new DomainValidationException("Import row counts are invalid.");
        }
    }

    private void EnsureRunning()
    {
        if (Status != BioWebTaImportBatchStatus.Running)
        {
            throw new DomainValidationException(
                "A terminal import batch cannot transition again.");
        }
    }

    private void EnsureCompletionTime(DateTimeOffset completedAtUtc)
    {
        if (completedAtUtc.ToUniversalTime() < StartedAtUtc)
        {
            throw new DomainValidationException(
                "Import completion time cannot precede its start time.");
        }
    }

    private static string Required(
        string? value,
        string field,
        int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainValidationException($"{field} is required.");
        }

        var normalized = value.Trim();
        if (normalized.Length > maximumLength)
        {
            throw new DomainValidationException(
                $"{field} cannot exceed {maximumLength} characters.");
        }

        return normalized;
    }
}
