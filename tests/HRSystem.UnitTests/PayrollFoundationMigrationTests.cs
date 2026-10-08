using HRSystem.Infrastructure.Persistence.Migrations;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace HRSystem.UnitTests;

public sealed class PayrollFoundationMigrationTests
{
    private static IReadOnlyList<MigrationOperation> Operations()
    {
        var migration = new AddFlexiblePayrollFoundation();
        var builder = new MigrationBuilder("Microsoft.EntityFrameworkCore.SqlServer");
        typeof(AddFlexiblePayrollFoundation).GetMethod("Up",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(migration, [builder]);
        return builder.Operations;
    }

    private static IReadOnlyList<MigrationOperation> ProrationOperations()
    {
        var migration = new AddPayrollFixedEarningsProration();
        var builder = new MigrationBuilder("Microsoft.EntityFrameworkCore.SqlServer");
        typeof(AddPayrollFixedEarningsProration).GetMethod("Up",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(migration, [builder]);
        return builder.Operations;
    }

    private static IReadOnlyList<MigrationOperation> P3Operations()
    {
        var migration = new AddPayrollAttendanceAllowanceAndLeaveDeduction();
        var builder = new MigrationBuilder("Microsoft.EntityFrameworkCore.SqlServer");
        typeof(AddPayrollAttendanceAllowanceAndLeaveDeduction).GetMethod("Up",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(migration, [builder]);
        return builder.Operations;
    }

    private static IReadOnlyList<MigrationOperation> P4Operations()
    {
        var migration = new AddPayrollOvertimePayCalculation();
        var builder = new MigrationBuilder("Microsoft.EntityFrameworkCore.SqlServer");
        typeof(AddPayrollOvertimePayCalculation).GetMethod("Up",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(migration, [builder]);
        return builder.Operations;
    }

    private static IReadOnlyList<MigrationOperation> P5AOperations()
    {
        var migration = new AddPayrollLaborInsuranceCalculation();
        var builder = new MigrationBuilder("Microsoft.EntityFrameworkCore.SqlServer");
        typeof(AddPayrollLaborInsuranceCalculation).GetMethod("Up",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(migration, [builder]);
        return builder.Operations;
    }

    private static IReadOnlyList<MigrationOperation> P5BOperations()
    {
        var migration = new AddPayrollHealthInsuranceCalculation();
        var builder = new MigrationBuilder("Microsoft.EntityFrameworkCore.SqlServer");
        typeof(AddPayrollHealthInsuranceCalculation).GetMethod("Up",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(migration, [builder]);
        return builder.Operations;
    }

    private static IReadOnlyList<MigrationOperation> P6Operations()
    {
        var migration = new AddPayrollTotalsCalculation();
        var builder = new MigrationBuilder("Microsoft.EntityFrameworkCore.SqlServer");
        typeof(AddPayrollTotalsCalculation).GetMethod("Up",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(migration, [builder]);
        return builder.Operations;
    }

    private static IReadOnlyList<MigrationOperation> LegacyAdjustmentOperations()
    {
        var migration = new AddPayrollLegacyAdjustmentComponents();
        var builder = new MigrationBuilder("Microsoft.EntityFrameworkCore.SqlServer");
        typeof(AddPayrollLegacyAdjustmentComponents).GetMethod("Up",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(migration, [builder]);
        return builder.Operations;
    }

    [Fact]
    public void Migration_Creates_Only_Payroll_Tables()
    {
        var tables = Operations().OfType<CreateTableOperation>().Select(x => x.Name).OrderBy(x => x).ToArray();
        Assert.Equal(11, tables.Length);
        Assert.All(tables, name => Assert.True(name.StartsWith("Payroll", StringComparison.Ordinal) ||
            name.StartsWith("EmployeePayroll", StringComparison.Ordinal)));
    }

    [Fact]
    public void Migration_Seeds_Fifteen_Component_Definitions_And_No_Employee_Assignments()
    {
        var seed = Assert.Single(Operations().OfType<InsertDataOperation>(), x => x.Table == "PayrollComponentDefinitions");
        Assert.Equal(15, seed.Values.GetLength(0));
        Assert.DoesNotContain(Operations().OfType<InsertDataOperation>(), x => x.Table == "EmployeePayrollAssignments");
    }

    [Fact]
    public void Migration_Uses_NoAction_Foreign_Keys()
    {
        var foreignKeys = Operations().OfType<CreateTableOperation>().SelectMany(x => x.ForeignKeys).ToArray();
        Assert.NotEmpty(foreignKeys);
        Assert.All(foreignKeys, x => Assert.Equal(ReferentialAction.NoAction, x.OnDelete));
    }

    [Fact]
    public void Migration_Has_Required_Unique_Indexes()
    {
        var names = Operations().OfType<CreateIndexOperation>().Where(x => x.IsUnique).Select(x => x.Name).ToArray();
        Assert.Contains("UX_PayrollComponentDefinitions_Code", names);
        Assert.Contains("UX_PayrollPlans_Code", names);
        Assert.Contains("UX_PayrollPeriods_Year_Month", names);
        Assert.Contains("UX_PayrollRuns_Period", names);
        Assert.Contains("UX_PayrollEmployeeSnapshots_Run_Employee", names);
    }

    [Fact]
    public void All_Monetary_Columns_Use_Decimal_18_2()
    {
        var moneyNames = new HashSet<string>(StringComparer.Ordinal)
        { "DefaultAmount", "Amount", "OverrideAmount", "StandardAmount", "ResolvedAmount" };
        var columns = Operations().OfType<CreateTableOperation>().SelectMany(x => x.Columns)
            .Where(x => moneyNames.Contains(x.Name)).ToArray();
        Assert.NotEmpty(columns);
        Assert.All(columns, x => Assert.Equal("decimal(18,2)", x.ColumnType));
    }

    [Fact]
    public void Proration_Migration_Adds_Only_Expected_Metadata_Columns()
    {
        var columns = ProrationOperations().OfType<AddColumnOperation>()
            .Select(x => $"{x.Table}.{x.Name}").OrderBy(x => x).ToArray();
        Assert.Equal([
            "PayrollEmployeeSnapshotComponents.FullMonthlyAmount",
            "PayrollEmployeeSnapshotComponents.PayableDays",
            "PayrollEmployeeSnapshotComponents.ProrationFactor",
            "PayrollEmployeeSnapshotComponents.ProrationKind",
            "PayrollEmployeeSnapshotComponents.RawProratedAmount",
            "PayrollPlanComponents.ProrationKind"
        ], columns);
        Assert.Empty(ProrationOperations().OfType<CreateTableOperation>());
        Assert.Empty(ProrationOperations().OfType<DeleteDataOperation>());
        var added = ProrationOperations().OfType<AddColumnOperation>().ToArray();
        Assert.Equal("decimal(18,2)", Assert.Single(added,
            x => x.Name == "FullMonthlyAmount").ColumnType);
        Assert.All(added.Where(x => x.Name is "ProrationFactor" or "RawProratedAmount"),
            x => Assert.Equal("decimal(18,6)", x.ColumnType));
        Assert.All(added.Where(x => x.Name == "ProrationKind"),
            x => Assert.Equal("smallint", x.ColumnType));
    }

    [Fact]
    public void Proration_Migration_Updates_Only_Plan_Policy_Metadata()
    {
        var updates = ProrationOperations().OfType<UpdateDataOperation>().ToArray();
        Assert.Equal(4, updates.Length);
        Assert.All(updates, x => Assert.Equal("PayrollPlanComponents", x.Table));
        Assert.All(updates, x => Assert.Equal(["ProrationKind"], x.Columns));
        Assert.Contains(updates, x => Equals(x.Values[0, 0], (short)1));
        Assert.Contains(updates, x => Equals(x.Values[0, 0], (short)2));
    }


    [Fact]
    public void P3_Migration_Creates_Only_Payroll_Policy_And_Evidence_Tables()
    {
        var tables = P3Operations().OfType<CreateTableOperation>()
            .Select(x => x.Name).OrderBy(x => x).ToArray();
        Assert.Equal([
            "PayrollAttendanceAllowanceEvidence",
            "PayrollAttendanceAllowanceSnapshots",
            "PayrollLeaveDeductionEvidence",
            "PayrollLeaveDeductionPolicies",
            "PayrollLeaveDeductionSnapshots"
        ], tables);
        Assert.DoesNotContain(P3Operations(), x => x is AddColumnOperation);
        Assert.All(P3Operations().OfType<CreateTableOperation>()
            .SelectMany(x => x.ForeignKeys),
            x => Assert.Equal(ReferentialAction.NoAction, x.OnDelete));
    }

    [Fact]
    public void P3_Migration_Seeds_Only_Confirmed_Personal_And_Sick_Policies()
    {
        var seed = Assert.Single(P3Operations().OfType<InsertDataOperation>());
        Assert.Equal("PayrollLeaveDeductionPolicies", seed.Table);
        Assert.Equal(2, seed.Values.GetLength(0));
        var codeIndex = Array.IndexOf(seed.Columns, "LeaveTypeCode");
        Assert.Equal(["PERSONAL", "SICK"], Enumerable.Range(0, 2)
            .Select(row => Assert.IsType<string>(seed.Values[row, codeIndex]))
            .OrderBy(x => x).ToArray());
    }

    [Fact]
    public void P3_Migration_Uses_Binary32_Fingerprints_And_Unique_Snapshot_Indexes()
    {
        var columns = P3Operations().OfType<CreateTableOperation>()
            .SelectMany(x => x.Columns).Where(x => x.Name == "SourceFingerprint").ToArray();
        Assert.Equal(2, columns.Length);
        Assert.All(columns, x => Assert.Equal("binary(32)", x.ColumnType));
        var indexes = P3Operations().OfType<CreateIndexOperation>().ToArray();
        Assert.Contains(indexes, x => x.Name ==
            "UX_PayrollAttendanceAllowanceSnapshots_Component" && x.IsUnique);
        Assert.Contains(indexes, x => x.Name ==
            "UX_PayrollLeaveDeductionSnapshots_Component" && x.IsUnique);
    }

    [Fact]
    public void P4_Migration_Changes_Only_Payroll_Metadata_And_Evidence()
    {
        var tables = P4Operations().OfType<CreateTableOperation>()
            .Select(x => x.Name).OrderBy(x => x).ToArray();
        Assert.Equal([
            "OvertimePayRatePolicies",
            "PayrollOvertimeBaseComponentEvidence",
            "PayrollOvertimeBucketEvidence",
            "PayrollOvertimeDayEvidence",
            "PayrollOvertimePaySnapshots",
            "PayrollOvertimeRecognitionEvidence"
        ], tables);
        var column = Assert.Single(P4Operations().OfType<AddColumnOperation>());
        Assert.Equal("PayrollComponentDefinitions", column.Table);
        Assert.Equal("IncludeInOvertimeHourlyBase", column.Name);
        Assert.DoesNotContain(P4Operations(), operation => operation is DropTableOperation);
    }

    [Fact]
    public void P4_Migration_Seeds_Metadata_And_Versioned_Rates()
    {
        var rates = Assert.Single(P4Operations().OfType<InsertDataOperation>(),
            x => x.Table == "OvertimePayRatePolicies");
        Assert.Equal(3, rates.Values.GetLength(0));
        var multipliers = Enumerable.Range(0, 3)
            .Select(row => Assert.IsType<decimal>(rates.Values[row,
                Array.IndexOf(rates.Columns, "Multiplier")])).ToArray();
        Assert.Equal([1.34m, 1.67m, 2.67m], multipliers);
        var updates = P4Operations().OfType<UpdateDataOperation>().ToArray();
        Assert.Equal(15, updates.Length);
        Assert.Equal(5, updates.Count(x => Equals(x.Values[0, 0], true)));
        Assert.All(updates, x => Assert.Equal("PayrollComponentDefinitions", x.Table));
    }

    [Fact]
    public void Payroll_Migrations_Preserve_History_Through_Occupational_Insurance()
    {
        var assembly = typeof(AddPayrollOvertimePayCalculation).Assembly;
        var ids = assembly.GetTypes()
            .Select(type => type.GetCustomAttributes(typeof(
                Microsoft.EntityFrameworkCore.Migrations.MigrationAttribute), false)
                .Cast<Microsoft.EntityFrameworkCore.Migrations.MigrationAttribute>()
                .SingleOrDefault()?.Id)
            .Where(x => x is not null).OrderBy(x => x).ToArray();
        Assert.Contains("20260823061050_AddAttendanceCorrectionRequests", ids);
        Assert.Contains("20260826023624_AddPayrollAttendanceAllowanceAndLeaveDeduction", ids);
        Assert.Contains(ids, x => x!.EndsWith("_AddPayrollOvertimePayCalculation", StringComparison.Ordinal));
        Assert.Contains(ids, x => x!.EndsWith("_AddPayrollLaborInsuranceCalculation", StringComparison.Ordinal));
        Assert.Contains(ids, x => x!.EndsWith(
            "_AddPayrollHealthInsuranceCalculation", StringComparison.Ordinal));
        Assert.Contains(ids, x => x!.EndsWith(
            "_AddApprovalWorkflowFoundation", StringComparison.Ordinal));
        Assert.Contains("20260830095141_AddPayrollLegacyAdjustmentComponents", ids);
        Assert.Contains("20260830141851_AddOwnerPrivateLinePairing", ids);
        Assert.Equal(38, ids.Length);
        Assert.Equal(
            "20260930061925_AddHolidayTrainingCompTime",
            ids[^1]);
    }

    [Fact]
    public void Legacy_Adjustment_Migration_Only_Seeds_Two_Manual_Earnings()
    {
        var seed = Assert.Single(LegacyAdjustmentOperations()
            .OfType<InsertDataOperation>());
        Assert.Equal("PayrollComponentDefinitions", seed.Table);
        Assert.Equal(2, seed.Values.GetLength(0));
        var codeIndex = Array.IndexOf(seed.Columns, "Code");
        var categoryIndex = Array.IndexOf(seed.Columns, "Category");
        var kindIndex = Array.IndexOf(seed.Columns, "CalculationKind");
        Assert.Equal([
            "LEGACY_ATTENDANCE_ALLOWANCE",
            "LEGACY_OVERTIME_PAY"
        ], Enumerable.Range(0, 2)
            .Select(row => Assert.IsType<string>(seed.Values[row, codeIndex]))
            .OrderBy(x => x).ToArray());
        Assert.All(Enumerable.Range(0, 2), row =>
        {
            Assert.Equal(1, seed.Values[row, categoryIndex]);
            Assert.Equal(3, seed.Values[row, kindIndex]);
        });
        Assert.DoesNotContain(LegacyAdjustmentOperations(), operation =>
            operation is CreateTableOperation or AddColumnOperation or UpdateDataOperation);
    }

    [Fact]
    public void P6_Migration_Is_Payroll_Only_And_Uses_Legal_Status_Default()
    {
        var additions = P6Operations().OfType<AddColumnOperation>().ToArray();
        Assert.Equal(8, additions.Length);
        Assert.All(additions, x => Assert.Equal("PayrollEmployeeSnapshots", x.Table));
        Assert.Equal(6, Assert.Single(additions,
            x => x.Name == "TotalCalculationStatus").DefaultValue);
        Assert.Equal(0, Assert.Single(additions,
            x => x.Name == "BlockingComponentCount").DefaultValue);
        var table = Assert.Single(P6Operations().OfType<CreateTableOperation>());
        Assert.Equal("PayrollTotalBlockingEvidence", table.Name);
        Assert.Equal(2, table.ForeignKeys.Count);
        Assert.All(table.ForeignKeys,
            x => Assert.Equal(ReferentialAction.NoAction, x.OnDelete));
        Assert.DoesNotContain(P6Operations(),
            x => x is InsertDataOperation or UpdateDataOperation or DeleteDataOperation);
    }

    [Fact]
    public void P5A_Migration_Creates_Only_Payroll_Insurance_Tables()
    {
        var tables = P5AOperations().OfType<CreateTableOperation>()
            .Select(x => x.Name).OrderBy(x => x).ToArray();
        Assert.Equal([
            "EmployeeLaborInsuranceEnrollments",
            "LaborInsuranceRatePolicies",
            "PayrollLaborInsuranceContributionEvidence",
            "PayrollLaborInsuranceSnapshots"
        ], tables);
        Assert.DoesNotContain(P5AOperations(), x => x is AddColumnOperation or InsertDataOperation);
        Assert.All(P5AOperations().OfType<CreateTableOperation>().SelectMany(x => x.ForeignKeys),
            x => Assert.Equal(ReferentialAction.NoAction, x.OnDelete));
    }

    [Fact]
    public void P5A_Migration_Uses_Expected_Precision_Indexes_And_Fingerprint()
    {
        var tables = P5AOperations().OfType<CreateTableOperation>().ToArray();
        Assert.Equal("binary(32)", tables.SelectMany(x => x.Columns)
            .Single(x => x.Name == "SourceFingerprint").ColumnType);
        Assert.All(tables.SelectMany(x => x.Columns).Where(x => x.Name.EndsWith("Rate")),
            x => Assert.Equal("decimal(9,8)", x.ColumnType));
        var indexes = P5AOperations().OfType<CreateIndexOperation>().ToArray();
        Assert.Contains(indexes, x => x.Name ==
            "UX_EmployeeLaborInsuranceEnrollments_Employee_From" && x.IsUnique);
        Assert.Contains(indexes, x => x.Name ==
            "UX_PayrollLaborInsuranceSnapshots_Component" && x.IsUnique);
    }

    [Fact]
    public void P5B_Migration_Creates_Only_Health_Insurance_Tables_Without_Seed()
    {
        var tables = P5BOperations().OfType<CreateTableOperation>()
            .Select(x => x.Name).OrderBy(x => x).ToArray();
        Assert.Equal([
            "EmployeeHealthInsuranceEnrollments",
            "HealthInsuranceRatePolicies",
            "PayrollHealthInsuranceEvidence",
            "PayrollHealthInsuranceSnapshots"
        ], tables);
        Assert.DoesNotContain(P5BOperations(), x => x is AddColumnOperation or
            InsertDataOperation or UpdateDataOperation or DeleteDataOperation);
        Assert.All(P5BOperations().OfType<CreateTableOperation>()
            .SelectMany(x => x.ForeignKeys),
            x => Assert.Equal(ReferentialAction.NoAction, x.OnDelete));
    }

    [Fact]
    public void P5B_Migration_Uses_Expected_Precision_Indexes_And_Fingerprint()
    {
        var tables = P5BOperations().OfType<CreateTableOperation>().ToArray();
        Assert.Equal("binary(32)", tables.SelectMany(x => x.Columns)
            .Single(x => x.Name == "SourceFingerprint").ColumnType);
        Assert.All(tables.SelectMany(x => x.Columns)
                .Where(x => x.Name is "GeneralPremiumRate" or "EmployeeShareRate"),
            x => Assert.Equal("decimal(9,8)", x.ColumnType));
        Assert.All(tables.SelectMany(x => x.Columns)
                .Where(x => x.Name == "RawEmployeeAmount"),
            x => Assert.Equal("decimal(18,6)", x.ColumnType));
        var indexes = P5BOperations().OfType<CreateIndexOperation>().ToArray();
        Assert.Contains(indexes, x => x.Name ==
            "UX_EmployeeHealthInsuranceEnrollments_Employee_From" && x.IsUnique);
        Assert.Contains(indexes, x => x.Name ==
            "UX_PayrollHealthInsuranceSnapshots_Component" && x.IsUnique);
        Assert.Contains(indexes, x => x.Name ==
            "UX_PayrollHealthInsuranceEvidence_Snapshot" && x.IsUnique);
    }
}
