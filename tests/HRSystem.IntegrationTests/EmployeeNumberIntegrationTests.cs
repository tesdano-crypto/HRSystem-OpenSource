using HRSystem.Application.Abstractions.Security;
using HRSystem.Application.Common.Exceptions;
using HRSystem.Application.Employees;
using HRSystem.Application.Security;
using HRSystem.Domain.MasterData;
using HRSystem.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace HRSystem.IntegrationTests;

public sealed class EmployeeNumberIntegrationTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 25, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task First_Create_Returns_Searches_And_Preserves_Mixed_Existing_Numbers()
    {
        await using var factory = new MasterDataWebApplicationFactory();
        var baseline = await SeedMixedBaselineAsync(factory);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<IEmployeeService>();
            var created = await service.CreateAsync(NewRequest(baseline.DepartmentId, "新進員工"));

            Assert.Equal("EMP0015", created.EmployeeNumber);
            Assert.Equal(
                created.Id,
                (await service.GetListAsync(new EmployeeQuery { Keyword = "EMP0015" })).Items.Single().Id);
            Assert.Equal(
                "TEST001",
                (await service.GetListAsync(new EmployeeQuery { Keyword = "test001" }))
                    .Items.Single().EmployeeNumber);
        }

        await using var verificationScope = factory.Services.CreateAsyncScope();
        var db = verificationScope.ServiceProvider.GetRequiredService<HRSystemDbContext>();
        var actual = await db.Employees
            .Where(x => baseline.EmployeeIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.EmployeeNumber);
        Assert.Equal(baseline.NumbersById, actual);
    }

    [Fact]
    public async Task Concurrent_Creates_Return_Distinct_Generated_Numbers()
    {
        await using var factory = new MasterDataWebApplicationFactory();
        var departmentId = await SeedDepartmentAsync(factory, "CON");

        async Task<EmployeeDto> CreateAsync(string name)
        {
            await using var scope = factory.Services.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<IEmployeeService>()
                .CreateAsync(NewRequest(departmentId, name));
        }

        var created = await Task.WhenAll(CreateAsync("併發甲"), CreateAsync("併發乙"));

        Assert.Equal(2, created.Select(x => x.EmployeeNumber).Distinct().Count());
        Assert.Equal(["EMP0015", "EMP0016"], created.Select(x => x.EmployeeNumber).Order().ToArray());
    }

    [Fact]
    public async Task Unauthorized_Create_Does_Not_Allocate_Number()
    {
        await using var factory = new MasterDataWebApplicationFactory();
        var departmentId = await SeedDepartmentAsync(factory, "AUT");
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HRSystemDbContext>();
        var service = new EmployeeService(
            db,
            factory.EmployeeNumbers,
            new NonAdminCurrentUser(),
            TimeProvider.System,
            NullLogger<EmployeeService>.Instance);

        await Assert.ThrowsAsync<ForbiddenAccessException>(() =>
            service.CreateAsync(NewRequest(departmentId, "未授權員工")));

        Assert.Equal(0, factory.EmployeeNumbers.ConsumedCount);
        Assert.Empty(db.Employees);
        Assert.Empty(db.AuditLogs);
    }

    [Fact]
    public async Task Capacity_And_Provider_Failures_Do_Not_Fallback_Or_Persist_Partial_Data()
    {
        await using (var capacityFactory = new MasterDataWebApplicationFactory(
            new TestEmployeeNumberSequence(9999)))
        {
            var departmentId = await SeedDepartmentAsync(capacityFactory, "MAX");
            await using var scope = capacityFactory.Services.CreateAsyncScope();
            var service = scope.ServiceProvider.GetRequiredService<IEmployeeService>();

            Assert.Equal(
                "EMP9999",
                (await service.CreateAsync(NewRequest(departmentId, "最後編號員工"))).EmployeeNumber);
            await Assert.ThrowsAsync<EmployeeNumberCapacityExceededException>(() =>
                service.CreateAsync(NewRequest(departmentId, "超出容量員工")));

            var db = scope.ServiceProvider.GetRequiredService<HRSystemDbContext>();
            Assert.Single(db.Employees);
            Assert.Single(db.AuditLogs.Where(x => x.EntityType == nameof(Employee)));
        }

        var failureSequence = new TestEmployeeNumberSequence
        {
            Failure = new EmployeeNumberGenerationException()
        };
        await using var failureFactory = new MasterDataWebApplicationFactory(failureSequence);
        var failureDepartmentId = await SeedDepartmentAsync(failureFactory, "ERR");
        await using var failureScope = failureFactory.Services.CreateAsyncScope();
        var failureService = failureScope.ServiceProvider.GetRequiredService<IEmployeeService>();

        await Assert.ThrowsAsync<EmployeeNumberGenerationException>(() =>
            failureService.CreateAsync(NewRequest(failureDepartmentId, "Provider 失敗員工")));

        var failureDb = failureScope.ServiceProvider.GetRequiredService<HRSystemDbContext>();
        Assert.Empty(failureDb.Employees);
        Assert.Empty(failureDb.AuditLogs);
    }

    private static async Task<MixedBaseline> SeedMixedBaselineAsync(
        MasterDataWebApplicationFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HRSystemDbContext>();
        var department = NewDepartment("MIX");
        var employees = Enumerable.Range(1, 14)
            .Select(value => NewEmployee($"EMP{value:D4}", department.Id))
            .Concat(
            [
                NewEmployee("TEST001", department.Id),
                NewEmployee("TEST002", department.Id),
                NewEmployee("LEGACY03", department.Id)
            ])
            .ToArray();
        db.Add(department);
        db.AddRange(employees);
        await db.SaveChangesAsync();
        return new MixedBaseline(
            department.Id,
            employees.Select(x => x.Id).ToHashSet(),
            employees.ToDictionary(x => x.Id, x => x.EmployeeNumber));
    }

    private static async Task<Guid> SeedDepartmentAsync(
        MasterDataWebApplicationFactory factory,
        string code)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HRSystemDbContext>();
        var department = NewDepartment(code);
        db.Departments.Add(department);
        await db.SaveChangesAsync();
        return department.Id;
    }

    private static Department NewDepartment(string code) =>
        new(Guid.NewGuid(), code, $"部門 {code}", Now);

    private static Employee NewEmployee(string number, Guid departmentId) =>
        new(
            Guid.NewGuid(),
            number,
            $"合成員工 {number}",
            departmentId,
            new DateOnly(2026, 1, 1),
            Now);

    private static CreateEmployeeRequest NewRequest(Guid departmentId, string name) => new()
    {
        ChineseName = name,
        DepartmentId = departmentId,
        HireDate = new DateOnly(2026, 7, 25)
    };

    private sealed class NonAdminCurrentUser : ICurrentUser
    {
        public string? UserId => "employee-number-test-manager";
        public Guid? EmployeeId => null;
        public string? DisplayName => "Employee Number Test Manager";
        public string? IpAddress => "127.0.0.1";
        public bool IsAuthenticated => true;
        public bool IsInRole(string role) => role == RoleNames.Manager;
        public bool HasPermission(string policy) =>
            RolePermissions.HasPermission([RoleNames.Manager], policy);
    }

    private sealed record MixedBaseline(
        Guid DepartmentId,
        HashSet<Guid> EmployeeIds,
        Dictionary<Guid, string> NumbersById);
}
