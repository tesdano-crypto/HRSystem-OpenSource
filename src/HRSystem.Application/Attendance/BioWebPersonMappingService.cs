using HRSystem.Application.Abstractions.Persistence;
using HRSystem.Application.Abstractions.Security;
using HRSystem.Application.Common.Auditing;
using HRSystem.Application.Common.Exceptions;
using HRSystem.Application.Common.Validation;
using HRSystem.Domain.Attendance;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HRSystem.Application.Attendance;

public sealed class BioWebPersonMappingService(
    IApplicationDbContext dbContext,
    ICurrentUser currentUser,
    TimeProvider timeProvider,
    IAttendanceRecalculationEngine recalculationEngine,
    ILogger<BioWebPersonMappingService> logger) : IBioWebPersonMappingService
{
    private const string EmployeeStartUniqueIndex =
        "UX_BioWebPersonMappings_EmployeeId_EffectiveFrom";
    private const string PinStartUniqueIndex =
        "UX_BioWebPersonMappings_BioWebPin_EffectiveFrom";

    public async Task<IReadOnlyList<BioWebPersonMappingDto>> GetMappingsAsync(
        CancellationToken cancellationToken = default)
    {
        AttendanceAuthorization.EnsureManage(currentUser);
        var mappings = await dbContext.BioWebPersonMappings
            .AsNoTracking()
            .Include(mapping => mapping.Employee)
            .OrderByDescending(mapping => mapping.IsActive)
            .ThenBy(mapping => mapping.BioWebPin)
            .ThenByDescending(mapping => mapping.EffectiveFrom)
            .ToListAsync(cancellationToken);
        return mappings.Select(Map).ToArray();
    }

    public async Task<IReadOnlyList<UnmappedBioWebPinDto>> GetUnmappedPinsAsync(
        CancellationToken cancellationToken = default)
    {
        AttendanceAuthorization.EnsureManage(currentUser);
        try
        {
            var summaries = await dbContext.AttendanceRawEvents
                .AsNoTracking()
                .Where(rawEvent =>
                    rawEvent.SourceSystem == AttendanceSourceSystems.BioWebTa &&
                    rawEvent.EmployeeId == null)
                .GroupBy(rawEvent => rawEvent.SourcePersonPin)
                .Select(group => new
                {
                    BioWebPin = group.Key,
                    EventCount = group.LongCount(),
                    FirstEventLocalDateTime =
                        group.Min(rawEvent => rawEvent.EventLocalDateTime),
                    LastEventLocalDateTime =
                        group.Max(rawEvent => rawEvent.EventLocalDateTime)
                })
                .OrderBy(item => item.BioWebPin)
                .ToListAsync(cancellationToken);

            return summaries
                .Select(item => new UnmappedBioWebPinDto(
                    item.BioWebPin,
                    item.EventCount,
                    item.FirstEventLocalDateTime,
                    item.LastEventLocalDateTime))
                .ToArray();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(
                "Attendance mapping load failed. OperationName={OperationName} " +
                "ExceptionType={ExceptionType} SanitizedMessage={SanitizedMessage}",
                nameof(GetUnmappedPinsAsync),
                exception.GetType().Name,
                "The unmapped PIN summary query could not be completed.");
            throw;
        }
    }

    public Task<BioWebPersonMappingDto> CreateAsync(
        CreateBioWebPersonMappingRequest request,
        CancellationToken cancellationToken = default)
    {
        AttendanceAuthorization.EnsureManage(currentUser);
        RequestValidator.Validate(request);
        return dbContext.ExecuteSerializableAsync(
            async transactionCancellationToken =>
            {
                var employee = await dbContext.Employees
                    .AsNoTracking()
                    .SingleOrDefaultAsync(
                        item => item.Id == request.EmployeeId,
                        transactionCancellationToken)
                    ?? throw new ApplicationValidationException(
                        "指定的 HRSystem 員工不存在。");
                var pin = request.BioWebPin.Trim();
                await EnsureNoOverlapAsync(
                    request.EmployeeId,
                    pin,
                    request.EffectiveFrom,
                    request.EffectiveTo,
                    null,
                    transactionCancellationToken);

                var mapping = new BioWebPersonMapping(
                    Guid.NewGuid(),
                    request.EmployeeId,
                    pin,
                    request.EffectiveFrom,
                    request.EffectiveTo,
                    timeProvider.GetUtcNow());
                dbContext.BioWebPersonMappings.Add(mapping);
                dbContext.AuditLogs.Add(AuditLogFactory.Create(
                    currentUser,
                    timeProvider,
                    AuditActions.BioWebMappingCreated,
                    nameof(BioWebPersonMapping),
                    mapping.Id.ToString(),
                    null,
                    AuditSnapshot(mapping)));
                await SaveAsync(transactionCancellationToken);
                return Map(mapping, employee.EmployeeNumber, employee.ChineseName);
            },
            cancellationToken);
    }

    public Task<BioWebPersonMappingDto> UpdateAsync(
        UpdateBioWebPersonMappingRequest request,
        CancellationToken cancellationToken = default)
    {
        AttendanceAuthorization.EnsureManage(currentUser);
        RequestValidator.Validate(request);
        return dbContext.ExecuteSerializableAsync(
            async transactionCancellationToken =>
            {
                var mapping = await dbContext.BioWebPersonMappings
                    .Include(item => item.Employee)
                    .SingleOrDefaultAsync(
                        item => item.Id == request.Id,
                        transactionCancellationToken)
                    ?? throw new EntityNotFoundException("找不到指定的 BioWebTA 人員對照。");
                EnsureRowVersion(mapping.RowVersion, request.RowVersion);
                if (mapping.IsActive)
                {
                    await EnsureNoOverlapAsync(
                        mapping.EmployeeId,
                        mapping.BioWebPin,
                        request.EffectiveFrom,
                        request.EffectiveTo,
                        mapping.Id,
                        transactionCancellationToken);
                }

                var oldValues = AuditSnapshot(mapping);
                mapping.UpdateEffectivePeriod(
                    request.EffectiveFrom,
                    request.EffectiveTo,
                    timeProvider.GetUtcNow());
                dbContext.AuditLogs.Add(AuditLogFactory.Create(
                    currentUser,
                    timeProvider,
                    AuditActions.BioWebMappingUpdated,
                    nameof(BioWebPersonMapping),
                    mapping.Id.ToString(),
                    oldValues,
                    AuditSnapshot(mapping)));
                await SaveAsync(transactionCancellationToken);
                return Map(mapping);
            },
            cancellationToken);
    }

    public Task DeactivateAsync(
        Guid id,
        string rowVersion,
        CancellationToken cancellationToken = default)
    {
        AttendanceAuthorization.EnsureManage(currentUser);
        return dbContext.ExecuteSerializableAsync(
            async transactionCancellationToken =>
            {
                var mapping = await dbContext.BioWebPersonMappings
                    .SingleOrDefaultAsync(
                        item => item.Id == id,
                        transactionCancellationToken)
                    ?? throw new EntityNotFoundException("找不到指定的 BioWebTA 人員對照。");
                EnsureRowVersion(mapping.RowVersion, rowVersion);
                if (!mapping.IsActive)
                {
                    throw new ApplicationValidationException("此人員對照已停用。");
                }

                var oldValues = AuditSnapshot(mapping);
                mapping.Deactivate(timeProvider.GetUtcNow());
                dbContext.AuditLogs.Add(AuditLogFactory.Create(
                    currentUser,
                    timeProvider,
                    AuditActions.BioWebMappingDeactivated,
                    nameof(BioWebPersonMapping),
                    mapping.Id.ToString(),
                    oldValues,
                    AuditSnapshot(mapping)));
                await SaveAsync(transactionCancellationToken);
            },
            cancellationToken);
    }

    public async Task<BioWebRelinkPreviewDto> GetRelinkPreviewAsync(
        Guid mappingId,
        CancellationToken cancellationToken = default)
    {
        AttendanceAuthorization.EnsureManage(currentUser);
        var mapping = await dbContext.BioWebPersonMappings
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Id == mappingId,
                cancellationToken)
            ?? throw new EntityNotFoundException(
                "找不到指定的 BioWebTA 人員對照。");
        if (!mapping.IsActive)
        {
            return new BioWebRelinkPreviewDto(mapping.Id, 0, 0);
        }

        var candidateCount = await RelinkCandidates(mapping)
            .LongCountAsync(cancellationToken);
        var unableCount = await RemainingUnmappedEvents(mapping)
            .LongCountAsync(cancellationToken) - candidateCount;
        return new BioWebRelinkPreviewDto(
            mapping.Id,
            candidateCount,
            Math.Max(0, unableCount));
    }

    public Task<BioWebRelinkResultDto> RelinkUnmappedEventsAsync(
        Guid mappingId,
        string rowVersion,
        CancellationToken cancellationToken = default)
    {
        AttendanceAuthorization.EnsureManage(currentUser);
        return dbContext.ExecuteSerializableAsync(
            async transactionCancellationToken =>
            {
                var mapping = await dbContext.BioWebPersonMappings
                    .SingleOrDefaultAsync(
                        item => item.Id == mappingId,
                        transactionCancellationToken)
                    ?? throw new EntityNotFoundException("找不到指定的 BioWebTA 人員對照。");
                EnsureRowVersion(mapping.RowVersion, rowVersion);
                if (!mapping.IsActive)
                {
                    throw new ApplicationValidationException(
                        "停用的人員對照不可執行未對照事件重新連結。");
                }

                var rawEvents = await RelinkCandidates(mapping).ToListAsync(
                    transactionCancellationToken);
                var affectedKeys = rawEvents
                    .Select(rawEvent => new AttendanceRecalculationKey(
                        mapping.EmployeeId,
                        DateOnly.FromDateTime(rawEvent.EventLocalDateTime)))
                    .Distinct()
                    .ToArray();
                foreach (var rawEvent in rawEvents)
                {
                    rawEvent.Relink(mapping.EmployeeId);
                }

                var recalculatedKeyCount = 0;
                if (rawEvents.Count > 0)
                {
                    await SaveAsync(transactionCancellationToken);
                    var recalculation = await recalculationEngine.RecalculateKeysAsync(
                        affectedKeys,
                        transactionCancellationToken);
                    recalculatedKeyCount = recalculation.ResultCount;
                }

                var unableToRelinkCount = await RemainingUnmappedEvents(mapping)
                    .LongCountAsync(transactionCancellationToken);
                dbContext.AuditLogs.Add(AuditLogFactory.Create(
                    currentUser,
                    timeProvider,
                    AuditActions.AttendanceUnmappedEventsRelinked,
                    nameof(BioWebPersonMapping),
                    mapping.Id.ToString(),
                    null,
                    new
                    {
                        MappingId = mapping.Id,
                        RelinkedCount = rawEvents.Count,
                        AffectedKeyCount = affectedKeys.Length
                    }));
                await SaveAsync(transactionCancellationToken);
                return new BioWebRelinkResultDto(
                    mapping.Id,
                    rawEvents.Count,
                    recalculatedKeyCount,
                    unableToRelinkCount,
                    rawEvents.Count > 0);
            },
            cancellationToken);
    }

    private IQueryable<AttendanceRawEvent> RelinkCandidates(
        BioWebPersonMapping mapping)
    {
        var candidates = RemainingUnmappedEvents(mapping).Where(rawEvent =>
            rawEvent.EventLocalDateTime >= mapping.EffectiveFrom);
        if (mapping.EffectiveTo.HasValue)
        {
            var effectiveTo = mapping.EffectiveTo.Value;
            candidates = candidates.Where(
                rawEvent => rawEvent.EventLocalDateTime < effectiveTo);
        }

        return candidates;
    }

    private IQueryable<AttendanceRawEvent> RemainingUnmappedEvents(
        BioWebPersonMapping mapping) =>
        dbContext.AttendanceRawEvents.Where(rawEvent =>
            rawEvent.SourceSystem == AttendanceSourceSystems.BioWebTa &&
            rawEvent.EmployeeId == null &&
            rawEvent.SourcePersonPin == mapping.BioWebPin);

    private async Task EnsureNoOverlapAsync(
        Guid employeeId,
        string bioWebPin,
        DateTime effectiveFrom,
        DateTime? effectiveTo,
        Guid? excludedId,
        CancellationToken cancellationToken)
    {
        var active = dbContext.BioWebPersonMappings
            .AsNoTracking()
            .Where(mapping =>
                mapping.IsActive &&
                (!excludedId.HasValue || mapping.Id != excludedId.Value));

        var employeeMappings = active.Where(mapping => mapping.EmployeeId == employeeId);
        var pinMappings = active.Where(mapping => mapping.BioWebPin == bioWebPin);
        if (effectiveTo.HasValue)
        {
            var end = effectiveTo.Value;
            employeeMappings = employeeMappings.Where(mapping => mapping.EffectiveFrom < end);
            pinMappings = pinMappings.Where(mapping => mapping.EffectiveFrom < end);
        }

        employeeMappings = employeeMappings.Where(
            mapping => mapping.EffectiveTo == null || mapping.EffectiveTo > effectiveFrom);
        pinMappings = pinMappings.Where(
            mapping => mapping.EffectiveTo == null || mapping.EffectiveTo > effectiveFrom);

        if (await employeeMappings.AnyAsync(cancellationToken))
        {
            throw new ApplicationValidationException(
                "同一員工不可有重疊的有效 BioWebTA 人員對照。");
        }

        if (await pinMappings.AnyAsync(cancellationToken))
        {
            throw new ApplicationValidationException(
                "同一 BioWeb PIN 不可在重疊期間對照多位員工。");
        }
    }

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConcurrencyConflictException();
        }
        catch (DbUpdateException exception) when (
            dbContext.IsUniqueConstraintViolation(exception, EmployeeStartUniqueIndex) ||
            dbContext.IsUniqueConstraintViolation(exception, PinStartUniqueIndex))
        {
            throw new ApplicationValidationException(
                "BioWebTA 人員對照與既有有效期間衝突。");
        }
    }

    private static void EnsureRowVersion(byte[] current, string supplied)
    {
        try
        {
            var expected = string.IsNullOrWhiteSpace(supplied)
                ? []
                : Convert.FromBase64String(supplied);
            if (!current.SequenceEqual(expected))
            {
                throw new ConcurrencyConflictException();
            }
        }
        catch (FormatException)
        {
            throw new ConcurrencyConflictException();
        }
    }

    private static object AuditSnapshot(BioWebPersonMapping mapping) => new
    {
        mapping.EmployeeId,
        mapping.EffectiveFrom,
        mapping.EffectiveTo,
        mapping.IsActive
    };

    private static BioWebPersonMappingDto Map(BioWebPersonMapping mapping) =>
        Map(
            mapping,
            mapping.Employee.EmployeeNumber,
            mapping.Employee.ChineseName);

    private static BioWebPersonMappingDto Map(
        BioWebPersonMapping mapping,
        string employeeNumber,
        string employeeName) =>
        new(
            mapping.Id,
            mapping.EmployeeId,
            employeeNumber,
            employeeName,
            mapping.BioWebPin,
            mapping.EffectiveFrom,
            mapping.EffectiveTo,
            mapping.IsActive,
            Convert.ToBase64String(mapping.RowVersion));
}
