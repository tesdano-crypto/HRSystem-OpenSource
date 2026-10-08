using HRSystem.Application.Common.Exceptions;
using HRSystem.Application.LeaveTypes;
using HRSystem.Domain.Common;
using HRSystem.Domain.MasterData;

namespace HRSystem.UnitTests;

public sealed class LeaveTypeRuleTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 6, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Code_Is_Normalized_And_Immutable()
    {
        var leaveType = NewGeneral("  custom_leave  ");

        Assert.Equal("CUSTOM_LEAVE", leaveType.Code);
        Assert.Throws<DomainValidationException>(() => leaveType.Update(
            "CHANGED", leaveType.Name, leaveType.Unit, leaveType.MinimumUnit,
            leaveType.RequiresReason, leaveType.IsPaid, leaveType.SortOrder, Now));
    }

    [Theory]
    [InlineData(LeaveCategory.General, LeaveCalculationMode.CalendarDays)]
    [InlineData(LeaveCategory.SpecialCalendarLeave, LeaveCalculationMode.WorkingSchedule)]
    [InlineData(LeaveCategory.LeaveOfAbsence, LeaveCalculationMode.CalendarDays)]
    public void Category_And_Calculation_Mode_Must_Match(
        LeaveCategory category,
        LeaveCalculationMode mode)
    {
        Assert.Throws<DomainValidationException>(() => New(
            category,
            mode,
            allowHourly: false,
            employeeEnabled: false));
    }

    [Fact]
    public void Working_Schedule_Requires_Minimum_Request_Minutes()
    {
        Assert.Throws<DomainValidationException>(() => new LeaveType(
            Guid.NewGuid(), "NO-MIN", "無最小值", LeaveUnit.Hour, 0.5m,
            true, false, 1, LeaveCategory.General,
            LeaveCalculationMode.WorkingSchedule, true, null, false, true,
            null, Now));
    }

    [Fact]
    public void Leave_Of_Absence_Cannot_Enable_Employee_Request()
    {
        Assert.Throws<DomainValidationException>(() => New(
            LeaveCategory.LeaveOfAbsence,
            LeaveCalculationMode.LeaveOfAbsence,
            allowHourly: false,
            employeeEnabled: true));
    }

    [Fact]
    public void Calendar_Day_Mode_Can_Enable_Employee_Request()
    {
        var leaveType = New(
            LeaveCategory.SpecialCalendarLeave,
            LeaveCalculationMode.CalendarDays,
            allowHourly: false,
            employeeEnabled: true);

        Assert.True(leaveType.IsEmployeeRequestEnabled);
    }

    [Fact]
    public void Special_Mode_Cannot_Allow_Hourly_Request()
    {
        Assert.Throws<DomainValidationException>(() => New(
            LeaveCategory.SpecialCalendarLeave,
            LeaveCalculationMode.CalendarDays,
            allowHourly: true,
            employeeEnabled: false));
    }

    [Fact]
    public async Task Application_Service_Rejects_Code_Change()
    {
        await using var db = TestDb.Create();
        var entity = NewGeneral("STABLE");
        db.LeaveTypes.Add(entity);
        await db.SaveChangesAsync();
        var service = new LeaveTypeService(
            db,
            new TestCurrentUser(),
            TimeProvider.System);

        var request = Request(entity);
        request.Code = "NEW-CODE";

        await Assert.ThrowsAsync<ApplicationValidationException>(() =>
            service.UpdateAsync(request));
        Assert.Equal("STABLE", entity.Code);
    }

    [Fact]
    public async Task Application_Service_Updates_Metadata_Without_Changing_Code()
    {
        await using var db = TestDb.Create();
        var entity = NewGeneral("STABLE");
        db.LeaveTypes.Add(entity);
        await db.SaveChangesAsync();
        var service = new LeaveTypeService(
            db,
            new TestCurrentUser(),
            TimeProvider.System);
        var request = Request(entity);
        request.Name = "更新後名稱";
        request.MinimumRequestMinutes = 60;
        request.Description = "  管理者說明  ";

        var updated = await service.UpdateAsync(request);

        Assert.Equal("STABLE", updated.Code);
        Assert.Equal("更新後名稱", updated.Name);
        Assert.Equal(60, updated.MinimumRequestMinutes);
        Assert.Equal("管理者說明", updated.Description);
    }

    private static LeaveType NewGeneral(string code) => new(
        Guid.NewGuid(), code, "測試假別", LeaveUnit.Hour, 0.5m,
        true, false, 1, LeaveCategory.General,
        LeaveCalculationMode.WorkingSchedule, true, 30, false, true,
        null, Now);

    private static LeaveType New(
        LeaveCategory category,
        LeaveCalculationMode mode,
        bool allowHourly,
        bool employeeEnabled) => new(
        Guid.NewGuid(), "SPECIAL", "特殊假別", LeaveUnit.Day, 1m,
        true, true, 1, category, mode, allowHourly, null, false,
        employeeEnabled, null, Now);

    private static UpdateLeaveTypeRequest Request(LeaveType entity) => new()
    {
        Id = entity.Id,
        Code = entity.Code,
        Name = entity.Name,
        Unit = entity.Unit,
        MinimumUnit = entity.MinimumUnit,
        RequiresReason = entity.RequiresReason,
        IsPaid = entity.IsPaid,
        SortOrder = entity.SortOrder,
        Category = entity.Category,
        CalculationMode = entity.CalculationMode,
        AllowHourlyRequest = entity.AllowHourlyRequest,
        MinimumRequestMinutes = entity.MinimumRequestMinutes,
        RequiresAttachment = entity.RequiresAttachment,
        IsEmployeeRequestEnabled = entity.IsEmployeeRequestEnabled,
        Description = entity.Description,
        RowVersion = Convert.ToBase64String(entity.RowVersion)
    };
}
