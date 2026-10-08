using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace HRSystem.IntegrationTests;

[Collection(DisposableSqlServerCollection.Name)]
public sealed class PayrollLegacyAdjustmentMigrationSqlIntegrationTests
{
    private const string Previous = "20260828093258_AddPayrollPayCycles";
    private const string Current = "20260830095141_AddPayrollLegacyAdjustmentComponents";

    [Fact]
    public async Task Migration_32_To_33_To_32_To_33_Only_Adds_Legacy_Definitions()
    {
        await using var database = await DisposableSqlServerDatabase.CreateAsync(
            "PayrollLegacyAdjustmentsUpDownUp", applyMigrations: false);
        await using var db = database.CreateDbContext();
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(Previous);

        var periods = await Scalar<int>(db, "SELECT COUNT(*) FROM dbo.PayrollPeriods;");
        var runs = await Scalar<int>(db, "SELECT COUNT(*) FROM dbo.PayrollRuns;");
        var snapshots = await Scalar<int>(db,
            "SELECT COUNT(*) FROM dbo.PayrollEmployeeSnapshots;");
        var adjustments = await Scalar<int>(db,
            "SELECT COUNT(*) FROM dbo.PayrollAdjustments;");
        var definitions = await Scalar<int>(db,
            "SELECT COUNT(*) FROM dbo.PayrollComponentDefinitions;");

        await migrator.MigrateAsync(Current);
        await AssertCurrent(db, definitions + 2, periods, runs, snapshots, adjustments);

        await migrator.MigrateAsync(Previous);
        Assert.Equal(0, await Scalar<int>(db, """
            SELECT COUNT(*) FROM dbo.PayrollComponentDefinitions
            WHERE Code IN (N'LEGACY_ATTENDANCE_ALLOWANCE', N'LEGACY_OVERTIME_PAY');
            """));
        Assert.Equal(definitions, await Scalar<int>(db,
            "SELECT COUNT(*) FROM dbo.PayrollComponentDefinitions;"));
        await AssertOperationalCounts(db, periods, runs, snapshots, adjustments);

        await migrator.MigrateAsync(Current);
        await AssertCurrent(db, definitions + 2, periods, runs, snapshots, adjustments);
    }

    private static async Task AssertCurrent(DbContext db, int definitions,
        int periods, int runs, int snapshots, int adjustments)
    {
        Assert.Equal(definitions, await Scalar<int>(db,
            "SELECT COUNT(*) FROM dbo.PayrollComponentDefinitions;"));
        Assert.Equal(2, await Scalar<int>(db, """
            SELECT COUNT(*) FROM dbo.PayrollComponentDefinitions
            WHERE Code IN (N'LEGACY_ATTENDANCE_ALLOWANCE', N'LEGACY_OVERTIME_PAY')
              AND Category = 1 AND CalculationKind = 3 AND IsActive = 1;
            """));
        Assert.Equal(4, await Scalar<int>(db, """
            SELECT COUNT(*) FROM dbo.PayrollComponentDefinitions
            WHERE (Code = N'ATTENDANCE_ALLOWANCE' AND CalculationKind = 2)
               OR (Code IN (N'OVERTIME_FIRST_2H', N'OVERTIME_AFTER_2H',
                            N'OVERTIME_AFTER_8H') AND CalculationKind = 4);
            """));
        await AssertOperationalCounts(db, periods, runs, snapshots, adjustments);
        Assert.Equal(Current, await Scalar<string>(db, """
            SELECT TOP(1) MigrationId FROM dbo.__EFMigrationsHistory
            ORDER BY MigrationId DESC;
            """));
    }

    private static async Task AssertOperationalCounts(DbContext db, int periods,
        int runs, int snapshots, int adjustments)
    {
        Assert.Equal(periods, await Scalar<int>(db,
            "SELECT COUNT(*) FROM dbo.PayrollPeriods;"));
        Assert.Equal(runs, await Scalar<int>(db,
            "SELECT COUNT(*) FROM dbo.PayrollRuns;"));
        Assert.Equal(snapshots, await Scalar<int>(db,
            "SELECT COUNT(*) FROM dbo.PayrollEmployeeSnapshots;"));
        Assert.Equal(adjustments, await Scalar<int>(db,
            "SELECT COUNT(*) FROM dbo.PayrollAdjustments;"));
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
