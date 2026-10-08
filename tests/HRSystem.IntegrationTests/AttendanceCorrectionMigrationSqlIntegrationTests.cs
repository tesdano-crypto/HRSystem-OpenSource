using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace HRSystem.IntegrationTests;

[Collection(DisposableSqlServerCollection.Name)]
public sealed class AttendanceCorrectionMigrationSqlIntegrationTests
{
    private const string Previous =
        "20260821041416_AddActualOvertimeRecognition";
    private const string Current =
        "20260823061050_AddAttendanceCorrectionRequests";

    [Fact]
    public async Task Migration_Up_Down_Up_Is_Repeatable_And_Model_Current()
    {
        await using var database = await DisposableSqlServerDatabase.CreateAsync(
            "AttendanceCorrectionUpDownUp", applyMigrations: false);
        await using var db = database.CreateDbContext();
        var migrator = db.GetService<IMigrator>();

        await migrator.MigrateAsync(Previous);
        Assert.False(await Exists(db, "AttendanceCorrectionRequests"));
        await migrator.MigrateAsync(Current);
        await AssertSchema(db);
        await migrator.MigrateAsync(Previous);
        Assert.False(await Exists(db, "AttendanceCorrectionRequests"));
        await migrator.MigrateAsync(Current);
        await AssertSchema(db);

        Assert.Equal(
            db.Database.GetMigrations().SkipWhile(x => x != Current).Skip(1),
            await db.Database.GetPendingMigrationsAsync());
        Assert.False(db.Database.HasPendingModelChanges());
    }

    [Fact]
    public async Task Migration_Does_Not_Backfill_Or_Modify_Business_Data()
    {
        await using var database = await DisposableSqlServerDatabase.CreateAsync(
            "AttendanceCorrectionNoBackfill", applyMigrations: false);
        await using var db = database.CreateDbContext();
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(Previous);
        var attendance = await Scalar<int>(db,
            "SELECT COUNT(*) FROM dbo.DailyAttendanceResults;");
        var raw = await Scalar<int>(db,
            "SELECT COUNT(*) FROM dbo.AttendanceRawEvents;");
        var adjustments = await Scalar<int>(db,
            "SELECT COUNT(*) FROM dbo.AttendanceAdjustments;");

        await migrator.MigrateAsync(Current);

        Assert.Equal(0, await Scalar<int>(db,
            "SELECT COUNT(*) FROM dbo.AttendanceCorrectionRequests;"));
        Assert.Equal(0, await Scalar<int>(db,
            "SELECT COUNT(*) FROM dbo.AttendanceCorrectionRequestHistories;"));
        Assert.Equal(attendance, await Scalar<int>(db,
            "SELECT COUNT(*) FROM dbo.DailyAttendanceResults;"));
        Assert.Equal(raw, await Scalar<int>(db,
            "SELECT COUNT(*) FROM dbo.AttendanceRawEvents;"));
        Assert.Equal(adjustments, await Scalar<int>(db,
            "SELECT COUNT(*) FROM dbo.AttendanceAdjustments;"));
    }

    private static async Task AssertSchema(DbContext db)
    {
        Assert.True(await Exists(db, "AttendanceCorrectionRequests"));
        Assert.True(await Exists(db,
            "AttendanceCorrectionRequestHistories"));
        Assert.Equal(5, await Scalar<int>(db,
            "SELECT COUNT(*) FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.AttendanceCorrectionRequests') AND name IN (N'IX_AttendanceCorrectionRequests_AttendanceResultId',N'IX_AttendanceCorrectionRequests_EmployeeId_WorkDate',N'IX_AttendanceCorrectionRequests_Status_SubmittedAtUtc',N'UX_AttendanceCorrectionRequests_ActiveType',N'UX_AttendanceCorrectionRequests_AppliedAdjustmentId');"));
        Assert.Equal(4, await Scalar<int>(db,
            "SELECT COUNT(*) FROM sys.foreign_keys WHERE name IN (N'FK_AttendanceCorrectionRequests_Employees_EmployeeId',N'FK_AttendanceCorrectionRequests_DailyAttendanceResults_AttendanceResultId',N'FK_AttendanceCorrectionRequests_AttendanceAdjustments_AppliedAttendanceAdjustmentId',N'FK_AttendanceCorrectionRequestHistories_AttendanceCorrectionRequests_RequestId') AND delete_referential_action=0;"));
        Assert.Equal(1, await Scalar<int>(db,
            "SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.AttendanceCorrectionRequests') AND name=N'RowVersion' AND system_type_id=189;"));
        Assert.Equal(32, await Scalar<int>(db,
            "SELECT max_length FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.AttendanceCorrectionRequests') AND name=N'SourceFingerprint';"));
        Assert.Equal(8, await Scalar<int>(db,
            "SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id IN (OBJECT_ID(N'dbo.AttendanceCorrectionRequests'),OBJECT_ID(N'dbo.AttendanceCorrectionRequestHistories'));"));
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
        var value = await command.ExecuteScalarAsync()
            ?? throw new InvalidOperationException();
        return (T)Convert.ChangeType(value, typeof(T));
    }
}
