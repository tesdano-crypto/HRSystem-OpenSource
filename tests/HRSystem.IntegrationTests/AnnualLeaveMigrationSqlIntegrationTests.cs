using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using HRSystem.Application.Abstractions.Security;
using HRSystem.Application.AnnualLeave;
using HRSystem.Application.LeaveRequests;
using HRSystem.Application.Security;
using HRSystem.Domain.AnnualLeave;
using HRSystem.Domain.LeaveRequests;
using HRSystem.Domain.MasterData;

namespace HRSystem.IntegrationTests;

[Collection(DisposableSqlServerCollection.Name)]
public sealed class AnnualLeaveMigrationSqlIntegrationTests
{
    private const string Previous = "20260811025039_AddNaturalDisasterAttendanceException";
    private const string Current = "20260812130014_AddAnniversaryAnnualLeaveEntitlements";

    [Fact]
    public async Task Migration_Up_Down_Up_Is_Repeatable_And_Model_Is_Current()
    {
        await using var database = await DisposableSqlServerDatabase.CreateAsync(
            "AnnualLeaveUpDownUp", applyMigrations: false);
        await using var db = database.CreateDbContext();
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(Previous);
        Assert.False(await Exists(db, "AnnualLeaveEntitlements"));
        await migrator.MigrateAsync(Current);
        await AssertSchema(db);
        await migrator.MigrateAsync(Previous);
        Assert.False(await Exists(db, "AnnualLeaveEntitlements"));
        await migrator.MigrateAsync(Current);
        await AssertSchema(db);
        Assert.Equal(
            db.Database.GetMigrations().SkipWhile(x => x != Current).Skip(1),
            await db.Database.GetPendingMigrationsAsync());
        Assert.False(db.Database.HasPendingModelChanges());
    }

    [Fact]
    public async Task Migration_Does_Not_Seed_Entitlements_Or_Modify_Leave_Data()
    {
        await using var database = await DisposableSqlServerDatabase.CreateAsync(
            "AnnualLeaveNoSeed", applyMigrations: false);
        await using var db = database.CreateDbContext();
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(Previous);
        var leaveCount = await Scalar<int>(db, "SELECT COUNT(*) FROM dbo.LeaveRequests;");
        await migrator.MigrateAsync(Current);
        Assert.Equal(0, await Scalar<int>(db, "SELECT COUNT(*) FROM dbo.AnnualLeaveEntitlements;"));
        Assert.Equal(0, await Scalar<int>(db, "SELECT COUNT(*) FROM dbo.AnnualLeaveAllocations;"));
        Assert.Equal(leaveCount, await Scalar<int>(db, "SELECT COUNT(*) FROM dbo.LeaveRequests;"));
    }

    [Fact]
    public async Task RowVersion_Allows_Only_One_Concurrent_Full_Balance_Reservation()
    {
        await using var database = await DisposableSqlServerDatabase.CreateAsync("AnnualLeaveConcurrency");
        Guid entitlementId;
        Guid firstRequestId;
        Guid secondRequestId;
        await using (var setup = database.CreateDbContext())
        {
            var now = DateTimeOffset.UtcNow;
            var department = new Department(Guid.NewGuid(), "ALTEST", "特休測試", now);
            var employee = new Employee(Guid.NewGuid(), "EMPAL01", "特休測試", department.Id,
                new DateOnly(2025, 1, 1), now);
            var type = new LeaveType(Guid.NewGuid(), AnnualLeavePolicy.LeaveTypeCode, "特休",
                LeaveUnit.Hour, .5m, true, true, 1, now);
            var entitlement = new AnnualLeaveEntitlement(Guid.NewGuid(), employee.Id,
                AnnualLeaveMilestone.Anniversary, 1, new DateOnly(2026, 1, 1),
                new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), 1m, 480, now);
            var first = NewRequest(employee.Id, type.Id, now);
            var second = NewRequest(employee.Id, type.Id, now);
            setup.AddRange(department, employee, type, entitlement, first, second);
            await setup.SaveChangesAsync();
            entitlementId = entitlement.Id; firstRequestId = first.Id; secondRequestId = second.Id;
        }

        using var barrier = new Barrier(2);
        async Task<bool> Reserve(Guid requestId)
        {
            await using var db = database.CreateDbContext();
            var entitlement = await db.AnnualLeaveEntitlements.SingleAsync(x => x.Id == entitlementId);
            barrier.SignalAndWait(TimeSpan.FromSeconds(10));
            entitlement.Reserve(480, DateTimeOffset.UtcNow);
            db.AnnualLeaveAllocations.Add(new AnnualLeaveAllocation(
                Guid.NewGuid(), requestId, entitlementId, 480, DateTimeOffset.UtcNow));
            try { await db.SaveChangesAsync(); return true; }
            catch (DbUpdateConcurrencyException) { return false; }
        }

        var outcomes = await Task.WhenAll(Reserve(firstRequestId), Reserve(secondRequestId));
        Assert.Single(outcomes, value => value);
        await using var verify = database.CreateDbContext();
        Assert.Equal(480, (await verify.AnnualLeaveEntitlements.SingleAsync(x => x.Id == entitlementId)).ReservedMinutes);
        Assert.Equal(1, await verify.AnnualLeaveAllocations.CountAsync());
    }

    [Fact]
    public async Task Historical_Backfill_Uses_Stored_Approved_Duration_And_Is_Idempotent()
    {
        await using var database = await DisposableSqlServerDatabase.CreateAsync("AnnualLeaveBackfillStoredDuration");
        Guid employeeId;
        await using (var setup = database.CreateDbContext())
        {
            var now = new DateTimeOffset(2026, 8, 12, 0, 0, 0, TimeSpan.Zero);
            var department = new Department(Guid.NewGuid(), "ALBACKFILL", "特休回填測試", now);
            var employee = new Employee(Guid.NewGuid(), "EMPALBF", "特休回填測試", department.Id,
                new DateOnly(2020, 8, 3), now);
            employee.Deactivate(now);
            var type = await setup.LeaveTypes.SingleOrDefaultAsync(x => x.Code == AnnualLeavePolicy.LeaveTypeCode);
            if (type is null)
            {
                type = new LeaveType(Guid.NewGuid(), AnnualLeavePolicy.LeaveTypeCode, "特休",
                    LeaveUnit.Hour, .5m, true, true, 1, now);
                setup.LeaveTypes.Add(type);
            }

            var request = NewRequest(employee.Id, type.Id, now);
            request.Submit("employee", now);
            request.Approve("manager", now);
            setup.AddRange(department, employee, request);
            await setup.SaveChangesAsync();
            employeeId = employee.Id;
        }

        await using (var apply = database.CreateDbContext())
        {
            var service = new AnnualLeaveService(
                apply,
                new AdminCurrentUser(),
                new ThrowingDurationCalculator(),
                TimeProvider.System);
            var preview = await service.PreviewBackfillAsync(new DateOnly(2026, 8, 12));
            var item = Assert.Single(preview.Items, x => x.EmployeeId == employeeId);
            Assert.Equal(1, item.AllocatableRequestCount);
            Assert.Equal(0, item.ManualReviewRequestCount);

            var first = await service.ApplyBackfillForEmployeeAsync(employeeId, new DateOnly(2026, 8, 12));
            Assert.Equal(1, first.RequestsAllocated);
            var second = await service.ApplyBackfillForEmployeeAsync(employeeId, new DateOnly(2026, 8, 12));
            Assert.Equal(0, second.RequestsAllocated);
        }

        await using var verify = database.CreateDbContext();
        var allocation = await verify.AnnualLeaveAllocations.SingleAsync();
        Assert.Equal(480, allocation.AllocatedMinutes);
        Assert.Equal(AnnualLeaveAllocationStatus.Consumed, allocation.Status);
        Assert.Equal(480, await verify.AnnualLeaveEntitlements.SumAsync(x => x.ConsumedMinutes));
    }

    private static LeaveRequest NewRequest(Guid employeeId, Guid leaveTypeId, DateTimeOffset now) => new(
        Guid.NewGuid(), $"AL-{Guid.NewGuid():N}", employeeId, leaveTypeId,
        new DateTimeOffset(2026, 8, 3, 0, 0, 0, TimeSpan.Zero),
        new DateTimeOffset(2026, 8, 3, 8, 0, 0, TimeSpan.Zero),
        8m, "並行測試", "integration-test", now);

    private static async Task AssertSchema(DbContext db)
    {
        Assert.True(await Exists(db, "AnnualLeaveEntitlements"));
        Assert.True(await Exists(db, "AnnualLeaveAllocations"));
        Assert.True(await Exists(db, "AnnualLeaveCarryForwards"));
        Assert.Equal(3, await Scalar<int>(db, "SELECT COUNT(*) FROM sys.foreign_keys WHERE parent_object_id IN (OBJECT_ID(N'dbo.AnnualLeaveEntitlements'), OBJECT_ID(N'dbo.AnnualLeaveAllocations')) AND delete_referential_action = 0;"));
        Assert.Equal(1, await Scalar<int>(db, "SELECT COUNT(*) FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.AnnualLeaveEntitlements') AND name=N'UX_AnnualLeaveEntitlements_Employee_Milestone' AND is_unique=1;"));
        Assert.Equal(1, await Scalar<int>(db, "SELECT COUNT(*) FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.AnnualLeaveAllocations') AND name=N'UX_AnnualLeaveAllocations_Request_Entitlement' AND is_unique=1;"));
    }

    private static async Task<bool> Exists(DbContext db, string table) =>
        await Scalar<int>(db, $"SELECT CASE WHEN OBJECT_ID(N'dbo.{table}',N'U') IS NULL THEN 0 ELSE 1 END;") == 1;
    private static async Task<T> Scalar<T>(DbContext db, string sql)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open) await connection.OpenAsync();
        await using var command = connection.CreateCommand(); command.CommandText = sql;
        var value = await command.ExecuteScalarAsync() ?? throw new InvalidOperationException();
        return (T)Convert.ChangeType(value, typeof(T));
    }

    private sealed class AdminCurrentUser : ICurrentUser
    {
        public string? UserId => "annual-leave-backfill-test";
        public Guid? EmployeeId => null;
        public string? DisplayName => "Annual Leave Backfill Test";
        public string? IpAddress => "127.0.0.1";
        public bool IsAuthenticated => true;
        public bool IsInRole(string role) => role == RoleNames.Admin;
        public bool HasPermission(string policy) => RolePermissions.HasPermission([RoleNames.Admin], policy);
    }

    private sealed class ThrowingDurationCalculator : ILeaveDurationCalculator
    {
        public Task<LeaveDurationEstimateDto> CalculateAsync(
            Guid employeeId,
            DateTimeOffset startAt,
            DateTimeOffset endAt,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Historical backfill must not use the current duration calculator.");

        public Task<LeaveDurationEstimateDto> CalculateAsync(
            Guid employeeId,
            DateTimeOffset startAt,
            DateTimeOffset endAt,
            LeaveCalculationMode calculationMode,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Historical backfill must not use the current duration calculator.");
    }
}
