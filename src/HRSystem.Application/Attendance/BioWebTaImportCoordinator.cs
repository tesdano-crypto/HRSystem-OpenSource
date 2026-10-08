using System.Data;
using System.Reflection;
using HRSystem.Application.Abstractions.Persistence;
using HRSystem.Application.Abstractions.Security;
using HRSystem.Application.Common.Auditing;
using HRSystem.Application.Common.Exceptions;
using HRSystem.Domain.Attendance;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HRSystem.Application.Attendance;

public sealed class BioWebTaImportCoordinator(
    IApplicationDbContext dbContext,
    IBioWebTaAttendanceSource source,
    IBioWebTaImportExecutionLock executionLock,
    IAttendanceRecalculationEngine recalculationEngine,
    BioWebTaScheduledImportOptions options,
    ICurrentUser currentUser,
    TimeProvider timeProvider,
    ILogger<BioWebTaImportCoordinator> logger) : IBioWebTaImportCoordinator
{
    private const string FingerprintUniqueIndex =
        "UX_AttendanceRawEvents_SourceSystem_Fingerprint";
    private const string ExternalIdUniqueIndex =
        "UX_AttendanceRawEvents_SourceSystem_ExternalEventId";

    public async Task<BioWebTaScheduledImportStatusDto> GetStatusAsync(
        CancellationToken cancellationToken = default)
    {
        AttendanceAuthorization.EnsureManage(currentUser);
        options.Validate();
        var latest = await dbContext.BioWebTaImportBatches
            .AsNoTracking()
            .OrderByDescending(item => item.StartedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
        var unmapped = await dbContext.AttendanceRawEvents
            .AsNoTracking()
            .LongCountAsync(item =>
                item.SourceSystem == AttendanceSourceSystems.BioWebTa &&
                item.EmployeeId == null,
                cancellationToken);
        return new BioWebTaScheduledImportStatusDto(
            options.Enabled,
            options.OverlapDays,
            options.TimeZoneId,
            latest?.StartedAtUtc,
            latest?.CompletedAtUtc,
            latest?.Status,
            latest?.SourceRowCount ?? 0,
            latest?.InsertedCount ?? 0,
            latest?.DuplicateCount ?? 0,
            latest?.ErrorSummary,
            unmapped);
    }

    public async Task<BioWebTaImportExecutionResult> PreviewAsync(
        BioWebTaImportExecutionRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureAuthorized(request.TriggerType);
        options.Validate();
        var startedAt = timeProvider.GetUtcNow();
        var window = ResolveWindow(request, startedAt);
        var sourceRecords = await ReadWindowAsync(window, cancellationToken);
        var plan = await BuildPlanAsync(sourceRecords, cancellationToken);
        var completedAt = timeProvider.GetUtcNow();
        return Result(
            null,
            BioWebTaImportExecutionOutcome.Preview,
            window,
            plan,
            affectedCount: plan.NewRows
                .Where(item => item.EmployeeId.HasValue)
                .Select(item => new AttendanceRecalculationKey(
                    item.EmployeeId!.Value,
                    DateOnly.FromDateTime(item.Record.EventLocalDateTime)))
                .Distinct()
                .Count(),
            recalculatedCount: 0,
            error: plan.Conflicts.Count == 0
                ? null
                : "Source event content mismatch.",
            startedAt,
            completedAt);
    }

    public async Task<BioWebTaImportExecutionResult> RunAsync(
        BioWebTaImportExecutionRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureAuthorized(request.TriggerType);
        options.Validate();
        var startedAt = timeProvider.GetUtcNow();
        var window = ResolveWindow(request, startedAt);
        if (request.DryRun)
        {
            return await PreviewAsync(request, cancellationToken);
        }

        if (request.TriggerType == BioWebTaImportTriggerType.Scheduled &&
            !options.Enabled)
        {
            return EmptyResult(
                BioWebTaImportExecutionOutcome.Disabled,
                window,
                startedAt,
                startedAt);
        }

        await using var lease = await executionLock.TryAcquireAsync(
            TimeSpan.Zero,
            cancellationToken);
        if (lease is null)
        {
            return EmptyResult(
                BioWebTaImportExecutionOutcome.SkippedAlreadyRunning,
                window,
                startedAt,
                timeProvider.GetUtcNow());
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(options.MaxExecutionMinutes));

        IReadOnlyList<BioWebAttendanceSourceRecord> sourceRecords;
        try
        {
            sourceRecords = await ReadWindowAsync(window, timeout.Token);
        }
        catch (OperationCanceledException) when (
            timeout.IsCancellationRequested &&
            !cancellationToken.IsCancellationRequested)
        {
            return await PersistTimeoutResultAsync(
                request.TriggerType,
                window,
                0,
                startedAt);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            var safeError = SafeErrorSummary(exception);
            var batchId = await PersistFailedBatchAsync(
                request.TriggerType,
                window,
                0,
                safeError,
                startedAt,
                CancellationToken.None);
            LogFailure(exception, batchId);
            return new BioWebTaImportExecutionResult(
                batchId,
                BioWebTaImportExecutionOutcome.Failed,
                window.From,
                window.To,
                0, 0, 0, 0, 0, 0, 0,
                safeError,
                startedAt,
                timeProvider.GetUtcNow());
        }

        ImportPlan plan;
        try
        {
            plan = await BuildPlanAsync(sourceRecords, timeout.Token);
        }
        catch (OperationCanceledException) when (
            timeout.IsCancellationRequested &&
            !cancellationToken.IsCancellationRequested)
        {
            return await PersistTimeoutResultAsync(
                request.TriggerType,
                window,
                sourceRecords.Count,
                startedAt);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            var safeError = SafeErrorSummary(exception);
            var batchId = await PersistFailedBatchAsync(
                request.TriggerType,
                window,
                sourceRecords.Count,
                safeError,
                startedAt,
                CancellationToken.None);
            LogFailure(exception, batchId);
            return new BioWebTaImportExecutionResult(
                batchId,
                BioWebTaImportExecutionOutcome.Failed,
                window.From,
                window.To,
                sourceRecords.Count, 0, 0, 0, 0, 0, 0,
                safeError,
                startedAt,
                timeProvider.GetUtcNow());
        }

        try
        {
            return await CommitPlanAsync(
                request.TriggerType,
                window,
                plan,
                startedAt,
                timeout.Token);
        }
        catch (OperationCanceledException) when (
            timeout.IsCancellationRequested &&
            !cancellationToken.IsCancellationRequested)
        {
            dbContext.ClearTrackedChanges();
            return await PersistTimeoutResultAsync(
                request.TriggerType,
                window,
                plan.SourceCount,
                startedAt);
        }
    }

    private async Task<BioWebTaImportExecutionResult> CommitPlanAsync(
        BioWebTaImportTriggerType triggerType,
        ImportWindow window,
        ImportPlan plan,
        DateTimeOffset startedAt,
        CancellationToken cancellationToken)
    {
        try
        {
            return await dbContext.ExecuteTransactionAsync(
                IsolationLevel.Serializable,
                async transactionToken =>
                {
                    var batch = NewBatch(triggerType, window, startedAt);
                    dbContext.BioWebTaImportBatches.Add(batch);
                    if (triggerType == BioWebTaImportTriggerType.Manual)
                    {
                        dbContext.AuditLogs.Add(AuditLogFactory.Create(
                            currentUser,
                            timeProvider,
                            AuditActions.BioWebTaImportRunNowRequested,
                            nameof(BioWebTaImportBatch),
                            batch.Id.ToString(),
                            null,
                            new
                            {
                                batch.Id,
                                TriggerType = triggerType.ToString(),
                                window.From,
                                window.To
                            }));
                    }

                    if (plan.Conflicts.Count > 0)
                    {
                        foreach (var conflict in plan.Conflicts)
                        {
                            dbContext.BioWebTaImportBatchIssues.Add(
                                new BioWebTaImportBatchIssue(
                                    Guid.NewGuid(),
                                    batch.Id,
                                    conflict.Record.ExternalEventId,
                                    AttendanceRawEventFingerprintV1.Version,
                                    conflict.Fingerprint,
                                    conflict.ExistingFingerprint,
                                    BioWebTaImportIssueCode.SourceEventContentMismatch,
                                    "Source event content mismatch.",
                                    timeProvider.GetUtcNow()));
                        }

                        var failedCount = plan.SourceCount -
                            plan.DuplicateCount - plan.Conflicts.Count;
                        batch.Fail(
                            plan.SourceCount,
                            0,
                            plan.DuplicateCount,
                            plan.Conflicts.Count,
                            failedCount,
                            "Source event content mismatch.",
                            timeProvider.GetUtcNow());
                        await dbContext.SaveChangesAsync(transactionToken);
                        return new BioWebTaImportExecutionResult(
                            batch.Id,
                            BioWebTaImportExecutionOutcome.Failed,
                            window.From,
                            window.To,
                            plan.SourceCount,
                            0,
                            plan.DuplicateCount,
                            plan.Conflicts.Count,
                            0,
                            0,
                            0,
                            batch.ErrorSummary,
                            startedAt,
                            batch.CompletedAtUtc!.Value);
                    }

                    var importedAt = timeProvider.GetUtcNow();
                    var entities = plan.NewRows.Select(item =>
                        new AttendanceRawEvent(
                            Guid.NewGuid(),
                            AttendanceSourceSystems.BioWebTa,
                            item.Record.ExternalEventId,
                            item.EmployeeId,
                            item.Record.SourcePersonPin,
                            item.Record.DeviceSerialNumber,
                            item.Record.EventLocalDateTime,
                            item.Record.StatusCode,
                            item.Record.VerifyCode,
                            item.Record.SourceCreatedTime,
                            importedAt)).ToArray();
                    dbContext.AttendanceRawEvents.AddRange(entities);
                    await dbContext.SaveChangesAsync(transactionToken);

                    var affectedKeys = entities
                        .Where(item => item.EmployeeId.HasValue)
                        .Select(item => new AttendanceRecalculationKey(
                            item.EmployeeId!.Value,
                            DateOnly.FromDateTime(item.EventLocalDateTime)))
                        .Distinct()
                        .ToArray();
                    var recalculatedCount = 0;
                    if (affectedKeys.Length > 0)
                    {
                        var recalculation = await recalculationEngine
                            .RecalculateKeysAsync(affectedKeys, transactionToken);
                        recalculatedCount = recalculation.ResultCount;
                    }

                    var unmappedCount = entities.Count(item => item.EmployeeId == null);
                    batch.Complete(
                        plan.SourceCount,
                        entities.Length,
                        plan.DuplicateCount,
                        completedWithWarnings: unmappedCount > 0,
                        timeProvider.GetUtcNow());
                    await dbContext.SaveChangesAsync(transactionToken);
                    var outcome = entities.Length == 0
                        ? BioWebTaImportExecutionOutcome.NoChanges
                        : BioWebTaImportExecutionOutcome.Completed;
                    logger.LogInformation(
                        "BioWebTA import completed. BatchId={BatchId} Scanned={Scanned} Added={Added} Duplicate={Duplicate} Unmapped={Unmapped} AffectedDates={AffectedDates} Result={Result}",
                        batch.Id,
                        plan.SourceCount,
                        entities.Length,
                        plan.DuplicateCount,
                        unmappedCount,
                        affectedKeys.Length,
                        outcome);
                    return Result(
                        batch.Id,
                        outcome,
                        window,
                        plan,
                        affectedKeys.Length,
                        recalculatedCount,
                        null,
                        startedAt,
                        batch.CompletedAtUtc!.Value,
                        unmappedCount);
                },
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            dbContext.ClearTrackedChanges();
            var safeError = exception is DbUpdateException updateException &&
                (dbContext.IsUniqueConstraintViolation(
                    updateException, FingerprintUniqueIndex) ||
                 dbContext.IsUniqueConstraintViolation(
                    updateException, ExternalIdUniqueIndex))
                ? "BioWebTA import encountered a concurrent source identity conflict."
                : SafeErrorSummary(exception);
            var failedBatchId = await PersistFailedBatchAsync(
                triggerType,
                window,
                plan.SourceCount,
                safeError,
                startedAt,
                CancellationToken.None);
            LogFailure(exception, failedBatchId);
            return new BioWebTaImportExecutionResult(
                failedBatchId,
                BioWebTaImportExecutionOutcome.Failed,
                window.From,
                window.To,
                plan.SourceCount,
                0,
                plan.DuplicateCount,
                plan.Conflicts.Count,
                0,
                0,
                0,
                safeError,
                startedAt,
                timeProvider.GetUtcNow());
        }
    }

    private async Task<ImportPlan> BuildPlanAsync(
        IReadOnlyList<BioWebAttendanceSourceRecord> records,
        CancellationToken cancellationToken)
    {
        ValidateSourceRecords(records);
        var existing = await dbContext.AttendanceRawEvents
            .AsNoTracking()
            .Where(item => item.SourceSystem == AttendanceSourceSystems.BioWebTa)
            .Select(item => new
            {
                item.ExternalEventId,
                item.SourceFingerprint
            })
            .ToListAsync(cancellationToken);
        var byExternalId = existing.ToDictionary(
            item => item.ExternalEventId,
            item => item.SourceFingerprint);
        var fingerprints = existing
            .Select(item => AttendanceRawEventFingerprintV1.ToHex(
                item.SourceFingerprint))
            .ToHashSet(StringComparer.Ordinal);

        var pins = records.Select(item => item.SourcePersonPin.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var mappings = pins.Length == 0
            ? []
            : await dbContext.BioWebPersonMappings
                .AsNoTracking()
                .Where(item => item.IsActive && pins.Contains(item.BioWebPin))
                .ToArrayAsync(cancellationToken);

        var newRows = new List<PlannedRow>();
        var conflicts = new List<ConflictRow>();
        var duplicateCount = 0;
        var incomingExternalIds = new Dictionary<long, string>();
        foreach (var record in records)
        {
            var fingerprint = AttendanceRawEventFingerprintV1.Compute(
                AttendanceSourceSystems.BioWebTa,
                record.SourcePersonPin,
                record.DeviceSerialNumber,
                record.EventLocalDateTime,
                record.StatusCode,
                record.VerifyCode);
            var hex = AttendanceRawEventFingerprintV1.ToHex(fingerprint);
            if (incomingExternalIds.TryGetValue(record.ExternalEventId, out var priorHex) &&
                !string.Equals(priorHex, hex, StringComparison.Ordinal))
            {
                conflicts.Add(new ConflictRow(record, fingerprint, null));
                continue;
            }

            incomingExternalIds[record.ExternalEventId] = hex;
            if (byExternalId.TryGetValue(record.ExternalEventId, out var existingFingerprint))
            {
                if (!existingFingerprint.AsSpan().SequenceEqual(fingerprint))
                {
                    conflicts.Add(new ConflictRow(
                        record,
                        fingerprint,
                        existingFingerprint));
                }
                else
                {
                    duplicateCount++;
                }
                continue;
            }

            if (!fingerprints.Add(hex))
            {
                duplicateCount++;
                continue;
            }

            var applicable = mappings.Where(item =>
                    string.Equals(
                        item.BioWebPin,
                        record.SourcePersonPin.Trim(),
                        StringComparison.Ordinal) &&
                    item.IsEffectiveAt(record.EventLocalDateTime))
                .ToArray();
            if (applicable.Length > 1)
            {
                throw new ApplicationValidationException(
                    "Multiple active BioWebTA mappings match a source event.");
            }

            newRows.Add(new PlannedRow(
                record,
                fingerprint,
                applicable.SingleOrDefault()?.EmployeeId));
        }

        return new ImportPlan(records.Count, duplicateCount, newRows, conflicts);
    }

    private async Task<IReadOnlyList<BioWebAttendanceSourceRecord>> ReadWindowAsync(
        ImportWindow window,
        CancellationToken cancellationToken)
    {
        var records = new List<BioWebAttendanceSourceRecord>();
        DateTime? afterTime = null;
        long afterId = 0;
        while (true)
        {
            var page = await source.ReadWindowPageAsync(
                new BioWebTaSourceWindowPageRequest(
                    window.From,
                    window.To,
                    afterTime,
                    afterId,
                    options.PageSize),
                cancellationToken);
            if (page.Count == 0)
            {
                break;
            }

            ValidatePageOrder(page, afterTime, afterId);
            records.AddRange(page);
            var last = page[^1];
            afterTime = last.EventLocalDateTime;
            afterId = last.ExternalEventId;
            if (page.Count < options.PageSize)
            {
                break;
            }
        }

        return records;
    }

    private async Task<Guid> PersistFailedBatchAsync(
        BioWebTaImportTriggerType triggerType,
        ImportWindow window,
        int sourceRowCount,
        string safeError,
        DateTimeOffset startedAt,
        CancellationToken cancellationToken)
    {
        return await dbContext.ExecuteTransactionAsync(
            IsolationLevel.ReadCommitted,
            async transactionToken =>
            {
                var batch = NewBatch(triggerType, window, startedAt);
                if (sourceRowCount == 0)
                {
                    batch.FailBeforeSourceRead(
                        safeError,
                        timeProvider.GetUtcNow());
                }
                else
                {
                    batch.Fail(
                        sourceRowCount,
                        0,
                        0,
                        0,
                        sourceRowCount,
                        safeError,
                        timeProvider.GetUtcNow());
                }
                dbContext.BioWebTaImportBatches.Add(batch);
                await dbContext.SaveChangesAsync(transactionToken);
                return batch.Id;
            },
            cancellationToken);
    }

    private async Task<BioWebTaImportExecutionResult> PersistTimeoutResultAsync(
        BioWebTaImportTriggerType triggerType,
        ImportWindow window,
        int sourceRowCount,
        DateTimeOffset startedAt)
    {
        const string safeError =
            "BioWebTA import exceeded its execution time limit.";
        var batchId = await PersistFailedBatchAsync(
            triggerType,
            window,
            sourceRowCount,
            safeError,
            startedAt,
            CancellationToken.None);
        logger.LogError(
            "BioWebTA import timed out. BatchId={BatchId}",
            batchId);
        return new BioWebTaImportExecutionResult(
            batchId,
            BioWebTaImportExecutionOutcome.Failed,
            window.From,
            window.To,
            sourceRowCount,
            0,
            0,
            0,
            0,
            0,
            0,
            safeError,
            startedAt,
            timeProvider.GetUtcNow());
    }

    private BioWebTaImportBatch NewBatch(
        BioWebTaImportTriggerType triggerType,
        ImportWindow window,
        DateTimeOffset startedAt) =>
        new(
            Guid.NewGuid(),
            triggerType,
            window.From,
            window.To,
            options.TimeZoneId,
            startedAt,
            Environment.MachineName,
            Environment.ProcessId,
            Assembly.GetEntryAssembly()?.GetCustomAttribute<
                AssemblyInformationalVersionAttribute>()?.InformationalVersion ??
            "unknown");

    private ImportWindow ResolveWindow(
        BioWebTaImportExecutionRequest request,
        DateTimeOffset startedAtUtc)
    {
        if (request.QueryFromLocal.HasValue != request.QueryToLocal.HasValue)
        {
            throw new ApplicationValidationException(
                "Both source-window boundaries are required.");
        }

        if (request.QueryFromLocal.HasValue)
        {
            return new ImportWindow(
                request.QueryFromLocal.Value,
                request.QueryToLocal!.Value);
        }

        var overlapDays = request.OverlapDays ?? options.OverlapDays;
        if (overlapDays is < 1 or > 31)
        {
            throw new ApplicationValidationException(
                "Overlap days must be between 1 and 31.");
        }

        var zone = ResolveTimeZone(options.TimeZoneId);
        var startedLocal = TimeZoneInfo.ConvertTime(startedAtUtc, zone).DateTime;
        var from = startedLocal.Date.AddDays(-(overlapDays - 1));
        return new ImportWindow(
            DateTime.SpecifyKind(from, DateTimeKind.Unspecified),
            DateTime.SpecifyKind(startedLocal, DateTimeKind.Unspecified));
    }

    private void EnsureAuthorized(BioWebTaImportTriggerType triggerType)
    {
        if (!Enum.IsDefined(triggerType))
        {
            throw new ApplicationValidationException("Import trigger is invalid.");
        }

        AttendanceAuthorization.EnsureManage(currentUser);
    }

    private static void ValidateSourceRecords(
        IReadOnlyList<BioWebAttendanceSourceRecord> records)
    {
        foreach (var record in records)
        {
            if (record.ExternalEventId <= 0 ||
                string.IsNullOrWhiteSpace(record.SourcePersonPin) ||
                record.SourcePersonPin.Trim().Length > 20 ||
                record.DeviceSerialNumber?.Trim().Length > 20 ||
                record.EventLocalDateTime.Kind != DateTimeKind.Unspecified ||
                record.SourceCreatedTime is { Kind: not DateTimeKind.Unspecified })
            {
                throw new ApplicationValidationException(
                    "BioWebTA returned an invalid source record.");
            }
        }
    }

    private static void ValidatePageOrder(
        IReadOnlyList<BioWebAttendanceSourceRecord> page,
        DateTime? afterTime,
        long afterId)
    {
        var cursorTime = afterTime;
        var cursorId = afterId;
        foreach (var record in page)
        {
            if (cursorTime.HasValue &&
                (record.EventLocalDateTime < cursorTime.Value ||
                 (record.EventLocalDateTime == cursorTime.Value &&
                  record.ExternalEventId <= cursorId)))
            {
                throw new ApplicationValidationException(
                    "BioWebTA source pagination order is invalid.");
            }

            cursorTime = record.EventLocalDateTime;
            cursorId = record.ExternalEventId;
        }
    }

    private static TimeZoneInfo ResolveTimeZone(string id)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (TimeZoneNotFoundException) when (id == "Asia/Taipei")
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Taipei Standard Time");
        }
    }

    private static string SafeErrorSummary(Exception exception) => exception switch
    {
        AttendanceSyncException sync => sync.Message,
        ApplicationValidationException validation => validation.Message,
        OperationCanceledException => "BioWebTA import was cancelled or timed out.",
        _ => "BioWebTA import failed safely."
    };

    private void LogFailure(Exception exception, Guid batchId) =>
        logger.LogError(
            "BioWebTA import failed. BatchId={BatchId} ExceptionType={ExceptionType}",
            batchId,
            exception.GetType().Name);

    private static BioWebTaImportExecutionResult EmptyResult(
        BioWebTaImportExecutionOutcome outcome,
        ImportWindow window,
        DateTimeOffset started,
        DateTimeOffset completed) =>
        new(null, outcome, window.From, window.To, 0, 0, 0, 0, 0, 0, 0,
            null, started, completed);

    private static BioWebTaImportExecutionResult Result(
        Guid? batchId,
        BioWebTaImportExecutionOutcome outcome,
        ImportWindow window,
        ImportPlan plan,
        int affectedCount,
        int recalculatedCount,
        string? error,
        DateTimeOffset started,
        DateTimeOffset completed,
        int? unmappedCount = null) =>
        new(
            batchId,
            outcome,
            window.From,
            window.To,
            plan.SourceCount,
            plan.NewRows.Count,
            plan.DuplicateCount,
            plan.Conflicts.Count,
            unmappedCount ?? plan.NewRows.Count(item => item.EmployeeId == null),
            affectedCount,
            recalculatedCount,
            error,
            started,
            completed);

    private sealed record ImportWindow
    {
        public ImportWindow(DateTime from, DateTime to)
        {
            if (from.Kind != DateTimeKind.Unspecified ||
                to.Kind != DateTimeKind.Unspecified || to <= from)
            {
                throw new ApplicationValidationException(
                    "Import window must be a non-empty local half-open interval.");
            }

            From = from;
            To = to;
        }

        public DateTime From { get; }
        public DateTime To { get; }
    }

    private sealed record PlannedRow(
        BioWebAttendanceSourceRecord Record,
        byte[] Fingerprint,
        Guid? EmployeeId);

    private sealed record ConflictRow(
        BioWebAttendanceSourceRecord Record,
        byte[] Fingerprint,
        byte[]? ExistingFingerprint);

    private sealed record ImportPlan(
        int SourceCount,
        int DuplicateCount,
        IReadOnlyList<PlannedRow> NewRows,
        IReadOnlyList<ConflictRow> Conflicts);
}
