using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace HRSystem.IntegrationTests;

[Collection(DisposableSqlServerCollection.Name)]
public sealed class OvertimeRequestMigrationSqlIntegrationTests
{
    private const string Previous =
        "20260820034658_AddAttendanceReviewResolutionLedger";
    private const string Current =
        "20260821010307_AddEmployeeOvertimeRequests";

    [Fact]
    public async Task Migration_Up_Down_Up_Is_Repeatable_And_Current()
    {
        await using var database = await DisposableSqlServerDatabase.CreateAsync(
            "OvertimeRequestUpDownUp", applyMigrations: false);
        await using var db = database.CreateDbContext();
        var migrator = db.GetService<IMigrator>();

        await migrator.MigrateAsync(Previous);
        Assert.False(await Exists(db, "OvertimeRequests"));
        await migrator.MigrateAsync(Current);
        await AssertSchema(db);
        await migrator.MigrateAsync(Previous);
        Assert.False(await Exists(db, "OvertimeRequests"));
        await migrator.MigrateAsync(Current);
        await AssertSchema(db);

        Assert.Equal(
            db.Database.GetMigrations().SkipWhile(x => x != Current).Skip(1),
            await db.Database.GetPendingMigrationsAsync());
        Assert.False(db.Database.HasPendingModelChanges());
    }

    [Fact]
    public async Task Migration_Does_Not_Backfill_Or_Change_Existing_Business_Data()
    {
        await using var database = await DisposableSqlServerDatabase.CreateAsync(
            "OvertimeRequestNoBackfill", applyMigrations: false);
        await using var db = database.CreateDbContext();
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(Previous);
        var employeeCount = await Scalar<int>(db, "SELECT COUNT(*) FROM dbo.Employees;");
        var attendanceCount = await Scalar<int>(db, "SELECT COUNT(*) FROM dbo.DailyAttendanceResults;");
        var rawCount = await Scalar<int>(db, "SELECT COUNT(*) FROM dbo.AttendanceRawEvents;");

        await migrator.MigrateAsync(Current);

        Assert.Equal(employeeCount, await Scalar<int>(db, "SELECT COUNT(*) FROM dbo.Employees;"));
        Assert.Equal(attendanceCount, await Scalar<int>(db, "SELECT COUNT(*) FROM dbo.DailyAttendanceResults;"));
        Assert.Equal(rawCount, await Scalar<int>(db, "SELECT COUNT(*) FROM dbo.AttendanceRawEvents;"));
        Assert.Equal(0, await Scalar<int>(db, "SELECT COUNT(*) FROM dbo.OvertimeRequests;"));
        Assert.Equal(0, await Scalar<int>(db, "SELECT COUNT(*) FROM dbo.OvertimeRequestHistories;"));
    }

    private static async Task AssertSchema(DbContext db)
    {
        Assert.True(await Exists(db, "OvertimeRequests"));
        Assert.True(await Exists(db, "OvertimeRequestHistories"));
        Assert.Equal(3, await Scalar<int>(db,
            "SELECT COUNT(*) FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.OvertimeRequests') AND name IN (N'IX_OvertimeRequests_EmployeeId_OvertimeDate',N'IX_OvertimeRequests_Status_OvertimeDate',N'IX_OvertimeRequests_SubmittedAtUtc');"));
        Assert.Equal(2, await Scalar<int>(db,
            "SELECT COUNT(*) FROM sys.foreign_keys WHERE name IN (N'FK_OvertimeRequests_Employees_EmployeeId',N'FK_OvertimeRequestHistories_OvertimeRequests_OvertimeRequestId') AND delete_referential_action=0;"));
        Assert.Equal(1, await Scalar<int>(db,
            "SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.OvertimeRequests') AND name=N'RowVersion' AND system_type_id=189;"));
        Assert.Equal(6, await Scalar<int>(db,
            "SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id IN (OBJECT_ID(N'dbo.OvertimeRequests'),OBJECT_ID(N'dbo.OvertimeRequestHistories'));"));
    }

    private static async Task<bool> Exists(DbContext db, string table) =>
        await Scalar<int>(db,
            $"SELECT CASE WHEN OBJECT_ID(N'dbo.{table}',N'U') IS NULL THEN 0 ELSE 1 END;") == 1;

    private static async Task<T> Scalar<T>(DbContext db, string sql)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        var value = await command.ExecuteScalarAsync() ?? throw new InvalidOperationException();
        return (T)Convert.ChangeType(value, typeof(T));
    }
}
