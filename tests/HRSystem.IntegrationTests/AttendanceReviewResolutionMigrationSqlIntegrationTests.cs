using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using HRSystem.Application.Abstractions.Security;
using HRSystem.Application.Attendance;
using HRSystem.Application.Security;
using HRSystem.Domain.Attendance;
using HRSystem.Domain.MasterData;

namespace HRSystem.IntegrationTests;

[Collection(DisposableSqlServerCollection.Name)]
public sealed class AttendanceReviewResolutionMigrationSqlIntegrationTests
{
    private const string Previous =
        "20260812130014_AddAnniversaryAnnualLeaveEntitlements";
    private const string Current =
        "20260820034658_AddAttendanceReviewResolutionLedger";

    [Fact]
    public async Task Migration_Up_Down_Up_Is_Repeatable_And_Model_Is_Current()
    {
        await using var database = await DisposableSqlServerDatabase.CreateAsync(
            "AttendanceReviewLedgerUpDownUp", applyMigrations: false);
        await using var db = database.CreateDbContext();
        var migrator = db.GetService<IMigrator>();

        await migrator.MigrateAsync(Previous);
        Assert.False(await Exists(db, "AttendanceReviewResolutions"));
        await migrator.MigrateAsync(Current);
        await AssertSchema(db);
        await migrator.MigrateAsync(Previous);
        Assert.False(await Exists(db, "AttendanceReviewResolutions"));
        await migrator.MigrateAsync(Current);
        await AssertSchema(db);

        Assert.Equal(
            db.Database.GetMigrations().SkipWhile(x => x != Current).Skip(1),
            await db.Database.GetPendingMigrationsAsync());
        Assert.False(db.Database.HasPendingModelChanges());
    }

    [Fact]
    public async Task Migration_Leaves_New_Ledger_Empty_And_Existing_Attendance_Untouched()
    {
        await using var database = await DisposableSqlServerDatabase.CreateAsync(
            "AttendanceReviewLedgerPreservesData", applyMigrations: false);
        await using var db = database.CreateDbContext();
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(Previous);
        var resultsBefore = await Scalar<int>(db,
            "SELECT COUNT(*) FROM dbo.DailyAttendanceResults;");
        var rawBefore = await Scalar<int>(db,
            "SELECT COUNT(*) FROM dbo.AttendanceRawEvents;");

        await migrator.MigrateAsync(Current);

        Assert.Equal(resultsBefore, await Scalar<int>(db,
            "SELECT COUNT(*) FROM dbo.DailyAttendanceResults;"));
        Assert.Equal(rawBefore, await Scalar<int>(db,
            "SELECT COUNT(*) FROM dbo.AttendanceRawEvents;"));
        Assert.Equal(0, await Scalar<int>(db,
            "SELECT COUNT(*) FROM dbo.AttendanceReviewResolutions;"));
        Assert.Equal(0, await Scalar<int>(db,
            "SELECT COUNT(*) FROM dbo.AttendanceReviewResolutionHistories;"));
    }

    [Fact]
    public async Task Concurrent_Resolve_Allows_One_Ledger_Winner()
    {
        await using var database = await DisposableSqlServerDatabase.CreateAsync(
            "AttendanceReviewLedgerConcurrent");
        Guid employeeId;
        Guid resultId;
        var date = new DateOnly(2026, 8, 20);
        await using (var seed = database.CreateDbContext())
        {
            var now = new DateTimeOffset(2026, 8, 20, 2, 0, 0, TimeSpan.Zero);
            var department = new Department(Guid.NewGuid(), "QA", "測試部", now);
            var employee = new Employee(Guid.NewGuid(), "EMP9988", "測試員工",
                department.Id, new DateOnly(2025, 1, 1), now);
            var shift = new AttendanceShift(Guid.NewGuid(), "NORMAL", "正常班",
                new TimeOnly(8, 0), new TimeOnly(8, 1), new TimeOnly(12, 0),
                new TimeOnly(13, 30), new TimeOnly(17, 30), 480, false, false, now);
            var snapshot = new AttendanceShiftSnapshot(shift.Id, shift.Name,
                shift.ScheduledStartTime, shift.LateThresholdTime,
                shift.LunchBreakStartTime, shift.LunchBreakEndTime,
                shift.ScheduledEndTime, shift.ExpectedWorkMinutes,
                shift.IsLunchPunchRequired, shift.IsOvernightShift);
            var clockInId = Guid.NewGuid();
            var clockOutId = Guid.NewGuid();
            var clockIn = new AttendanceRawEvent(clockInId,
                AttendanceSourceSystems.BioWebTa, 99001, employee.Id, "TEST",
                null, date.ToDateTime(new TimeOnly(8, 30)), null, null, null, now);
            var clockOut = new AttendanceRawEvent(clockOutId,
                AttendanceSourceSystems.BioWebTa, 99002, employee.Id, "TEST",
                null, date.ToDateTime(new TimeOnly(17, 30)), null, null, null, now);
            var calculation = AttendanceDailyCalculator.Calculate(date, true, snapshot,
                [new(clockInId, clockIn.EventLocalDateTime),
                 new(clockOutId, clockOut.EventLocalDateTime)]);
            var daily = new DailyAttendanceResult(Guid.NewGuid(), employee.Id, date, now);
            daily.Recalculate(true, AttendanceCalendarClassification.WorkingDay,
                shift, calculation, "concurrency-test", now);
            seed.AddRange(department, employee, shift, clockIn, clockOut, daily);
            await seed.SaveChangesAsync();
            employeeId = employee.Id;
            resultId = daily.Id;
        }

        AttendanceReviewAnomalyDto anomaly;
        await using (var read = database.CreateDbContext())
        {
            var service = new AttendanceReviewService(read, new AdminCurrentUser());
            anomaly = (await service.SearchAsync(new AttendanceReviewQuery
            {
                StartDate = date,
                EndDate = date,
                EmployeeIds = [employeeId]
            })).Items.Single().ReviewItems.Single(item =>
                item.AnomalyType == AttendanceReviewAnomalyType.Late);
        }

        await using var firstDb = database.CreateDbContext();
        await using var secondDb = database.CreateDbContext();
        var request = new ResolveAttendanceReviewRequest(resultId, anomaly.AnomalyType,
            anomaly.SourceFingerprint,
            AttendanceReviewResolutionReason.ConfirmedAttendance, null, null);
        var first = Attempt(new AttendanceReviewService(firstDb,
            new AdminCurrentUser()).ResolveAsync(request));
        var second = Attempt(new AttendanceReviewService(secondDb,
            new AdminCurrentUser()).ResolveAsync(request));
        var outcomes = await Task.WhenAll(first, second);

        Assert.Single(outcomes, outcome => outcome);
        await using var verify = database.CreateDbContext();
        Assert.Equal(1, await verify.AttendanceReviewResolutions.CountAsync());
        Assert.Equal(2, await verify.AttendanceReviewResolutionHistories.CountAsync());
    }

    private static async Task AssertSchema(DbContext db)
    {
        Assert.True(await Exists(db, "AttendanceReviewResolutions"));
        Assert.True(await Exists(db, "AttendanceReviewResolutionHistories"));
        Assert.Equal(1, await Scalar<int>(db,
            "SELECT COUNT(*) FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.AttendanceReviewResolutions') AND name=N'UX_AttendanceReviewResolutions_Employee_WorkDate_Anomaly' AND is_unique=1;"));
        Assert.Equal(3, await Scalar<int>(db,
            "SELECT COUNT(*) FROM sys.foreign_keys WHERE name IN (N'FK_AttendanceReviewResolutions_DailyAttendanceResults_DailyAttendanceResultId',N'FK_AttendanceReviewResolutions_Employees_EmployeeId',N'FK_AttendanceReviewResolutionHistories_AttendanceReviewResolutions_AttendanceReviewResolutionId') AND delete_referential_action=0;"));
        Assert.Equal(1, await Scalar<int>(db,
            "SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.AttendanceReviewResolutions') AND name=N'RowVersion' AND system_type_id=189;"));
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
        var value = await command.ExecuteScalarAsync() ??
            throw new InvalidOperationException();
        return (T)Convert.ChangeType(value, typeof(T));
    }

    private static async Task<bool> Attempt(Task<AttendanceReviewResolutionResult> action)
    {
        try
        {
            await action;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private sealed class AdminCurrentUser : ICurrentUser
    {
        public string? UserId => "attendance-review-sql-test";
        public Guid? EmployeeId => null;
        public string? DisplayName => "SQL 測試";
        public string? IpAddress => "127.0.0.1";
        public bool IsAuthenticated => true;
        public bool IsInRole(string role) => role == RoleNames.Admin;
        public bool HasPermission(string policy) =>
            RolePermissions.HasPermission([RoleNames.Admin], policy);
    }
}
