namespace HRSystem.Application.CompanyCalendars;

public interface ICompanyCalendarManifestReader
{
    Task<CompanyCalendarManifest> ReadAsync(
        Stream manifest,
        CancellationToken cancellationToken = default);
}
