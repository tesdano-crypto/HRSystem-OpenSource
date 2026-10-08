using System.Data;
using HRSystem.Application.Abstractions.Persistence;
using HRSystem.Application.Abstractions.Security;
using HRSystem.Application.Common.Auditing;
using HRSystem.Application.Common.Exceptions;
using HRSystem.Domain.Attendance;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HRSystem.Application.Attendance;

public sealed class AttendanceImportService(
    IApplicationDbContext dbContext,
    IBioWebTaAttendanceSource source,
    AttendanceImportSettings settings,
    ICurrentUser currentUser,
    TimeProvider timeProvider,
    ILogger<AttendanceImportService> logger,
    IAttendanceRecalculationEngine? attendanceRecalculationEngine = null)
    : IAttendanceImportService
{
    private readonly IAttendanceRecalculationEngine _attendanceRecalculationEngine =
        attendanceRecalculationEngine ??
        new AttendanceRecalculationEngine(dbContext, timeProvider);
    private const string RawEventUniqueIndex =
        "UX_AttendanceRawEvents_SourceSystem_ExternalEventId";

    public async Task<AttendanceImportStatusDto> GetStatusAsync(
        CancellationToken cancellationToken = default)
    {
        AttendanceAuthorization.EnsureManage(currentUser);
        var state = await dbContext.AttendanceSyncStates
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.SourceSystem == AttendanceSourceSystems.BioWebTa,
                cancellationToken);
        var unmapped = await dbContext.AttendanceRawEvents
            .AsNoTracking()
            .LongCountAsync(
                item =>
                    item.SourceSystem == AttendanceSourceSystems.BioWebTa &&
                    item.EmployeeId == null,
                cancellationToken);
        return state is null
            ? new AttendanceImportStatusDto(
                AttendanceSourceSystems.BioWebTa,
                0,
                null,
                null,
                0,
                null,
                unmapped)
            : new AttendanceImportStatusDto(
                state.SourceSystem,
                state.LastExternalEventId,
                state.LastAttemptAtUtc,
                state.LastSuccessfulSyncAtUtc,
                state.LastImportedCount,
                state.LastErrorSummary,
                unmapped);
    }

    public async Task<AttendanceSyncResultDto> SyncNowAsync(
        CancellationToken cancellationToken = default)
    {
        AttendanceAuthorization.EnsureManage(currentUser);
        var startedAtUtc = timeProvider.GetUtcNow();
        var startingCursor = await dbContext.AttendanceSyncStates
            .AsNoTracking()
            .Where(state => state.SourceSystem == AttendanceSourceSystems.BioWebTa)
            .Select(state => (long?)state.LastExternalEventId)
            .SingleOrDefaultAsync(cancellationToken) ?? 0;

        await RecordRequestedAsync(startingCursor, startedAtUtc, cancellationToken);

        try
        {
            var sourceRecords = await source.ReadAfterAsync(
                startingCursor,
                settings.BatchSize,
                cancellationToken);
            ValidateSourceBatch(sourceRecords, startingCursor, settings.BatchSize);
            return await CommitBatchAsync(
                sourceRecords,
                startedAtUtc,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            dbContext.ClearTrackedChanges();
            throw;
        }
        catch (Exception exception)
        {
            dbContext.ClearTrackedChanges();
            var safeSummary = SafeErrorSummary(exception);
            try
            {
                await RecordFailureAsync(
                    safeSummary,
                    timeProvider.GetUtcNow(),
                    CancellationToken.None);
            }
            catch (Exception failureAuditException)
            {
                logger.LogError(
                    "Attendance sync failure audit could not be persisted. Type={ExceptionType}",
                    failureAuditException.GetType().Name);
            }

            LogSafeFailure(exception);
            throw new AttendanceSyncException(safeSummary);
        }
    }

    private Task RecordRequestedAsync(
        long startingCursor,
        DateTimeOffset requestedAtUtc,
        CancellationToken cancellationToken) =>
        dbContext.ExecuteTransactionAsync(
            IsolationLevel.ReadCommitted,
            async transactionCancellationToken =>
            {
                dbContext.AuditLogs.Add(AuditLogFactory.Create(
                    currentUser,
                    timeProvider,
                    AuditActions.AttendanceSyncRequested,
                    nameof(AttendanceSyncState),
                    AttendanceSourceSystems.BioWebTa,
                    null,
                    new
                    {
                        SourceSystem = AttendanceSourceSystems.BioWebTa,
                        StartingCursor = startingCursor,
                        RequestedAtUtc = requestedAtUtc
                    }));
                await dbContext.SaveChangesAsync(transactionCancellationToken);
            },
            cancellationToken);

    private Task<AttendanceSyncResultDto> CommitBatchAsync(
        IReadOnlyList<BioWebAttendanceSourceRecord> sourceRecords,
        DateTimeOffset startedAtUtc,
        CancellationToken cancellationToken) =>
        dbContext.ExecuteSerializableAsync(
            async transactionCancellationToken =>
            {
                var state = await dbContext.AttendanceSyncStates
                    .SingleOrDefaultAsync(
                        item => item.SourceSystem == AttendanceSourceSystems.BioWebTa,
                        transactionCancellationToken);
                if (state is null)
                {
                    state = new AttendanceSyncState(
                        AttendanceSourceSystems.BioWebTa,
                        startedAtUtc);
                    dbContext.AttendanceSyncStates.Add(state);
                }
                else
                {
                    state.MarkAttempt(startedAtUtc);
                }

                var previousCursor = state.LastExternalEventId;
                var eligible = sourceRecords
                    .Where(record => record.ExternalEventId > previousCursor)
                    .ToArray();
                var externalIds = eligible
                    .Select(record => record.ExternalEventId)
                    .ToArray();
                var existingIds = externalIds.Length == 0
                    ? []
                    : await dbContext.AttendanceRawEvents
                        .AsNoTracking()
                        .Where(rawEvent =>
                            rawEvent.SourceSystem == AttendanceSourceSystems.BioWebTa &&
                            externalIds.Contains(rawEvent.ExternalEventId))
                        .Select(rawEvent => rawEvent.ExternalEventId)
                        .ToArrayAsync(transactionCancellationToken);
                var existingSet = existingIds.ToHashSet();

                var pins = eligible
                    .Select(record => record.SourcePersonPin)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
                var mappings = pins.Length == 0
                    ? []
                    : await dbContext.BioWebPersonMappings
                        .AsNoTracking()
                        .Where(mapping =>
                            mapping.IsActive &&
                            pins.Contains(mapping.BioWebPin))
                        .ToArrayAsync(transactionCancellationToken);

                var importedAtUtc = timeProvider.GetUtcNow();
                var newEvents = new List<AttendanceRawEvent>();
                foreach (var record in eligible)
                {
                    if (existingSet.Contains(record.ExternalEventId))
                    {
                        continue;
                    }

                    var applicableMappings = mappings
                        .Where(mapping =>
                            string.Equals(
                                mapping.BioWebPin,
                                record.SourcePersonPin,
                                StringComparison.Ordinal) &&
                            mapping.IsEffectiveAt(record.EventLocalDateTime))
                        .ToArray();
                    if (applicableMappings.Length > 1)
                    {
                        throw new ApplicationValidationException(
                            "BioWebTA 人員對照存在重疊，匯入已安全停止。");
                    }

                    newEvents.Add(new AttendanceRawEvent(
                        Guid.NewGuid(),
                        AttendanceSourceSystems.BioWebTa,
                        record.ExternalEventId,
                        applicableMappings.SingleOrDefault()?.EmployeeId,
                        record.SourcePersonPin,
                        record.DeviceSerialNumber,
                        record.EventLocalDateTime,
                        record.StatusCode,
                        record.VerifyCode,
                        record.SourceCreatedTime,
                        importedAtUtc));
                }

                dbContext.AttendanceRawEvents.AddRange(newEvents);
                var lastCursor = sourceRecords.Count == 0
                    ? previousCursor
                    : Math.Max(
                        previousCursor,
                        sourceRecords[^1].ExternalEventId);
                state.MarkSucceeded(lastCursor, newEvents.Count, importedAtUtc);
                var unmappedImportedCount = newEvents.Count(item => item.EmployeeId == null);
                dbContext.AuditLogs.Add(AuditLogFactory.Create(
                    currentUser,
                    timeProvider,
                    AuditActions.AttendanceSyncCompleted,
                    nameof(AttendanceSyncState),
                    AttendanceSourceSystems.BioWebTa,
                    new { LastExternalEventId = previousCursor },
                    new
                    {
                        LastExternalEventId = lastCursor,
                        ImportedCount = newEvents.Count,
                        UnmappedImportedCount = unmappedImportedCount
                    }));

                await SaveImportAsync(transactionCancellationToken);
                var affectedDates = newEvents
                    .Where(item => item.EmployeeId.HasValue)
                    .Select(item => new AttendanceRecalculationKey(
                        item.EmployeeId!.Value,
                        DateOnly.FromDateTime(item.EventLocalDateTime)))
                    .Distinct()
                    .ToArray();
                if (affectedDates.Length > 0)
                {
                    await _attendanceRecalculationEngine
                        .RecalculateKeysAsync(
                            affectedDates,
                            transactionCancellationToken);
                    await SaveImportAsync(transactionCancellationToken);
                }

                var totalUnmappedCount = await dbContext.AttendanceRawEvents
                    .AsNoTracking()
                    .LongCountAsync(
                        item =>
                            item.SourceSystem == AttendanceSourceSystems.BioWebTa &&
                            item.EmployeeId == null,
                        transactionCancellationToken);
                return new AttendanceSyncResultDto(
                    AttendanceSourceSystems.BioWebTa,
                    previousCursor,
                    lastCursor,
                    newEvents.Count,
                    unmappedImportedCount,
                    totalUnmappedCount,
                    importedAtUtc);
            },
            cancellationToken);

    private Task RecordFailureAsync(
        string safeErrorSummary,
        DateTimeOffset failedAtUtc,
        CancellationToken cancellationToken) =>
        dbContext.ExecuteTransactionAsync(
            IsolationLevel.ReadCommitted,
            async transactionCancellationToken =>
            {
                var state = await dbContext.AttendanceSyncStates
                    .SingleOrDefaultAsync(
                        item => item.SourceSystem == AttendanceSourceSystems.BioWebTa,
                        transactionCancellationToken);
                if (state is null)
                {
                    state = new AttendanceSyncState(
                        AttendanceSourceSystems.BioWebTa,
                        failedAtUtc);
                    dbContext.AttendanceSyncStates.Add(state);
                }

                state.MarkFailed(safeErrorSummary, failedAtUtc);
                dbContext.AuditLogs.Add(AuditLogFactory.Create(
                    currentUser,
                    timeProvider,
                    AuditActions.AttendanceSyncFailed,
                    nameof(AttendanceSyncState),
                    AttendanceSourceSystems.BioWebTa,
                    null,
                    new
                    {
                        state.LastExternalEventId,
                        SafeErrorSummary = safeErrorSummary
                    }));
                await dbContext.SaveChangesAsync(transactionCancellationToken);
            },
            cancellationToken);

    private async Task SaveImportAsync(CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConcurrencyConflictException(
                "另一個匯入程序已更新 BioWebTA 游標，本批次未提交。");
        }
        catch (DbUpdateException exception) when (
            dbContext.IsUniqueConstraintViolation(exception, RawEventUniqueIndex))
        {
            throw new ApplicationValidationException(
                "來源事件已由另一個匯入程序寫入，本批次未提交。");
        }
    }

    private static void ValidateSourceBatch(
        IReadOnlyList<BioWebAttendanceSourceRecord> sourceRecords,
        long startingCursor,
        int batchSize)
    {
        if (sourceRecords.Count > batchSize)
        {
            throw new ApplicationValidationException(
                "BioWebTA 來源回傳筆數超過核准批次上限。");
        }

        var previousId = startingCursor;
        foreach (var record in sourceRecords)
        {
            if (record.ExternalEventId <= previousId)
            {
                throw new ApplicationValidationException(
                    "BioWebTA 來源事件未依 AttLog.Id 嚴格遞增排序。");
            }

            if (string.IsNullOrWhiteSpace(record.SourcePersonPin) ||
                record.SourcePersonPin.Trim().Length > 20 ||
                record.DeviceSerialNumber?.Trim().Length > 20 ||
                record.EventLocalDateTime.Kind != DateTimeKind.Unspecified ||
                record.SourceCreatedTime is { Kind: not DateTimeKind.Unspecified })
            {
                throw new ApplicationValidationException(
                    "BioWebTA 來源事件包含不符合核准格式的欄位。");
            }

            previousId = record.ExternalEventId;
        }
    }

    private static string SafeErrorSummary(Exception exception) => exception switch
    {
        AttendanceSyncException syncException => syncException.Message,
        ConcurrencyConflictException =>
            "另一個匯入程序已更新同步狀態，本批次未提交。",
        ApplicationValidationException validationException =>
            validationException.Message,
        DbUpdateException =>
            "HRSystem 無法原子提交 BioWebTA 匯入批次。",
        _ =>
            "BioWebTA 出勤來源目前無法連線或讀取，請稍後重試並檢查安全診斷紀錄。"
    };

    private void LogSafeFailure(Exception exception)
    {
        logger.LogError(
            "BioWebTA attendance sync failed. Type={ExceptionType}",
            exception.GetType().Name);
    }
}
