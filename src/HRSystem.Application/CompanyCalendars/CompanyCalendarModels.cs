using System.ComponentModel.DataAnnotations;
using HRSystem.Domain.CompanyCalendars;

namespace HRSystem.Application.CompanyCalendars;

public sealed record CompanyCalendarManifest(
    string SchemaVersion,
    int CalendarYear,
    CompanyCalendarManifestSource Source,
    string ManifestVersion,
    IReadOnlyList<CompanyCalendarManifestDate> Dates,
    string ManifestHash);

public sealed record CompanyCalendarManifestSource(
    string Authority,
    string Title,
    DateOnly PublicationDate,
    string Url,
    string? DocumentIdentifier,
    string? ContentSha256);

public sealed record CompanyCalendarManifestDate(
    DateOnly Date,
    CompanyCalendarDayType DayType,
    string Name,
    string? SourceNote,
    string? SourceReference);

public sealed record CompanyCalendarManifestPreview(
    int Year,
    string SourceAuthority,
    string SourceTitle,
    DateOnly SourcePublishedDate,
    string SourceReference,
    string? SourceDocumentIdentifier,
    string? SourceContentHash,
    string ManifestVersion,
    string ManifestHash,
    int TotalDays,
    int WorkingDays,
    int NonWorkingDays,
    int WeekendDays,
    int NationalHolidayDays,
    int SubstituteHolidayDays,
    IReadOnlyList<CompanyCalendarDayDto> Days);

public sealed record CompanyCalendarYearDto(
    Guid Id,
    int Year,
    CompanyCalendarStatus Status,
    string SourceAuthority,
    string SourceTitle,
    DateOnly SourcePublishedDate,
    string SourceReference,
    string? SourceDocumentIdentifier,
    string? SourceContentHash,
    string ManifestVersion,
    string ManifestHash,
    DateTimeOffset? PublishedAtUtc,
    string? PublishedBy,
    DateTimeOffset? ArchivedAtUtc,
    string? ArchivedBy,
    bool HasManualOverrides,
    string RowVersion);

public sealed record CompanyCalendarDayDto(
    Guid Id,
    int CalendarYear,
    DateOnly Date,
    CompanyCalendarDayType BaseDayType,
    string? BaseName,
    string? SourceNote,
    string? SourceReference,
    CompanyCalendarDayType DayType,
    string? Name,
    string? Description,
    string? OverrideReason,
    bool IsWorkingDay,
    bool IsManualOverride,
    string RowVersion);

public sealed record CompanyCalendarYearDetailDto(
    CompanyCalendarYearDto Year,
    IReadOnlyList<CompanyCalendarDayDto> Days);

public sealed class InitializeCompanyCalendarRequest
{
    [Required]
    public byte[] ManifestBytes { get; init; } = [];

    [Required, StringLength(64, MinimumLength = 64)]
    public string ExpectedManifestHash { get; init; } = string.Empty;
}

public sealed class ImportCalendarRevisionRequest
{
    public int Year { get; init; }
    [Required]
    public byte[] ManifestBytes { get; init; } = [];
    [Required, StringLength(64, MinimumLength = 64)]
    public string ExpectedManifestHash { get; init; } = string.Empty;
    public string YearRowVersion { get; init; } = string.Empty;
}

public sealed class PublishCompanyCalendarRequest
{
    public int Year { get; init; }
    public string RowVersion { get; init; } = string.Empty;
    [Required]
    public string ExpectedManifestVersion { get; init; } = string.Empty;
    [Required, StringLength(64, MinimumLength = 64)]
    public string ExpectedManifestHash { get; init; } = string.Empty;
}

public sealed class ArchiveCompanyCalendarRequest
{
    public int Year { get; init; }
    public string RowVersion { get; init; } = string.Empty;
}

public class ChangeCompanyCalendarDayRequest
{
    public int Year { get; init; }
    public DateOnly Date { get; init; }
    [StringLength(100)]
    public string? Name { get; init; }
    [StringLength(500)]
    public string? Description { get; init; }
    [Required, StringLength(500)]
    public string Reason { get; init; } = string.Empty;
    public string YearRowVersion { get; init; } = string.Empty;
    public string DayRowVersion { get; init; } = string.Empty;
}

public sealed class OverridePublishedCalendarDayRequest : ChangeCompanyCalendarDayRequest
{
    public CompanyCalendarDayType DayType { get; init; }
}
