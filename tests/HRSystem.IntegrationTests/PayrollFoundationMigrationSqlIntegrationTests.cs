using System.Data.Common;
using HRSystem.Application.Abstractions.Security;
using HRSystem.Application.Common.Exceptions;
using HRSystem.Application.Payroll;
using HRSystem.Application.Security;
using HRSystem.Domain.MasterData;
using HRSystem.Domain.Payroll;
using HRSystem.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace HRSystem.IntegrationTests;

[Collection(DisposableSqlServerCollection.Name)]
public sealed class PayrollFoundationMigrationSqlIntegrationTests
{
    private const string Previous = "20260825081710_AddFlexiblePayrollFoundation";
    private const string Current = "20260826005335_AddPayrollFixedEarningsProration";
    private const string P3 = "20260826023624_AddPayrollAttendanceAllowanceAndLeaveDeduction";
    private const string P4 = "20260826135325_AddPayrollOvertimePayCalculation";
    private const string P5A = "20260827004620_AddPayrollLaborInsuranceCalculation";
    private const string P5B = "20260827065054_AddPayrollHealthInsuranceCalculation";
    private const string P6 = "20260827131618_AddPayrollTotalsCalculation";
    private const string Approval = "20260828032342_AddApprovalWorkflowFoundation";
    private const string Revision = "20260828042718_AddPayrollCalculationRevisions";
    private const string Finalization = "20260828054445_AddPayrollFinalization";
    private const string PayCycles = "20260828093258_AddPayrollPayCycles";
    private const string LegacyAdjustments =
        "20260830095141_AddPayrollLegacyAdjustmentComponents";
    private const string OwnerLinePairing =
        "20260830141851_AddOwnerPrivateLinePairing";
    private const string PeriodicAccrual =
        "20260831020315_AddPeriodicAccruedPayAndOccupationalInsuranceSalary";
    private const string CompTime = "20260831040212_AddCompTimeLedger";
    private const string IndependentOccupational =
        "20260831142119_AddIndependentOccupationalInsuranceEnrollment";
    private const string NonWorkingPunch =
        "20260903142616_AddNonWorkingDayPunchReviewSupport";
    private const string TrainingCompTime = "20260930061925_AddHolidayTrainingCompTime";

    [Fact]
    public async Task Migration_Up_Down_Up_Is_Repeatable_And_Model_Is_Current()
    {
        await using var database = await DisposableSqlServerDatabase.CreateAsync(
            "PayrollFoundationUpDownUp", applyMigrations: false);
        await using var db = database.CreateDbContext();
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(Previous);
        Assert.True(await Exists(db, "PayrollPeriods"));
        Assert.Equal(0, await Scalar<int>(db, "SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.PayrollPlanComponents') AND name=N'ProrationKind';"));
        await migrator.MigrateAsync(Current); await AssertSchema(db);
        await migrator.MigrateAsync(Previous);
        Assert.True(await Exists(db, "PayrollPeriods"));
        Assert.Equal(0, await Scalar<int>(db, "SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.PayrollPlanComponents') AND name=N'ProrationKind';"));
        await migrator.MigrateAsync(Current); await AssertSchema(db);
        Assert.Equal(15, await Scalar<int>(db, "SELECT COUNT(*) FROM dbo.PayrollComponentDefinitions;"));
        Assert.Equal(2, await Scalar<int>(db, "SELECT COUNT(*) FROM dbo.PayrollPlans;"));
        Assert.Equal(5, await Scalar<int>(db, "SELECT COUNT(*) FROM dbo.PayrollSeniorityTiers;"));
        Assert.Equal([P3, P4, P5A, P5B, P6, Approval, Revision, Finalization,
                PayCycles, LegacyAdjustments, OwnerLinePairing, PeriodicAccrual,
                CompTime, IndependentOccupational, NonWorkingPunch, TrainingCompTime],
            await db.Database.GetPendingMigrationsAsync());
    }

    [Fact]
    public async Task Migration_Seeds_Settings_But_No_Employee_Assignment_Or_Operational_Data()
    {
        await using var database = await DisposableSqlServerDatabase.CreateAsync("PayrollFoundationSeed");
        await using var db = database.CreateDbContext();
        Assert.Equal(18, await Scalar<int>(db, "SELECT COUNT(*) FROM dbo.PayrollComponentDefinitions;"));
        Assert.Equal(3, await Scalar<int>(db, "SELECT COUNT(*) FROM dbo.PayrollPlans;"));
        Assert.Equal(5, await Scalar<int>(db, "SELECT COUNT(*) FROM dbo.PayrollSeniorityTiers;"));
        Assert.Equal(0, await Scalar<int>(db, "SELECT COUNT(*) FROM dbo.EmployeePayrollAssignments;"));
        Assert.Equal(0, await Scalar<int>(db, "SELECT COUNT(*) FROM dbo.PayrollPeriods;"));
        Assert.Equal(0, await Scalar<int>(db, "SELECT COUNT(*) FROM dbo.PayrollRuns;"));
        Assert.Equal(0, await Scalar<int>(db, "SELECT COUNT(*) FROM dbo.PayrollEmployeeSnapshots;"));
    }

    [Fact]
    public async Task Unique_Constraints_Protect_Period_Run_And_Snapshot_Identity()
    {
        await using var database = await DisposableSqlServerDatabase.CreateAsync("PayrollFoundationIndexes");
        await using var db = database.CreateDbContext();
        Assert.Equal(6, await Scalar<int>(db, """
            SELECT COUNT(*) FROM sys.indexes WHERE is_unique=1 AND name IN
            (N'UX_PayrollComponentDefinitions_Code',N'UX_PayrollPlans_Code',
             N'UX_PayrollPeriods_Year_Month',N'UX_PayrollRuns_Period_Revision',
             N'UX_PayrollEmployeeSnapshots_Run_Employee',
             N'UX_PayrollCurrentSnapshots_Snapshot');
            """));
        Assert.Equal(16, await Scalar<int>(db, """
            SELECT COUNT(*) FROM sys.foreign_keys
            WHERE parent_object_id IN
            (OBJECT_ID(N'dbo.EmployeePayrollAssignments'),OBJECT_ID(N'dbo.EmployeePayrollComponentOverrides'),
             OBJECT_ID(N'dbo.PayrollAdjustments'),OBJECT_ID(N'dbo.PayrollRuns'),
             OBJECT_ID(N'dbo.PayrollPlanComponents'),OBJECT_ID(N'dbo.PayrollSeniorityTiers'),
             OBJECT_ID(N'dbo.PayrollEmployeeSnapshots'),OBJECT_ID(N'dbo.PayrollEmployeeSnapshotComponents'))
              AND delete_referential_action=0;
            """));
    }

    [Fact]
    public async Task Schema_Has_Checks_RowVersions_Money_Precision_And_Effective_Indexes()
    {
        await using var database = await DisposableSqlServerDatabase.CreateAsync("PayrollFoundationMetadata");
        await using var db = database.CreateDbContext();
        Assert.Equal(14, await Scalar<int>(db, """
            SELECT COUNT(*) FROM sys.check_constraints WHERE name IN
            (N'CK_PayrollComponentDefinitions_EffectiveRange',N'CK_PayrollComponentDefinitions_SortOrder',
             N'CK_PayrollPlans_EffectiveRange',N'CK_PayrollPlanComponents_Amount',
             N'CK_PayrollPlanComponents_EffectiveRange',N'CK_PayrollSeniorityTiers_Range',
             N'CK_PayrollSeniorityTiers_Amount',N'CK_EmployeePayrollAssignments_EffectiveRange',
             N'CK_EmployeePayrollComponentOverrides_Amount',N'CK_EmployeePayrollComponentOverrides_EffectiveRange',
             N'CK_EmployeePayrollComponentOverrides_ModeAmount',N'CK_PayrollPeriods_Month',
             N'CK_PayrollPeriods_Dates',N'CK_PayrollAdjustments_Amount');
            """));
        Assert.Equal(9, await Scalar<int>(db, """
            SELECT COUNT(*) FROM sys.columns WHERE system_type_id=189 AND name=N'RowVersion'
              AND object_id IN
              (OBJECT_ID(N'dbo.PayrollComponentDefinitions'),OBJECT_ID(N'dbo.PayrollPlans'),
               OBJECT_ID(N'dbo.PayrollPlanComponents'),OBJECT_ID(N'dbo.EmployeePayrollAssignments'),
               OBJECT_ID(N'dbo.EmployeePayrollComponentOverrides'),OBJECT_ID(N'dbo.PayrollAdjustments'),
               OBJECT_ID(N'dbo.PayrollPeriods'),OBJECT_ID(N'dbo.PayrollRuns'),
               OBJECT_ID(N'dbo.PayrollEmployeeSnapshots'));
            """));
        Assert.Equal(8, await Scalar<int>(db, """
            SELECT COUNT(*) FROM sys.columns WHERE precision=18 AND scale=2
              AND object_id IN
              (OBJECT_ID(N'dbo.PayrollPlanComponents'),OBJECT_ID(N'dbo.PayrollSeniorityTiers'),
               OBJECT_ID(N'dbo.EmployeePayrollComponentOverrides'),OBJECT_ID(N'dbo.PayrollAdjustments'),
               OBJECT_ID(N'dbo.PayrollEmployeeSnapshotComponents'));
            """));
        Assert.Equal(2, await Scalar<int>(db, """
            SELECT COUNT(*) FROM sys.indexes WHERE name IN
            (N'IX_EmployeePayrollAssignments_EffectiveLookup',N'IX_EmployeePayrollOverrides_EffectiveLookup');
            """));
    }

    [Fact]
    public async Task Sql_Proration_Persists_Approved_Regression_And_Typed_Pending_Status()
    {
        await using var database = await DisposableSqlServerDatabase.CreateAsync("PayrollP2Proration");
        await using var db = database.CreateDbContext();
        var now = new DateTimeOffset(2026, 8, 25, 1, 0, 0, TimeSpan.Zero);
        var department = new Department(Guid.NewGuid(), $"P{Guid.NewGuid():N}"[..10],
            "SQL P2 測試部", now);
        var employee = new Employee(Guid.NewGuid(), $"P{Guid.NewGuid():N}"[..12],
            "SQL P2 測試員工", department.Id, new DateOnly(2026, 7, 13), now);
        var fullMonthEmployee = new Employee(Guid.NewGuid(), $"P{Guid.NewGuid():N}"[..12],
            "SQL P2 完整月測試員工", department.Id, new DateOnly(2020, 1, 1), now);
        db.Departments.Add(department);
        db.Employees.AddRange(employee, fullMonthEmployee);
        await db.SaveChangesAsync();
        var service = CreateService(db);
        var plan = await db.PayrollPlans.SingleAsync(x => x.Code == "STANDARD_MONTHLY");
        await service.AssignPlanAsync(new()
        {
            EmployeeId = employee.Id,
            PayrollPlanId = plan.Id,
            EffectiveFrom = new DateOnly(2026, 1, 1)
        });
        await service.AssignPlanAsync(new()
        {
            EmployeeId = fullMonthEmployee.Id,
            PayrollPlanId = plan.Id,
            EffectiveFrom = new DateOnly(2026, 1, 1)
        });
        foreach (var (code, amount) in new[]
                 {
                     ("PERFORMANCE", 4200m),
                     ("JOB_ALLOWANCE", 3000m),
                     ("CERTIFICATE_ALLOWANCE", 2500m)
                 })
        {
            var definition = await db.PayrollComponentDefinitions.SingleAsync(x => x.Code == code);
            await service.CreateOverrideAsync(new()
            {
                EmployeeId = employee.Id,
                ComponentDefinitionId = definition.Id,
                Mode = PayrollOverrideMode.Replace,
                Amount = amount,
                EffectiveFrom = new DateOnly(2026, 7, 1)
            });
        }

        var preview = await service.PreviewFixedEarningsAsync(employee.Id, 2026, 7);
        Assert.Equal(18683, Assert.Single(preview.Components,
            x => x.Code == "BASE_SALARY").ResolvedAmount);
        Assert.Equal(1267, Assert.Single(preview.Components,
            x => x.Code == "MEAL_ALLOWANCE").ResolvedAmount);
        foreach (var code in new[] { "PERFORMANCE", "JOB_ALLOWANCE", "CERTIFICATE_ALLOWANCE" })
        {
            var pending = Assert.Single(preview.Components, x => x.Code == code);
            Assert.Equal(PayrollCalculationStatus.PolicyPending, pending.CalculationStatus);
            Assert.NotNull(pending.FullMonthlyAmount);
            Assert.Null(pending.ResolvedAmount);
        }

        var runId = await service.CreateDraftAsync(await service.CreatePeriodAsync(2026, 7));
        var persisted = await db.PayrollEmployeeSnapshotComponents.AsNoTracking()
            .Where(x => x.PayrollEmployeeSnapshot.PayrollRunId == runId)
            .ToListAsync();
        var partialSnapshotId = await db.PayrollEmployeeSnapshots.AsNoTracking()
            .Where(x => x.PayrollRunId == runId && x.EmployeeId == employee.Id)
            .Select(x => x.Id).SingleAsync();
        var salary = Assert.Single(persisted, x =>
            x.PayrollEmployeeSnapshotId == partialSnapshotId &&
            x.ComponentCode == "BASE_SALARY");
        Assert.Equal(PayrollProrationKind.Monthly30Day, salary.ProrationKind);
        Assert.Equal(19, salary.PayableDays);
        Assert.Equal(18683.333333m, salary.RawProratedAmount);
        Assert.Equal(18683, salary.ResolvedAmount);

        foreach (var code in new[] { "PERFORMANCE", "JOB_ALLOWANCE", "CERTIFICATE_ALLOWANCE" })
        {
            var pending = Assert.Single(persisted,
                x => x.PayrollEmployeeSnapshotId == partialSnapshotId && x.ComponentCode == code);
            Assert.Equal(PayrollCalculationStatus.PolicyPending, pending.CalculationStatus);
            Assert.NotNull(pending.FullMonthlyAmount);
            Assert.Null(pending.ResolvedAmount);
        }
        var laborInsurance = Assert.Single(persisted,
            x => x.PayrollEmployeeSnapshotId == partialSnapshotId &&
                x.ComponentCode == "LABOR_INSURANCE");
        Assert.Equal(PayrollCalculationStatus.NeedsSetup,
            laborInsurance.CalculationStatus);
        Assert.Null(laborInsurance.ResolvedAmount);
        var healthInsurance = Assert.Single(persisted,
            x => x.PayrollEmployeeSnapshotId == partialSnapshotId &&
                x.ComponentCode == "HEALTH_INSURANCE");
        Assert.Equal(PayrollCalculationStatus.NeedsSetup,
            healthInsurance.CalculationStatus);
        Assert.Null(healthInsurance.ResolvedAmount);
        foreach (var code in new[]
                 {
                     "OVERTIME_FIRST_2H", "OVERTIME_AFTER_2H",
                     "OVERTIME_AFTER_8H"
                 })
        {
            var resolved = Assert.Single(persisted,
                x => x.PayrollEmployeeSnapshotId == partialSnapshotId && x.ComponentCode == code);
            Assert.Equal(PayrollCalculationStatus.Resolved,
                resolved.CalculationStatus);
            Assert.Equal(0m, resolved.ResolvedAmount);
        }
        foreach (var code in new[] { "ATTENDANCE_ALLOWANCE", "LEAVE_DEDUCTION" })
        {
            var needsReview = Assert.Single(persisted,
                x => x.PayrollEmployeeSnapshotId == partialSnapshotId && x.ComponentCode == code);
            Assert.Equal(PayrollCalculationStatus.NeedsReview,
                needsReview.CalculationStatus);
            Assert.Null(needsReview.ResolvedAmount);
        }

        var fullSnapshotId = await db.PayrollEmployeeSnapshots.AsNoTracking()
            .Where(x => x.PayrollRunId == runId && x.EmployeeId == fullMonthEmployee.Id)
            .Select(x => x.Id).SingleAsync();
        var fullSalary = Assert.Single(persisted,
            x => x.PayrollEmployeeSnapshotId == fullSnapshotId && x.ComponentCode == "BASE_SALARY");
        Assert.Equal(31, fullSalary.PayableDays);
        Assert.Equal(1m, fullSalary.ProrationFactor);
        Assert.Equal(29500m, fullSalary.RawProratedAmount);
        Assert.Equal(29500m, fullSalary.ResolvedAmount);
    }

    [Fact]
    public async Task P3_Migration_23_To_24_Down_And_ReUp_Is_Repeatable()
    {
        await using var database = await DisposableSqlServerDatabase.CreateAsync(
            "PayrollP3UpDownUp", applyMigrations: false);
        await using var db = database.CreateDbContext();
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(Current);
        Assert.False(await Exists(db, "PayrollLeaveDeductionPolicies"));
        await migrator.MigrateAsync(P3);
        await AssertP3Schema(db);
        await migrator.MigrateAsync(Current);
        Assert.False(await Exists(db, "PayrollLeaveDeductionPolicies"));
        Assert.False(await Exists(db, "PayrollAttendanceAllowanceEvidence"));
        await migrator.MigrateAsync(P3);
        await AssertP3Schema(db);
        Assert.Equal([P4, P5A, P5B, P6, Approval, Revision, Finalization,
                PayCycles, LegacyAdjustments, OwnerLinePairing, PeriodicAccrual,
                CompTime, IndependentOccupational, NonWorkingPunch, TrainingCompTime],
            await db.Database.GetPendingMigrationsAsync());
    }

    [Fact]
    public async Task P4_Migration_24_To_25_Down_And_ReUp_Is_Repeatable()
    {
        await using var database = await DisposableSqlServerDatabase.CreateAsync(
            "PayrollP4UpDownUp", applyMigrations: false);
        await using var db = database.CreateDbContext();
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(P3);
        var protectedSignature = await ProtectedSchemaSignature(db);
        Assert.False(await Exists(db, "OvertimePayRatePolicies"));
        await migrator.MigrateAsync(P4);
        await AssertP4Schema(db);
        Assert.Equal(protectedSignature, await ProtectedSchemaSignature(db));
        await migrator.MigrateAsync(P3);
        Assert.False(await Exists(db, "PayrollOvertimePaySnapshots"));
        Assert.Equal(0, await Scalar<int>(db, """
            SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.PayrollComponentDefinitions')
              AND name=N'IncludeInOvertimeHourlyBase';
            """));
        await migrator.MigrateAsync(P4);
        await AssertP4Schema(db);
        Assert.Equal(protectedSignature, await ProtectedSchemaSignature(db));
        Assert.Equal([P5A, P5B, P6, Approval, Revision, Finalization, PayCycles,
                LegacyAdjustments, OwnerLinePairing, PeriodicAccrual,
                CompTime, IndependentOccupational, NonWorkingPunch, TrainingCompTime],
            await db.Database.GetPendingMigrationsAsync());
    }

    [Fact]
    public async Task P5A_Migration_25_To_26_Down_And_ReUp_Is_Repeatable()
    {
        await using var database = await DisposableSqlServerDatabase.CreateAsync(
            "PayrollP5AUpDownUp", applyMigrations: false);
        await using var db = database.CreateDbContext();
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(P4);
        var protectedSignature = await P5AProtectedSchemaSignature(db);
        Assert.False(await Exists(db, "EmployeeLaborInsuranceEnrollments"));
        await migrator.MigrateAsync(P5A);
        await AssertP5ASchema(db);
        Assert.Equal(protectedSignature, await P5AProtectedSchemaSignature(db));
        await migrator.MigrateAsync(P4);
        Assert.False(await Exists(db, "PayrollLaborInsuranceSnapshots"));
        Assert.False(await Exists(db, "LaborInsuranceRatePolicies"));
        await migrator.MigrateAsync(P5A);
        await AssertP5ASchema(db);
        Assert.Equal(protectedSignature, await P5AProtectedSchemaSignature(db));
        Assert.Equal([P5B, P6, Approval, Revision, Finalization, PayCycles,
                LegacyAdjustments, OwnerLinePairing, PeriodicAccrual,
                CompTime, IndependentOccupational, NonWorkingPunch, TrainingCompTime],
            await db.Database.GetPendingMigrationsAsync());
        Assert.False(db.Database.HasPendingModelChanges(),
            "The repository model must match the latest P5B snapshot even when P5B is pending.");
    }

    [Fact]
    public async Task P5B_Migration_26_To_27_Down_And_ReUp_Is_Repeatable()
    {
        await using var database = await DisposableSqlServerDatabase.CreateAsync(
            "PayrollP5BUpDownUp", applyMigrations: false);
        await using var db = database.CreateDbContext();
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(P5A);
        var protectedSignature = await P5BProtectedSchemaSignature(db);
        Assert.False(await Exists(db, "EmployeeHealthInsuranceEnrollments"));
        await migrator.MigrateAsync(P5B);
        await AssertP5BSchema(db);
        Assert.Equal(protectedSignature, await P5BProtectedSchemaSignature(db));
        await migrator.MigrateAsync(P5A);
        Assert.False(await Exists(db, "PayrollHealthInsuranceSnapshots"));
        Assert.False(await Exists(db, "HealthInsuranceRatePolicies"));
        await migrator.MigrateAsync(P5B);
        await AssertP5BSchema(db);
        Assert.Equal(protectedSignature, await P5BProtectedSchemaSignature(db));
        Assert.Equal([P6, Approval, Revision, Finalization, PayCycles,
                LegacyAdjustments, OwnerLinePairing, PeriodicAccrual,
                CompTime, IndependentOccupational, NonWorkingPunch, TrainingCompTime],
            await db.Database.GetPendingMigrationsAsync());
        Assert.False(db.Database.HasPendingModelChanges());
    }

    [Fact]
    public async Task P6_Migration_27_To_28_Defaults_Existing_Snapshot_And_Is_Repeatable()
    {
        await using var database = await DisposableSqlServerDatabase.CreateAsync(
            "PayrollP6UpDownUp", applyMigrations: false);
        await using var db = database.CreateDbContext();
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(P5B);
        var protectedSignature = await P6ProtectedSchemaSignature(db);
        var (employee, _) = await SeedEmployeesAsync(db, includeSecond: false);
        var periodId = Guid.NewGuid();
        var runId = Guid.NewGuid();
        var snapshotId = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT dbo.PayrollPeriods
                (Id, [Year], [Month], PeriodStart, PeriodEnd, [Status])
            VALUES ({periodId}, 2026, 8, {new DateOnly(2026, 8, 1)},
                {new DateOnly(2026, 8, 31)}, 2);
            INSERT dbo.PayrollRuns
                (Id, PayrollPeriodId, [Status], CreatedAtUtc, CreatedBy)
            VALUES ({runId}, {periodId}, 1, {new DateTimeOffset(2026, 8, 27, 0, 0, 0, TimeSpan.Zero)}, N'p6-migration');
            INSERT dbo.PayrollEmployeeSnapshots
                (Id, PayrollRunId, EmployeeId, EmployeeCode, EmployeeName,
                 DepartmentId, DepartmentName, EmploymentStart, EmploymentEnd,
                 PayrollPlanId, PayrollPlanCode, SnapshotAtUtc, SetupStatus)
            VALUES ({snapshotId}, {runId}, {employee.Id}, N'P6-OLD', N'P6 Existing',
                NULL, N'P6', {new DateOnly(2020, 1, 1)}, NULL, NULL, NULL,
                {new DateTimeOffset(2026, 8, 27, 0, 0, 0, TimeSpan.Zero)}, 1);
            """);

        await migrator.MigrateAsync(P6);
        await AssertP6Schema(db);
        Assert.Equal((int)PayrollCalculationStatus.NotCalculated,
            await Scalar<int>(db, $"SELECT TotalCalculationStatus FROM dbo.PayrollEmployeeSnapshots WHERE Id='{snapshotId:D}';"));
        Assert.Equal((int)PayrollCalculationStatus.NotCalculated,
            await Scalar<int>(db,
                $"SELECT TotalCalculationStatus FROM dbo.PayrollEmployeeSnapshots WHERE Id='{snapshotId:D}';"));
        Assert.Equal(0, await Scalar<int>(db,
            $"SELECT COUNT(TotalSourceFingerprint) FROM dbo.PayrollEmployeeSnapshots WHERE Id='{snapshotId:D}';"));
        Assert.Equal(0, await Scalar<int>(db,
            $"SELECT COUNT(NetPay) FROM dbo.PayrollEmployeeSnapshots WHERE Id='{snapshotId:D}';"));
        Assert.Equal(protectedSignature, await P6ProtectedSchemaSignature(db));

        await migrator.MigrateAsync(P5B);
        Assert.False(await Exists(db, "PayrollTotalBlockingEvidence"));
        Assert.Equal(0, await Scalar<int>(db, """
            SELECT COUNT(*) FROM sys.columns
            WHERE object_id=OBJECT_ID(N'dbo.PayrollEmployeeSnapshots')
              AND name IN (N'GrossPay',N'TotalDeductions',N'NetPay',N'TotalCalculationStatus',
                           N'TotalSourceFingerprint',N'TotalSourceFingerprintVersion',
                           N'BlockingComponentCount',N'TotalsCalculatedAtUtc');
            """));
        await migrator.MigrateAsync(P6);
        await AssertP6Schema(db);
        Assert.Equal(protectedSignature, await P6ProtectedSchemaSignature(db));
        Assert.Equal([Approval, Revision, Finalization, PayCycles,
                LegacyAdjustments, OwnerLinePairing, PeriodicAccrual,
                CompTime, IndependentOccupational, NonWorkingPunch, TrainingCompTime],
            await db.Database.GetPendingMigrationsAsync());
        Assert.False(db.Database.HasPendingModelChanges());
    }

    [Fact]
    public async Task Draft_For_Fifty_Employees_Uses_Bounded_Batch_Queries()
    {
        await using var database = await DisposableSqlServerDatabase.CreateAsync(
            "PayrollP4BoundedQueries");
        var counter = new PayrollCommandCounter();
        await using var db = database.CreateDbContext(counter);
        var now = new DateTimeOffset(2026, 8, 25, 1, 0, 0, TimeSpan.Zero);
        var department = new Department(Guid.NewGuid(), $"P{Guid.NewGuid():N}"[..10],
            "P4 批次查詢測試部", now);
        var plan = await db.PayrollPlans.SingleAsync(x => x.Code == "STANDARD_MONTHLY");
        var employees = Enumerable.Range(1, 50)
            .Select(index => new Employee(Guid.NewGuid(), $"P4{index:000}",
                $"P4 批次員工 {index:00}", department.Id,
                new DateOnly(2020, 1, 1), now))
            .ToArray();
        db.Departments.Add(department);
        db.Employees.AddRange(employees);
        db.EmployeePayrollAssignments.AddRange(employees.Select(employee =>
            new EmployeePayrollAssignment(Guid.NewGuid(), employee.Id, plan.Id,
                new DateOnly(2026, 1, 1))));
        await db.SaveChangesAsync();
        var service = CreateService(db);
        var periodId = await service.CreatePeriodAsync(2026, 7);
        counter.Clear();

        var runId = await service.CreateDraftAsync(periodId);

        var selectCount = counter.Commands.Count(command =>
            command.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase));
        Assert.True(selectCount <= 17,
            $"Expected bounded batch reads, but observed {selectCount} SELECT commands.");
        Assert.Equal(50, await db.PayrollEmployeeSnapshots.CountAsync(
            x => x.PayrollRunId == runId));
    }

    [Fact]
    public async Task Sql_Service_Enforces_Effective_Ranges_Adjustments_And_Partial_Missing_Setup()
    {
        await using var database = await DisposableSqlServerDatabase.CreateAsync("PayrollFoundationBehavior");
        await using var db = database.CreateDbContext();
        var (first, second) = await SeedEmployeesAsync(db);
        var service = CreateService(db);
        var plan = await db.PayrollPlans.SingleAsync(x => x.Code == "STANDARD_MONTHLY");
        await service.AssignPlanAsync(new() { EmployeeId = first.Id, PayrollPlanId = plan.Id,
            EffectiveFrom = new DateOnly(2026, 1, 1) });
        await Assert.ThrowsAsync<ApplicationValidationException>(() => service.AssignPlanAsync(new()
        { EmployeeId = first.Id, PayrollPlanId = plan.Id, EffectiveFrom = new DateOnly(2026, 8, 1) }));

        var performance = await db.PayrollComponentDefinitions.SingleAsync(x => x.Code == "PERFORMANCE");
        var overrideId = await service.CreateOverrideAsync(new() { EmployeeId = first.Id,
            ComponentDefinitionId = performance.Id, Mode = PayrollOverrideMode.Replace,
            Amount = 12000, EffectiveFrom = new DateOnly(2026, 8, 1) });
        Assert.Equal(12000, (await db.EmployeePayrollComponentOverrides.AsNoTracking()
            .SingleAsync(x => x.Id == overrideId)).OverrideAmount);

        var periodId = await service.CreatePeriodAsync(2026, 8);
        var caseBonus = await db.PayrollComponentDefinitions.SingleAsync(x => x.Code == "CASE_BONUS");
        await service.CreateAdjustmentAsync(new() { PayrollPeriodId = periodId, EmployeeId = first.Id,
            ComponentDefinitionId = caseBonus.Id, Amount = 2300,
            Direction = PayrollAdjustmentDirection.Earning, Reason = "SQL Gate" });
        var baseSalary = await db.PayrollComponentDefinitions.SingleAsync(x => x.Code == "BASE_SALARY");
        await Assert.ThrowsAsync<ApplicationValidationException>(() => service.CreateAdjustmentAsync(new()
        { PayrollPeriodId = periodId, EmployeeId = first.Id, ComponentDefinitionId = baseSalary.Id,
            Amount = 1, Direction = PayrollAdjustmentDirection.Earning, Reason = "invalid" }));

        var initial = await service.CreateInitialDraftAsync(periodId);
        Assert.NotNull(initial.RunId);
        Assert.Equal(2, initial.NeedsSetup);
        Assert.NotNull(Assert.Single(initial.Items, x => x.EmployeeId == first.Id).SnapshotId);
        Assert.Null(Assert.Single(initial.Items, x => x.EmployeeId == second.Id).SnapshotId);
        Assert.Single(await db.PayrollRuns.AsNoTracking().ToListAsync());
        Assert.Single(await db.PayrollEmployeeSnapshots.AsNoTracking().ToListAsync());
        Assert.Equal(PayrollPeriodStatus.DraftCreated,
            (await db.PayrollPeriods.AsNoTracking().SingleAsync(x => x.Id == periodId)).Status);

        await service.AssignPlanAsync(new() { EmployeeId = second.Id, PayrollPlanId = plan.Id,
            EffectiveFrom = new DateOnly(2026, 1, 1) });
        var recalculated = await service.RecalculateEmployeeAsync(periodId, second.Id);
        Assert.NotNull(recalculated.RunId);
        Assert.Equal(2, await db.PayrollEmployeeSnapshots.AsNoTracking()
            .CountAsync(x => x.PayrollPeriodId == periodId));
        Assert.Equal(2, await db.PayrollPeriodEmployeeCurrentSnapshots.AsNoTracking()
            .CountAsync(x => x.PayrollPeriodId == periodId));
        Assert.Contains(await db.PayrollEmployeeSnapshotComponents.AsNoTracking().ToListAsync(),
            x => x.SourceType == PayrollSnapshotSourceType.ManualAdjustment && x.ResolvedAmount == 2300);
    }

    [Fact]
    public async Task Concurrent_Employee_Recalculation_Leaves_One_Unambiguous_Current_Pointer()
    {
        await using var database = await DisposableSqlServerDatabase.CreateAsync(
            "PayrollRevisionConcurrency");
        Guid periodId;
        Guid employeeId;
        await using (var setupDb = database.CreateDbContext())
        {
            var (employee, _) = await SeedEmployeesAsync(setupDb, includeSecond: false);
            employeeId = employee.Id;
            var plan = await setupDb.PayrollPlans.SingleAsync(x =>
                x.Code == "STANDARD_MONTHLY");
            var setupService = CreateService(setupDb);
            await setupService.AssignPlanAsync(new()
            {
                EmployeeId = employee.Id,
                PayrollPlanId = plan.Id,
                EffectiveFrom = new DateOnly(2026, 1, 1)
            });
            periodId = await setupService.CreatePeriodAsync(2026, 8);
            await setupService.CreateInitialDraftAsync(periodId);
        }

        await using var firstDb = database.CreateDbContext();
        await using var secondDb = database.CreateDbContext();
        var start = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var first = Attempt(CreateService(firstDb));
        var second = Attempt(CreateService(secondDb));
        start.SetResult(true);
        var attempts = await Task.WhenAll(first, second);

        Assert.Contains(attempts, x => x.Succeeded);
        Assert.All(attempts.Where(x => !x.Succeeded), x => Assert.NotNull(x.Error));
        await using var verifyDb = database.CreateDbContext();
        var runs = await verifyDb.PayrollRuns.AsNoTracking()
            .Where(x => x.PayrollPeriodId == periodId)
            .OrderBy(x => x.RevisionNumber).ToListAsync();
        Assert.Equal(runs.Count, runs.Select(x => x.RevisionNumber).Distinct().Count());
        var pointer = await verifyDb.PayrollPeriodEmployeeCurrentSnapshots
            .AsNoTracking().SingleAsync(x => x.PayrollPeriodId == periodId &&
                x.EmployeeId == employeeId);
        Assert.True(await verifyDb.PayrollEmployeeSnapshots.AsNoTracking().AnyAsync(x =>
            x.Id == pointer.PayrollEmployeeSnapshotId &&
            x.PayrollPeriodId == periodId && x.EmployeeId == employeeId));

        async Task<(bool Succeeded, Exception? Error)> Attempt(IPayrollService service)
        {
            await start.Task;
            try
            {
                await service.RecalculateEmployeeAsync(periodId, employeeId);
                return (true, null);
            }
            catch (Exception ex)
            {
                return (false, ex);
            }
        }
    }

    [Fact]
    public async Task Sql_Unique_Check_And_RowVersion_Constraints_Are_Enforced()
    {
        await using var database = await DisposableSqlServerDatabase.CreateAsync("PayrollFoundationConstraints");
        Guid employeeId;
        Guid periodId;
        Guid runId;
        Guid adjustmentId;
        await using (var setup = database.CreateDbContext())
        {
            var (employee, _) = await SeedEmployeesAsync(setup, includeSecond: false);
            employeeId = employee.Id;
            var service = CreateService(setup);
            var plan = await setup.PayrollPlans.SingleAsync(x => x.Code == "STANDARD_MONTHLY");
            await service.AssignPlanAsync(new() { EmployeeId = employeeId, PayrollPlanId = plan.Id,
                EffectiveFrom = new DateOnly(2026, 1, 1) });
            periodId = await service.CreatePeriodAsync(2026, 8);
            var component = await setup.PayrollComponentDefinitions.SingleAsync(x => x.Code == "CASE_BONUS");
            adjustmentId = await service.CreateAdjustmentAsync(new() { PayrollPeriodId = periodId,
                EmployeeId = employeeId, ComponentDefinitionId = component.Id, Amount = 100,
                Direction = PayrollAdjustmentDirection.Earning, Reason = "constraint" });
            runId = await service.CreateDraftAsync(periodId);
        }

        await using (var duplicatePeriod = database.CreateDbContext())
        {
            duplicatePeriod.PayrollPeriods.Add(new PayrollPeriod(Guid.NewGuid(), 2026, 8));
            await Assert.ThrowsAsync<DbUpdateException>(() => duplicatePeriod.SaveChangesAsync());
        }
        await using (var duplicateRun = database.CreateDbContext())
        {
            duplicateRun.PayrollRuns.Add(new PayrollRun(Guid.NewGuid(), periodId, 1,
                PayrollRunTrigger.InitialBatch, "gate", DateTimeOffset.UtcNow));
            await Assert.ThrowsAsync<DbUpdateException>(() => duplicateRun.SaveChangesAsync());
        }
        await using (var duplicateSnapshot = database.CreateDbContext())
        {
            duplicateSnapshot.PayrollEmployeeSnapshots.Add(new PayrollEmployeeSnapshot(Guid.NewGuid(), periodId, runId,
                employeeId, "PAY001", "SQL 薪資測試員工", null, "SQL 薪資測試部",
                new DateOnly(2020, 1, 1), null, null, null, DateTimeOffset.UtcNow,
                PayrollEmployeeSetupStatus.Ready));
            await Assert.ThrowsAsync<DbUpdateException>(() => duplicateSnapshot.SaveChangesAsync());
        }
        await using (var invalidAmount = database.CreateDbContext())
        {
            await Assert.ThrowsAsync<SqlException>(() => invalidAmount.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE dbo.PayrollAdjustments SET Amount={0m} WHERE Id={adjustmentId}"));
        }

        await using var firstWriter = database.CreateDbContext();
        await using var staleWriter = database.CreateDbContext();
        var current = await firstWriter.PayrollAdjustments.SingleAsync(x => x.Id == adjustmentId);
        var stale = await staleWriter.PayrollAdjustments.SingleAsync(x => x.Id == adjustmentId);
        current.Update(200, PayrollAdjustmentDirection.Earning, "first writer");
        await firstWriter.SaveChangesAsync();
        stale.Update(300, PayrollAdjustmentDirection.Earning, "stale writer");
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => staleWriter.SaveChangesAsync());
    }

    private static async Task AssertSchema(DbContext db)
    {
        var tables = new[] { "PayrollComponentDefinitions", "PayrollPlans", "PayrollPlanComponents",
            "PayrollSeniorityTiers", "EmployeePayrollAssignments", "EmployeePayrollComponentOverrides",
            "PayrollAdjustments", "PayrollPeriods", "PayrollRuns", "PayrollEmployeeSnapshots",
            "PayrollEmployeeSnapshotComponents" };
        foreach (var table in tables) Assert.True(await Exists(db, table), table);
        Assert.Equal(9, await Scalar<int>(db, """
            SELECT COUNT(*) FROM sys.columns WHERE system_type_id=189 AND object_id IN
            (OBJECT_ID(N'dbo.PayrollComponentDefinitions'),OBJECT_ID(N'dbo.PayrollPlans'),
             OBJECT_ID(N'dbo.PayrollPlanComponents'),OBJECT_ID(N'dbo.EmployeePayrollAssignments'),
             OBJECT_ID(N'dbo.EmployeePayrollComponentOverrides'),OBJECT_ID(N'dbo.PayrollAdjustments'),
             OBJECT_ID(N'dbo.PayrollPeriods'),OBJECT_ID(N'dbo.PayrollRuns'),
             OBJECT_ID(N'dbo.PayrollEmployeeSnapshots'));
            """));
        Assert.Equal(1, await Scalar<int>(db, """
            SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.PayrollPlanComponents')
              AND name=N'ProrationKind' AND system_type_id=52 AND is_nullable=0;
            """));
        Assert.Equal(5, await Scalar<int>(db, """
            SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.PayrollEmployeeSnapshotComponents')
              AND name IN (N'ProrationKind',N'FullMonthlyAmount',N'PayableDays',N'ProrationFactor',N'RawProratedAmount');
            """));
        Assert.Equal(4, await Scalar<int>(db, """
            SELECT COUNT(*) FROM sys.check_constraints WHERE name IN
              (N'CK_PayrollPlanComponents_ProrationKind',N'CK_PayrollEmployeeSnapshotComponents_ProrationKind',
               N'CK_PayrollEmployeeSnapshotComponents_PayableDays',N'CK_PayrollEmployeeSnapshotComponents_ProrationFactor');
            """));
    }

    private static async Task AssertP3Schema(DbContext db)
    {
        foreach (var table in new[]
                 {
                     "PayrollAttendanceAllowanceSnapshots",
                     "PayrollAttendanceAllowanceEvidence",
                     "PayrollLeaveDeductionPolicies",
                     "PayrollLeaveDeductionSnapshots",
                     "PayrollLeaveDeductionEvidence"
                 })
            Assert.True(await Exists(db, table), table);
        Assert.Equal(2, await Scalar<int>(db,
            "SELECT COUNT(*) FROM dbo.PayrollLeaveDeductionPolicies WHERE LeaveTypeCode IN (N'PERSONAL',N'SICK');"));
        Assert.Equal(2, await Scalar<int>(db, """
            SELECT COUNT(*) FROM sys.columns
            WHERE name=N'SourceFingerprint' AND max_length=32
              AND object_id IN (OBJECT_ID(N'dbo.PayrollAttendanceAllowanceSnapshots'),
                                OBJECT_ID(N'dbo.PayrollLeaveDeductionSnapshots'));
            """));
        Assert.Equal(2, await Scalar<int>(db, """
            SELECT COUNT(*) FROM sys.indexes WHERE is_unique=1 AND name IN
              (N'UX_PayrollAttendanceAllowanceSnapshots_Component',
               N'UX_PayrollLeaveDeductionSnapshots_Component');
            """));
    }

    private static async Task AssertP4Schema(DbContext db)
    {
        foreach (var table in new[] { "OvertimePayRatePolicies",
                     "PayrollOvertimePaySnapshots", "PayrollOvertimeBaseComponentEvidence",
                     "PayrollOvertimeBucketEvidence", "PayrollOvertimeDayEvidence",
                     "PayrollOvertimeRecognitionEvidence" })
            Assert.True(await Exists(db, table), table);
        Assert.Equal(5, await Scalar<int>(db, """
            SELECT COUNT(*) FROM dbo.PayrollComponentDefinitions
            WHERE IncludeInOvertimeHourlyBase=1 AND Code IN
              (N'BASE_SALARY',N'PERFORMANCE',N'MEAL_ALLOWANCE',N'JOB_ALLOWANCE',N'ATTENDANCE_ALLOWANCE');
            """));
        Assert.Equal(0, await Scalar<int>(db, """
            SELECT COUNT(*) FROM dbo.PayrollComponentDefinitions
            WHERE IncludeInOvertimeHourlyBase=1 AND Code NOT IN
              (N'BASE_SALARY',N'PERFORMANCE',N'MEAL_ALLOWANCE',N'JOB_ALLOWANCE',N'ATTENDANCE_ALLOWANCE');
            """));
        Assert.Equal(3, await Scalar<int>(db, "SELECT COUNT(*) FROM dbo.OvertimePayRatePolicies;"));
        Assert.Equal(3, await Scalar<int>(db, """
            SELECT COUNT(*) FROM dbo.OvertimePayRatePolicies
            WHERE (Bucket=1 AND Multiplier=1.34) OR (Bucket=2 AND Multiplier=1.67)
               OR (Bucket=3 AND Multiplier=2.67);
            """));
        Assert.Equal(4, await Scalar<int>(db, """
            SELECT COUNT(*) FROM sys.columns WHERE
              (object_id=OBJECT_ID(N'dbo.PayrollOvertimePaySnapshots') AND name=N'HourlyBase' AND precision=18 AND scale=6) OR
              (object_id=OBJECT_ID(N'dbo.PayrollOvertimeBucketEvidence') AND name IN (N'RawPay') AND precision=18 AND scale=6) OR
              (object_id=OBJECT_ID(N'dbo.PayrollOvertimeBucketEvidence') AND name=N'Multiplier' AND precision=9 AND scale=4) OR
              (object_id=OBJECT_ID(N'dbo.OvertimePayRatePolicies') AND name=N'Multiplier' AND precision=9 AND scale=4);
            """));
        Assert.Equal(5, await Scalar<int>(db, """
            SELECT COUNT(*) FROM sys.indexes WHERE is_unique=1 AND name IN
              (N'UX_PayrollOvertimePaySnapshots_EmployeeSnapshot',
               N'UX_PayrollOvertimeBaseEvidence_Snapshot_Component',
               N'UX_PayrollOvertimeBucketEvidence_Snapshot_Bucket',
               N'UX_PayrollOvertimeDayEvidence_Snapshot_Date',
               N'UX_PayrollOvertimeRecognitionEvidence_Snapshot_Recognition');
            """));
    }

    private static async Task AssertP5ASchema(DbContext db)
    {
        foreach (var table in new[] { "EmployeeLaborInsuranceEnrollments",
                     "LaborInsuranceRatePolicies", "PayrollLaborInsuranceSnapshots",
                     "PayrollLaborInsuranceContributionEvidence" })
            Assert.True(await Exists(db, table), table);
        Assert.Equal(0, await Scalar<int>(db,
            "SELECT COUNT(*) FROM dbo.LaborInsuranceRatePolicies;"));
        Assert.Equal(1, await Scalar<int>(db, """
            SELECT COUNT(*) FROM sys.columns WHERE
              object_id=OBJECT_ID(N'dbo.PayrollLaborInsuranceSnapshots') AND
              name=N'SourceFingerprint' AND max_length=32;
            """));
        Assert.Equal(5, await Scalar<int>(db, """
            SELECT COUNT(*) FROM sys.indexes WHERE is_unique=1 AND name IN
              (N'UX_EmployeeLaborInsuranceEnrollments_Employee_From',
               N'UX_LaborInsuranceRatePolicies_Version_From',
               N'UX_PayrollLaborInsuranceSnapshots_Component',
               N'UX_PayrollLaborInsuranceEvidence_Snapshot_Kind')
              OR is_unique=0 AND name=N'IX_LaborInsuranceRatePolicies_EffectiveLookup';
            """));
        Assert.Equal(3, await Scalar<int>(db, """
            SELECT COUNT(*) FROM sys.foreign_keys WHERE delete_referential_action=0
              AND parent_object_id IN
              (OBJECT_ID(N'dbo.EmployeeLaborInsuranceEnrollments'),
               OBJECT_ID(N'dbo.PayrollLaborInsuranceSnapshots'),
               OBJECT_ID(N'dbo.PayrollLaborInsuranceContributionEvidence'));
            """));
        Assert.Equal(7, await Scalar<int>(db, """
            SELECT COUNT(*) FROM sys.columns WHERE
              (object_id=OBJECT_ID(N'dbo.EmployeeLaborInsuranceEnrollments') AND
               name=N'MonthlyInsuredSalary' AND precision=18 AND scale=2) OR
              (object_id=OBJECT_ID(N'dbo.LaborInsuranceRatePolicies') AND
               name IN (N'OrdinaryAccidentInsuranceRate',N'EmploymentInsuranceRate',N'EmployeeShareRate') AND precision=9 AND scale=8) OR
              (object_id=OBJECT_ID(N'dbo.PayrollLaborInsuranceContributionEvidence') AND
               name=N'RawEmployeeAmount' AND precision=18 AND scale=6) OR
              (object_id=OBJECT_ID(N'dbo.PayrollLaborInsuranceContributionEvidence') AND
               name=N'RoundedDisplayAmount' AND precision=18 AND scale=2) OR
              (object_id=OBJECT_ID(N'dbo.PayrollLaborInsuranceSnapshots') AND
               name=N'FinalEmployeeDeduction' AND precision=18 AND scale=2);
            """));
    }

    private static async Task AssertP5BSchema(DbContext db)
    {
        foreach (var table in new[] { "EmployeeHealthInsuranceEnrollments",
                     "HealthInsuranceRatePolicies", "PayrollHealthInsuranceSnapshots",
                     "PayrollHealthInsuranceEvidence" })
            Assert.True(await Exists(db, table), table);
        Assert.Equal(0, await Scalar<int>(db,
            "SELECT COUNT(*) FROM dbo.HealthInsuranceRatePolicies;"));
        Assert.Equal(1, await Scalar<int>(db, """
            SELECT COUNT(*) FROM sys.columns WHERE
              object_id=OBJECT_ID(N'dbo.PayrollHealthInsuranceSnapshots') AND
              name=N'SourceFingerprint' AND max_length=32;
            """));
        Assert.Equal(4, await Scalar<int>(db, """
            SELECT COUNT(*) FROM sys.indexes WHERE is_unique=1 AND name IN
              (N'UX_EmployeeHealthInsuranceEnrollments_Employee_From',
               N'UX_HealthInsuranceRatePolicies_Version_From',
               N'UX_PayrollHealthInsuranceSnapshots_Component',
               N'UX_PayrollHealthInsuranceEvidence_Snapshot');
            """));
        Assert.Equal(3, await Scalar<int>(db, """
            SELECT COUNT(*) FROM sys.foreign_keys WHERE delete_referential_action=0
              AND parent_object_id IN
              (OBJECT_ID(N'dbo.EmployeeHealthInsuranceEnrollments'),
               OBJECT_ID(N'dbo.PayrollHealthInsuranceSnapshots'),
               OBJECT_ID(N'dbo.PayrollHealthInsuranceEvidence'));
            """));
    }

    private static async Task AssertP6Schema(DbContext db)
    {
        Assert.True(await Exists(db, "PayrollTotalBlockingEvidence"));
        Assert.Equal(8, await Scalar<int>(db, """
            SELECT COUNT(*) FROM sys.columns
            WHERE object_id=OBJECT_ID(N'dbo.PayrollEmployeeSnapshots')
              AND name IN (N'GrossPay',N'TotalDeductions',N'NetPay',N'TotalCalculationStatus',
                           N'TotalSourceFingerprint',N'TotalSourceFingerprintVersion',
                           N'BlockingComponentCount',N'TotalsCalculatedAtUtc');
            """));
        Assert.Equal(1, await Scalar<int>(db, """
            SELECT COUNT(*) FROM sys.columns
            WHERE object_id=OBJECT_ID(N'dbo.PayrollEmployeeSnapshots')
              AND name=N'NetPay' AND is_nullable=1 AND precision=18 AND scale=2;
            """));
        Assert.Equal(1, await Scalar<int>(db, """
            SELECT COUNT(*) FROM sys.columns
            WHERE object_id=OBJECT_ID(N'dbo.PayrollEmployeeSnapshots')
              AND name=N'TotalSourceFingerprint' AND max_length=32;
            """));
        Assert.Equal(1, await Scalar<int>(db, """
            SELECT COUNT(*) FROM sys.indexes
            WHERE object_id=OBJECT_ID(N'dbo.PayrollTotalBlockingEvidence')
              AND name=N'UX_PayrollTotalBlockingEvidence_Snapshot_Code_Reason'
              AND is_unique=1;
            """));
        Assert.Equal(2, await Scalar<int>(db, """
            SELECT COUNT(*) FROM sys.foreign_keys
            WHERE parent_object_id=OBJECT_ID(N'dbo.PayrollTotalBlockingEvidence')
              AND delete_referential_action=0;
            """));
        Assert.Equal(2, await Scalar<int>(db, """
            SELECT COUNT(*) FROM sys.check_constraints
            WHERE parent_object_id=OBJECT_ID(N'dbo.PayrollEmployeeSnapshots')
              AND name IN (N'CK_PayrollEmployeeSnapshots_Totals',
                           N'CK_PayrollEmployeeSnapshots_TotalFingerprint');
            """));
    }

    private static Task<string> ProtectedSchemaSignature(DbContext db) => Scalar<string>(db, """
        SELECT CONVERT(varchar(64), HASHBYTES('SHA2_256', STRING_AGG(CONVERT(nvarchar(max),
          CONCAT(o.name,N'|',c.name,N'|',c.system_type_id,N'|',c.max_length,N'|',c.precision,N'|',c.scale,N'|',c.is_nullable)),N';')
          WITHIN GROUP (ORDER BY o.name,c.column_id)),2)
        FROM sys.objects o JOIN sys.columns c ON c.object_id=o.object_id
        WHERE o.name IN (N'OvertimeRequests',N'OvertimeRecognitions',N'DailyAttendanceResults',
                         N'AttendanceRawEvents',N'LeaveRequests');
        """);

    private static Task<string> P5AProtectedSchemaSignature(DbContext db) => Scalar<string>(db, """
        SELECT CONVERT(varchar(64), HASHBYTES('SHA2_256', STRING_AGG(CONVERT(nvarchar(max),
          CONCAT(o.name,N'|',c.name,N'|',c.system_type_id,N'|',c.max_length,N'|',c.precision,N'|',c.scale,N'|',c.is_nullable)),N';')
          WITHIN GROUP (ORDER BY o.name,c.column_id)),2)
        FROM sys.objects o JOIN sys.columns c ON c.object_id=o.object_id
        WHERE o.name IN (N'OvertimeRequests',N'OvertimeRecognitions',N'DailyAttendanceResults',
                         N'AttendanceRawEvents',N'LeaveRequests')
           OR ((o.name LIKE N'Payroll%' OR o.name LIKE N'EmployeePayroll%' OR
                o.name=N'OvertimePayRatePolicies') AND o.name NOT IN
               (N'PayrollLaborInsuranceSnapshots',
                N'PayrollLaborInsuranceContributionEvidence'));
        """);
    private static Task<string> P5BProtectedSchemaSignature(DbContext db) => Scalar<string>(db, """
        SELECT CONVERT(varchar(64), HASHBYTES('SHA2_256', STRING_AGG(CONVERT(nvarchar(max),
          CONCAT(o.name,N'|',c.name,N'|',c.system_type_id,N'|',c.max_length,N'|',c.precision,N'|',c.scale,N'|',c.is_nullable)),N';')
          WITHIN GROUP (ORDER BY o.name,c.column_id)),2)
        FROM sys.objects o JOIN sys.columns c ON c.object_id=o.object_id
        WHERE o.name IN (N'OvertimeRequests',N'OvertimeRecognitions',N'DailyAttendanceResults',
                         N'AttendanceRawEvents',N'LeaveRequests',
                         N'EmployeeLaborInsuranceEnrollments',N'LaborInsuranceRatePolicies',
                         N'PayrollLaborInsuranceSnapshots',N'PayrollLaborInsuranceContributionEvidence')
           OR ((o.name LIKE N'Payroll%' OR o.name LIKE N'EmployeePayroll%') AND o.name NOT IN
               (N'PayrollHealthInsuranceSnapshots',N'PayrollHealthInsuranceEvidence'));
        """);
    private static Task<string> P6ProtectedSchemaSignature(DbContext db) => Scalar<string>(db, """
        SELECT CONVERT(varchar(64), HASHBYTES('SHA2_256', STRING_AGG(CONVERT(nvarchar(max),
          CONCAT(o.name,N'|',c.name,N'|',c.system_type_id,N'|',c.max_length,N'|',c.precision,N'|',c.scale,N'|',c.is_nullable)),N';')
          WITHIN GROUP (ORDER BY o.name,c.column_id)),2)
        FROM sys.objects o JOIN sys.columns c ON c.object_id=o.object_id
        WHERE o.type=N'U'
          AND o.name NOT IN (N'PayrollEmployeeSnapshots',N'PayrollTotalBlockingEvidence',N'__EFMigrationsHistory');
        """);
    private static async Task<bool> Exists(DbContext db, string table) =>
        await Scalar<int>(db, $"SELECT CASE WHEN OBJECT_ID(N'dbo.{table}',N'U') IS NULL THEN 0 ELSE 1 END;") == 1;
    private static async Task<T> Scalar<T>(DbContext db, string sql)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open) await connection.OpenAsync();
        await using var command = connection.CreateCommand(); command.CommandText = sql;
        return (T)Convert.ChangeType(await command.ExecuteScalarAsync() ?? throw new InvalidOperationException(), typeof(T));
    }

    private static PayrollService CreateService(HRSystemDbContext db) =>
        new(db, new AdminUser(), new FixedTimeProvider(
            new DateTimeOffset(2026, 8, 25, 1, 0, 0, TimeSpan.Zero)));

    private static async Task<(Employee First, Employee Second)> SeedEmployeesAsync(
        HRSystemDbContext db, bool includeSecond = true)
    {
        var now = new DateTimeOffset(2026, 8, 25, 1, 0, 0, TimeSpan.Zero);
        var department = new Department(Guid.NewGuid(), $"P{Guid.NewGuid():N}"[..10],
            "SQL 薪資測試部", now);
        var first = new Employee(Guid.NewGuid(), $"P{Guid.NewGuid():N}"[..12],
            "SQL 薪資測試員工", department.Id, new DateOnly(2020, 1, 1), now);
        var second = new Employee(Guid.NewGuid(), $"P{Guid.NewGuid():N}"[..12],
            "SQL 薪資第二員工", department.Id, new DateOnly(2021, 1, 1), now);
        db.Departments.Add(department);
        db.Employees.Add(first);
        if (includeSecond) db.Employees.Add(second);
        await db.SaveChangesAsync();
        return (first, second);
    }

    private sealed class AdminUser : ICurrentUser
    {
        public string? UserId => "payroll-sql-gate";
        public Guid? EmployeeId => null;
        public string? DisplayName => "Payroll SQL Gate";
        public string? IpAddress => "127.0.0.1";
        public bool IsAuthenticated => true;
        public bool IsInRole(string role) => role == RoleNames.Admin;
        public bool HasPermission(string policy) => RolePermissions.HasPermission([RoleNames.Admin], policy);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class PayrollCommandCounter : DbCommandInterceptor
    {
        public List<string> Commands { get; } = [];
        public void Clear() => Commands.Clear();

        public override ValueTask<InterceptionResult<DbDataReader>>
            ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
                InterceptionResult<DbDataReader> result,
                CancellationToken cancellationToken = default)
        {
            Commands.Add(command.CommandText);
            return base.ReaderExecutingAsync(command, eventData, result,
                cancellationToken);
        }
    }
}
