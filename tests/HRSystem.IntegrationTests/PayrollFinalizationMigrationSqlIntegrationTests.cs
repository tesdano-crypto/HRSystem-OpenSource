using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using HRSystem.Domain.Approvals;
using HRSystem.Domain.Payroll;
using HRSystem.Infrastructure.Identity;

namespace HRSystem.IntegrationTests;

[Collection(DisposableSqlServerCollection.Name)]
public sealed class PayrollFinalizationMigrationSqlIntegrationTests
{
    private const string Previous = "20260828042718_AddPayrollCalculationRevisions";
    private const string Current = "20260828054445_AddPayrollFinalization";

    [Fact]
    public async Task Migration_Up_Down_Up_Preserves_Legacy_And_Uses_NoAction_Fks()
    {
        await using var database = await DisposableSqlServerDatabase.CreateAsync(
            "PayrollFinalizationUpDownUp", applyMigrations: false);
        await using var db = database.CreateDbContext();
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(Previous);
        var periods = await ScalarAsync<int>(db, "SELECT COUNT(*) FROM dbo.PayrollPeriods;");
        var snapshots = await ScalarAsync<int>(db, "SELECT COUNT(*) FROM dbo.PayrollEmployeeSnapshots;");

        await migrator.MigrateAsync(Current);
        await AssertSchema(db);
        Assert.Equal(periods, await ScalarAsync<int>(db, "SELECT COUNT(*) FROM dbo.PayrollPeriods;"));
        Assert.Equal(snapshots, await ScalarAsync<int>(db, "SELECT COUNT(*) FROM dbo.PayrollEmployeeSnapshots;"));

        await migrator.MigrateAsync(Previous);
        Assert.Equal(0, await ScalarAsync<int>(db, """
            SELECT COUNT(*) FROM sys.tables WHERE name IN
              (N'PayrollFinalizations', N'PayrollFinalEmployeeSnapshots');
            """));
        await migrator.MigrateAsync(Current);
        await AssertSchema(db);
    }

    [Fact]
    public async Task Down_Refuses_When_Finalization_History_Exists()
    {
        await using var database = await DisposableSqlServerDatabase.CreateAsync(
            "PayrollFinalizationDownGuard", applyMigrations: false);
        await using var db = database.CreateDbContext();
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(Current);
        var period = new PayrollPeriod(Guid.NewGuid(), 2026, 8);
        period.MarkDraftCreated();
        var fingerprint = Enumerable.Repeat((byte)7, 32).ToArray();
        var now = DateTimeOffset.Parse("2026-08-28T04:00:00Z");
        var approval = new Approval(Guid.NewGuid(), ApprovalType.Payroll,
            nameof(PayrollPeriod), period.Id.ToString(), 1, fingerprint,
            "薪資簽核", "[]", "requester", "approver", now);
        approval.Approve("approver", ApprovalChannel.Web, now);
        db.Users.AddRange(
            new ApplicationUser { Id = "requester", UserName = "requester",
                NormalizedUserName = "REQUESTER", DisplayName = "送簽人", CreatedAtUtc = now },
            new ApplicationUser { Id = "approver", UserName = "approver",
                NormalizedUserName = "APPROVER", DisplayName = "核准人", CreatedAtUtc = now });
        db.AddRange(period, approval);
        await db.SaveChangesAsync();
        var finalizationId = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO dbo.PayrollFinalizations
              (Id, PayrollPeriodId, FinalVersionNumber, ApprovalId,
               ApprovedByUserId, ApprovedByDisplayName, ApprovedAtUtc,
               ApprovalChannel, FinalizedByUserId, FinalizedByDisplayName,
               FinalizedAtUtc, MonthFingerprintVersion, MonthFingerprint,
               EmployeeCount, GrossPay, TotalDeductions, NetPay, Status)
            VALUES ({finalizationId}, {period.Id}, 1, {approval.Id},
               N'approver', N'核准人', {now}, 2, N'admin', N'管理員',
               {now}, 1, {fingerprint}, 1, 100, 10, 90, 1);
            """);

        await Assert.ThrowsAnyAsync<Exception>(() => migrator.MigrateAsync(Previous));
        Assert.Equal(Current, await ScalarAsync<string>(db, """
            SELECT TOP(1) MigrationId FROM dbo.__EFMigrationsHistory
            ORDER BY MigrationId DESC;
            """));
    }

    private static async Task AssertSchema(DbContext db)
    {
        Assert.Equal(2, await ScalarAsync<int>(db, """
            SELECT COUNT(*) FROM sys.tables WHERE name IN
              (N'PayrollFinalizations', N'PayrollFinalEmployeeSnapshots');
            """));
        Assert.Equal(4, await ScalarAsync<int>(db, """
            SELECT COUNT(*) FROM sys.indexes WHERE is_unique=1 AND name IN
              (N'UX_PayrollFinalizations_Period', N'UX_PayrollFinalizations_Approval',
               N'UX_PayrollFinalEmployeeSnapshots_Finalization_Employee',
               N'UX_PayrollFinalEmployeeSnapshots_Snapshot');
            """));
        Assert.Equal(4, await ScalarAsync<int>(db, """
            SELECT COUNT(*) FROM sys.foreign_keys
            WHERE parent_object_id IN
              (OBJECT_ID(N'dbo.PayrollFinalizations'),
               OBJECT_ID(N'dbo.PayrollFinalEmployeeSnapshots'))
              AND delete_referential_action=0;
            """));
        Assert.Equal(Current, await ScalarAsync<string>(db, """
            SELECT TOP(1) MigrationId FROM dbo.__EFMigrationsHistory
            ORDER BY MigrationId DESC;
            """));
    }

    private static async Task<T> ScalarAsync<T>(DbContext db, string sql)
    {
        await db.Database.OpenConnectionAsync();
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = sql;
        var value = await command.ExecuteScalarAsync();
        if (value is T typed) return typed;
        return (T)Convert.ChangeType(value!, typeof(T),
            System.Globalization.CultureInfo.InvariantCulture);
    }
}
