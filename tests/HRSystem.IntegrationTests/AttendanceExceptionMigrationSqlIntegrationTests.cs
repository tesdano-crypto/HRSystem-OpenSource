using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace HRSystem.IntegrationTests;

[Collection(DisposableSqlServerCollection.Name)]
public sealed class AttendanceExceptionMigrationSqlIntegrationTests
{
    private const string Previous="20260809125653_AddParentalLeaveOfAbsence";
    private const string Current="20260811025039_AddNaturalDisasterAttendanceException";

    [Fact]
    public async Task Migration_Up_Down_Up_Is_Repeatable_And_Model_Is_Current()
    {
        await using var database=await DisposableSqlServerDatabase.CreateAsync("AttendanceExceptionUpDownUp",applyMigrations:false);
        await using var db=database.CreateDbContext();var migrator=db.GetService<IMigrator>();
        await migrator.MigrateAsync(Previous);Assert.False(await Exists(db,"AttendanceExceptions"));
        await migrator.MigrateAsync(Current);await AssertSchema(db);
        await migrator.MigrateAsync(Previous);Assert.False(await Exists(db,"AttendanceExceptions"));
        await migrator.MigrateAsync(Current);await AssertSchema(db);
        Assert.Equal(
            db.Database.GetMigrations().SkipWhile(x => x != Current).Skip(1),
            await db.Database.GetPendingMigrationsAsync());
        Assert.False(db.Database.HasPendingModelChanges());
    }

    [Fact]
    public async Task Migration_Preserves_Existing_Attendance_Data()
    {
        await using var database=await DisposableSqlServerDatabase.CreateAsync("AttendanceExceptionExisting",applyMigrations:false);
        await using var db=database.CreateDbContext();var migrator=db.GetService<IMigrator>();await migrator.MigrateAsync(Previous);
        var before=await Scalar<int>(db,"SELECT COUNT(*) FROM dbo.DailyAttendanceResults;");
        await migrator.MigrateAsync(Current);
        Assert.Equal(before,await Scalar<int>(db,"SELECT COUNT(*) FROM dbo.DailyAttendanceResults;"));
        Assert.Equal(0,await Scalar<int>(db,"SELECT COUNT(*) FROM dbo.DailyAttendanceResults WHERE IsAttendanceExempted=1 OR AttendanceExceptionMinutes<>0;"));
    }

    private static async Task AssertSchema(DbContext db)
    {
        Assert.True(await Exists(db,"AttendanceExceptions"));Assert.True(await Exists(db,"AttendanceExceptionHistories"));
        Assert.Equal(8,await Scalar<int>(db,"SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.DailyAttendanceResults') AND name LIKE N'AttendanceException%' OR object_id=OBJECT_ID(N'dbo.DailyAttendanceResults') AND name=N'IsAttendanceExempted';"));
        Assert.Equal(1,await Scalar<int>(db,"SELECT COUNT(*) FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.AttendanceExceptions') AND name=N'UX_AttendanceExceptions_Employee_WorkDate_Active' AND is_unique=1;"));
        Assert.Equal(3,await Scalar<int>(db,"SELECT COUNT(*) FROM sys.foreign_keys WHERE name IN (N'FK_AttendanceExceptions_Employees_EmployeeId',N'FK_AttendanceExceptionHistories_AttendanceExceptions_AttendanceExceptionId',N'FK_DailyAttendanceResults_AttendanceExceptions_AttendanceExceptionSourceId') AND delete_referential_action=0;"));
    }
    private static async Task<bool> Exists(DbContext db,string table)=>await Scalar<int>(db,$"SELECT CASE WHEN OBJECT_ID(N'dbo.{table}',N'U') IS NULL THEN 0 ELSE 1 END;")==1;
    private static async Task<T> Scalar<T>(DbContext db,string sql){var c=db.Database.GetDbConnection();if(c.State!=System.Data.ConnectionState.Open)await c.OpenAsync();await using var cmd=c.CreateCommand();cmd.CommandText=sql;var v=await cmd.ExecuteScalarAsync()??throw new InvalidOperationException();return (T)Convert.ChangeType(v,typeof(T));}
}
