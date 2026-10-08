namespace HRSystem.Application.CompanyCalendars;

public interface ICompanyCalendarService
{
    Task<CompanyCalendarManifestPreview> ValidateManifestAsync(
        Stream manifest,
        CancellationToken cancellationToken = default);
    Task<CompanyCalendarManifestPreview> PreviewInitializationAsync(
        Stream manifest,
        CancellationToken cancellationToken = default);
    Task<CompanyCalendarYearDetailDto> InitializeDraftAsync(
        InitializeCompanyCalendarRequest request,
        CancellationToken cancellationToken = default);
    Task<CompanyCalendarYearDetailDto> ImportManifestRevisionAsync(
        ImportCalendarRevisionRequest request,
        CancellationToken cancellationToken = default);
    Task<CompanyCalendarYearDetailDto> PublishAsync(
        PublishCompanyCalendarRequest request,
        CancellationToken cancellationToken = default);
    Task<CompanyCalendarYearDetailDto> ArchiveAsync(
        ArchiveCompanyCalendarRequest request,
        CancellationToken cancellationToken = default);
    Task<CompanyCalendarYearDetailDto> SetCompanyHolidayAsync(
        ChangeCompanyCalendarDayRequest request,
        CancellationToken cancellationToken = default);
    Task<CompanyCalendarYearDetailDto> RemoveCompanyHolidayAsync(
        ChangeCompanyCalendarDayRequest request,
        CancellationToken cancellationToken = default);
    Task<CompanyCalendarYearDetailDto> SetExceptionalWorkingDayAsync(
        ChangeCompanyCalendarDayRequest request,
        CancellationToken cancellationToken = default);
    Task<CompanyCalendarYearDetailDto> RemoveExceptionalWorkingDayAsync(
        ChangeCompanyCalendarDayRequest request,
        CancellationToken cancellationToken = default);
    Task<CompanyCalendarYearDetailDto> OverridePublishedDayAsync(
        OverridePublishedCalendarDayRequest request,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CompanyCalendarYearDto>> GetManagementYearsAsync(
        CancellationToken cancellationToken = default);
    Task<CompanyCalendarYearDetailDto> GetManagementYearAsync(
        int year,
        CancellationToken cancellationToken = default);
}
