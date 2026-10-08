using System.Net;
using HRSystem.Application.Security;
using Microsoft.AspNetCore.Mvc.Testing;

namespace HRSystem.IntegrationTests;

public sealed class AttendanceExcelExportWebIntegrationTests
{
    private const string ExportUrl =
        "/api/attendance-review/export.xlsx?startDate=2026-08-01&endDate=2026-08-31" +
        "&employeeId=10000000-0000-0000-0000-000000000001&quickFilter=All" +
        "&onlyAnomalies=false&onlyPending=false";

    [Fact]
    public async Task Admin_Can_Download_Empty_But_Valid_Workbook()
    {
        await using var factory = new MasterDataWebApplicationFactory();
        using var client = CreateClient(factory, RoleNames.Admin);

        var response = await client.GetAsync(ExportUrl);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("Attendance_20260801_20260831.xlsx",
            response.Content.Headers.ContentDisposition?.FileName,
            StringComparison.Ordinal);
        Assert.NotEmpty(await response.Content.ReadAsByteArrayAsync());
    }

    [Theory]
    [InlineData(RoleNames.Manager)]
    [InlineData(RoleNames.Employee)]
    public async Task Non_Admin_Cannot_Directly_Call_Export_Endpoint(string role)
    {
        await using var factory = new MasterDataWebApplicationFactory();
        using var client = CreateClient(factory, role);

        var response = await client.GetAsync(ExportUrl);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Export_Endpoint_Rejects_More_Than_92_Days()
    {
        await using var factory = new MasterDataWebApplicationFactory();
        using var client = CreateClient(factory, RoleNames.Admin);
        var url = ExportUrl.Replace("endDate=2026-08-31", "endDate=2026-11-01",
            StringComparison.Ordinal);

        var response = await client.GetAsync(url);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static HttpClient CreateClient(
        MasterDataWebApplicationFactory factory,
        string role)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        client.DefaultRequestHeaders.Add(TestAuthenticationHandler.RoleHeader, role);
        return client;
    }
}
