using HRSystem.Domain.MasterData;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace HRSystem.IntegrationTests;

[Collection(DisposableSqlServerCollection.Name)]
public sealed class PayrollRevisionMigrationSqlIntegrationTests
{
    private const string Previous = "20260828032342_AddApprovalWorkflowFoundation";
    private const string Current = "20260828042718_AddPayrollCalculationRevisions";

    [Fact]
    public async Task Legacy_Data_Backfills_Current_Pointer_And_Up_Down_Up_Is_Repeatable()
    {
        await using var database = await DisposableSqlServerDatabase.CreateAsync(
            "PayrollRevisionUpDownUp", applyMigrations: false);
        await using var db = database.CreateDbContext();
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(Previous);

        var now = new DateTimeOffset(2026, 8, 28, 4, 0, 0, TimeSpan.Zero);
        var department = new Department(Guid.NewGuid(), "PAY-R", "版本測試部", now);
        var employee = new Employee(Guid.NewGuid(), "PAYR001", "版本測試員工",
            department.Id, new DateOnly(2020, 1, 1), now);
        db.Departments.Add(department);
        db.Employees.Add(employee);
        await db.SaveChangesAsync();
        var periodId = Guid.NewGuid();
        var runId = Guid.NewGuid();
        var snapshotId = Guid.NewGuid();
        var fingerprint = Enumerable.Range(1, 32).Select(x => (byte)x).ToArray();
        var periodStart = new DateOnly(2026, 8, 1);
        var periodEnd = new DateOnly(2026, 8, 31);
        var employmentStart = new DateOnly(2020, 1, 1);
        const string actor = "legacy-gate";
        const string employeeCode = "PAYR001";
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO dbo.PayrollPeriods
                (Id, Year, Month, PeriodStart, PeriodEnd, Status)
            VALUES ({periodId}, 2026, 8, {periodStart}, {periodEnd}, 2);
            INSERT INTO dbo.PayrollRuns
                (Id, PayrollPeriodId, Status, CreatedAtUtc, CreatedBy, FinalizedAtUtc)
            VALUES ({runId}, {periodId}, 1, {now}, {actor}, NULL);
            INSERT INTO dbo.PayrollEmployeeSnapshots
                (Id, PayrollRunId, EmployeeId, EmployeeCode, EmployeeName,
                 DepartmentId, DepartmentName, EmploymentStart, EmploymentEnd,
                 PayrollPlanId, PayrollPlanCode, SnapshotAtUtc, SetupStatus,
                 GrossPay, TotalDeductions, NetPay, TotalCalculationStatus,
                 TotalsCalculatedAtUtc, BlockingComponentCount,
                 TotalSourceFingerprintVersion, TotalSourceFingerprint)
            VALUES ({snapshotId}, {runId}, {employee.Id}, {employeeCode},
                {employee.ChineseName}, {department.Id}, {department.Name},
                {employmentStart}, NULL, NULL, NULL, {now}, 1,
                42000, 3000, 39000, 1, {now}, 0, 1, {fingerprint});
            """);

        await migrator.MigrateAsync(Current);
        await AssertRevisionSchemaAndBackfill(db, periodId, employee.Id,
            runId, snapshotId, fingerprint);

        await migrator.MigrateAsync(Previous);
        Assert.Equal(0, await ScalarAsync<int>(db, """
            SELECT COUNT(*) FROM sys.tables
            WHERE object_id=OBJECT_ID(N'dbo.PayrollPeriodEmployeeCurrentSnapshots');
            """));
        Assert.Equal(1, await ScalarAsync<int>(db, """
            SELECT COUNT(*) FROM sys.indexes
            WHERE object_id=OBJECT_ID(N'dbo.PayrollRuns')
              AND name=N'UX_PayrollRuns_Period' AND is_unique=1;
            """));

        await migrator.MigrateAsync(Current);
        await AssertRevisionSchemaAndBackfill(db, periodId, employee.Id,
            runId, snapshotId, fingerprint);
    }

    private static async Task AssertRevisionSchemaAndBackfill(DbContext db,
        Guid periodId, Guid employeeId, Guid runId, Guid snapshotId,
        byte[] fingerprint)
    {
        Assert.Equal(1, await ScalarAsync<int>(db, """
            SELECT COUNT(*) FROM dbo.PayrollPeriodEmployeeCurrentSnapshots;
            """));
        Assert.Equal(snapshotId, await ScalarAsync<Guid>(db, $"""
            SELECT PayrollEmployeeSnapshotId
            FROM dbo.PayrollPeriodEmployeeCurrentSnapshots
            WHERE PayrollPeriodId='{periodId}' AND EmployeeId='{employeeId}';
            """));
        Assert.Equal(1, await ScalarAsync<int>(db, $"""
            SELECT RevisionNumber FROM dbo.PayrollRuns WHERE Id='{runId}';
            """));
        Assert.Equal(1, await ScalarAsync<byte>(db, $"""
            SELECT [Trigger] FROM dbo.PayrollRuns WHERE Id='{runId}';
            """));
        Assert.Equal(periodId, await ScalarAsync<Guid>(db, $"""
            SELECT PayrollPeriodId FROM dbo.PayrollEmployeeSnapshots WHERE Id='{snapshotId}';
            """));
        Assert.Equal(Convert.ToHexString(fingerprint), await ScalarAsync<string>(db, $"""
            SELECT CONVERT(varchar(64), TotalSourceFingerprint, 2)
            FROM dbo.PayrollEmployeeSnapshots WHERE Id='{snapshotId}';
            """));
        Assert.Equal("PAYR001", await ScalarAsync<string>(db, $"""
            SELECT EmployeeCode FROM dbo.PayrollEmployeeSnapshots WHERE Id='{snapshotId}';
            """));
        Assert.Equal(42000m, await ScalarAsync<decimal>(db, $"""
            SELECT GrossPay FROM dbo.PayrollEmployeeSnapshots WHERE Id='{snapshotId}';
            """));
        Assert.Equal(3, await ScalarAsync<int>(db, """
            SELECT COUNT(*) FROM sys.indexes WHERE is_unique=1 AND name IN
              (N'UX_PayrollRuns_Period_Revision',
               N'UX_PayrollCurrentSnapshots_Snapshot',
               N'PK_PayrollPeriodEmployeeCurrentSnapshots');
            """));
        Assert.Equal(3, await ScalarAsync<int>(db, """
            SELECT COUNT(*) FROM sys.foreign_keys
            WHERE parent_object_id=OBJECT_ID(N'dbo.PayrollPeriodEmployeeCurrentSnapshots')
              AND delete_referential_action=0;
            """));
        Assert.Equal(Current, await ScalarAsync<string>(db, """
            SELECT TOP(1) MigrationId FROM dbo.__EFMigrationsHistory
            ORDER BY MigrationId DESC;
            """));
    }

    private static async Task<T> ScalarAsync<T>(DbContext db, string sql)
    {
        await db.Database.OpenConnectionAsync();
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = sql;
        var value = await command.ExecuteScalarAsync();
        if (value is T typed) return typed;
        return (T)Convert.ChangeType(value!, typeof(T),
            System.Globalization.CultureInfo.InvariantCulture);
    }
}
