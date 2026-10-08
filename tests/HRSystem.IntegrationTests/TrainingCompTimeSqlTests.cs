using HRSystem.Application.Abstractions.Security;
using HRSystem.Application.CompTime;
using HRSystem.Application.Security;
using HRSystem.Domain.CompTime;
using HRSystem.Domain.LeaveRequests;
using HRSystem.Domain.MasterData;
using HRSystem.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace HRSystem.IntegrationTests;

[Collection(DisposableSqlServerCollection.Name)]
public sealed class TrainingCompTimeSqlTests
{
    private const string Previous = "20260903142616_AddNonWorkingDayPunchReviewSupport";
    private const string Current = "20260930061925_AddHolidayTrainingCompTime";
    private static readonly DateTimeOffset Now = new(2026, 9, 30, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Migration_Baseline_Is_Exact_Idempotent_And_Old_Restore_Is_Once()
    {
        await using var database = await DisposableSqlServerDatabase.CreateAsync("TrainingCutover", false);
        await using var db = database.CreateDbContext();
        var migrator = db.GetService<IMigrator>(); await migrator.MigrateAsync(Previous);
        var (employee, type) = await Seed(db);
        var leave = NewLeave(employee.Id, type.Id, 3);
        db.LeaveRequests.Add(leave);
        db.CompTimeTransactions.AddRange(Opening(employee.Id, 8, 0), Opening(employee.Id, 8, 1),
            new(Guid.NewGuid(), employee.Id, CompTimeTransactionType.Consume, 3, new(2026, 9, 1), CompTimeSourceType.LeaveRequest, leave.Id, "old", "test", Now));
        await db.SaveChangesAsync();
        var before = await db.CompTimeTransactions.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.Hours, x.RowVersion }).ToArrayAsync();
        await migrator.MigrateAsync(Current); await migrator.MigrateAsync(Current);
        var pool = await db.CompTimeLegacyPools.SingleAsync();
        Assert.Equal(13, pool.BaselineHours); Assert.Equal(3, pool.LedgerCount);
        Assert.Equal(3, await db.CompTimeLegacyMembers.CountAsync());
        var after = await db.CompTimeTransactions.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.Hours, x.RowVersion }).ToArrayAsync();
        Assert.Equal(before.Select(x => (x.Id, x.Hours, Convert.ToHexString(x.RowVersion))), after.Select(x => (x.Id, x.Hours, Convert.ToHexString(x.RowVersion))));
        var service = Service(db); await db.Entry(leave).Reference(x => x.LeaveType).LoadAsync();
        await db.ExecuteSerializableAsync(async ct => { await service.RestoreAfterCancellationAsync(leave, ct); await db.SaveChangesAsync(ct); });
        await db.ExecuteSerializableAsync(async ct => { await service.RestoreAfterCancellationAsync(leave, ct); await db.SaveChangesAsync(ct); });
        Assert.Single(await db.CompTimeLegacyReturns.ToListAsync());
        Assert.Equal(16, (await service.GetAdminBalanceAsync(employee.Id)).AvailableHours);
        var newLeave = NewLeave(employee.Id, type.Id, 5); db.LeaveRequests.Add(newLeave); await db.SaveChangesAsync();
        await db.Entry(newLeave).Reference(x => x.LeaveType).LoadAsync();
        await db.ExecuteSerializableAsync(async ct => { await service.ConsumeForApprovalAsync(newLeave, ct); await db.SaveChangesAsync(ct); });
        Assert.Equal(pool.Id, (await db.CompTimeAllocations.SingleAsync()).LegacyPoolId);
        Assert.Equal(11, (await service.GetAdminBalanceAsync(employee.Id)).AvailableHours);
        await Assert.ThrowsAnyAsync<Exception>(() => migrator.MigrateAsync(Previous));
        Assert.Equal(11, (await service.GetAdminBalanceAsync(employee.Id)).AvailableHours);
    }

    [Fact]
    public async Task Negative_Baseline_Stops_And_Rolls_Back_Schema_And_Ledger()
    {
        await using var database = await DisposableSqlServerDatabase.CreateAsync("TrainingBadBaseline", false);
        await using var db = database.CreateDbContext(); var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(Previous); var (employee, type) = await Seed(db);
        var leave = NewLeave(employee.Id, type.Id, 3); db.Add(leave);
        db.Add(new CompTimeTransaction(Guid.NewGuid(), employee.Id, CompTimeTransactionType.Consume, 3,
            new(2026, 9, 1), CompTimeSourceType.LeaveRequest, leave.Id, "old", "test", Now));
        await db.SaveChangesAsync();
        await Assert.ThrowsAnyAsync<Exception>(() => migrator.MigrateAsync(Current));
        Assert.DoesNotContain(Current, await db.Database.GetAppliedMigrationsAsync());
        Assert.Single(await db.CompTimeTransactions.ToListAsync());
        Assert.Equal(0, await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM sys.tables WHERE name = 'CompTimeLegacyPools'").SingleAsync());
    }

    [Fact]
    public async Task Concurrent_Consumes_Cannot_Overallocate_And_Failed_Save_Rolls_Back()
    {
        await using var database = await DisposableSqlServerDatabase.CreateAsync("TrainingConcurrency");
        Guid employeeId, firstId, secondId;
        await using (var db = database.CreateDbContext())
        {
            var (employee, type) = await Seed(db); employeeId = employee.Id;
            await Service(db).CreateLegacyOpeningBalanceAsync(new() { EmployeeId = employee.Id, Hours = 8, CutoverDate = new(2026, 1, 1), Reason = "test" });
            var first = NewLeave(employee.Id, type.Id, 6); var second = NewLeave(employee.Id, type.Id, 6);
            firstId = first.Id; secondId = second.Id; db.AddRange(first, second); await db.SaveChangesAsync();
        }
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var arrived = 0;
        async Task<bool> Consume(Guid id)
        {
            await using var db = database.CreateDbContext();
            var leave = await db.LeaveRequests.Include(x => x.LeaveType).SingleAsync(x => x.Id == id);
            if (Interlocked.Increment(ref arrived) == 2) ready.SetResult(); await ready.Task;
            try
            {
                await db.ExecuteSerializableAsync(async ct => { await Service(db).ConsumeForApprovalAsync(leave, ct); await db.SaveChangesAsync(ct); });
                return true;
            }
            catch (Exception ex) when (ex is HRSystem.Application.Common.Exceptions.ApplicationValidationException || IsDeadlock(ex))
            { return false; }
        }
        var outcomes = await Task.WhenAll(Consume(firstId), Consume(secondId));
        Assert.Single(outcomes, x => x);
        await using var check = database.CreateDbContext();
        Assert.Equal(2, (await Service(check).GetAdminBalanceAsync(employeeId)).AvailableHours);
        Assert.Equal(6, await check.CompTimeAllocations.SumAsync(x => x.AllocatedHours));
        Assert.Single(await check.CompTimeTransactions.Where(x => x.TransactionType == CompTimeTransactionType.Consume).ToListAsync());
        var typeId = await check.LeaveTypes.Where(x => x.Code == "COMP_TIME").Select(x => x.Id).SingleAsync();
        var rollbackLeave = NewLeave(employeeId, typeId, 1); check.Add(rollbackLeave); await check.SaveChangesAsync();
        await check.Entry(rollbackLeave).Reference(x => x.LeaveType).LoadAsync();
        await Assert.ThrowsAsync<HRSystem.Application.Common.Exceptions.ApplicationValidationException>(() =>
            check.ExecuteSerializableAsync(async ct => {
                await Service(check).ConsumeForApprovalAsync(rollbackLeave, ct); await check.SaveChangesAsync(ct);
                throw new HRSystem.Application.Common.Exceptions.ApplicationValidationException("simulate subsequent approval failure");
            }));
        Assert.Equal(2, (await Service(check).GetAdminBalanceAsync(employeeId)).AvailableHours);
        Assert.Single(await check.CompTimeAllocations.ToListAsync());
    }

    [Fact]
    public async Task Approval_Unique_Index_And_Immutable_Approved_Grant()
    {
        await using var database = await DisposableSqlServerDatabase.CreateAsync("TrainingApproval");
        await using var db = database.CreateDbContext(); var (employee, _) = await Seed(db); var service = Service(db);
        var draft = await service.CreateTrainingAsync(new() { EmployeeId = employee.Id, TrainingDate = new(2026, 9, 27), Hours = 8, CourseOrReason = "training" });
        var req = new ApproveTrainingCompTimeRequest(draft.Id, draft.RowVersion, draft.EvidenceFingerprint, "calendar reviewed");
        await service.ApproveTrainingAsync(req); await service.ApproveTrainingAsync(req);
        Assert.Single(await db.CompTimeTransactions.ToListAsync());
        db.CompTimeTransactions.Add(new(Guid.NewGuid(), employee.Id, CompTimeTransactionType.Grant, 8, new(2026, 9, 27), CompTimeSourceType.TrainingCompTimeGrant, draft.Id, "duplicate", "test", Now));
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync()); db.ClearTrackedChanges();
        var approved = await db.TrainingCompTimeGrants.SingleAsync();
        db.Entry(approved).Property(x => x.ApprovedHours).CurrentValue = 9;
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    private static CompTimeTransaction Opening(Guid employee, decimal hours, int offset) => new(Guid.NewGuid(), employee, CompTimeTransactionType.Grant, hours,
        new DateOnly(2026, 1, 1).AddDays(offset), CompTimeSourceType.LegacyOpeningBalance, null, "opening", "test", Now);
    private static LeaveRequest NewLeave(Guid employee, Guid type, decimal hours) => new(Guid.NewGuid(), Guid.NewGuid().ToString(), employee, type, Now.AddDays(1), Now.AddDays(1).AddHours((double)hours), hours, "test", "test", Now);
    private static async Task<(Employee, LeaveType)> Seed(HRSystemDbContext db)
    {
        var dep = new Department(Guid.NewGuid(), "TRSQL", "測試", Now);
        var employee = new Employee(Guid.NewGuid(), "TRSQL1", "測試", dep.Id, new(2020, 1, 1), Now);
        db.AddRange(dep, employee); await db.SaveChangesAsync();
        return (employee, await db.LeaveTypes.SingleAsync(x => x.Code == "COMP_TIME"));
    }
    private static CompTimeService Service(HRSystemDbContext db) => new(db, new Actor(), new Clock());
    private static bool IsDeadlock(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
            if (current is Microsoft.Data.SqlClient.SqlException sql && sql.Number == 1205) return true;
        return false;
    }
    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => Now; }
    private sealed class Actor : ICurrentUser
    {
        public string? UserId => "training-sql-test"; public Guid? EmployeeId => null; public string? DisplayName => "test"; public string? IpAddress => null;
        public bool IsAuthenticated => true; public bool IsInRole(string role) => role == RoleNames.Admin;
        public bool HasPermission(string policy) => RolePermissions.HasPermission([RoleNames.Admin], policy);
    }
}
