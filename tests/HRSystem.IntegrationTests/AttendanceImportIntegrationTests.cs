using System.Net;
using System.Reflection;
using HRSystem.Application.Attendance;
using HRSystem.Application.Common.Auditing;
using HRSystem.Application.Security;
using HRSystem.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HRSystem.IntegrationTests;

public sealed class AttendanceImportIntegrationTests
{
    [Fact]
    public async Task Admin_Can_Open_Mapping_And_Import_Pages()
    {
        await using var factory = new MasterDataWebApplicationFactory();
        using var client = CreateClient(factory);

        var mappings = await client.GetAsync("/admin/biowebta-mappings");
        var import = await client.GetAsync("/admin/attendance-import");
        var importHtml = await import.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, mappings.StatusCode);
        Assert.Equal(HttpStatusCode.OK, import.StatusCode);
        Assert.Contains("自動匯入目前停用", importHtml, StringComparison.Ordinal);
        Assert.Contains("尚無自動匯入紀錄", importHtml, StringComparison.Ordinal);
        Assert.Contains("尚無手動匯入紀錄", importHtml, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(RoleNames.Manager)]
    [InlineData(RoleNames.Employee)]
    public async Task Non_Admin_Cannot_Open_Attendance_Administration(string role)
    {
        await using var factory = new MasterDataWebApplicationFactory();
        using var client = CreateClient(factory);
        client.DefaultRequestHeaders.Add(TestAuthenticationHandler.RoleHeader, role);

        var mappings = await client.GetAsync("/admin/biowebta-mappings");
        var import = await client.GetAsync("/admin/attendance-import");

        Assert.Equal(HttpStatusCode.Forbidden, mappings.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, import.StatusCode);
    }

    [Fact]
    public async Task Manual_Sync_Uses_Fake_Source_And_Persists_Raw_Event()
    {
        await using var factory = new MasterDataWebApplicationFactory();
        factory.BioWebTaSource.Add(new BioWebAttendanceSourceRecord(
            9001,
            "UNMAPPED-INTEGRATION",
            "DEVICE-TEST",
            Local(2026, 7, 27, 8),
            4,
            7,
            Local(2026, 7, 27, 8, 1)));
        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IAttendanceImportService>();

        var result = await service.SyncNowAsync();
        var db = scope.ServiceProvider.GetRequiredService<HRSystemDbContext>();

        Assert.Equal(1, result.ImportedCount);
        Assert.Equal(1, result.UnmappedImportedCount);
        Assert.Equal(1, factory.BioWebTaSource.ReadCount);
        Assert.Equal(1, await db.AttendanceRawEvents.CountAsync());
        Assert.Contains(
            await db.AuditLogs.ToListAsync(),
            item => item.Action == AuditActions.AttendanceSyncCompleted);
    }

    [Fact]
    public async Task Attendance_Dependencies_Resolve_Without_Source_Credentials()
    {
        await using var factory = new MasterDataWebApplicationFactory();
        await using var scope = factory.Services.CreateAsyncScope();

        Assert.IsType<TestBioWebTaAttendanceSource>(
            scope.ServiceProvider.GetRequiredService<IBioWebTaAttendanceSource>());
        Assert.NotNull(
            scope.ServiceProvider.GetRequiredService<IBioWebPersonMappingService>());
        Assert.NotNull(
            scope.ServiceProvider.GetRequiredService<IAttendanceImportService>());
        Assert.NotNull(
            scope.ServiceProvider.GetRequiredService<IBioWebTaImportCoordinator>());
        Assert.NotNull(
            scope.ServiceProvider.GetRequiredService<IBioWebTaImportStatusService>());
    }

    [Fact]
    public async Task Manual_And_Scheduled_Coordinator_Persists_Operational_Batch()
    {
        await using var factory = new MasterDataWebApplicationFactory(services =>
        {
            services.RemoveAll<BioWebTaScheduledImportOptions>();
            services.AddSingleton(new BioWebTaScheduledImportOptions
            {
                Enabled = true,
                OverlapDays = 3,
                PageSize = 500,
                MaxExecutionMinutes = 30,
                TimeZoneId = "Asia/Taipei"
            });
            services.RemoveAll<IBioWebTaImportExecutionLock>();
            services.AddSingleton<IBioWebTaImportExecutionLock, AvailableLock>();
        });
        factory.BioWebTaSource.Add(new BioWebAttendanceSourceRecord(
            9101,
            "UNMAPPED-SCHEDULED",
            "DEVICE-TEST",
            Local(2026, 8, 12, 8),
            4,
            7,
            Local(2026, 8, 12, 8, 1)));
        await using var scope = factory.Services.CreateAsyncScope();
        var coordinator = scope.ServiceProvider
            .GetRequiredService<IBioWebTaImportCoordinator>();

        var result = await coordinator.RunAsync(new(
            HRSystem.Domain.Attendance.BioWebTaImportTriggerType.Manual,
            Local(2026, 8, 11),
            Local(2026, 8, 13)));
        var db = scope.ServiceProvider.GetRequiredService<HRSystemDbContext>();

        Assert.Equal(BioWebTaImportExecutionOutcome.Completed, result.Outcome);
        Assert.Equal(1, result.InsertedCount);
        Assert.Equal(1, result.UnmappedCount);
        Assert.Equal(0, result.RecalculatedCount);
        Assert.Single(await db.BioWebTaImportBatches.ToListAsync());
        Assert.Single(await db.AttendanceRawEvents.ToListAsync());
    }

    [Fact]
    public async Task Mapping_load_failure_shows_only_generic_error_and_writes_nothing()
    {
        const string unsafeDetail =
            "Invalid source detail;Server=private;User Id=private;Password=private";
        await using var factory = new MasterDataWebApplicationFactory(services =>
        {
            services.RemoveAll<IBioWebPersonMappingService>();
            services.AddSingleton<IBioWebPersonMappingService>(
                new FailingMappingService(unsafeDetail));
        });
        using var client = CreateClient(factory);

        var response = await client.GetAsync("/admin/biowebta-mappings");
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HRSystemDbContext>();
        var message = InvokeMappingPageFriendlyError(
            new InvalidOperationException(unsafeDetail));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(
            "處理 BioWebTA 對照時發生錯誤，請稍後再試。",
            message,
            StringComparison.Ordinal);
        Assert.DoesNotContain(unsafeDetail, message, StringComparison.Ordinal);
        Assert.DoesNotContain("Password=", message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, factory.BioWebTaSource.ReadCount);
        Assert.Equal(0, await db.AttendanceSyncStates.CountAsync());
        Assert.Equal(0, await db.AttendanceRawEvents.CountAsync());
        Assert.Equal(0, await db.BioWebPersonMappings.CountAsync());
    }

    private static HttpClient CreateClient(
        MasterDataWebApplicationFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false
        });

    private static DateTime Local(
        int year,
        int month,
        int day,
        int hour = 0,
        int minute = 0) =>
        new(year, month, day, hour, minute, 0, DateTimeKind.Unspecified);

    private static string InvokeMappingPageFriendlyError(Exception exception)
    {
        var componentType = typeof(Program).Assembly.GetType(
            "HRSystem.Web.Components.Pages.AdminBioWebTaMappings",
            throwOnError: true)!;
        var method = componentType.GetMethod(
            "Friendly",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Mapping page error formatter was not found.");
        return (string)method.Invoke(null, [exception])!;
    }

    private sealed class FailingMappingService(string unsafeDetail)
        : IBioWebPersonMappingService
    {
        public Task<IReadOnlyList<BioWebPersonMappingDto>> GetMappingsAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromException<IReadOnlyList<BioWebPersonMappingDto>>(
                new InvalidOperationException(unsafeDetail));

        public Task<IReadOnlyList<UnmappedBioWebPinDto>> GetUnmappedPinsAsync(
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<BioWebPersonMappingDto> CreateAsync(
            CreateBioWebPersonMappingRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<BioWebPersonMappingDto> UpdateAsync(
            UpdateBioWebPersonMappingRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task DeactivateAsync(
            Guid id,
            string rowVersion,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<BioWebRelinkPreviewDto> GetRelinkPreviewAsync(
            Guid mappingId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<BioWebRelinkResultDto> RelinkUnmappedEventsAsync(
            Guid mappingId,
            string rowVersion,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class AvailableLock : IBioWebTaImportExecutionLock
    {
        public ValueTask<IAsyncDisposable?> TryAcquireAsync(
            TimeSpan timeout,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IAsyncDisposable?>(new Lease());

        private sealed class Lease : IAsyncDisposable
        {
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
