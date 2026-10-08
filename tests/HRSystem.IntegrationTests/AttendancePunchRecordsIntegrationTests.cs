using System.Net;
using System.Reflection;
using HRSystem.Application.Attendance;
using HRSystem.Application.Common.Models;
using HRSystem.Web.Components.Shared;

namespace HRSystem.IntegrationTests;

public sealed class AttendancePunchRecordsIntegrationTests
{
    [Fact]
    public async Task Direct_route_requires_existing_self_service_authorization()
    {
        await using var factory = new MasterDataWebApplicationFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthenticationHandler.RoleHeader, "Visitor");

        var response = await client.GetAsync("/attendance/punch-records");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Admin_page_renders_exact_employee_selector_for_long_range_queries()
    {
        await using var factory = new MasterDataWebApplicationFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthenticationHandler.RoleHeader, "Admin");

        var response = await client.GetAsync("/attendance/punch-records");
        var content = await response.Content.ReadAsStringAsync();

        response.EnsureSuccessStatusCode();
        Assert.Contains("id=\"punch-exact-employee\"", content, StringComparison.Ordinal);
        Assert.Contains("精確員工（長區間）", content, StringComparison.Ordinal);
        Assert.Contains("員工（編號／姓名）", content, StringComparison.Ordinal);
    }

    [Fact]
    public void Generic_page_error_does_not_expose_source_or_secret_detail()
    {
        const string unsafeDetail = "Server=private;Password=private;PIN=00042";
        var componentType = typeof(Program).Assembly.GetType(
            "HRSystem.Web.Components.Pages.AttendancePunchRecords",
            throwOnError: true)!;
        var friendly = componentType.GetMethod(
            "Friendly",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Punch record error formatter was not found.");

        var message = (string)friendly.Invoke(null, [new InvalidOperationException(unsafeDetail)])!;

        Assert.Equal("讀取打卡紀錄時發生錯誤，請稍後再試。", message);
        Assert.DoesNotContain("private", message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Password", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Imported_utc_time_is_displayed_as_taiwan_time()
    {
        var formatted = TaipeiTime.Format(new DateTimeOffset(
            2026, 7, 28, 0, 30, 0, TimeSpan.Zero));

        Assert.Equal("2026-07-28 08:30:00", formatted);
    }

    [Fact]
    public async Task Search_returns_to_first_page_and_reset_restores_seven_day_defaults()
    {
        var componentType = typeof(Program).Assembly.GetType(
            "HRSystem.Web.Components.Pages.AttendancePunchRecords",
            throwOnError: true)!;
        var component = Activator.CreateInstance(componentType, nonPublic: true)
            ?? throw new InvalidOperationException("Punch record component could not be created.");
        var service = new CapturingPunchRecordService();
        SetInjectedProperty(componentType, component, "PunchRecordService", service);
        SetInjectedProperty(
            componentType,
            component,
            "TimeProvider",
            new FixedTimeProvider(new DateTimeOffset(
                2026, 7, 28, 1, 0, 0, TimeSpan.Zero)));
        var queryField = componentType.GetField(
            "_query",
            BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("Punch record query state was not found.");
        queryField.SetValue(component, new AttendancePunchRecordQuery
        {
            DateFrom = new DateOnly(2026, 1, 1),
            DateTo = new DateOnly(2026, 1, 31),
            EmployeeId = Guid.NewGuid(),
            EmployeeKeyword = "keyword",
            BioWebPin = "05",
            MappingStatus = AttendancePunchMappingStatus.Unmapped,
            DeviceSerialNumber = "DEVICE",
            StatusCode = 7,
            VerifyCode = 9,
            PageNumber = 8,
            PageSize = 100
        });

        await InvokeAsync(componentType, component, "SearchAsync");

        Assert.NotNull(service.LastQuery);
        Assert.Equal(1, service.LastQuery.PageNumber);
        Assert.Equal("keyword", service.LastQuery.EmployeeKeyword);
        Assert.Equal("05", service.LastQuery.BioWebPin);

        await InvokeAsync(componentType, component, "ResetAsync");

        var reset = Assert.IsType<AttendancePunchRecordQuery>(
            queryField.GetValue(component));
        Assert.Equal(new DateOnly(2026, 7, 22), reset.DateFrom);
        Assert.Equal(new DateOnly(2026, 7, 28), reset.DateTo);
        Assert.Null(reset.EmployeeId);
        Assert.Null(reset.EmployeeKeyword);
        Assert.Null(reset.BioWebPin);
        Assert.Equal(AttendancePunchMappingStatus.All, reset.MappingStatus);
        Assert.Null(reset.DeviceSerialNumber);
        Assert.Null(reset.StatusCode);
        Assert.Null(reset.VerifyCode);
        Assert.Equal(1, reset.PageNumber);
        Assert.Equal(50, reset.PageSize);
    }

    private static void SetInjectedProperty(
        Type componentType,
        object component,
        string propertyName,
        object value)
    {
        var property = componentType.GetProperty(
            propertyName,
            BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException(
                $"Punch record component property {propertyName} was not found.");
        property.SetValue(component, value);
    }

    private static async Task InvokeAsync(
        Type componentType,
        object component,
        string methodName)
    {
        var method = componentType.GetMethod(
            methodName,
            BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException(
                $"Punch record component method {methodName} was not found.");
        await ((Task?)method.Invoke(component, null)
            ?? throw new InvalidOperationException(
                $"Punch record component method {methodName} returned no task."));
    }

    private sealed class CapturingPunchRecordService : IAttendancePunchRecordService
    {
        public AttendancePunchRecordQuery? LastQuery { get; private set; }

        public Task<IReadOnlyList<AttendancePunchEmployeeOptionDto>>
            GetEmployeeOptionsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AttendancePunchEmployeeOptionDto>>([]);

        public Task<PagedResult<AttendancePunchRecordDto>> GetListAsync(
            AttendancePunchRecordQuery query,
            CancellationToken cancellationToken = default)
        {
            LastQuery = new AttendancePunchRecordQuery
            {
                DateFrom = query.DateFrom,
                DateTo = query.DateTo,
                EmployeeId = query.EmployeeId,
                EmployeeKeyword = query.EmployeeKeyword,
                BioWebPin = query.BioWebPin,
                MappingStatus = query.MappingStatus,
                DeviceSerialNumber = query.DeviceSerialNumber,
                StatusCode = query.StatusCode,
                VerifyCode = query.VerifyCode,
                PageNumber = query.PageNumber,
                PageSize = query.PageSize
            };
            return Task.FromResult(new PagedResult<AttendancePunchRecordDto>(
                [],
                0,
                query.PageNumber,
                query.PageSize));
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
