using HRSystem.Application.Abstractions.Persistence;
using HRSystem.Application.Abstractions.Security;
using HRSystem.Application.Common.Exceptions;
using HRSystem.Domain.CompanyCalendars;
using Microsoft.EntityFrameworkCore;

namespace HRSystem.Application.CompanyCalendars;

public sealed class CompanyCalendarQueryService(
    IApplicationDbContext dbContext,
    ICurrentUser currentUser) : ICompanyCalendarQueryService
{
    public async Task<CompanyCalendarDayDto> GetDayAsync(
        DateOnly date,
        CancellationToken cancellationToken = default)
    {
        EnsureRead();
        var day = await dbContext.CompanyCalendarDays.AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Date == date &&
                    item.Year.Status == CompanyCalendarStatus.Published,
                cancellationToken)
            ?? throw new EntityNotFoundException("此日期尚無已發布的公司行事曆。");
        return CompanyCalendarMapper.Map(day);
    }

    public async Task<bool> IsWorkingDayAsync(
        DateOnly date,
        CancellationToken cancellationToken = default) =>
        (await GetDayAsync(date, cancellationToken)).IsWorkingDay;

    public async Task<int> CountWorkingDaysAsync(
        DateOnly start,
        DateOnly end,
        CancellationToken cancellationToken = default)
    {
        EnsureRead();
        if (start > end)
        {
            throw new ApplicationValidationException("開始日期不得晚於結束日期。");
        }

        var inclusiveDays = end.DayNumber - start.DayNumber + 1;
        if (inclusiveDays > 366)
        {
            throw new ApplicationValidationException("查詢範圍不得超過 366 天。");
        }

        var days = await dbContext.CompanyCalendarDays.AsNoTracking()
            .Where(day => day.Date >= start &&
                day.Date <= end &&
                day.Year.Status == CompanyCalendarStatus.Published)
            .OrderBy(day => day.Date)
            .ToListAsync(cancellationToken);
        if (days.Count != inclusiveDays)
        {
            throw new EntityNotFoundException("查詢範圍尚無完整的已發布公司行事曆。");
        }

        return days.Count(day => day.IsWorkingDay);
    }

    public async Task<IReadOnlyList<CompanyCalendarDayDto>> GetMonthAsync(
        int year,
        int month,
        CancellationToken cancellationToken = default)
    {
        EnsureRead();
        if (month is < 1 or > 12)
        {
            throw new ApplicationValidationException("月份必須介於 1 與 12。");
        }

        var days = await dbContext.CompanyCalendarDays.AsNoTracking()
            .Where(day => day.CalendarYear == year &&
                day.Date.Month == month &&
                day.Year.Status == CompanyCalendarStatus.Published)
            .OrderBy(day => day.Date)
            .ToListAsync(cancellationToken);
        if (days.Count == 0)
        {
            throw new EntityNotFoundException("找不到已發布的公司行事曆。");
        }

        return days.Select(CompanyCalendarMapper.Map).ToArray();
    }

    public async Task<CompanyCalendarYearDetailDto> GetPublishedYearAsync(
        int year,
        CancellationToken cancellationToken = default)
    {
        EnsureRead();
        var entity = await dbContext.CompanyCalendarYears.AsNoTracking()
            .Include(item => item.Days)
            .SingleOrDefaultAsync(
                item => item.Year == year &&
                    item.Status == CompanyCalendarStatus.Published,
                cancellationToken)
            ?? throw new EntityNotFoundException("找不到已發布的公司行事曆。");
        return CompanyCalendarMapper.MapDetail(entity);
    }

    public async Task<IReadOnlyList<CompanyCalendarYearDto>> GetPublishedAndArchivedYearsAsync(
        CancellationToken cancellationToken = default)
    {
        EnsureRead();
        var years = await dbContext.CompanyCalendarYears.AsNoTracking()
            .Include(item => item.Days)
            .Where(item => item.Status != CompanyCalendarStatus.Draft)
            .OrderByDescending(item => item.Year)
            .ToListAsync(cancellationToken);
        return years.Select(CompanyCalendarMapper.Map).ToArray();
    }

    public async Task<CompanyCalendarYearDetailDto> GetHistoricalYearAsync(
        int year,
        CancellationToken cancellationToken = default)
    {
        EnsureRead();
        var entity = await dbContext.CompanyCalendarYears.AsNoTracking()
            .Include(item => item.Days)
            .SingleOrDefaultAsync(
                item => item.Year == year &&
                    item.Status != CompanyCalendarStatus.Draft,
                cancellationToken)
            ?? throw new EntityNotFoundException("找不到可檢視的公司行事曆。");
        return CompanyCalendarMapper.MapDetail(entity);
    }

    private void EnsureRead() => CompanyCalendarAuthorization.EnsureRead(currentUser);
}
