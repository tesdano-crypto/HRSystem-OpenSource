using HRSystem.Application.Abstractions.Persistence;
using HRSystem.Application.Abstractions.Security;
using HRSystem.Domain.Attendance;
using Microsoft.EntityFrameworkCore;

namespace HRSystem.Application.Attendance;

public sealed class BioWebTaImportStatusService(
    IApplicationDbContext dbContext,
    BioWebTaScheduledImportOptions options,
    ICurrentUser currentUser,
    TimeProvider timeProvider) : IBioWebTaImportStatusService
{
    private const int RecentBatchLimit = 15;

    public async Task<BioWebTaImportStatusDto> GetAsync(
        CancellationToken cancellationToken = default)
    {
        AttendanceAuthorization.EnsureManage(currentUser);
        options.Validate();

        var recent = await Project(Batches()
            .OrderByDescending(item => item.StartedAtUtc)
            .Take(RecentBatchLimit))
            .ToArrayAsync(cancellationToken);
        var lastScheduled = await Project(Batches()
            .Where(item => item.TriggerType == BioWebTaImportTriggerType.Scheduled)
            .OrderByDescending(item => item.StartedAtUtc)
            .Take(1))
            .FirstOrDefaultAsync(cancellationToken);
        var lastManual = await Project(Batches()
            .Where(item => item.TriggerType == BioWebTaImportTriggerType.Manual)
            .OrderByDescending(item => item.StartedAtUtc)
            .Take(1))
            .FirstOrDefaultAsync(cancellationToken);
        var unmapped = await dbContext.AttendanceRawEvents
            .AsNoTracking()
            .LongCountAsync(item =>
                item.SourceSystem == AttendanceSourceSystems.BioWebTa &&
                item.EmployeeId == null,
                cancellationToken);

        return new BioWebTaImportStatusDto(
            options.Enabled,
            options.OverlapDays,
            options.TimeZoneId,
            BioWebTaImportSchedule.WeekdayRunTimes,
            BioWebTaImportSchedule.GetNextRunAtUtc(
                timeProvider.GetUtcNow(),
                options.TimeZoneId),
            lastScheduled,
            lastManual,
            recent,
            unmapped);
    }

    private IQueryable<BioWebTaImportBatch> Batches() =>
        dbContext.BioWebTaImportBatches
            .AsNoTracking();

    private static IQueryable<BioWebTaImportBatchSummaryDto> Project(
        IQueryable<BioWebTaImportBatch> query) =>
        query.Select(item => new BioWebTaImportBatchSummaryDto(
                item.Id,
                item.TriggerType,
                item.QueryFromLocal,
                item.QueryToLocal,
                item.StartedAtUtc,
                item.CompletedAtUtc,
                item.Status,
                item.SourceRowCount,
                item.InsertedCount,
                item.DuplicateCount,
                item.ConflictCount,
                item.FailedCount,
                item.ErrorSummary));
}
