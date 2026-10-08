using HRSystem.Domain.Attendance;
using HRSystem.Domain.MasterData;
using HRSystem.Application.Attendance;
using HRSystem.Application.Abstractions.Security;
using HRSystem.Application.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Data.Common;

namespace HRSystem.IntegrationTests;

[Collection(DisposableSqlServerCollection.Name)]
public sealed class NonWorkingDayPunchSqlTests
{
    [Fact]
    public async Task Daily_Punch_Projection_Uses_Batched_SQL_And_Pages_After_Pending_Filter()
    {
        await using var database = await DisposableSqlServerDatabase.CreateAsync("DailyPunchProjection");
        var counter = new ReadCounter();
        await using var db = database.CreateDbContext(counter);
        var now = DateTimeOffset.UtcNow;
        var date = new DateOnly(2026, 8, 29);
        var department = new Department(Guid.NewGuid(), "DP", "測試", now);
        db.Add(department);
        async Task AddEmployee(int index)
        {
            var employee = new Employee(Guid.NewGuid(), $"DP{index:D3}", "測試", department.Id, new(2025, 1, 1), now);
            var daily = new DailyAttendanceResult(Guid.NewGuid(), employee.Id, date, now);
            daily.Recalculate(false, AttendanceCalendarClassification.Weekend, null,
                AttendanceDailyCalculator.Calculate(date, false, null, []), "test", now);
            db.AddRange(employee, daily,
                new AttendanceRawEvent(Guid.NewGuid(), AttendanceSourceSystems.BioWebTa, 100000 + index * 2, employee.Id,
                    employee.EmployeeNumber, null, date.ToDateTime(new(10, 9, 14)), null, null, null, now),
                new AttendanceRawEvent(Guid.NewGuid(), AttendanceSourceSystems.BioWebTa, 100001 + index * 2, employee.Id,
                    employee.EmployeeNumber, null, date.ToDateTime(new(12, 36, 12)), null, null, null, now));
            await db.SaveChangesAsync();
        }
        await AddEmployee(1);
        var service = new AttendanceManagementService(db, new Admin(), TimeProvider.System);
        var query = new DailyAttendanceQuery { DateFrom = date, DateTo = date, ExceptionsOnly = true, PageSize = 1 };
        counter.Queries.Clear();
        Assert.Single((await service.GetDailyResultsAsync(query)).Items);
        var oneEmployeeReads = counter.Queries.Count;
        for (var i = 2; i <= 10; i++) await AddEmployee(i);
        db.ChangeTracker.Clear();
        query.PageNumber = 2;
        counter.Queries.Clear();

        var result = await service.GetDailyResultsAsync(query);

        Assert.Equal(10, result.TotalCount);
        var row = Assert.Single(result.Items);
        Assert.Equal("DP002", row.EmployeeNumber);
        Assert.Equal(new TimeSpan(2, 26, 58), row.PunchEvidence!.PunchSpan);
        Assert.True(row.NonWorkingPunchPending);
        Assert.Null(row.EffectiveClockInLocalTime);
        Assert.Equal(oneEmployeeReads, counter.Queries.Count);
        Assert.Contains(counter.Queries, sql => sql.Contains("ORDER BY", StringComparison.Ordinal) && sql.Contains("OFFSET", StringComparison.Ordinal));
        Assert.False(db.ChangeTracker.HasChanges());
        Assert.Empty(db.PayrollRuns);
        Assert.Empty(db.AttendanceAdjustments);
        Assert.Empty(db.AttendanceReviewResolutions);
    }

    private sealed class ReadCounter : DbCommandInterceptor
    {
        public List<string> Queries { get; } = [];
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Queries.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }
    }

    [Fact]
    public async Task Up_Down_Up_Query_And_Source_Invariants_Are_Enforced()
    {
        await using var database = await DisposableSqlServerDatabase.CreateAsync("NonWorkingPunch", false);
        await using var db = database.CreateDbContext();
        var migrator = db.GetService<IMigrator>();
        const string previous = "20260831142119_AddIndependentOccupationalInsuranceEnrollment";
        const string current = "20260903142616_AddNonWorkingDayPunchReviewSupport";
        await migrator.MigrateAsync(previous);
        Assert.Equal(36, (await db.Database.GetAppliedMigrationsAsync()).Count());
        await migrator.MigrateAsync(current);
        Assert.Equal(37, (await db.Database.GetAppliedMigrationsAsync()).Count());
        await migrator.MigrateAsync(previous);
        Assert.Equal(36, (await db.Database.GetAppliedMigrationsAsync()).Count());
        await migrator.MigrateAsync(current);
        Assert.False(db.Database.HasPendingModelChanges());
        var now = DateTimeOffset.UtcNow;
        var date = new DateOnly(2026, 8, 29);
        var department = new Department(Guid.NewGuid(), "NWSQL", "測試", now);
        var employee = new Employee(Guid.NewGuid(), "NWSQL001", "測試", department.Id, new(2025, 1, 1), now);
        db.AddRange(department, employee,
            new AttendanceRawEvent(Guid.NewGuid(), AttendanceSourceSystems.BioWebTa, 90001, employee.Id, "TEST", null,
                date.ToDateTime(new(8, 3)), null, null, null, now),
            new AttendanceRawEvent(Guid.NewGuid(), AttendanceSourceSystems.BioWebTa, 90002, employee.Id, "TEST", null,
                date.ToDateTime(new(12, 6)), null, null, null, now));
        await db.SaveChangesAsync();
        var service = new AttendanceReviewService(db, new Admin());
        var query = new AttendanceReviewQuery { StartDate = date, EndDate = date, EmployeeIds = [employee.Id], OnlyAnomalies = true };
        var row = Assert.Single((await service.SearchAsync(query)).Items);
        var saved = await service.ResolveAsync(new(Guid.Empty, AttendanceReviewAnomalyType.NonWorkingDayPunch,
            row.PunchEvidence!.Fingerprint, AttendanceReviewResolutionReason.NonWorkActivity, "私人取物", null)
        { EmployeeId = employee.Id, WorkDate = date });
        Assert.Empty((await service.SearchAsync(query)).Items);
        Assert.Empty(db.DailyAttendanceResults);
        Assert.Equal(2, await db.AttendanceRawEvents.CountAsync());
        Assert.Equal(2, await db.AttendanceReviewResolutionHistories.CountAsync());
        await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE AttendanceReviewResolutions SET AnomalyType = 1 WHERE Id = {saved.ResolutionId}"));
        await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE AttendanceReviewResolutions SET Reason = 10 WHERE Id = {saved.ResolutionId}"));
        await Assert.ThrowsAsync<SqlException>(() => migrator.MigrateAsync(previous));
        Assert.Equal(37, (await db.Database.GetAppliedMigrationsAsync()).Count());
        Assert.Single(db.AttendanceReviewResolutions);
    }

    private sealed class Admin : ICurrentUser
    {
        public string? UserId => "nw-sql-admin";
        public Guid? EmployeeId => null;
        public string? DisplayName => "test";
        public string? IpAddress => null;
        public bool IsAuthenticated => true;
        public bool IsInRole(string role) => role == RoleNames.Admin;
        public bool HasPermission(string policy) => RolePermissions.HasPermission([RoleNames.Admin], policy);
    }
}
