using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace HRSystem.IntegrationTests;

[Collection(DisposableSqlServerCollection.Name)]
public sealed class ParentalLeaveMigrationSqlIntegrationTests
{
    private const string PreviousMigration =
        "20260809083358_FixLeaveTypeEmployeeRequestModeConstraint";
    private const string CurrentMigration =
        "20260809125653_AddParentalLeaveOfAbsence";

    [Fact]
    public async Task Migration_Up_Down_Up_Is_Repeatable_And_Leaves_No_Pending_Model()
    {
        await using var database = await DisposableSqlServerDatabase.CreateAsync(
            "ParentalLeaveUpDownUp",
            applyMigrations: false);
        await using var db = database.CreateDbContext();
        var migrator = db.GetService<IMigrator>();

        await migrator.MigrateAsync(PreviousMigration);
        Assert.False(await TableExistsAsync(db, "ParentalLeaveRequests"));

        await migrator.MigrateAsync(CurrentMigration);
        await AssertSchemaAsync(db);

        await migrator.MigrateAsync(PreviousMigration);
        Assert.False(await TableExistsAsync(db, "ParentalLeaveRequests"));

        await migrator.MigrateAsync(CurrentMigration);
        await AssertSchemaAsync(db);
        Assert.Equal(
            db.Database.GetMigrations().SkipWhile(x => x != CurrentMigration).Skip(1),
            await db.Database.GetPendingMigrationsAsync());
        Assert.False(db.Database.HasPendingModelChanges());
    }

    [Fact]
    public async Task Migration_Does_Not_Change_Existing_Attendance_Row()
    {
        await using var database = await DisposableSqlServerDatabase.CreateAsync(
            "ParentalLeaveExistingAttendance",
            applyMigrations: false);
        await using var db = database.CreateDbContext();
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(PreviousMigration);
        var before = await ScalarAsync<int>(db,
            "SELECT COUNT(*) FROM dbo.DailyAttendanceResults;");

        await migrator.MigrateAsync(CurrentMigration);

        Assert.Equal(before, await ScalarAsync<int>(db,
            "SELECT COUNT(*) FROM dbo.DailyAttendanceResults;"));
        Assert.Equal(0, await ScalarAsync<int>(db,
            "SELECT COUNT(*) FROM dbo.DailyAttendanceResults WHERE IsEmploymentSuspended = 1;"));
    }

    private static async Task AssertSchemaAsync(DbContext db)
    {
        Assert.True(await TableExistsAsync(db, "ParentalLeaveRequests"));
        Assert.True(await TableExistsAsync(db, "ParentalLeaveApprovalHistories"));
        Assert.Equal(2, await ScalarAsync<int>(db, """
            SELECT COUNT(*) FROM sys.columns
            WHERE object_id = OBJECT_ID(N'dbo.DailyAttendanceResults')
              AND name IN (N'IsEmploymentSuspended', N'EmploymentSuspensionSourceId');
            """));
        Assert.Equal(1, await ScalarAsync<int>(db, """
            SELECT COUNT(*) FROM sys.foreign_keys
            WHERE name = N'FK_DailyAttendanceResults_ParentalLeaveRequests_EmploymentSuspensionSourceId'
              AND delete_referential_action = 0;
            """));
    }

    private static Task<bool> TableExistsAsync(DbContext db, string table) =>
        ScalarAsync<int>(db,
            $"SELECT CASE WHEN OBJECT_ID(N'dbo.{table}', N'U') IS NULL THEN 0 ELSE 1 END;")
            .ContinueWith(task => task.Result == 1, TaskScheduler.Default);

    private static async Task<T> ScalarAsync<T>(DbContext db, string sql)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync();
        }
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        var value = await command.ExecuteScalarAsync()
            ?? throw new InvalidOperationException("SQL scalar result was null.");
        return (T)Convert.ChangeType(value, typeof(T));
    }
}
