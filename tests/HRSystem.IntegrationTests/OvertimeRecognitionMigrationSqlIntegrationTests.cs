using HRSystem.Domain.MasterData;
using HRSystem.Domain.Overtime;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace HRSystem.IntegrationTests;

[Collection(DisposableSqlServerCollection.Name)]
public sealed class OvertimeRecognitionMigrationSqlIntegrationTests
{
    private const string Previous =
        "20260821010307_AddEmployeeOvertimeRequests";
    private const string Current =
        "20260821041416_AddActualOvertimeRecognition";
    private static readonly DateTimeOffset Now =
        new(2026, 8, 21, 13, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Migration_Up_Down_Up_Is_Repeatable_And_Current()
    {
        await using var database = await DisposableSqlServerDatabase.CreateAsync(
            "OvertimeRecognitionUpDownUp", applyMigrations: false);
        await using var db = database.CreateDbContext();
        var migrator = db.GetService<IMigrator>();

        await migrator.MigrateAsync(Previous);
        Assert.False(await Exists(db, "OvertimeRecognitions"));
        await migrator.MigrateAsync(Current);
        await AssertSchema(db);
        await migrator.MigrateAsync(Previous);
        Assert.False(await Exists(db, "OvertimeRecognitions"));
        await migrator.MigrateAsync(Current);
        await AssertSchema(db);

        Assert.Equal(
            db.Database.GetMigrations().SkipWhile(x => x != Current).Skip(1),
            await db.Database.GetPendingMigrationsAsync());
        Assert.False(db.Database.HasPendingModelChanges());
    }

    [Fact]
    public async Task Migration_Does_Not_Backfill_Approved_Requests()
    {
        await using var database = await DisposableSqlServerDatabase.CreateAsync(
            "OvertimeRecognitionNoBackfill", applyMigrations: false);
        await using var db = database.CreateDbContext();
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(Previous);
        var department = new Department(Guid.NewGuid(), "G1", "認列測試部", Now);
        var employee = new Employee(Guid.NewGuid(), "EMP-G1-MIG", "遷移測試",
            department.Id, new DateOnly(2025, 1, 1), Now);
        var request = new OvertimeRequest(Guid.NewGuid(), employee.Id,
            Local(17, 30), Local(20, 30), "Migration test", "employee", Now);
        request.Submit("employee", Now);
        request.Approve(OvertimeReviewReason.ApprovedAsRequested,
            null, "admin", Now);
        db.AddRange(department, employee, request);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        await migrator.MigrateAsync(Current);

        Assert.Equal(1, await Scalar<int>(db,
            "SELECT COUNT(*) FROM dbo.OvertimeRequests;"));
        Assert.Equal(0, await Scalar<int>(db,
            "SELECT COUNT(*) FROM dbo.OvertimeRecognitions;"));
        Assert.Equal(0, await Scalar<int>(db,
            "SELECT COUNT(*) FROM dbo.OvertimeRecognitionHistories;"));
    }

    private static async Task AssertSchema(DbContext db)
    {
        Assert.True(await Exists(db, "OvertimeRecognitions"));
        Assert.True(await Exists(db, "OvertimeRecognitionHistories"));
        Assert.Equal(3, await Scalar<int>(db,
            "SELECT COUNT(*) FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.OvertimeRecognitions') AND name IN (N'UX_OvertimeRecognitions_OvertimeRequestId',N'IX_OvertimeRecognitions_Status_WorkDate',N'IX_OvertimeRecognitions_EmployeeId_WorkDate');"));
        Assert.Equal(3, await Scalar<int>(db,
            "SELECT COUNT(*) FROM sys.foreign_keys WHERE name IN (N'FK_OvertimeRecognitions_Employees_EmployeeId',N'FK_OvertimeRecognitions_OvertimeRequests_OvertimeRequestId',N'FK_OvertimeRecognitionHistories_OvertimeRecognitions_RecognitionId') AND delete_referential_action=0;"));
        Assert.Equal(1, await Scalar<int>(db,
            "SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.OvertimeRecognitions') AND name=N'RowVersion' AND system_type_id=189;"));
        Assert.Equal(8, await Scalar<int>(db,
            "SELECT COUNT(*) FROM sys.check_constraints WHERE name IN (N'CK_OvertimeRequests_ThirtyMinuteUnit',N'CK_OvertimeRecognitions_Status',N'CK_OvertimeRecognitions_ApprovedRange',N'CK_OvertimeRecognitions_SuggestedRange',N'CK_OvertimeRecognitions_RecognizedRange',N'CK_OvertimeRecognitions_Confirmed',N'CK_OvertimeRecognitionHistories_Action',N'CK_OvertimeRecognitionHistories_ToStatus');"));
    }

    private static DateTime Local(int hour, int minute) =>
        DateTime.SpecifyKind(new DateTime(2026, 8, 21, hour, minute, 0),
            DateTimeKind.Unspecified);
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
        return (T)Convert.ChangeType(await command.ExecuteScalarAsync()
            ?? throw new InvalidOperationException(), typeof(T));
    }
}
