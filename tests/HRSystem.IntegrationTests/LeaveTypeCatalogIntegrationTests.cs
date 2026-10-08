using Bunit;
using HRSystem.Application.Common.Models;
using HRSystem.Application.LeaveTypes;
using HRSystem.Domain.MasterData;
using HRSystem.Infrastructure.Persistence;
using HRSystem.Infrastructure.Persistence.Seed;
using HRSystem.Web.Components.Pages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.FluentUI.AspNetCore.Components;

namespace HRSystem.IntegrationTests;

public sealed class LeaveTypeCatalogIntegrationTests
{
    [Fact]
    public async Task Standard_Catalog_Seed_Is_Idempotent()
    {
        await using var db = CreateDb();
        var service = CreateService(db);

        var first = await service.SeedAsync();
        var second = await service.SeedAsync();

        Assert.Equal(15, first.CreatedCount);
        Assert.Equal(0, second.CreatedCount);
        Assert.Equal(15, second.ExistingCount);
        Assert.Empty(first.ConflictingCodes);
        Assert.Empty(second.ConflictingCodes);
        Assert.Equal(15, await db.LeaveTypes.CountAsync());
        Assert.Equal(15, await db.AuditLogs.CountAsync(item =>
            item.Action == "StandardLeaveTypeInitialized"));
        var compTime = await db.LeaveTypes.SingleAsync(item => item.Code == "COMP_TIME");
        Assert.Equal("補休", compTime.Name);
        Assert.True(compTime.IsPaid);
        Assert.True(compTime.IsEmployeeRequestEnabled);
        Assert.Equal(LeaveCalculationMode.WorkingSchedule, compTime.CalculationMode);
    }

    [Fact]
    public async Task Standard_Catalog_Does_Not_Overwrite_Administrator_Changes()
    {
        await using var db = CreateDb();
        var annual = NewExisting("ANNUAL", "公司自訂特休");
        annual.Update(
            annual.Name, annual.Unit, annual.MinimumUnit,
            annual.RequiresReason, annual.IsPaid, 999,
            LeaveCategory.General, LeaveCalculationMode.WorkingSchedule,
            true, 60, false, false, "公司自訂說明", Now);
        annual.Deactivate(Now);
        db.LeaveTypes.Add(annual);
        await db.SaveChangesAsync();

        var result = await CreateService(db).SeedAsync();
        var unchanged = await db.LeaveTypes.SingleAsync(item => item.Code == "ANNUAL");

        Assert.Equal(14, result.CreatedCount);
        Assert.Equal("公司自訂特休", unchanged.Name);
        Assert.False(unchanged.IsActive);
        Assert.False(unchanged.IsEmployeeRequestEnabled);
        Assert.Equal(999, unchanged.SortOrder);
        Assert.Equal("公司自訂說明", unchanged.Description);
    }

    [Fact]
    public async Task Synonymous_Name_With_Different_Code_Is_Reported_And_Not_Auto_Merged()
    {
        await using var db = CreateDb();
        var existing = NewExisting("CUSTOM_MARRIAGE", "婚假");
        db.LeaveTypes.Add(existing);
        await db.SaveChangesAsync();

        var result = await CreateService(db).SeedAsync();

        Assert.Contains("MARRIAGE", result.ConflictingCodes);
        Assert.False(await db.LeaveTypes.AnyAsync(item => item.Code == "MARRIAGE"));
        Assert.Equal(existing.Id, (await db.LeaveTypes.SingleAsync(item => item.Name == "婚假")).Id);
    }

    [Fact]
    public async Task Special_Standard_Types_Are_Present_But_Employee_Request_Is_Disabled()
    {
        await using var db = CreateDb();
        await CreateService(db).SeedAsync();

        var special = await db.LeaveTypes
            .Where(item => item.CalculationMode != LeaveCalculationMode.WorkingSchedule)
            .ToListAsync();

        Assert.Equal(4, special.Count);
        Assert.All(special, item => Assert.False(item.IsEmployeeRequestEnabled));
        Assert.All(special, item => Assert.False(item.AllowHourlyRequest));
    }

    [Fact]
    public async Task Explicit_Calendar_Activation_Enables_Only_Three_Approved_Types()
    {
        await using var db = CreateDb();
        await CreateService(db).SeedAsync();
        var activation = new CalendarDayLeaveTypeActivationService(
            db,
            TimeProvider.System);

        var first = await activation.ActivateAsync();
        var second = await activation.ActivateAsync();

        Assert.Equal(3, first.ActivatedCount);
        Assert.Equal(0, first.AlreadyActiveCount);
        Assert.Equal(0, second.ActivatedCount);
        Assert.Equal(3, second.AlreadyActiveCount);
        var enabledCodes = await db.LeaveTypes
            .Where(item => item.IsEmployeeRequestEnabled &&
                item.CalculationMode == LeaveCalculationMode.CalendarDays)
            .Select(item => item.Code)
            .OrderBy(item => item)
            .ToArrayAsync();
        Assert.Equal(
            ["MATERNITY", "MISCARRIAGE", "PREGNANCY_BED_REST"],
            enabledCodes);
        Assert.False(await db.LeaveTypes
            .Where(item => item.Code == "PARENTAL_LEAVE_WITHOUT_PAY")
            .Select(item => item.IsEmployeeRequestEnabled)
            .SingleAsync());
        Assert.Equal(3, await db.AuditLogs.CountAsync(item =>
            item.Action == "CalendarDayLeaveTypeEnabled"));
    }

    private static readonly DateTimeOffset Now =
        new(2026, 8, 6, 0, 0, 0, TimeSpan.Zero);

    private static HRSystemDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<HRSystemDbContext>()
            .UseInMemoryDatabase($"leave-catalog-{Guid.NewGuid():N}")
            .Options;
        return new HRSystemDbContext(options);
    }

    private static StandardLeaveTypeSeedService CreateService(HRSystemDbContext db) =>
        new(db, TimeProvider.System, NullLogger<StandardLeaveTypeSeedService>.Instance);

    private static LeaveType NewExisting(string code, string name) => new(
        Guid.NewGuid(), code, name, LeaveUnit.Hour, 0.5m,
        true, true, 1, LeaveCategory.General,
        LeaveCalculationMode.WorkingSchedule, true, 30, false, true,
        null, Now);
}

public sealed class LeaveTypeCatalogWebTests : BunitContext
{
    public LeaveTypeCatalogWebTests()
    {
        Services.AddFluentUIComponents();
        Services.AddSingleton<ILeaveTypeService>(new EmptyLeaveTypeService());
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void Admin_Editor_Shows_Rule_Metadata_And_Special_Flow_Warnings()
    {
        var cut = Render<LeaveTypes>();
        var create = Assert.Single(
            cut.FindAll("fluent-button"),
            item => item.TextContent.Trim() == "新增假別");
        create.Click();

        Assert.Contains("假別代碼", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("分類", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("計算模式", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("允許按小時申請", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("最小申請分鐘", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("需要附件", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("開放員工申請", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("顯示順序", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("說明", cut.Markup, StringComparison.Ordinal);

        cut.Find("#leave-calculation")
            .Change(LeaveCalculationMode.CalendarDays.ToString());
        Assert.Contains("第一版僅支援 MATERNITY", cut.Markup, StringComparison.Ordinal);

        cut.Find("#leave-calculation")
            .Change(LeaveCalculationMode.LeaveOfAbsence.ToString());
        Assert.Contains("專用留職停薪流程", cut.Markup, StringComparison.Ordinal);
    }

    private sealed class EmptyLeaveTypeService : ILeaveTypeService
    {
        public Task<PagedResult<LeaveTypeDto>> GetListAsync(
            LeaveTypeQuery query,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new PagedResult<LeaveTypeDto>([], 0, 1, 10));

        public Task<LeaveTypeDto> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<LeaveTypeDto> CreateAsync(CreateLeaveTypeRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<LeaveTypeDto> UpdateAsync(UpdateLeaveTypeRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task SetActiveAsync(Guid id, bool isActive, string rowVersion, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
