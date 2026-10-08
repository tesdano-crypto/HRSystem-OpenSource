using HRSystem.Domain.MasterData;
using HRSystem.Domain.Payroll;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace HRSystem.IntegrationTests;

[Collection(DisposableSqlServerCollection.Name)]
public sealed class PayrollPayCycleMigrationSqlIntegrationTests
{
    private const string Previous = "20260828054445_AddPayrollFinalization";
    private const string Current = "20260828093258_AddPayrollPayCycles";

    [Fact]
    public async Task Migration_31_To_32_To_31_To_32_Preserves_Existing_Payroll_Data()
    {
        await using var database = await DisposableSqlServerDatabase.CreateAsync(
            "PayrollPayCyclesUpDownUp", applyMigrations: false);
        await using var db = database.CreateDbContext();
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(Previous);
        var periods = await Scalar<int>(db,
            "SELECT COUNT(*) FROM dbo.PayrollPeriods;");
        var assignments = await Scalar<int>(db,
            "SELECT COUNT(*) FROM dbo.EmployeePayrollAssignments;");

        await migrator.MigrateAsync(Current);
        await AssertSchema(db);
        Assert.Equal(periods, await Scalar<int>(db,
            "SELECT COUNT(*) FROM dbo.PayrollPeriods;"));
        Assert.Equal(assignments, await Scalar<int>(db,
            "SELECT COUNT(*) FROM dbo.EmployeePayrollAssignments;"));

        await migrator.MigrateAsync(Previous);
        Assert.Equal(0, await Scalar<int>(db, """
            SELECT COUNT(*) FROM sys.tables
            WHERE name = N'EmployeePayrollPayCycles';
            """));
        await migrator.MigrateAsync(Current);
        await AssertSchema(db);
    }

    [Fact]
    public async Task Constraints_Reject_Invalid_Details_And_Duplicate_Start()
    {
        await using var database = await DisposableSqlServerDatabase.CreateAsync(
            "PayrollPayCyclesConstraints");
        await using var db = database.CreateDbContext();
        var now = DateTimeOffset.Parse("2026-08-28T09:30:00Z");
        var department = new Department(Guid.NewGuid(), "PC", "週期測試", now);
        var employee = new Employee(Guid.NewGuid(), "PC001", "週期測試員工",
            department.Id, new DateOnly(2020, 1, 1), now);
        db.AddRange(department, employee);
        db.EmployeePayrollPayCycles.Add(new EmployeePayrollPayCycle(Guid.NewGuid(),
            employee.Id, PayrollPayCycleType.SemiannualFixed,
            new DateOnly(2025, 12, 1), anchorPayMonth: new DateOnly(2025, 12, 1),
            fixedPaymentAmount: 4000, reason: "test"));
        await db.SaveChangesAsync();

        await Assert.ThrowsAnyAsync<Exception>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO dbo.EmployeePayrollPayCycles
              (Id, EmployeeId, Type, EffectiveFrom, EffectiveTo,
               AnchorPayMonth, FixedPaymentAmount, Reason, IsActive)
            VALUES ({Guid.NewGuid()}, {employee.Id}, 2, {new DateOnly(2026, 2, 1)},
               NULL, {new DateOnly(2026, 2, 1)}, 0, N'invalid', 1);
            """));
        await Assert.ThrowsAnyAsync<Exception>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO dbo.EmployeePayrollPayCycles
              (Id, EmployeeId, Type, EffectiveFrom, EffectiveTo,
               AnchorPayMonth, FixedPaymentAmount, Reason, IsActive)
            VALUES ({Guid.NewGuid()}, {employee.Id}, 1, {new DateOnly(2025, 12, 1)},
               NULL, NULL, NULL, N'duplicate', 1);
            """));
    }

    private static async Task AssertSchema(DbContext db)
    {
        Assert.Equal(1, await Scalar<int>(db, """
            SELECT COUNT(*) FROM sys.tables
            WHERE name = N'EmployeePayrollPayCycles';
            """));
        Assert.Equal(2, await Scalar<int>(db, """
            SELECT COUNT(*) FROM sys.indexes
            WHERE object_id = OBJECT_ID(N'dbo.EmployeePayrollPayCycles')
              AND name IN (N'UX_EmployeePayrollPayCycles_Employee_From',
                           N'IX_EmployeePayrollPayCycles_EffectiveLookup');
            """));
        Assert.Equal(1, await Scalar<int>(db, """
            SELECT COUNT(*) FROM sys.foreign_keys
            WHERE parent_object_id = OBJECT_ID(N'dbo.EmployeePayrollPayCycles')
              AND delete_referential_action = 0;
            """));
        Assert.Equal(1, await Scalar<int>(db, """
            SELECT COUNT(*) FROM dbo.PayrollComponentDefinitions
            WHERE Code = N'PERIODIC_FIXED_PAY';
            """));
        Assert.Equal(Current, await Scalar<string>(db, """
            SELECT TOP(1) MigrationId FROM dbo.__EFMigrationsHistory
            ORDER BY MigrationId DESC;
            """));
    }

    private static async Task<T> Scalar<T>(DbContext db, string sql)
    {
        await db.Database.OpenConnectionAsync();
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = sql;
        var value = await command.ExecuteScalarAsync();
        return value is T typed ? typed : (T)Convert.ChangeType(value!, typeof(T),
            System.Globalization.CultureInfo.InvariantCulture);
    }
}
