using HRSystem.Application.Common.Exceptions;
using HRSystem.Application.Departments;
using HRSystem.Application.Employees;
using HRSystem.Application.LeaveTypes;
using HRSystem.Application.Security;
using HRSystem.Domain.MasterData;
using Microsoft.EntityFrameworkCore;

namespace HRSystem.UnitTests;

public sealed class MasterDataIntegrityTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 19, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Department_With_Active_Employee_Cannot_Be_Deactivated_And_Is_Not_Partially_Changed()
    {
        await using var db = TestDb.Create();
        var department = NewDepartment("OPS", "營運部");
        db.AddRange(department, NewEmployee("E001", department.Id));
        await db.SaveChangesAsync();
        var service = new DepartmentService(db, new TestCurrentUser(), TimeProvider.System);

        var exception = await Assert.ThrowsAsync<ApplicationValidationException>(() =>
            service.SetActiveAsync(department.Id, false, RowVersion(department.RowVersion)));

        Assert.Contains("請先轉調或停用", exception.Message, StringComparison.Ordinal);
        Assert.True(department.IsActive);
        Assert.Empty(db.AuditLogs);
    }

    [Fact]
    public async Task Department_With_Only_Inactive_Employees_Can_Be_Deactivated_And_Is_Audited()
    {
        await using var db = TestDb.Create();
        var department = NewDepartment("OLD", "舊部門");
        var employee = NewEmployee("E002", department.Id);
        employee.Deactivate(Now);
        db.AddRange(department, employee);
        await db.SaveChangesAsync();
        var service = new DepartmentService(db, new TestCurrentUser(), TimeProvider.System);

        await service.SetActiveAsync(department.Id, false, RowVersion(department.RowVersion));

        Assert.False(department.IsActive);
        Assert.Contains(db.AuditLogs, x => x.EntityType == nameof(Department) && x.Action == "Deactivated");
    }

    [Fact]
    public async Task Empty_Department_Can_Be_Deactivated()
    {
        await using var db = TestDb.Create();
        var department = NewDepartment("NEW", "新部門");
        db.Departments.Add(department);
        await db.SaveChangesAsync();

        await new DepartmentService(db, new TestCurrentUser(), TimeProvider.System)
            .SetActiveAsync(department.Id, false, RowVersion(department.RowVersion));

        Assert.False(department.IsActive);
    }

    [Fact]
    public async Task Department_Deactivation_Rejects_Stale_RowVersion()
    {
        await using var db = TestDb.Create();
        var department = NewDepartment("FIN", "財務部");
        db.Departments.Add(department);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<ConcurrencyConflictException>(() =>
            new DepartmentService(db, new TestCurrentUser(), TimeProvider.System)
                .SetActiveAsync(department.Id, false, Convert.ToBase64String([1])));
    }

    [Fact]
    public async Task Department_Query_Supports_Filter_And_Pagination()
    {
        await using var db = TestDb.Create();
        db.Departments.AddRange(NewDepartment("A01", "甲部"), NewDepartment("A02", "乙部"), NewDepartment("B01", "丙部"));
        await db.SaveChangesAsync();

        var result = await new DepartmentService(db, new TestCurrentUser(RoleNames.Manager), TimeProvider.System)
            .GetListAsync(new DepartmentQuery { Keyword = "A", PageNumber = 2, PageSize = 1 });

        Assert.Equal(2, result.TotalCount);
        Assert.Single(result.Items);
        Assert.Equal("A02", result.Items[0].Code);
    }

    [Fact]
    public async Task Department_Update_Deactivation_And_Reactivation_Remain_Supported()
    {
        await using var db = TestDb.Create();
        var department = NewDepartment("LIFE", "原名稱");
        db.Departments.Add(department);
        await db.SaveChangesAsync();
        var service = new DepartmentService(db, new TestCurrentUser(), TimeProvider.System);

        var updated = await service.UpdateAsync(new UpdateDepartmentRequest
        {
            Id = department.Id,
            Code = department.Code,
            Name = "更新名稱",
            RowVersion = RowVersion(department.RowVersion)
        });
        await service.SetActiveAsync(department.Id, false, updated.RowVersion);
        await service.SetActiveAsync(department.Id, true, RowVersion(department.RowVersion));

        Assert.Equal("更新名稱", department.Name);
        Assert.True(department.IsActive);
        Assert.Contains(db.AuditLogs, x => x.EntityType == nameof(Department) && x.Action == "Updated");
        Assert.Contains(db.AuditLogs, x => x.EntityType == nameof(Department) && x.Action == "Activated");
    }

    [Fact]
    public async Task Manager_Can_Read_But_Cannot_Manage_Departments()
    {
        await using var db = TestDb.Create();
        var department = NewDepartment("HR", "人資部");
        db.Departments.Add(department);
        await db.SaveChangesAsync();
        var service = new DepartmentService(db, new TestCurrentUser(RoleNames.Manager), TimeProvider.System);

        Assert.Equal(department.Id, (await service.GetAsync(department.Id)).Id);
        await Assert.ThrowsAsync<ForbiddenAccessException>(() =>
            service.SetActiveAsync(department.Id, false, RowVersion(department.RowVersion)));
    }

    [Fact]
    public async Task Department_Manager_Cannot_Transfer_And_Failure_Does_Not_Partially_Change_Employee()
    {
        await using var db = TestDb.Create();
        var source = NewDepartment("SRC", "原部門");
        var target = NewDepartment("DST", "新部門");
        var manager = NewEmployee("M001", source.Id);
        db.AddRange(source, target, manager);
        await db.SaveChangesAsync();
        source.Update(source.Code, source.Name, manager.Id, Now);
        await db.SaveChangesAsync();
        var service = TestEmployeeServices.Create(db);

        var exception = await Assert.ThrowsAsync<ApplicationValidationException>(() =>
            service.UpdateAsync(Update(manager, target.Id)));

        Assert.Contains("請先改派或移除部門主管", exception.Message, StringComparison.Ordinal);
        Assert.Equal(source.Id, manager.DepartmentId);
        Assert.Empty(db.AuditLogs);
    }

    [Fact]
    public async Task Department_Manager_Cannot_Be_Deactivated()
    {
        await using var db = TestDb.Create();
        var department = NewDepartment("MGT", "管理部");
        var manager = NewEmployee("M002", department.Id);
        db.AddRange(department, manager);
        await db.SaveChangesAsync();
        department.Update(department.Code, department.Name, manager.Id, Now);
        await db.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<ApplicationValidationException>(() =>
            TestEmployeeServices.Create(db)
                .SetActiveAsync(manager.Id, false, RowVersion(manager.RowVersion)));

        Assert.Contains("請先改派或移除部門主管", exception.Message, StringComparison.Ordinal);
        Assert.True(manager.IsActive);
        Assert.Empty(db.AuditLogs);
    }

    [Fact]
    public async Task Employee_In_Inactive_Department_Cannot_Be_Activated()
    {
        await using var db = TestDb.Create();
        var department = NewDepartment("OFF", "停用部門");
        var employee = NewEmployee("E006", department.Id);
        employee.Deactivate(Now);
        department.Deactivate(Now);
        db.AddRange(department, employee);
        await db.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<ApplicationValidationException>(() =>
            TestEmployeeServices.Create(db)
                .SetActiveAsync(employee.Id, true, RowVersion(employee.RowVersion)));

        Assert.Contains("存在且啟用的部門", exception.Message, StringComparison.Ordinal);
        Assert.False(employee.IsActive);
        Assert.Empty(db.AuditLogs);
    }

    [Fact]
    public async Task Employee_Can_Transfer_After_Manager_Assignment_Is_Removed()
    {
        await using var db = TestDb.Create();
        var source = NewDepartment("S01", "來源部門");
        var target = NewDepartment("T01", "目標部門");
        var manager = NewEmployee("M003", source.Id);
        db.AddRange(source, target, manager);
        await db.SaveChangesAsync();
        source.Update(source.Code, source.Name, manager.Id, Now);
        await db.SaveChangesAsync();
        source.Update(source.Code, source.Name, null, Now);
        await db.SaveChangesAsync();

        var updated = await TestEmployeeServices.Create(db)
            .UpdateAsync(Update(manager, target.Id));

        Assert.Equal(target.Id, updated.DepartmentId);
        Assert.Equal("M003", updated.EmployeeNumber);
    }

    [Fact]
    public async Task Employee_Can_Be_Deactivated_After_Manager_Is_Reassigned_And_Is_Audited()
    {
        await using var db = TestDb.Create();
        var department = NewDepartment("S02", "來源部門");
        var oldManager = NewEmployee("M004", department.Id);
        var newManager = NewEmployee("M005", department.Id);
        db.AddRange(department, oldManager, newManager);
        await db.SaveChangesAsync();
        department.Update(department.Code, department.Name, newManager.Id, Now);
        await db.SaveChangesAsync();

        await TestEmployeeServices.Create(db)
            .SetActiveAsync(oldManager.Id, false, RowVersion(oldManager.RowVersion));

        Assert.False(oldManager.IsActive);
        Assert.Equal("M004", oldManager.EmployeeNumber);
        Assert.Contains(db.AuditLogs, x => x.EntityType == nameof(Employee) && x.Action == "Deactivated");
    }

    [Fact]
    public async Task Non_Manager_Employee_Can_Transfer()
    {
        await using var db = TestDb.Create();
        var source = NewDepartment("S03", "來源部門");
        var target = NewDepartment("T03", "目標部門");
        var employee = NewEmployee("E003", source.Id);
        db.AddRange(source, target, employee);
        await db.SaveChangesAsync();

        var updated = await TestEmployeeServices.Create(db)
            .UpdateAsync(Update(employee, target.Id));

        Assert.Equal(target.Id, updated.DepartmentId);
        Assert.Equal("E003", updated.EmployeeNumber);
        Assert.Contains(db.AuditLogs, x => x.EntityType == nameof(Employee) && x.Action == "Updated");
    }

    [Fact]
    public async Task Employee_Number_Remains_Immutable_During_Name_Status_And_Reactivation_Changes()
    {
        await using var db = TestDb.Create();
        var department = NewDepartment("IMM", "不可變測試部門");
        var employee = NewEmployee("EMP0007", department.Id);
        db.AddRange(department, employee);
        await db.SaveChangesAsync();
        var sequence = new TestEmployeeNumberSequence();
        var service = TestEmployeeServices.Create(db, sequence: sequence);
        var update = Update(employee, department.Id);
        update.ChineseName = "更新後姓名";

        var updated = await service.UpdateAsync(update);
        await service.SetActiveAsync(employee.Id, false, updated.RowVersion);
        await service.SetActiveAsync(employee.Id, true, RowVersion(employee.RowVersion));

        Assert.Equal("EMP0007", employee.EmployeeNumber);
        Assert.Equal("更新後姓名", employee.ChineseName);
        Assert.True(employee.IsActive);
        Assert.Equal(0, sequence.ConsumedCount);
    }

    [Fact]
    public async Task Employee_Update_Rejects_Stale_RowVersion()
    {
        await using var db = TestDb.Create();
        var department = NewDepartment("ROW", "版本部門");
        var employee = NewEmployee("E004", department.Id);
        db.AddRange(department, employee);
        await db.SaveChangesAsync();
        var request = Update(employee, department.Id);
        request.RowVersion = Convert.ToBase64String([1]);

        await Assert.ThrowsAsync<ConcurrencyConflictException>(() =>
            TestEmployeeServices.Create(db).UpdateAsync(request));
    }

    [Fact]
    public async Task Manager_Can_Read_But_Cannot_Manage_Employees()
    {
        await using var db = TestDb.Create();
        var department = NewDepartment("READ", "查詢部門");
        var employee = NewEmployee("E005", department.Id);
        db.AddRange(department, employee);
        await db.SaveChangesAsync();
        var service = TestEmployeeServices.Create(db, new TestCurrentUser(RoleNames.Manager));

        Assert.Equal(employee.Id, (await service.GetAsync(employee.Id)).Id);
        await Assert.ThrowsAsync<ForbiddenAccessException>(() =>
            service.SetActiveAsync(employee.Id, false, RowVersion(employee.RowVersion)));
    }

    [Fact]
    public async Task Employee_Query_Supports_Keyword_Department_Status_And_Pagination()
    {
        await using var db = TestDb.Create();
        var target = NewDepartment("QRY", "查詢部門");
        var other = NewDepartment("OTH", "其他部門");
        var first = NewEmployee("TEAM01", target.Id);
        var second = NewEmployee("TEAM02", target.Id);
        var inactive = NewEmployee("TEAM03", target.Id);
        inactive.Deactivate(Now);
        db.AddRange(target, other, first, second, inactive, NewEmployee("TEAM04", other.Id));
        await db.SaveChangesAsync();

        var result = await TestEmployeeServices.Create(db, new TestCurrentUser(RoleNames.Manager))
            .GetListAsync(new EmployeeQuery
            {
                Keyword = "team",
                DepartmentId = target.Id,
                IsActive = true,
                PageNumber = 2,
                PageSize = 1
            });

        Assert.Equal(2, result.TotalCount);
        Assert.Single(result.Items);
        Assert.Equal("TEAM02", result.Items[0].EmployeeNumber);
    }

    [Fact]
    public async Task LeaveType_Is_Admin_Only_And_Uses_RowVersion()
    {
        await using var db = TestDb.Create();
        var leaveType = NewLeaveType();
        db.LeaveTypes.Add(leaveType);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<ForbiddenAccessException>(() =>
            new LeaveTypeService(db, new TestCurrentUser(RoleNames.Manager), TimeProvider.System)
                .GetAsync(leaveType.Id));
        await Assert.ThrowsAsync<ConcurrencyConflictException>(() =>
            new LeaveTypeService(db, new TestCurrentUser(), TimeProvider.System)
                .SetActiveAsync(leaveType.Id, false, Convert.ToBase64String([1])));
    }

    [Fact]
    public async Task LeaveType_Filter_And_Pagination_Remain_Supported()
    {
        await using var db = TestDb.Create();
        var active1 = NewLeaveType("A", 1);
        var active2 = NewLeaveType("B", 2);
        var inactive = NewLeaveType("C", 3);
        inactive.Deactivate(Now);
        db.LeaveTypes.AddRange(active1, active2, inactive);
        await db.SaveChangesAsync();

        var result = await new LeaveTypeService(db, new TestCurrentUser(), TimeProvider.System)
            .GetListAsync(new LeaveTypeQuery { IsActive = true, PageNumber = 2, PageSize = 1 });

        Assert.Equal(2, result.TotalCount);
        Assert.Single(result.Items);
        Assert.Equal("B", result.Items[0].Code);
    }

    [Fact]
    public async Task LeaveType_Update_Deactivation_And_Reactivation_Remain_Supported_And_Audited()
    {
        await using var db = TestDb.Create();
        var leaveType = NewLeaveType("LIFE", 1);
        db.LeaveTypes.Add(leaveType);
        await db.SaveChangesAsync();
        var service = new LeaveTypeService(db, new TestCurrentUser(), TimeProvider.System);

        var updated = await service.UpdateAsync(new UpdateLeaveTypeRequest
        {
            Id = leaveType.Id,
            Code = leaveType.Code,
            Name = "更新假別",
            Unit = leaveType.Unit,
            MinimumUnit = leaveType.MinimumUnit,
            RequiresReason = leaveType.RequiresReason,
            IsPaid = leaveType.IsPaid,
            SortOrder = 2,
            RowVersion = RowVersion(leaveType.RowVersion)
        });
        await service.SetActiveAsync(leaveType.Id, false, updated.RowVersion);
        await service.SetActiveAsync(leaveType.Id, true, RowVersion(leaveType.RowVersion));

        Assert.Equal("更新假別", leaveType.Name);
        Assert.True(leaveType.IsActive);
        Assert.Contains(db.AuditLogs, x => x.EntityType == nameof(LeaveType) && x.Action == "Updated");
        Assert.Contains(db.AuditLogs, x => x.EntityType == nameof(LeaveType) && x.Action == "Activated");
    }

    private static Department NewDepartment(string code, string name) => new(Guid.NewGuid(), code, name, Now);

    private static Employee NewEmployee(string number, Guid departmentId) =>
        new(Guid.NewGuid(), number, $"員工{number}", departmentId, new DateOnly(2026, 1, 1), Now);

    private static LeaveType NewLeaveType(string code = "ANNUAL", int sortOrder = 1) =>
        new(Guid.NewGuid(), code, $"假別{code}", LeaveUnit.Hour, 0.5m, true, false, sortOrder, Now);

    private static UpdateEmployeeRequest Update(Employee employee, Guid departmentId) => new()
    {
        Id = employee.Id,
        ChineseName = employee.ChineseName,
        EnglishName = employee.EnglishName,
        DepartmentId = departmentId,
        JobTitle = employee.JobTitle,
        HireDate = employee.HireDate,
        TerminationDate = employee.TerminationDate,
        Email = employee.Email,
        MobilePhone = employee.MobilePhone,
        RowVersion = RowVersion(employee.RowVersion)
    };

    private static string RowVersion(byte[] value) => Convert.ToBase64String(value);
}
