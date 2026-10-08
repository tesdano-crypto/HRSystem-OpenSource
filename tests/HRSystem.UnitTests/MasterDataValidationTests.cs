using HRSystem.Application.Common.Exceptions;
using HRSystem.Application.Departments;
using HRSystem.Application.Employees;
using HRSystem.Application.Security;
using HRSystem.Domain.Common;
using HRSystem.Domain.MasterData;

namespace HRSystem.UnitTests;

public sealed class MasterDataValidationTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 18, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Department_Code_Cannot_Be_Duplicated_Case_Insensitively()
    {
        await using var db = TestDb.Create();
        var service = new DepartmentService(db, new TestCurrentUser(), TimeProvider.System);
        await service.CreateAsync(new CreateDepartmentRequest { Code = "adm", Name = "管理部" });

        var exception = await Assert.ThrowsAsync<ApplicationValidationException>(() =>
            service.CreateAsync(new CreateDepartmentRequest { Code = " ADM ", Name = "另一部門" }));

        Assert.Contains("已存在", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Department_Name_Is_Required()
    {
        var exception = Assert.Throws<DomainValidationException>(() =>
            new Department(Guid.NewGuid(), "ADM", "  ", Now));
        Assert.Contains("部門名稱", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Employee_Create_Uses_Generated_Number()
    {
        await using var db = TestDb.Create();
        var department = new Department(Guid.NewGuid(), "ADM", "管理部", Now);
        db.Departments.Add(department);
        await db.SaveChangesAsync();
        var sequence = new TestEmployeeNumberSequence();
        var service = TestEmployeeServices.Create(db, sequence: sequence);
        var request = new CreateEmployeeRequest
        {
            ChineseName = "測試員工", DepartmentId = department.Id,
            HireDate = new DateOnly(2026, 1, 1)
        };

        var created = await service.CreateAsync(request);

        Assert.Equal("EMP0015", created.EmployeeNumber);
        Assert.Equal(1, sequence.ConsumedCount);
        Assert.Single(db.Employees);
        Assert.Single(db.AuditLogs);
    }

    [Fact]
    public void TerminationDate_Cannot_Be_Earlier_Than_HireDate()
    {
        var exception = Assert.Throws<DomainValidationException>(() =>
            new Employee(Guid.NewGuid(), "TEST001", "測試員工", Guid.NewGuid(),
                new DateOnly(2026, 2, 1), Now, terminationDate: new DateOnly(2026, 1, 31)));
        Assert.Contains("離職日", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Employee_Cannot_Use_Inactive_Department()
    {
        await using var db = TestDb.Create();
        var department = new Department(Guid.NewGuid(), "OLD", "停用部門", Now);
        department.Deactivate(Now);
        db.Departments.Add(department);
        await db.SaveChangesAsync();
        var sequence = new TestEmployeeNumberSequence();
        var service = TestEmployeeServices.Create(db, sequence: sequence);

        var exception = await Assert.ThrowsAsync<ApplicationValidationException>(() =>
            service.CreateAsync(new CreateEmployeeRequest
            {
                ChineseName = "測試員工", DepartmentId = department.Id,
                HireDate = new DateOnly(2026, 1, 1)
            }));
        Assert.Contains("啟用", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, sequence.ConsumedCount);
    }

    [Fact]
    public void Employee_Create_And_Update_Contracts_Have_No_Writable_Number()
    {
        Assert.Null(typeof(CreateEmployeeRequest).GetProperty(nameof(Employee.EmployeeNumber)));
        Assert.Null(typeof(UpdateEmployeeRequest).GetProperty(nameof(Employee.EmployeeNumber)));
    }

    [Fact]
    public async Task Non_Admin_Is_Rejected_Before_EmployeeNumber_Allocation()
    {
        await using var db = TestDb.Create();
        var sequence = new TestEmployeeNumberSequence();
        var service = TestEmployeeServices.Create(
            db,
            new TestCurrentUser(RoleNames.Manager),
            sequence);

        await Assert.ThrowsAsync<ForbiddenAccessException>(() =>
            service.CreateAsync(new CreateEmployeeRequest
            {
                ChineseName = "未授權員工",
                DepartmentId = Guid.NewGuid(),
                HireDate = new DateOnly(2026, 1, 1)
            }));

        Assert.Equal(0, sequence.ConsumedCount);
    }

    [Fact]
    public async Task Capacity_Exhaustion_Creates_No_Employee_Or_AuditLog()
    {
        await using var db = TestDb.Create();
        var department = new Department(Guid.NewGuid(), "CAP", "容量測試部門", Now);
        db.Departments.Add(department);
        await db.SaveChangesAsync();
        var service = TestEmployeeServices.Create(
            db,
            sequence: new TestEmployeeNumberSequence(10000));

        await Assert.ThrowsAsync<EmployeeNumberCapacityExceededException>(() =>
            service.CreateAsync(new CreateEmployeeRequest
            {
                ChineseName = "容量測試員工",
                DepartmentId = department.Id,
                HireDate = new DateOnly(2026, 1, 1)
            }));

        Assert.Empty(db.Employees);
        Assert.Empty(db.AuditLogs);
    }

    [Fact]
    public async Task Sequence_Provider_Failure_Creates_No_Employee_Or_AuditLog()
    {
        await using var db = TestDb.Create();
        var department = new Department(Guid.NewGuid(), "SEQ", "序號測試部門", Now);
        db.Departments.Add(department);
        await db.SaveChangesAsync();
        var sequence = new TestEmployeeNumberSequence
        {
            Failure = new EmployeeNumberGenerationException()
        };
        var service = TestEmployeeServices.Create(db, sequence: sequence);

        await Assert.ThrowsAsync<EmployeeNumberGenerationException>(() =>
            service.CreateAsync(new CreateEmployeeRequest
            {
                ChineseName = "序號測試員工",
                DepartmentId = department.Id,
                HireDate = new DateOnly(2026, 1, 1)
            }));

        Assert.Equal(1, sequence.ConsumedCount);
        Assert.Empty(db.Employees);
        Assert.Empty(db.AuditLogs);
    }

    [Fact]
    public void LeaveType_MinimumUnit_Must_Be_Positive()
    {
        var exception = Assert.Throws<DomainValidationException>(() =>
            new LeaveType(Guid.NewGuid(), "SICK", "病假", LeaveUnit.Hour, 0, true, false, 0, Now));
        Assert.Contains("大於 0", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void LeaveType_Day_Unit_Allows_Half_Day_Or_Whole_Days_Only()
    {
        _ = new LeaveType(Guid.NewGuid(), "ANNUAL", "特別休假", LeaveUnit.Day, 0.5m, false, true, 0, Now);
        _ = new LeaveType(Guid.NewGuid(), "OFFICIAL", "公假", LeaveUnit.Day, 2m, false, true, 1, Now);
        Assert.Throws<DomainValidationException>(() =>
            new LeaveType(Guid.NewGuid(), "INVALID", "不合法", LeaveUnit.Day, 1.5m, false, false, 2, Now));
    }

    [Fact]
    public async Task Non_Admin_Cannot_Modify_Master_Data()
    {
        await using var db = TestDb.Create();
        var service = new DepartmentService(db, new TestCurrentUser(RoleNames.Manager), TimeProvider.System);
        await Assert.ThrowsAsync<ForbiddenAccessException>(() =>
            service.CreateAsync(new CreateDepartmentRequest { Code = "NEW", Name = "新部門" }));
    }
}
