using System.Data;
using HRSystem.Application.Abstractions.Persistence;
using HRSystem.Application.Abstractions.Security;
using HRSystem.Application.Common.Auditing;
using HRSystem.Application.Common.Exceptions;
using HRSystem.Application.Common.Validation;
using HRSystem.Domain.CompanyCalendars;
using Microsoft.EntityFrameworkCore;

namespace HRSystem.Application.CompanyCalendars;

public sealed class CompanyCalendarService(
    IApplicationDbContext dbContext,
    ICompanyCalendarManifestReader manifestReader,
    ICurrentUser currentUser,
    TimeProvider timeProvider) : ICompanyCalendarService
{
    private const string YearUniqueIndex = "UX_CompanyCalendarYears_Year";
    private const string DayUniqueIndex = "UX_CompanyCalendarDays_Year_Date";

    public async Task<CompanyCalendarManifestPreview> ValidateManifestAsync(
        Stream manifest,
        CancellationToken cancellationToken = default)
    {
        EnsureManage();
        return CompanyCalendarManifestValidator.ValidateAndPreview(
            await manifestReader.ReadAsync(manifest, cancellationToken));
    }

    public Task<CompanyCalendarManifestPreview> PreviewInitializationAsync(
        Stream manifest,
        CancellationToken cancellationToken = default) =>
        ValidateManifestAsync(manifest, cancellationToken);

    public async Task<CompanyCalendarYearDetailDto> InitializeDraftAsync(
        InitializeCompanyCalendarRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureManage();
        RequestValidator.Validate(request);
        var manifest = await ReadBytesAsync(request.ManifestBytes, cancellationToken);
        var preview = CompanyCalendarManifestValidator.ValidateAndPreview(manifest);
        if (!string.Equals(
                preview.ManifestHash,
                request.ExpectedManifestHash,
                StringComparison.Ordinal))
        {
            throw new ApplicationValidationException(
                "Manifest 已變更，請重新預覽後再建立草稿。");
        }

        if (await dbContext.CompanyCalendarYears.AsNoTracking()
            .AnyAsync(year => year.Year == preview.Year, cancellationToken))
        {
            throw new ApplicationValidationException("此年度公司行事曆已存在。");
        }

        return await dbContext.ExecuteTransactionAsync(
            IsolationLevel.ReadCommitted,
            async transactionCancellationToken =>
            {
                var now = timeProvider.GetUtcNow();
                var actor = Actor();
                var year = new CompanyCalendarYear(
                    Guid.NewGuid(),
                    preview.Year,
                    preview.SourceAuthority,
                    preview.SourceTitle,
                    preview.SourcePublishedDate,
                    preview.SourceReference,
                    preview.SourceDocumentIdentifier,
                    preview.SourceContentHash,
                    preview.ManifestVersion,
                    preview.ManifestHash,
                    actor,
                    now);
                foreach (var item in preview.Days)
                {
                    year.Days.Add(new CompanyCalendarDay(
                        Guid.NewGuid(),
                        year.Id,
                        year.Year,
                        item.Date,
                        item.BaseDayType,
                        item.BaseName,
                        item.SourceNote,
                        item.SourceReference,
                        actor,
                        now));
                }

                dbContext.CompanyCalendarYears.Add(year);
                dbContext.AuditLogs.Add(AuditLogFactory.Create(
                    currentUser,
                    timeProvider,
                    AuditActions.CompanyCalendarInitialized,
                    nameof(CompanyCalendarYear),
                    year.Id.ToString(),
                    null,
                    AggregateSnapshot(year)));
                await SaveAsync(transactionCancellationToken);
                return CompanyCalendarMapper.MapDetail(year);
            },
            cancellationToken);
    }

    public async Task<CompanyCalendarYearDetailDto> ImportManifestRevisionAsync(
        ImportCalendarRevisionRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureManage();
        RequestValidator.Validate(request);
        var manifest = await ReadBytesAsync(request.ManifestBytes, cancellationToken);
        var preview = CompanyCalendarManifestValidator.ValidateAndPreview(manifest);
        if (preview.Year != request.Year ||
            !string.Equals(preview.ManifestHash, request.ExpectedManifestHash, StringComparison.Ordinal))
        {
            throw new ApplicationValidationException(
                "Manifest 年度或雜湊與預覽內容不一致。");
        }

        return await dbContext.ExecuteTransactionAsync(
            IsolationLevel.ReadCommitted,
            async transactionCancellationToken =>
            {
                var year = await GetTrackedYearAsync(request.Year, transactionCancellationToken);
                if (year.Status == CompanyCalendarStatus.Archived)
                {
                    throw new ApplicationValidationException("已封存的行事曆不可匯入修訂。");
                }

                EnsureRowVersion(year.RowVersion, request.YearRowVersion);
                EnsureComplete(year);
                var actor = Actor();
                var now = timeProvider.GetUtcNow();
                var oldAggregate = AggregateSnapshot(year);
                var incoming = preview.Days.ToDictionary(day => day.Date);
                foreach (var day in year.Days)
                {
                    var oldDay = DaySnapshot(day);
                    var replacement = incoming[day.Date];
                    day.UpdateBaseline(
                        replacement.BaseDayType,
                        replacement.BaseName,
                        replacement.SourceNote,
                        replacement.SourceReference,
                        actor,
                        now);
                    if (year.Status == CompanyCalendarStatus.Published &&
                        !Equals(oldDay, DaySnapshot(day)))
                    {
                        dbContext.AuditLogs.Add(AuditLogFactory.Create(
                            currentUser,
                            timeProvider,
                            AuditActions.CompanyCalendarDayOverridden,
                            nameof(CompanyCalendarDay),
                            day.Id.ToString(),
                            oldDay,
                            DaySnapshot(day)));
                    }
                }

                year.UpdateSource(
                    preview.SourceAuthority,
                    preview.SourceTitle,
                    preview.SourcePublishedDate,
                    preview.SourceReference,
                    preview.SourceDocumentIdentifier,
                    preview.SourceContentHash,
                    preview.ManifestVersion,
                    preview.ManifestHash,
                    actor,
                    now);
                dbContext.AuditLogs.Add(AuditLogFactory.Create(
                    currentUser,
                    timeProvider,
                    AuditActions.CompanyCalendarManifestImported,
                    nameof(CompanyCalendarYear),
                    year.Id.ToString(),
                    oldAggregate,
                    AggregateSnapshot(year)));
                await SaveAsync(transactionCancellationToken);
                return CompanyCalendarMapper.MapDetail(year);
            },
            cancellationToken);
    }

    public Task<CompanyCalendarYearDetailDto> PublishAsync(
        PublishCompanyCalendarRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureManage();
        RequestValidator.Validate(request);
        return dbContext.ExecuteTransactionAsync(
            IsolationLevel.ReadCommitted,
            async transactionCancellationToken =>
            {
                var year = await GetTrackedYearAsync(request.Year, transactionCancellationToken);
                EnsureRowVersion(year.RowVersion, request.RowVersion);
                if (!string.Equals(
                        year.ManifestVersion,
                        request.ExpectedManifestVersion,
                        StringComparison.Ordinal) ||
                    !string.Equals(
                        year.ManifestHash,
                        request.ExpectedManifestHash,
                        StringComparison.Ordinal))
                {
                    throw new ApplicationValidationException(
                        "Manifest 版本或雜湊不一致，請重新檢查來源。");
                }

                EnsureComplete(year);
                var oldValues = AggregateSnapshot(year);
                year.Publish(Actor(), timeProvider.GetUtcNow());
                dbContext.AuditLogs.Add(AuditLogFactory.Create(
                    currentUser,
                    timeProvider,
                    AuditActions.CompanyCalendarPublished,
                    nameof(CompanyCalendarYear),
                    year.Id.ToString(),
                    oldValues,
                    AggregateSnapshot(year)));
                await SaveAsync(transactionCancellationToken);
                return CompanyCalendarMapper.MapDetail(year);
            },
            cancellationToken);
    }

    public Task<CompanyCalendarYearDetailDto> ArchiveAsync(
        ArchiveCompanyCalendarRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureManage();
        RequestValidator.Validate(request);
        return dbContext.ExecuteTransactionAsync(
            IsolationLevel.ReadCommitted,
            async transactionCancellationToken =>
            {
                var year = await GetTrackedYearAsync(request.Year, transactionCancellationToken);
                EnsureRowVersion(year.RowVersion, request.RowVersion);
                var oldValues = AggregateSnapshot(year);
                year.Archive(Actor(), timeProvider.GetUtcNow());
                dbContext.AuditLogs.Add(AuditLogFactory.Create(
                    currentUser,
                    timeProvider,
                    AuditActions.CompanyCalendarArchived,
                    nameof(CompanyCalendarYear),
                    year.Id.ToString(),
                    oldValues,
                    AggregateSnapshot(year)));
                await SaveAsync(transactionCancellationToken);
                return CompanyCalendarMapper.MapDetail(year);
            },
            cancellationToken);
    }

    public Task<CompanyCalendarYearDetailDto> SetCompanyHolidayAsync(
        ChangeCompanyCalendarDayRequest request,
        CancellationToken cancellationToken = default) =>
        ChangeDayAsync(
            request,
            AuditActions.CompanyHolidayCreated,
            requirePublished: false,
            (day, actor, now) => day.SetCompanyHoliday(
                request.Name ?? string.Empty,
                request.Description,
                request.Reason,
                actor,
                now),
            cancellationToken);

    public Task<CompanyCalendarYearDetailDto> RemoveCompanyHolidayAsync(
        ChangeCompanyCalendarDayRequest request,
        CancellationToken cancellationToken = default) =>
        ChangeDayAsync(
            request,
            AuditActions.CompanyHolidayRemoved,
            requirePublished: false,
            (day, actor, now) => day.RemoveCompanyHoliday(request.Reason, actor, now),
            cancellationToken);

    public Task<CompanyCalendarYearDetailDto> SetExceptionalWorkingDayAsync(
        ChangeCompanyCalendarDayRequest request,
        CancellationToken cancellationToken = default) =>
        ChangeDayAsync(
            request,
            AuditActions.ExceptionalWorkingDayCreated,
            requirePublished: false,
            (day, actor, now) => day.SetExceptionalWorkingDay(
                request.Name ?? string.Empty,
                request.Description,
                request.Reason,
                actor,
                now),
            cancellationToken);

    public Task<CompanyCalendarYearDetailDto> RemoveExceptionalWorkingDayAsync(
        ChangeCompanyCalendarDayRequest request,
        CancellationToken cancellationToken = default) =>
        ChangeDayAsync(
            request,
            AuditActions.ExceptionalWorkingDayRemoved,
            requirePublished: false,
            (day, actor, now) => day.RemoveExceptionalWorkingDay(request.Reason, actor, now),
            cancellationToken);

    public Task<CompanyCalendarYearDetailDto> OverridePublishedDayAsync(
        OverridePublishedCalendarDayRequest request,
        CancellationToken cancellationToken = default) =>
        ChangeDayAsync(
            request,
            AuditActions.CompanyCalendarDayOverridden,
            requirePublished: true,
            (day, actor, now) => day.OverridePublishedDay(
                request.DayType,
                request.Name,
                request.Description,
                request.Reason,
                actor,
                now),
            cancellationToken);

    public async Task<IReadOnlyList<CompanyCalendarYearDto>> GetManagementYearsAsync(
        CancellationToken cancellationToken = default)
    {
        EnsureManage();
        var years = await dbContext.CompanyCalendarYears.AsNoTracking()
            .Include(year => year.Days)
            .OrderByDescending(year => year.Year)
            .ToListAsync(cancellationToken);
        return years.Select(CompanyCalendarMapper.Map).ToArray();
    }

    public async Task<CompanyCalendarYearDetailDto> GetManagementYearAsync(
        int year,
        CancellationToken cancellationToken = default)
    {
        EnsureManage();
        var entity = await dbContext.CompanyCalendarYears.AsNoTracking()
            .Include(item => item.Days)
            .SingleOrDefaultAsync(item => item.Year == year, cancellationToken)
            ?? throw new EntityNotFoundException("找不到指定的公司行事曆資料。");
        return CompanyCalendarMapper.MapDetail(entity);
    }

    private Task<CompanyCalendarYearDetailDto> ChangeDayAsync(
        ChangeCompanyCalendarDayRequest request,
        string auditAction,
        bool requirePublished,
        Action<CompanyCalendarDay, string, DateTimeOffset> mutate,
        CancellationToken cancellationToken)
    {
        EnsureManage();
        RequestValidator.Validate(request);
        return dbContext.ExecuteTransactionAsync(
            IsolationLevel.ReadCommitted,
            async transactionCancellationToken =>
            {
                var year = await GetTrackedYearAsync(request.Year, transactionCancellationToken);
                if (year.Status == CompanyCalendarStatus.Archived ||
                    requirePublished && year.Status != CompanyCalendarStatus.Published)
                {
                    throw new ApplicationValidationException("目前狀態不允許此操作。");
                }

                EnsureRowVersion(year.RowVersion, request.YearRowVersion);
                var day = year.Days.SingleOrDefault(item => item.Date == request.Date)
                    ?? throw new EntityNotFoundException("找不到指定的公司行事曆日期。");
                EnsureRowVersion(day.RowVersion, request.DayRowVersion);
                var oldValues = DaySnapshot(day);
                var actor = Actor();
                var now = timeProvider.GetUtcNow();
                mutate(day, actor, now);
                year.Touch(actor, now);
                dbContext.AuditLogs.Add(AuditLogFactory.Create(
                    currentUser,
                    timeProvider,
                    auditAction,
                    nameof(CompanyCalendarDay),
                    day.Id.ToString(),
                    oldValues,
                    DaySnapshot(day)));
                await SaveAsync(transactionCancellationToken);
                return CompanyCalendarMapper.MapDetail(year);
            },
            cancellationToken);
    }

    private async Task<CompanyCalendarYear> GetTrackedYearAsync(
        int year,
        CancellationToken cancellationToken) =>
        await dbContext.CompanyCalendarYears
            .Include(item => item.Days)
            .SingleOrDefaultAsync(item => item.Year == year, cancellationToken)
        ?? throw new EntityNotFoundException("找不到指定的公司行事曆資料。");

    private async Task<CompanyCalendarManifest> ReadBytesAsync(
        byte[] bytes,
        CancellationToken cancellationToken)
    {
        if (bytes.Length is 0 or > 256 * 1024)
        {
            throw new ApplicationValidationException("行事曆檔案大小必須介於 1 byte 與 256 KiB。");
        }

        await using var stream = new MemoryStream(bytes, writable: false);
        return await manifestReader.ReadAsync(stream, cancellationToken);
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
            dbContext.IsUniqueConstraintViolation(exception, YearUniqueIndex))
        {
            throw new ApplicationValidationException("此年度公司行事曆已存在。");
        }
        catch (DbUpdateException exception) when (
            dbContext.IsUniqueConstraintViolation(exception, DayUniqueIndex))
        {
            throw new ApplicationValidationException("同一日期不可重複。");
        }
    }

    private void EnsureManage() => CompanyCalendarAuthorization.EnsureManage(currentUser);
    private string Actor() => CompanyCalendarAuthorization.Actor(currentUser);

    private static void EnsureComplete(CompanyCalendarYear year)
    {
        var expected = DateTime.IsLeapYear(year.Year) ? 366 : 365;
        if (year.Days.Count != expected ||
            year.Days.Select(day => day.Date).Distinct().Count() != expected ||
            year.Days.Any(day => day.Date.Year != year.Year))
        {
            throw new ApplicationValidationException("行事曆日期不完整，無法執行此操作。");
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

    private static object AggregateSnapshot(CompanyCalendarYear year) => new
    {
        year.Year,
        year.Status,
        TotalDays = year.Days.Count,
        WorkingDays = year.Days.Count(day => day.IsWorkingDay),
        NonWorkingDays = year.Days.Count(day => !day.IsWorkingDay),
        WeekendDays = year.Days.Count(day =>
            day.Date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday),
        NationalHolidayDays = year.Days.Count(day =>
            day.DayType == CompanyCalendarDayType.NationalHoliday),
        SubstituteHolidayDays = year.Days.Count(day =>
            day.DayType == CompanyCalendarDayType.SubstituteHoliday),
        OverrideDays = year.Days.Count(day => day.IsManualOverride),
        year.SourceAuthority,
        year.SourceTitle,
        year.SourcePublishedDate,
        year.SourceReference,
        year.SourceDocumentIdentifier,
        year.ManifestVersion,
        year.ManifestHash
    };

    private static object DaySnapshot(CompanyCalendarDay day) => new
    {
        day.CalendarYear,
        day.Date,
        day.BaseDayType,
        day.DayType,
        day.IsWorkingDay,
        day.Name,
        day.Description,
        day.OverrideReason,
        day.IsManualOverride,
        day.SourceReference
    };
}
