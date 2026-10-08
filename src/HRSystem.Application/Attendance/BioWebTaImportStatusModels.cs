using HRSystem.Domain.Attendance;

namespace HRSystem.Application.Attendance;

public sealed record BioWebTaImportBatchSummaryDto(
    Guid Id,
    BioWebTaImportTriggerType TriggerType,
    DateTime QueryFromLocal,
    DateTime QueryToLocal,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    BioWebTaImportBatchStatus Status,
    int SourceRowCount,
    int InsertedCount,
    int DuplicateCount,
    int ConflictCount,
    int FailedCount,
    string? SafeErrorSummary)
{
    public bool IsNoChanges =>
        Status == BioWebTaImportBatchStatus.Completed &&
        InsertedCount == 0 &&
        ConflictCount == 0 &&
        FailedCount == 0;

    public TimeSpan? Duration => CompletedAtUtc - StartedAtUtc;
}

public sealed record BioWebTaImportStatusDto(
    bool IsScheduleEnabled,
    int OverlapDays,
    string TimeZoneId,
    IReadOnlyList<TimeOnly> ScheduleTimes,
    DateTimeOffset NextScheduledAtUtc,
    BioWebTaImportBatchSummaryDto? LastScheduled,
    BioWebTaImportBatchSummaryDto? LastManual,
    IReadOnlyList<BioWebTaImportBatchSummaryDto> RecentBatches,
    long UnmappedEventCount);

public interface IBioWebTaImportStatusService
{
    Task<BioWebTaImportStatusDto> GetAsync(
        CancellationToken cancellationToken = default);
}
