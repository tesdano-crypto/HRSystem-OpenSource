using HRSystem.Application.Common.Exceptions;
using HRSystem.Domain.CompanyCalendars;

namespace HRSystem.Application.CompanyCalendars;

public static class CompanyCalendarManifestValidator
{
    private static readonly HashSet<string> ApprovedHosts =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "www.dgpa.gov.tw",
            "dgpa.gov.tw"
        };

    public static CompanyCalendarManifestPreview ValidateAndPreview(
        CompanyCalendarManifest manifest)
    {
        if (manifest.SchemaVersion != "1.0")
        {
            throw Invalid("不支援的 schemaVersion。");
        }

        if (manifest.CalendarYear is not (2026 or 2027))
        {
            throw Invalid("目前只支援已驗證的 2026 與 2027 年資料。");
        }

        ValidateRequired(manifest.Source.Authority, 200, "來源機關");
        ValidateRequired(manifest.Source.Title, 300, "來源標題");
        ValidateHttpsOfficialUrl(manifest.Source.Url, "來源網址");
        ValidateOptional(manifest.Source.DocumentIdentifier, 200, "來源文號");
        ValidateHash(manifest.Source.ContentSha256, false, "來源附件雜湊");
        ValidateRequired(manifest.ManifestVersion, 50, "Manifest 版本");
        ValidateHash(manifest.ManifestHash, true, "Manifest 雜湊");

        var previous = DateOnly.MinValue;
        var dates = new Dictionary<DateOnly, CompanyCalendarManifestDate>();
        foreach (var item in manifest.Dates)
        {
            if (item.Date.Year != manifest.CalendarYear)
            {
                throw Invalid("Manifest 日期不屬於指定年度。");
            }

            if (item.Date <= previous)
            {
                throw Invalid("Manifest 日期必須依日期遞增且不可重複。");
            }

            if (item.DayType is not (CompanyCalendarDayType.NationalHoliday
                or CompanyCalendarDayType.SubstituteHoliday))
            {
                throw Invalid("官方 Manifest 只允許國定假日與補假日。");
            }

            ValidateRequired(item.Name, 100, "日期名稱");
            ValidateOptional(item.SourceNote, 500, "來源說明");
            if (item.SourceReference is not null)
            {
                ValidateHttpsOfficialUrl(item.SourceReference, "日期來源網址");
            }

            if (!dates.TryAdd(item.Date, item))
            {
                throw Invalid("Manifest 日期不可重複。");
            }

            previous = item.Date;
        }

        var days = GenerateDays(manifest, dates);
        var totalDays = DateTime.IsLeapYear(manifest.CalendarYear) ? 366 : 365;
        if (days.Count != totalDays || days.Select(day => day.Date).Distinct().Count() != totalDays)
        {
            throw Invalid("行事曆日期不完整。");
        }

        return new CompanyCalendarManifestPreview(
            manifest.CalendarYear,
            manifest.Source.Authority.Trim(),
            manifest.Source.Title.Trim(),
            manifest.Source.PublicationDate,
            manifest.Source.Url.Trim(),
            manifest.Source.DocumentIdentifier?.Trim(),
            manifest.Source.ContentSha256?.Trim(),
            manifest.ManifestVersion.Trim(),
            manifest.ManifestHash,
            days.Count,
            days.Count(day => day.IsWorkingDay),
            days.Count(day => !day.IsWorkingDay),
            days.Count(day => day.Date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday),
            days.Count(day => day.DayType == CompanyCalendarDayType.NationalHoliday),
            days.Count(day => day.DayType == CompanyCalendarDayType.SubstituteHoliday),
            days);
    }

    public static IReadOnlyList<CompanyCalendarDayDto> GenerateDays(
        CompanyCalendarManifest manifest)
    {
        var dates = manifest.Dates.ToDictionary(item => item.Date);
        return GenerateDays(manifest, dates);
    }

    private static IReadOnlyList<CompanyCalendarDayDto> GenerateDays(
        CompanyCalendarManifest manifest,
        IReadOnlyDictionary<DateOnly, CompanyCalendarManifestDate> exceptions)
    {
        var start = new DateOnly(manifest.CalendarYear, 1, 1);
        var end = new DateOnly(manifest.CalendarYear, 12, 31);
        var result = new List<CompanyCalendarDayDto>();
        for (var date = start; date <= end; date = date.AddDays(1))
        {
            var dayType = date.DayOfWeek switch
            {
                DayOfWeek.Saturday => CompanyCalendarDayType.Saturday,
                DayOfWeek.Sunday => CompanyCalendarDayType.Sunday,
                _ => CompanyCalendarDayType.WorkingDay
            };
            string? name = null;
            string? note = null;
            string? reference = null;
            if (exceptions.TryGetValue(date, out var exception))
            {
                dayType = exception.DayType;
                name = exception.Name.Trim();
                note = exception.SourceNote?.Trim();
                reference = exception.SourceReference?.Trim();
            }

            var isWorking = dayType is CompanyCalendarDayType.WorkingDay
                or CompanyCalendarDayType.ExceptionalWorkingDay;
            result.Add(new CompanyCalendarDayDto(
                Guid.Empty,
                manifest.CalendarYear,
                date,
                dayType,
                name,
                note,
                reference,
                dayType,
                name,
                null,
                null,
                isWorking,
                false,
                string.Empty));
        }

        return result;
    }

    private static void ValidateRequired(string? value, int maximumLength, string field)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Length > maximumLength)
        {
            throw Invalid($"{field}缺少或超過長度限制。");
        }
    }

    private static void ValidateOptional(string? value, int maximumLength, string field)
    {
        if (value?.Trim().Length > maximumLength)
        {
            throw Invalid($"{field}超過長度限制。");
        }
    }

    private static void ValidateHash(string? value, bool required, string field)
    {
        if (!required && string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        if (value is null || value.Length != 64 ||
            value.Any(character => character is not (>= '0' and <= '9')
                and not (>= 'a' and <= 'f')))
        {
            throw Invalid($"{field}必須為 64 碼小寫 SHA-256。");
        }
    }

    private static void ValidateHttpsOfficialUrl(string? value, string field)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            !ApprovedHosts.Contains(uri.Host))
        {
            throw Invalid($"{field}必須是核准的 DGPA HTTPS 官方網址。");
        }
    }

    private static ApplicationValidationException Invalid(string detail) =>
        new($"行事曆檔案格式或日期資料不正確：{detail}");
}
