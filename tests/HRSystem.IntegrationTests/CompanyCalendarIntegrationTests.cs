using System.Net;
using HRSystem.Application.Abstractions.Security;
using HRSystem.Application.Common.Exceptions;
using HRSystem.Application.CompanyCalendars;
using HRSystem.Application.Security;
using HRSystem.Domain.Auditing;
using HRSystem.Domain.CompanyCalendars;
using HRSystem.Infrastructure.CompanyCalendars;
using HRSystem.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HRSystem.IntegrationTests;

public sealed class CompanyCalendarIntegrationTests
{
    [Fact]
    public async Task Calendar_Services_Are_Registered()
    {
        await using var factory = new MasterDataWebApplicationFactory();
        await using var scope = factory.Services.CreateAsyncScope();
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<ICompanyCalendarService>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<ICompanyCalendarQueryService>());
        Assert.IsType<JsonCompanyCalendarManifestReader>(
            scope.ServiceProvider.GetRequiredService<ICompanyCalendarManifestReader>());
    }

    [Theory]
    [InlineData("Admin", HttpStatusCode.OK)]
    [InlineData("Manager", HttpStatusCode.OK)]
    [InlineData("Employee", HttpStatusCode.OK)]
    public async Task Approved_Roles_Can_Open_Read_Calendar(
        string role,
        HttpStatusCode expected)
    {
        await using var factory = new MasterDataWebApplicationFactory();
        var client = factory.CreateClient(
            new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false
            });
        client.DefaultRequestHeaders.Add(TestAuthenticationHandler.RoleHeader, role);
        var response = await client.GetAsync("/company-calendar");
        Assert.Equal(expected, response.StatusCode);
    }

    [Theory]
    [InlineData("Manager")]
    [InlineData("Employee")]
    public async Task NonAdmin_Is_Forbidden_From_Admin_Calendar_Route(string role)
    {
        await using var factory = new MasterDataWebApplicationFactory();
        var client = factory.CreateClient(
            new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false
            });
        client.DefaultRequestHeaders.Add(TestAuthenticationHandler.RoleHeader, role);
        var response = await client.GetAsync("/admin/company-calendar");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Initialization_Publication_Read_Override_And_Archive_Are_Persisted()
    {
        await using var factory = new MasterDataWebApplicationFactory();
        CompanyCalendarYearDetailDto draft;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<ICompanyCalendarService>();
            var bytes = await File.ReadAllBytesAsync(ManifestPath(2026));
            await using var stream = new MemoryStream(bytes);
            var preview = await service.PreviewInitializationAsync(stream);
            draft = await service.InitializeDraftAsync(new InitializeCompanyCalendarRequest
            {
                ManifestBytes = bytes,
                ExpectedManifestHash = preview.ManifestHash
            });
            Assert.Equal(365, draft.Days.Count);
            Assert.Equal(CompanyCalendarStatus.Draft, draft.Year.Status);
        }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var query = scope.ServiceProvider.GetRequiredService<ICompanyCalendarQueryService>();
            await Assert.ThrowsAsync<EntityNotFoundException>(() =>
                query.GetPublishedYearAsync(2026));
        }

        CompanyCalendarYearDetailDto published;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<ICompanyCalendarService>();
            published = await service.PublishAsync(new PublishCompanyCalendarRequest
            {
                Year = 2026,
                RowVersion = draft.Year.RowVersion,
                ExpectedManifestVersion = draft.Year.ManifestVersion,
                ExpectedManifestHash = draft.Year.ManifestHash
            });
        }

        CompanyCalendarYearDetailDto overridden;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<ICompanyCalendarService>();
            var day = published.Days.Single(item => item.Date == new DateOnly(2026, 7, 6));
            overridden = await service.OverridePublishedDayAsync(
                new OverridePublishedCalendarDayRequest
                {
                    Year = 2026,
                    Date = day.Date,
                    DayType = CompanyCalendarDayType.CompanyHoliday,
                    Name = "整合測試公司假日",
                    Reason = "整合測試核准",
                    YearRowVersion = published.Year.RowVersion,
                    DayRowVersion = day.RowVersion
                });
        }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var query = scope.ServiceProvider.GetRequiredService<ICompanyCalendarQueryService>();
            Assert.False(await query.IsWorkingDayAsync(new DateOnly(2026, 7, 6)));
            Assert.Equal(31, (await query.GetMonthAsync(2026, 7)).Count);
            var service = scope.ServiceProvider.GetRequiredService<ICompanyCalendarService>();
            _ = await service.ArchiveAsync(new ArchiveCompanyCalendarRequest
            {
                Year = 2026,
                RowVersion = overridden.Year.RowVersion
            });
        }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HRSystemDbContext>();
            Assert.Equal(365, await db.CompanyCalendarDays.CountAsync());
            Assert.Equal(
                4,
                await db.AuditLogs.CountAsync(log =>
                    log.EntityType == nameof(CompanyCalendarYear) ||
                    log.EntityType == nameof(CompanyCalendarDay)));
            Assert.Equal(
                CompanyCalendarStatus.Archived,
                await db.CompanyCalendarYears.Select(year => year.Status).SingleAsync());
        }
    }

    [Theory]
    [InlineData(RoleNames.Manager)]
    [InlineData(RoleNames.Employee)]
    public async Task Direct_NonAdmin_Service_Invocation_Is_Denied(string role)
    {
        await using var db = new HRSystemDbContext(
            new DbContextOptionsBuilder<HRSystemDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options);
        var service = new CompanyCalendarService(
            db,
            new JsonCompanyCalendarManifestReader(),
            new RoleCurrentUser(role),
            TimeProvider.System);
        await Assert.ThrowsAsync<ForbiddenAccessException>(() =>
            service.GetManagementYearsAsync());
    }

    private static string ManifestPath(int year)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "HRSystem.slnx")))
        {
            directory = directory.Parent;
        }

        return Path.Combine(
            directory?.FullName ?? throw new InvalidOperationException("找不到 Repository Root。"),
            "data",
            "company-calendar",
            $"{year}.json");
    }

    private sealed class RoleCurrentUser(string role) : ICurrentUser
    {
        public string? UserId => "calendar-integration-user";
        public Guid? EmployeeId => null;
        public string? DisplayName => "Calendar Integration User";
        public string? IpAddress => "127.0.0.1";
        public bool IsAuthenticated => true;
        public bool IsInRole(string expectedRole) => role == expectedRole;
        public bool HasPermission(string policy) => RolePermissions.HasPermission([role], policy);
    }
}
