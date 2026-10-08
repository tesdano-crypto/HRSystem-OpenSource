using System.Text;
using HRSystem.Application.Common.Exceptions;
using HRSystem.Application.CompanyCalendars;
using HRSystem.Domain.CompanyCalendars;
using HRSystem.Infrastructure.CompanyCalendars;

namespace HRSystem.UnitTests;

public sealed class CompanyCalendarManifestTests
{
    [Theory]
    [InlineData(2026, 365, 16, 6)]
    [InlineData(2027, 365, 16, 8)]
    public async Task Reviewed_Manifest_Is_Canonical_And_Complete(
        int year,
        int expectedDays,
        int expectedNational,
        int expectedSubstitute)
    {
        await using var stream = File.OpenRead(ManifestPath(year));
        var manifest = await new JsonCompanyCalendarManifestReader().ReadAsync(stream);
        var preview = CompanyCalendarManifestValidator.ValidateAndPreview(manifest);

        Assert.Equal(expectedDays, preview.TotalDays);
        Assert.Equal(expectedNational, preview.NationalHolidayDays);
        Assert.Equal(expectedSubstitute, preview.SubstituteHolidayDays);
        Assert.Equal(expectedDays, preview.Days.Select(day => day.Date).Distinct().Count());
        Assert.Equal(64, preview.ManifestHash.Length);
        Assert.All(preview.Days, day => Assert.Equal(year, day.Date.Year));
    }

    [Fact]
    public async Task Weekend_Holiday_Uses_Official_Type_And_Preserves_Weekend_Identity()
    {
        await using var stream = File.OpenRead(ManifestPath(2026));
        var manifest = await new JsonCompanyCalendarManifestReader().ReadAsync(stream);
        var preview = CompanyCalendarManifestValidator.ValidateAndPreview(manifest);
        var peaceDay = Assert.Single(preview.Days, day =>
            day.Date == new DateOnly(2026, 2, 28));

        Assert.Equal(DayOfWeek.Saturday, peaceDay.Date.DayOfWeek);
        Assert.Equal(CompanyCalendarDayType.NationalHoliday, peaceDay.DayType);
        Assert.False(peaceDay.IsWorkingDay);
    }

    [Fact]
    public async Task Leap_Year_Generation_Produces_366_Dates()
    {
        await using var stream = File.OpenRead(ManifestPath(2027));
        var source = await new JsonCompanyCalendarManifestReader().ReadAsync(stream);
        var leap = source with
        {
            CalendarYear = 2028,
            Dates = [],
            ManifestHash = new string('a', 64)
        };

        var generated = CompanyCalendarManifestValidator.GenerateDays(leap);
        Assert.Equal(366, generated.Count);
        Assert.Contains(generated, day => day.Date == new DateOnly(2028, 2, 29));
    }

    [Fact]
    public async Task Reader_Rejects_Bom_Noncanonical_And_Unknown_Fields()
    {
        var bytes = await File.ReadAllBytesAsync(ManifestPath(2026));
        var bom = Encoding.UTF8.GetPreamble().Concat(bytes).ToArray();
        await Assert.ThrowsAsync<ApplicationValidationException>(() =>
            ReadAsync(bom));

        var noncanonical = Encoding.UTF8.GetBytes(
            Encoding.UTF8.GetString(bytes).Replace("  \"schemaVersion\"", "    \"schemaVersion\""));
        await Assert.ThrowsAsync<ApplicationValidationException>(() =>
            ReadAsync(noncanonical));

        var unknown = Encoding.UTF8.GetBytes(
            Encoding.UTF8.GetString(bytes).Replace(
                "\"schemaVersion\": \"1.0\",",
                "\"schemaVersion\": \"1.0\",\n  \"unexpected\": true,"));
        await Assert.ThrowsAsync<ApplicationValidationException>(() =>
            ReadAsync(unknown));
    }

    [Fact]
    public async Task Reader_Rejects_Oversized_Input()
    {
        var bytes = new byte[256 * 1024 + 1];
        await Assert.ThrowsAsync<ApplicationValidationException>(() =>
            ReadAsync(bytes));
    }

    [Fact]
    public void Validator_Rejects_Duplicate_Unsupported_And_External_Source()
    {
        var source = new CompanyCalendarManifestSource(
            "測試機關",
            "測試標題",
            new DateOnly(2025, 1, 1),
            "https://example.com/calendar",
            null,
            null);
        var manifest = new CompanyCalendarManifest(
            "1.0",
            2026,
            source,
            "1.0",
            [
                new(
                    new DateOnly(2026, 1, 1),
                    CompanyCalendarDayType.NationalHoliday,
                    "假日",
                    null,
                    null)
            ],
            new string('a', 64));
        Assert.Throws<ApplicationValidationException>(() =>
            CompanyCalendarManifestValidator.ValidateAndPreview(manifest));
    }

    private static async Task ReadAsync(byte[] bytes)
    {
        await using var stream = new MemoryStream(bytes);
        _ = await new JsonCompanyCalendarManifestReader().ReadAsync(stream);
    }

    internal static string ManifestPath(int year)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "HRSystem.slnx")))
        {
            directory = directory.Parent;
        }

        if (directory is null)
        {
            throw new InvalidOperationException("找不到測試 Repository Root。");
        }

        return Path.Combine(directory.FullName, "data", "company-calendar", $"{year}.json");
    }
}
