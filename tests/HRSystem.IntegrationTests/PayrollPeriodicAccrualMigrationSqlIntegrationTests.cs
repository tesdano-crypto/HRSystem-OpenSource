using HRSystem.Domain.MasterData;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace HRSystem.IntegrationTests;

[Collection(DisposableSqlServerCollection.Name)]
public sealed class PayrollPeriodicAccrualMigrationSqlIntegrationTests
{
    private const string Previous =
        "20260830141851_AddOwnerPrivateLinePairing";
    private const string Current =
        "20260831020315_AddPeriodicAccruedPayAndOccupationalInsuranceSalary";

    [Fact]
    public async Task Migration_34_To_35_To_34_To_35_Preserves_Legacy_Data()
    {
        await using var database = await DisposableSqlServerDatabase.CreateAsync(
            "PayrollPeriodicAccrualUpDownUp", applyMigrations: false);
        await using var db = database.CreateDbContext();
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(Previous);

        var now = DateTimeOffset.Parse("2026-08-31T02:10:00Z");
        var department = new Department(Guid.NewGuid(), "P35", "P35", now);
        var employee = new Employee(Guid.NewGuid(), "P35001", "P35",
            department.Id, new DateOnly(2020, 1, 1), now);
        db.AddRange(department, employee);
        await db.SaveChangesAsync();
        var payCycleId = Guid.NewGuid();
        var enrollmentId = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO dbo.EmployeePayrollPayCycles
              (Id, EmployeeId, Type, EffectiveFrom, EffectiveTo,
               AnchorPayMonth, FixedPaymentAmount, Reason, IsActive)
            VALUES ({payCycleId}, {employee.Id}, 2, {new DateOnly(2026, 1, 1)},
               NULL, {new DateOnly(2025, 12, 1)}, {4000m}, N'legacy', 1);
            INSERT INTO dbo.EmployeeLaborInsuranceEnrollments
              (Id, EmployeeId, Status, MonthlyInsuredSalary,
               EffectiveFrom, EffectiveTo, IsActive)
            VALUES ({enrollmentId}, {employee.Id}, 1, {45800m},
               {new DateOnly(2026, 1, 1)}, NULL, 1);
            """);

        var periods = await Scalar<int>(db,
            "SELECT COUNT(*) FROM dbo.PayrollPeriods;");
        var snapshots = await Scalar<int>(db,
            "SELECT COUNT(*) FROM dbo.PayrollEmployeeSnapshots;");
        var adjustments = await Scalar<int>(db,
            "SELECT COUNT(*) FROM dbo.PayrollAdjustments;");
        var approvals = await Scalar<int>(db,
            "SELECT COUNT(*) FROM dbo.Approvals;");
        var bindings = await Scalar<int>(db,
            "SELECT COUNT(*) FROM dbo.LineUserBindings;");

        await migrator.MigrateAsync(Current);
        await AssertCurrentSchema(db);
        Assert.Equal(1, await Scalar<int>(db, $"""
            SELECT COUNT(*) FROM dbo.EmployeePayrollPayCycles
            WHERE Id = '{payCycleId}' AND Type = 2
              AND FixedPaymentAmount = 4000
              AND MonthlyFixedAmount IS NULL AND CycleMonths IS NULL
              AND PaymentTiming IS NULL;
            """));
        Assert.Equal(1, await Scalar<int>(db, $"""
            SELECT COUNT(*) FROM dbo.EmployeeLaborInsuranceEnrollments
            WHERE Id = '{enrollmentId}'
              AND MonthlyLaborInsuredSalary = 45800
              AND MonthlyOccupationalInsuredSalary IS NULL;
            """));
        await AssertOperationalCounts(db, periods, snapshots, adjustments,
            approvals, bindings);

        await migrator.MigrateAsync(Previous);
        Assert.Equal(1, await Scalar<int>(db, $"""
            SELECT COUNT(*) FROM dbo.EmployeeLaborInsuranceEnrollments
            WHERE Id = '{enrollmentId}' AND MonthlyInsuredSalary = 45800;
            """));
        Assert.Equal(0, await Scalar<int>(db, """
            SELECT COUNT(*) FROM sys.tables WHERE name IN
              (N'PayrollPeriodicAccrualSnapshots',
               N'PayrollPeriodicAccrualMonthEvidence');
            """));
        await AssertOperationalCounts(db, periods, snapshots, adjustments,
            approvals, bindings);

        await migrator.MigrateAsync(Current);
        await AssertCurrentSchema(db);
        await AssertOperationalCounts(db, periods, snapshots, adjustments,
            approvals, bindings);
    }

    [Fact]
    public async Task Migration35_Constraints_Accept_Typed_Periodic_And_Separate_Salaries()
    {
        await using var database = await DisposableSqlServerDatabase.CreateAsync(
            "PayrollPeriodicAccrualConstraints", applyMigrations: false);
        await using var db = database.CreateDbContext();
        await db.GetService<IMigrator>().MigrateAsync(Current);
        var now = DateTimeOffset.Parse("2026-08-31T02:15:00Z");
        var department = new Department(Guid.NewGuid(), "P35C", "P35C", now);
        var employee = new Employee(Guid.NewGuid(), "P35002", "P35C",
            department.Id, new DateOnly(2020, 1, 1), now);
        db.AddRange(department, employee);
        await db.SaveChangesAsync();

        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO dbo.EmployeePayrollPayCycles
              (Id, EmployeeId, Type, EffectiveFrom, EffectiveTo,
               AnchorPayMonth, FixedPaymentAmount, MonthlyFixedAmount,
               CycleMonths, PaymentTiming, Reason, IsActive)
            VALUES ({Guid.NewGuid()}, {employee.Id}, 3,
               {new DateOnly(2026, 1, 1)}, NULL,
               {new DateOnly(2025, 12, 1)}, NULL, {4000m}, 6, 1,
               N'typed periodic', 1);
            INSERT INTO dbo.EmployeeLaborInsuranceEnrollments
              (Id, EmployeeId, Status, MonthlyLaborInsuredSalary,
               MonthlyOccupationalInsuredSalary, EffectiveFrom,
               EffectiveTo, IsActive)
            VALUES ({Guid.NewGuid()}, {employee.Id}, 1, {45800m}, {72800m},
               {new DateOnly(2026, 1, 1)}, NULL, 1);
            """);

        Assert.Equal(1, await Scalar<int>(db, """
            SELECT COUNT(*) FROM dbo.EmployeePayrollPayCycles
            WHERE Type = 3 AND MonthlyFixedAmount = 4000
              AND CycleMonths = 6 AND PaymentTiming = 1;
            """));
        Assert.Equal(1, await Scalar<int>(db, """
            SELECT COUNT(*) FROM dbo.EmployeeLaborInsuranceEnrollments
            WHERE MonthlyLaborInsuredSalary = 45800
              AND MonthlyOccupationalInsuredSalary = 72800;
            """));
    }

    private static async Task AssertCurrentSchema(DbContext db)
    {
        Assert.Equal(2, await Scalar<int>(db, """
            SELECT COUNT(*) FROM sys.tables WHERE name IN
              (N'PayrollPeriodicAccrualSnapshots',
               N'PayrollPeriodicAccrualMonthEvidence');
            """));
        Assert.Equal(5, await Scalar<int>(db, """
            SELECT COUNT(*) FROM sys.columns WHERE
              (object_id = OBJECT_ID(N'dbo.EmployeePayrollPayCycles') AND
               name IN (N'MonthlyFixedAmount', N'CycleMonths', N'PaymentTiming'))
              OR (object_id = OBJECT_ID(N'dbo.EmployeeLaborInsuranceEnrollments') AND
               name IN (N'MonthlyLaborInsuredSalary',
                        N'MonthlyOccupationalInsuredSalary'));
            """));
        Assert.Equal(2, await Scalar<int>(db, """
            SELECT COUNT(*) FROM sys.indexes WHERE name IN
              (N'UX_PayrollPeriodicAccrualSnapshots_Component',
               N'UX_PayrollPeriodicAccrualMonthEvidence_Snapshot_Month');
            """));
        Assert.Equal(2, await Scalar<int>(db, """
            SELECT COUNT(*) FROM sys.foreign_keys WHERE
              parent_object_id IN
                (OBJECT_ID(N'dbo.PayrollPeriodicAccrualSnapshots'),
                 OBJECT_ID(N'dbo.PayrollPeriodicAccrualMonthEvidence'))
              AND delete_referential_action = 0;
            """));
        Assert.Equal(Current, await Scalar<string>(db, """
            SELECT TOP(1) MigrationId FROM dbo.__EFMigrationsHistory
            ORDER BY MigrationId DESC;
            """));
    }

    private static async Task AssertOperationalCounts(DbContext db,
        int periods, int snapshots, int adjustments, int approvals, int bindings)
    {
        Assert.Equal(periods, await Scalar<int>(db,
            "SELECT COUNT(*) FROM dbo.PayrollPeriods;"));
        Assert.Equal(snapshots, await Scalar<int>(db,
            "SELECT COUNT(*) FROM dbo.PayrollEmployeeSnapshots;"));
        Assert.Equal(adjustments, await Scalar<int>(db,
            "SELECT COUNT(*) FROM dbo.PayrollAdjustments;"));
        Assert.Equal(approvals, await Scalar<int>(db,
            "SELECT COUNT(*) FROM dbo.Approvals;"));
        Assert.Equal(bindings, await Scalar<int>(db,
            "SELECT COUNT(*) FROM dbo.LineUserBindings;"));
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
