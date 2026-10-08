using HRSystem.Application.AuditLogs;
using HRSystem.Application.Common.Exceptions;
using HRSystem.Application.Departments;
using HRSystem.Application.Employees;
using HRSystem.Application.LeaveTypes;
using HRSystem.Application.Security;
using HRSystem.Domain.MasterData;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using System.Net;

namespace HRSystem.IntegrationTests;

public sealed class MasterDataIntegrationTests : IClassFixture<MasterDataWebApplicationFactory>
{
    private readonly MasterDataWebApplicationFactory _factory;

    public MasterDataIntegrationTests(MasterDataWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Admin_Can_Read_Department_Page()
    {
        var client = CreateClient();
        var html = await client.GetStringAsync("/departments");
        Assert.Contains("部門管理", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Manager_Can_Read_Department_Page()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthenticationHandler.RoleHeader, RoleNames.Manager);
        var response = await client.GetAsync("/departments");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Admin_Can_Create_Department()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IDepartmentService>();
        var created = await service.CreateAsync(new CreateDepartmentRequest
        {
            Code = $"D{Guid.NewGuid():N}"[..10], Name = "整合測試部門"
        });
        Assert.Equal("整合測試部門", created.Name);
    }

    [Fact]
    public async Task Employee_Cannot_Enter_Department_Page()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthenticationHandler.RoleHeader, RoleNames.Employee);
        var response = await client.GetAsync("/departments");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Admin_Can_Create_Employee()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var departments = scope.ServiceProvider.GetRequiredService<IDepartmentService>();
        var employees = scope.ServiceProvider.GetRequiredService<IEmployeeService>();
        var department = await departments.CreateAsync(new CreateDepartmentRequest
        {
            Code = $"E{Guid.NewGuid():N}"[..10], Name = "員工測試部門"
        });
        var employee = await employees.CreateAsync(new CreateEmployeeRequest
        {
            ChineseName = "整合測試員工",
            DepartmentId = department.Id, HireDate = new DateOnly(2026, 7, 18)
        });
        Assert.Equal(department.Id, employee.DepartmentId);
        Assert.Equal("EMP0015", employee.EmployeeNumber);
    }

    [Fact]
    public async Task Admin_And_Manager_Can_Read_Employee_Page_But_Employee_Cannot()
    {
        var admin = CreateClient();
        var manager = CreateClient();
        manager.DefaultRequestHeaders.Add(TestAuthenticationHandler.RoleHeader, RoleNames.Manager);
        var employee = CreateClient();
        employee.DefaultRequestHeaders.Add(TestAuthenticationHandler.RoleHeader, RoleNames.Employee);

        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/employees")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await manager.GetAsync("/employees")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await employee.GetAsync("/employees")).StatusCode);
    }

    [Fact]
    public async Task Admin_Can_Create_LeaveType()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<ILeaveTypeService>();
        var created = await service.CreateAsync(new CreateLeaveTypeRequest
        {
            Code = $"L{Guid.NewGuid():N}"[..10], Name = "整合測試假別",
            Unit = LeaveUnit.Hour, MinimumUnit = 0.5m
        });
        Assert.Equal(0.5m, created.MinimumUnit);
    }

    [Fact]
    public async Task Only_Admin_Can_Read_LeaveType_Page()
    {
        var admin = CreateClient();
        var manager = CreateClient();
        manager.DefaultRequestHeaders.Add(TestAuthenticationHandler.RoleHeader, RoleNames.Manager);
        var employee = CreateClient();
        employee.DefaultRequestHeaders.Add(TestAuthenticationHandler.RoleHeader, RoleNames.Employee);

        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/leave-types")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.GetAsync("/leave-types")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await employee.GetAsync("/leave-types")).StatusCode);
    }

    [Fact]
    public async Task Create_Operation_Writes_AuditLog()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var departments = scope.ServiceProvider.GetRequiredService<IDepartmentService>();
        var logs = scope.ServiceProvider.GetRequiredService<IAuditLogService>();
        await departments.CreateAsync(new CreateDepartmentRequest
        {
            Code = $"A{Guid.NewGuid():N}"[..10], Name = "稽核測試部門"
        });
        var result = await logs.GetListAsync(new AuditLogQuery { Action = "Created", PageSize = 100 });
        Assert.Contains(result.Items, x => x.EntityType == "Department");
    }

    [Fact]
    public async Task Duplicate_Code_Returns_Friendly_Validation_Error()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IDepartmentService>();
        var code = $"X{Guid.NewGuid():N}"[..10];
        await service.CreateAsync(new CreateDepartmentRequest { Code = code, Name = "第一部門" });
        var exception = await Assert.ThrowsAsync<ApplicationValidationException>(() =>
            service.CreateAsync(new CreateDepartmentRequest { Code = code.ToLowerInvariant(), Name = "第二部門" }));
        Assert.Contains("已存在", exception.Message, StringComparison.Ordinal);
    }

    private HttpClient CreateClient() => _factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false
    });
}
