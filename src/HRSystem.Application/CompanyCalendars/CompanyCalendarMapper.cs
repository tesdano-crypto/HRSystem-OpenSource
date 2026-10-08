using HRSystem.Domain.CompanyCalendars;

namespace HRSystem.Application.CompanyCalendars;

internal static class CompanyCalendarMapper
{
    public static CompanyCalendarYearDto Map(CompanyCalendarYear year) => new(
        year.Id,
        year.Year,
        year.Status,
        year.SourceAuthority,
        year.SourceTitle,
        year.SourcePublishedDate,
        year.SourceReference,
        year.SourceDocumentIdentifier,
        year.SourceContentHash,
        year.ManifestVersion,
        year.ManifestHash,
        year.PublishedAtUtc,
        year.PublishedBy,
        year.ArchivedAtUtc,
        year.ArchivedBy,
        year.Days.Any(day => day.IsManualOverride),
        Convert.ToBase64String(year.RowVersion));

    public static CompanyCalendarDayDto Map(CompanyCalendarDay day) => new(
        day.Id,
        day.CalendarYear,
        day.Date,
        day.BaseDayType,
        day.BaseName,
        day.SourceNote,
        day.SourceReference,
        day.DayType,
        day.Name,
        day.Description,
        day.OverrideReason,
        day.IsWorkingDay,
        day.IsManualOverride,
        Convert.ToBase64String(day.RowVersion));

    public static CompanyCalendarYearDetailDto MapDetail(CompanyCalendarYear year) =>
        new(
            Map(year),
            year.Days.OrderBy(day => day.Date).Select(Map).ToArray());
}
