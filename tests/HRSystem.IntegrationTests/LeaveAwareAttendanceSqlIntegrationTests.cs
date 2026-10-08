using HRSystem.Domain.Attendance;
using HRSystem.Domain.MasterData;
using HRSystem.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace HRSystem.IntegrationTests;

[Collection(DisposableSqlServerCollection.Name)]
public sealed class LeaveAwareAttendanceSqlIntegrationTests
{
    private const string PreviousMigration =
        "20260728074830_AddAttendanceManagementFoundation";

    [Fact]
    [Trait("Category", "SqlAttendance")]
    public async Task Migration_Up_Down_Up_Preserves_Existing_Daily_Result()
    {
        var database = await DisposableSqlServerDatabase.CreateAsync(
            "LeaveAwareAttendance");
        var resultId = Guid.NewGuid();
        try
        {
            await using (var setup = database.CreateDbContext())
            {
                var now = new DateTimeOffset(
                    2026, 8, 1, 0, 0, 0, TimeSpan.Zero);
                var department = new Department(
                    Guid.NewGuid(), "LEAVE-SQL", "Leave SQL", now);
                var employee = new Employee(
                    Guid.NewGuid(),
                    "EMP9701",
                    "Leave SQL Employee",
                    department.Id,
                    new DateOnly(2026, 1, 1),
                    now);
                var workDate = new DateOnly(2026, 8, 1);
                var daily = new DailyAttendanceResult(
                    resultId, employee.Id, workDate, now);
                daily.Recalculate(
                    false,
                    AttendanceCalendarClassification.FallbackRestDay,
                    null,
                    AttendanceDailyCalculator.Calculate(
                        workDate,
                        false,
                        null,
                        []),
                    "pre-phase-8.2",
                    now);
                setup.AddRange(department, employee, daily);
                await setup.SaveChangesAsync();
            }

            await using (var down = database.CreateDbContext())
            {
                await down.GetService<IMigrator>()
                    .MigrateAsync(PreviousMigration);
                Assert.Equal(
                    0,
                    await ScalarAsync(
                        down,
                        "SELECT COUNT(*) AS [Value] FROM [sys].[tables] WHERE [name] = N'DailyAttendanceLeaveSegments'"));
                Assert.Equal(
                    0,
                    await ScalarAsync(
                        down,
                        "SELECT COUNT(*) AS [Value] FROM [sys].[columns] WHERE [object_id] = OBJECT_ID(N'[dbo].[DailyAttendanceResults]') AND [name] = N'ApprovedLeaveMinutes'"));
                Assert.Equal(
                    1,
                    await ScalarAsync(
                        down,
                        $"SELECT COUNT(*) AS [Value] FROM [dbo].[DailyAttendanceResults] WHERE [Id] = '{resultId:D}'"));
            }

            await using (var up = database.CreateDbContext())
            {
                await up.GetService<IMigrator>().MigrateAsync();
                var daily = await up.DailyAttendanceResults
                    .AsNoTracking()
                    .SingleAsync(item => item.Id == resultId);

                Assert.Equal(0, daily.ApprovedLeaveMinutes);
                Assert.Equal(0, daily.RequiredAttendanceMinutes);
                Assert.Equal(0, daily.RecognizedWorkMinutes);
                Assert.Equal(0, daily.MissingMinutes);
                Assert.Equal(0, daily.WorkedDuringApprovedLeaveMinutes);
                Assert.Equal(LeaveCoverageStatus.None,
                    daily.LeaveCoverageStatus);
                Assert.Equal(
                    DisposableSqlServerDatabase.ExpectedMigrationCount,
                    (await up.Database.GetAppliedMigrationsAsync()).Count());
                Assert.Empty(await up.Database.GetPendingMigrationsAsync());
            }
        }
        finally
        {
            await database.DisposeAsync();
        }

        Assert.False(
            await DisposableSqlServerDatabase.DatabaseExistsAsync(
                database.DatabaseName));
    }

    private static Task<int> ScalarAsync(
        HRSystemDbContext db,
        string sql) =>
        db.Database.SqlQueryRaw<int>(sql).SingleAsync();
}
