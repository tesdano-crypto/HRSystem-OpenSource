namespace HRSystem.Application.CompanyCalendars;

public interface ICompanyCalendarQueryService
{
    Task<CompanyCalendarDayDto> GetDayAsync(
        DateOnly date,
        CancellationToken cancellationToken = default);
    Task<bool> IsWorkingDayAsync(
        DateOnly date,
        CancellationToken cancellationToken = default);
    Task<int> CountWorkingDaysAsync(
        DateOnly start,
        DateOnly end,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CompanyCalendarDayDto>> GetMonthAsync(
        int year,
        int month,
        CancellationToken cancellationToken = default);
    Task<CompanyCalendarYearDetailDto> GetPublishedYearAsync(
        int year,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CompanyCalendarYearDto>> GetPublishedAndArchivedYearsAsync(
        CancellationToken cancellationToken = default);
    Task<CompanyCalendarYearDetailDto> GetHistoricalYearAsync(
        int year,
        CancellationToken cancellationToken = default);
}
