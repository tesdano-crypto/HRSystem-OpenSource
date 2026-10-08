using HRSystem.Domain.MasterData;
using HRSystem.Domain.Payroll;
using HRSystem.Application.Abstractions.Security;
using HRSystem.Application.Common.Exceptions;
using HRSystem.Application.Payroll;
using HRSystem.Application.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace HRSystem.IntegrationTests;

[Collection(DisposableSqlServerCollection.Name)]
public sealed class OccupationalInsuranceMigrationSqlIntegrationTests
{
    private const string Previous = "20260831040212_AddCompTimeLedger";
    private const string Current =
        "20260831142119_AddIndependentOccupationalInsuranceEnrollment";

    [Fact]
    public async Task Application_Workflow_And_Cleanup_Query_Execute_On_Sql()
    {
        await using var database = await DisposableSqlServerDatabase.CreateAsync("OccupationalWorkflow");
        await using var db = database.CreateDbContext();
        var department = new Department(Guid.NewGuid(), "OISQL", "OISQL", DateTimeOffset.UtcNow);
        var employee = new Employee(Guid.NewGuid(), "OISQL001", "OISQL", department.Id,
            new DateOnly(2020, 1, 1), DateTimeOffset.UtcNow);
        db.AddRange(department, employee);
        await db.SaveChangesAsync();
        var service = new InsuranceManagementService(db, new AccountingUser(), TimeProvider.System);
        var request = new PreviewOccupationalInsuranceChangeRequest
        {
            EmployeeId = employee.Id, MonthlyInsuredSalary = 72800,
            CoverageFrom = new DateOnly(2026, 7, 13), Reason = "獨立災保測試"
        };
        var preview = await service.PreviewOccupationalAsync(request);
        Assert.True(preview.CanApply);
        await service.ApplyOccupationalAsync(request, preview.PreviewToken);
        await Assert.ThrowsAsync<ApplicationValidationException>(() =>
            service.ApplyOccupationalAsync(request, preview.PreviewToken));
        Assert.Equal(1, await db.EmployeeOccupationalInsuranceEnrollments.CountAsync());
        Assert.Equal(1, await db.AuditLogs.CountAsync());
        Assert.Empty(await db.EmployeeLaborInsuranceEnrollments.ToListAsync());
        var detail = await service.GetEmployeeAsync(employee.Id);
        Assert.Equal(72800, Assert.Single(detail.OccupationalHistory).MonthlyInsuredSalary);

        var labor = new PreviewLaborInsuranceChangeRequest
        {
            EmployeeId = employee.Id, MonthlyLaborInsuredSalary = 45800,
            CoverageFrom = new DateOnly(2026, 8, 31), Reason = "清理 query 測試"
        };
        var laborPreview = await service.PreviewLaborAsync(labor);
        var row = await service.ApplyLaborAsync(labor, laborPreview.PreviewToken);
        await service.DeactivateLaborEnrollmentAsync(row.EnrollmentId, "受控清理測試");
        db.ChangeTracker.Clear();
        Assert.False((await db.EmployeeLaborInsuranceEnrollments.SingleAsync()).IsActive);
        Assert.Equal(3, await db.AuditLogs.CountAsync());
        Assert.Empty(await db.PayrollEmployeeSnapshots.ToListAsync());
    }

    private sealed class AccountingUser : ICurrentUser
    {
        public string? UserId => "insurance-sql-test";
        public Guid? EmployeeId => null;
        public string? DisplayName => "Insurance test";
        public string? IpAddress => "127.0.0.1";
        public bool IsAuthenticated => true;
        public bool IsInRole(string role) => role == RoleNames.Accounting;
        public bool HasPermission(string policy) => RolePermissions.HasPermission([RoleNames.Accounting], policy);
    }

    [Fact]
    public async Task Migration_36_To_37_To_36_To_37_Preserves_Legacy_Value()
    {
        await using var database = await DisposableSqlServerDatabase.CreateAsync(
            "OccupationalInsuranceUpDownUp", applyMigrations: false);
        await using var db = database.CreateDbContext();
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(Previous);

        var now = DateTimeOffset.Parse("2026-08-31T14:30:00Z");
        var department = new Department(Guid.NewGuid(), "OI37", "OI37", now);
        var employee = new Employee(Guid.NewGuid(), "OI37001", "OI37",
            department.Id, new DateOnly(2020, 1, 1), now);
        var labor = new EmployeeLaborInsuranceEnrollment(Guid.NewGuid(),
            employee.Id, LaborInsuranceEnrollmentStatus.Enrolled, 45800,
            new DateOnly(2026, 8, 31));
        db.AddRange(department, employee, labor);
        await db.SaveChangesAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE dbo.EmployeeLaborInsuranceEnrollments
            SET MonthlyOccupationalInsuredSalary = 72800
            WHERE Id = {labor.Id};
            """);

        await migrator.MigrateAsync(Current);
        await AssertCurrentSchema(db, labor.Id);

        var enrollment = new EmployeeOccupationalInsuranceEnrollment(Guid.NewGuid(),
            employee.Id, OccupationalInsuranceEnrollmentStatus.Enrolled, 72800,
            new DateOnly(2026, 9, 1));
        db.EmployeeOccupationalInsuranceEnrollments.Add(enrollment);
        await db.SaveChangesAsync();
        Assert.Equal(8, enrollment.RowVersion.Length);
        await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(() =>
            db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE dbo.EmployeeOccupationalInsuranceEnrollments
                SET MonthlyInsuredSalary = NULL WHERE Id = {enrollment.Id};
                """));
        Assert.Equal(72800m, await db.EmployeeOccupationalInsuranceEnrollments
            .Where(x => x.Id == enrollment.Id).Select(x => x.MonthlyInsuredSalary)
            .SingleAsync());
        db.EmployeeOccupationalInsuranceEnrollments.Remove(enrollment);
        await db.SaveChangesAsync();

        await migrator.MigrateAsync(Previous);
        Assert.Equal(72800m, await Scalar<decimal>(db, $"""
            SELECT MonthlyOccupationalInsuredSalary
            FROM dbo.EmployeeLaborInsuranceEnrollments
            WHERE Id = '{labor.Id}';
            """));
        Assert.Equal(0, await Scalar<int>(db, """
            SELECT COUNT(*) FROM sys.tables
            WHERE name = N'EmployeeOccupationalInsuranceEnrollments';
            """));

        await migrator.MigrateAsync(Current);
        await AssertCurrentSchema(db, labor.Id);
    }

    private static async Task AssertCurrentSchema(DbContext db, Guid laborId)
    {
        Assert.Equal(1, await Scalar<int>(db, """
            SELECT COUNT(*) FROM sys.tables
            WHERE name = N'EmployeeOccupationalInsuranceEnrollments';
            """));
        Assert.Equal(72800m, await Scalar<decimal>(db, $"""
            SELECT LegacyMonthlyOccupationalInsuredSalary
            FROM dbo.EmployeeLaborInsuranceEnrollments
            WHERE Id = '{laborId}';
            """));
        Assert.Equal(0, await Scalar<int>(db, """
            SELECT COUNT(*) FROM dbo.EmployeeOccupationalInsuranceEnrollments;
            """));
        Assert.Equal(2, await Scalar<int>(db, """
            SELECT COUNT(*) FROM sys.indexes WHERE name IN
              (N'UX_EmployeeOccupationalInsuranceEnrollments_Employee_From',
               N'IX_EmployeeOccupationalInsuranceEnrollments_EffectiveLookup');
            """));
        Assert.Equal(1, await Scalar<int>(db, """
            SELECT COUNT(*) FROM sys.foreign_keys
            WHERE name = N'FK_EmployeeOccupationalInsuranceEnrollments_Employees_EmployeeId'
              AND delete_referential_action = 0;
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
