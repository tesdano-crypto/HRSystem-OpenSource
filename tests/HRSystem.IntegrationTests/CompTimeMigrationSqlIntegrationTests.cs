using HRSystem.Domain.LeaveRequests;
using HRSystem.Domain.MasterData;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace HRSystem.IntegrationTests;

[Collection(DisposableSqlServerCollection.Name)]
public sealed class CompTimeMigrationSqlIntegrationTests
{
    private const string Previous =
        "20260831020315_AddPeriodicAccruedPayAndOccupationalInsuranceSalary";
    private const string Current = "20260831040212_AddCompTimeLedger";

    [Fact]
    public async Task Migration_35_To_36_To_35_To_36_Preserves_Existing_Data()
    {
        await using var database = await DisposableSqlServerDatabase.CreateAsync(
            "CompTimeUpDownUp",
            applyMigrations: false);
        await using var db = database.CreateDbContext();
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(Previous);

        var now = DateTimeOffset.Parse("2026-08-31T04:10:00Z");
        var department = new Department(Guid.NewGuid(), "CT36", "CT36", now);
        var employee = new Employee(Guid.NewGuid(), "CT36001", "CT36",
            department.Id, new DateOnly(2020, 1, 1), now);
        var personal = new LeaveType(Guid.NewGuid(), "CT36_PERSONAL", "CT36事假",
            LeaveUnit.Hour, 0.5m, true, false, 990, now);
        var request = new LeaveRequest(
            Guid.NewGuid(),
            "CT36-REQUEST",
            employee.Id,
            personal.Id,
            DateTimeOffset.Parse("2026-09-01T00:00:00Z"),
            DateTimeOffset.Parse("2026-09-01T01:00:00Z"),
            1m,
            "migration preservation",
            "migration-test",
            now);
        db.AddRange(department, employee, personal, request);
        await db.SaveChangesAsync();

        var payroll = await Count(db, "PayrollPeriods");
        var attendance = await Count(db, "DailyAttendanceResults");
        var insurance = await Count(db, "EmployeeLaborInsuranceEnrollments");
        var pairing = await Count(db, "LinePairingRequests");

        await migrator.MigrateAsync(Current);
        await AssertCurrentSchema(db);
        Assert.Equal(1, await Scalar<int>(db, """
            SELECT COUNT(*) FROM dbo.LeaveTypes
            WHERE Code = N'COMP_TIME' AND Name = N'補休'
              AND Unit = 1 AND MinimumUnit = 0.5
              AND MinimumRequestMinutes = 30
              AND IsPaid = 1 AND IsActive = 1
              AND IsEmployeeRequestEnabled = 1;
            """));
        Assert.Equal(1, await Scalar<int>(db, $"""
            SELECT COUNT(*) FROM dbo.LeaveRequests WHERE Id = '{request.Id}';
            """));
        await AssertProtectedCounts(db, payroll, attendance, insurance, pairing);

        await migrator.MigrateAsync(Previous);
        Assert.Equal(0, await Scalar<int>(db, """
            SELECT COUNT(*) FROM sys.tables WHERE name = N'CompTimeTransactions';
            """));
        Assert.Equal(0, await Scalar<int>(db, """
            SELECT COUNT(*) FROM dbo.LeaveTypes WHERE Code = N'COMP_TIME';
            """));
        Assert.Equal(1, await Scalar<int>(db, $"""
            SELECT COUNT(*) FROM dbo.LeaveRequests WHERE Id = '{request.Id}';
            """));
        await AssertProtectedCounts(db, payroll, attendance, insurance, pairing);

        await migrator.MigrateAsync(Current);
        await AssertCurrentSchema(db);
        Assert.Equal(1, await Scalar<int>(db, """
            SELECT COUNT(*) FROM dbo.LeaveTypes WHERE Code = N'COMP_TIME';
            """));
        await AssertProtectedCounts(db, payroll, attendance, insurance, pairing);
    }

    private static async Task AssertCurrentSchema(DbContext db)
    {
        Assert.Equal(1, await Scalar<int>(db, """
            SELECT COUNT(*) FROM sys.tables WHERE name = N'CompTimeTransactions';
            """));
        Assert.Equal(2, await Scalar<int>(db, """
            SELECT COUNT(*) FROM sys.indexes WHERE name IN
              (N'UX_CompTimeTransactions_LegacyOpeningBalance',
               N'UX_CompTimeTransactions_LeaveRequestAction');
            """));
        Assert.Equal(1, await Scalar<int>(db, """
            SELECT COUNT(*) FROM sys.foreign_keys
            WHERE name = N'FK_CompTimeTransactions_Employees_EmployeeId'
              AND delete_referential_action = 0;
            """));
        Assert.Equal(4, await Scalar<int>(db, """
            SELECT COUNT(*) FROM sys.check_constraints
            WHERE parent_object_id = OBJECT_ID(N'dbo.CompTimeTransactions');
            """));
        Assert.Equal(Current, await Scalar<string>(db, """
            SELECT TOP(1) MigrationId FROM dbo.__EFMigrationsHistory
            ORDER BY MigrationId DESC;
            """));
    }

    private static async Task AssertProtectedCounts(
        DbContext db,
        int payroll,
        int attendance,
        int insurance,
        int pairing)
    {
        Assert.Equal(payroll, await Count(db, "PayrollPeriods"));
        Assert.Equal(attendance, await Count(db, "DailyAttendanceResults"));
        Assert.Equal(insurance, await Count(db, "EmployeeLaborInsuranceEnrollments"));
        Assert.Equal(pairing, await Count(db, "LinePairingRequests"));
    }

    private static Task<int> Count(DbContext db, string table) =>
        Scalar<int>(db, $"SELECT COUNT(*) FROM dbo.[{table}];");

    private static async Task<T> Scalar<T>(DbContext db, string sql)
    {
        await db.Database.OpenConnectionAsync();
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = sql;
        var value = await command.ExecuteScalarAsync();
        return value is T typed
            ? typed
            : (T)Convert.ChangeType(
                value!,
                typeof(T),
                System.Globalization.CultureInfo.InvariantCulture);
    }
}
