using HRSystem.Application.Common.Exceptions;
using HRSystem.Application.CompanyCalendars;
using HRSystem.Domain.CompanyCalendars;
using HRSystem.Infrastructure.CompanyCalendars;
using Microsoft.EntityFrameworkCore;

namespace HRSystem.UnitTests;

public sealed class CompanyCalendarServiceTests
{
    [Fact]
    public async Task Initialize_Publish_Query_And_Archive_Are_Audited_And_Atomic()
    {
        await using var db = TestDb.Create();
        var service = CreateService(db);
        var bytes = await File.ReadAllBytesAsync(
            CompanyCalendarManifestTests.ManifestPath(2026));
        await using var previewStream = new MemoryStream(bytes);
        var preview = await service.PreviewInitializationAsync(previewStream);
        var draft = await service.InitializeDraftAsync(
            new InitializeCompanyCalendarRequest
            {
                ManifestBytes = bytes,
                ExpectedManifestHash = preview.ManifestHash
            });

        Assert.Equal(365, draft.Days.Count);
        Assert.Equal(CompanyCalendarStatus.Draft, draft.Year.Status);
        Assert.Single(db.AuditLogs);

        var published = await service.PublishAsync(new PublishCompanyCalendarRequest
        {
            Year = 2026,
            RowVersion = draft.Year.RowVersion,
            ExpectedManifestVersion = draft.Year.ManifestVersion,
            ExpectedManifestHash = draft.Year.ManifestHash
        });
        Assert.Equal(CompanyCalendarStatus.Published, published.Year.Status);

        var query = new CompanyCalendarQueryService(db, new TestCurrentUser());
        Assert.False(await query.IsWorkingDayAsync(new DateOnly(2026, 1, 1)));
        Assert.Equal(
            published.Days.Count(day => day.IsWorkingDay),
            await query.CountWorkingDaysAsync(
                new DateOnly(2026, 1, 1),
                new DateOnly(2026, 12, 31)));

        var archived = await service.ArchiveAsync(new ArchiveCompanyCalendarRequest
        {
            Year = 2026,
            RowVersion = published.Year.RowVersion
        });
        Assert.Equal(CompanyCalendarStatus.Archived, archived.Year.Status);
        await Assert.ThrowsAsync<EntityNotFoundException>(() =>
            query.GetPublishedYearAsync(2026));
        Assert.Equal(3, await db.AuditLogs.CountAsync());
    }

    [Fact]
    public async Task Manual_Override_Touches_Year_And_Restores_Baseline()
    {
        await using var db = TestDb.Create();
        var service = CreateService(db);
        var draft = await InitializeAsync(service, 2026);
        var target = draft.Days.Single(day => day.Date == new DateOnly(2026, 7, 6));

        var changed = await service.SetCompanyHolidayAsync(
            ChangeRequest(draft, target, "公司活動日"));
        var changedDay = changed.Days.Single(day => day.Date == target.Date);
        Assert.Equal(CompanyCalendarDayType.CompanyHoliday, changedDay.DayType);
        Assert.True(changedDay.IsManualOverride);

        var restored = await service.RemoveCompanyHolidayAsync(
            ChangeRequest(changed, changedDay, null));
        var restoredDay = restored.Days.Single(day => day.Date == target.Date);
        Assert.Equal(CompanyCalendarDayType.WorkingDay, restoredDay.DayType);
        Assert.False(restoredDay.IsManualOverride);
        Assert.Equal(3, await db.AuditLogs.CountAsync());
    }

    [Fact]
    public async Task Query_Rejects_Draft_Invalid_Range_And_Over_366_Days()
    {
        await using var db = TestDb.Create();
        var service = CreateService(db);
        _ = await InitializeAsync(service, 2026);
        var query = new CompanyCalendarQueryService(db, new TestCurrentUser());

        await Assert.ThrowsAsync<EntityNotFoundException>(() =>
            query.GetDayAsync(new DateOnly(2026, 1, 1)));
        await Assert.ThrowsAsync<ApplicationValidationException>(() =>
            query.CountWorkingDaysAsync(
                new DateOnly(2026, 1, 2),
                new DateOnly(2026, 1, 1)));
        await Assert.ThrowsAsync<ApplicationValidationException>(() =>
            query.CountWorkingDaysAsync(
                new DateOnly(2026, 1, 1),
                new DateOnly(2027, 1, 2)));
    }

    [Theory]
    [InlineData("Manager")]
    [InlineData("Employee")]
    public async Task NonAdmin_Can_Read_But_Cannot_Manage(string role)
    {
        await using var db = TestDb.Create();
        var adminService = CreateService(db);
        var draft = await InitializeAsync(adminService, 2026);
        _ = await adminService.PublishAsync(new PublishCompanyCalendarRequest
        {
            Year = 2026,
            RowVersion = draft.Year.RowVersion,
            ExpectedManifestVersion = draft.Year.ManifestVersion,
            ExpectedManifestHash = draft.Year.ManifestHash
        });

        var user = new TestCurrentUser(role);
        var query = new CompanyCalendarQueryService(db, user);
        Assert.NotNull(await query.GetDayAsync(new DateOnly(2026, 1, 1)));
        var service = CreateService(db, user);
        await Assert.ThrowsAsync<ForbiddenAccessException>(() =>
            service.GetManagementYearsAsync());
    }

    private static CompanyCalendarService CreateService(
        HRSystem.Infrastructure.Persistence.HRSystemDbContext db,
        HRSystem.Application.Abstractions.Security.ICurrentUser? user = null) =>
        new(
            db,
            new JsonCompanyCalendarManifestReader(),
            user ?? new TestCurrentUser(),
            TimeProvider.System);

    private static async Task<CompanyCalendarYearDetailDto> InitializeAsync(
        CompanyCalendarService service,
        int year)
    {
        var bytes = await File.ReadAllBytesAsync(
            CompanyCalendarManifestTests.ManifestPath(year));
        await using var stream = new MemoryStream(bytes);
        var preview = await service.PreviewInitializationAsync(stream);
        return await service.InitializeDraftAsync(new InitializeCompanyCalendarRequest
        {
            ManifestBytes = bytes,
            ExpectedManifestHash = preview.ManifestHash
        });
    }

    private static ChangeCompanyCalendarDayRequest ChangeRequest(
        CompanyCalendarYearDetailDto year,
        CompanyCalendarDayDto day,
        string? name) =>
        new()
        {
            Year = year.Year.Year,
            Date = day.Date,
            Name = name,
            Description = "單元測試",
            Reason = "人工核准測試",
            YearRowVersion = year.Year.RowVersion,
            DayRowVersion = day.RowVersion
        };
}
