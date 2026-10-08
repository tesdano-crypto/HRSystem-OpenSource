using HRSystem.Domain.Attendance;

namespace HRSystem.Application.Attendance;

public sealed class BioWebTaScheduledImportOptions
{
    public const string SectionName = "BioWebTA:ScheduledImport";

    public bool Enabled { get; set; }
    public int OverlapDays { get; set; } = 3;
    public int PageSize { get; set; } = 500;
    public int MaxExecutionMinutes { get; set; } = 30;
    public string TimeZoneId { get; set; } = "Asia/Taipei";

    public void Validate()
    {
        if (OverlapDays is < 1 or > 31)
        {
            throw new ArgumentOutOfRangeException(nameof(OverlapDays));
        }

        if (PageSize is < 1 or > 1000)
        {
            throw new ArgumentOutOfRangeException(nameof(PageSize));
        }

        if (MaxExecutionMinutes is < 1 or > 120)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxExecutionMinutes));
        }

        if (string.IsNullOrWhiteSpace(TimeZoneId))
        {
            throw new ArgumentException("Time zone is required.", nameof(TimeZoneId));
        }
    }
}

public sealed record BioWebTaSourceWindowPageRequest(
    DateTime QueryFromLocal,
    DateTime QueryToLocal,
    DateTime? AfterEventLocalDateTime,
    long AfterExternalEventId,
    int PageSize);

public sealed record BioWebTaImportExecutionRequest(
    BioWebTaImportTriggerType TriggerType,
    DateTime? QueryFromLocal = null,
    DateTime? QueryToLocal = null,
    bool DryRun = false,
    int? OverlapDays = null);

public enum BioWebTaImportExecutionOutcome
{
    Completed = 1,
    NoChanges = 2,
    Failed = 3,
    SkippedAlreadyRunning = 4,
    Disabled = 5,
    Preview = 6
}

public sealed record BioWebTaImportExecutionResult(
    Guid? BatchId,
    BioWebTaImportExecutionOutcome Outcome,
    DateTime QueryFromLocal,
    DateTime QueryToLocal,
    int SourceRowCount,
    int InsertedCount,
    int DuplicateCount,
    int ConflictCount,
    int UnmappedCount,
    int AffectedEmployeeDateCount,
    int RecalculatedCount,
    string? SafeErrorSummary,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc);

public sealed record BioWebTaScheduledImportStatusDto(
    bool Enabled,
    int OverlapDays,
    string TimeZoneId,
    DateTimeOffset? LastStartedAtUtc,
    DateTimeOffset? LastCompletedAtUtc,
    BioWebTaImportBatchStatus? LastStatus,
    int LastSourceRowCount,
    int LastInsertedCount,
    int LastDuplicateCount,
    string? LastErrorSummary,
    long UnmappedEventCount);

public interface IBioWebTaImportCoordinator
{
    Task<BioWebTaImportExecutionResult> PreviewAsync(
        BioWebTaImportExecutionRequest request,
        CancellationToken cancellationToken = default);

    Task<BioWebTaImportExecutionResult> RunAsync(
        BioWebTaImportExecutionRequest request,
        CancellationToken cancellationToken = default);

    Task<BioWebTaScheduledImportStatusDto> GetStatusAsync(
        CancellationToken cancellationToken = default);
}

public interface IBioWebTaImportExecutionLock
{
    ValueTask<IAsyncDisposable?> TryAcquireAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken = default);
}
