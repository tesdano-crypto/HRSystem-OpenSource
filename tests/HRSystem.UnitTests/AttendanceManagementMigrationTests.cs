using HRSystem.Infrastructure.Persistence.Migrations;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace HRSystem.UnitTests;

public sealed class AttendanceManagementMigrationTests
{
    private static IReadOnlyList<MigrationOperation> Operations()
    {
        var migration = new AddAttendanceManagementFoundation();
        var builder = new MigrationBuilder(
            "Microsoft.EntityFrameworkCore.SqlServer");
        typeof(AddAttendanceManagementFoundation)
            .GetMethod(
                "Up",
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic)!
            .Invoke(migration, [builder]);
        return builder.Operations;
    }

    [Fact]
    public void Migration_Creates_Only_Approved_Attendance_Tables()
    {
        var tables = Operations()
            .OfType<CreateTableOperation>()
            .Select(item => item.Name)
            .OrderBy(item => item)
            .ToArray();

        Assert.Equal(
            [
                "AttendanceAdjustments",
                "AttendanceShifts",
                "DailyAttendanceResults",
                "EmployeeShiftAssignments"
            ],
            tables);
    }

    [Fact]
    public void Migration_Has_No_Destructive_Or_Data_Mutation_Operation()
    {
        var operations = Operations();

        Assert.DoesNotContain(operations, item =>
            item is DropTableOperation or
                DropColumnOperation or
                AlterColumnOperation or
                AlterTableOperation or
                SqlOperation or
                InsertDataOperation or
                UpdateDataOperation or
                DeleteDataOperation);
    }

    [Fact]
    public void Migration_Does_Not_Alter_Raw_Import_Or_Business_Tables()
    {
        var touchedTables = Operations()
            .Select(item => item switch
            {
                CreateTableOperation createTable => createTable.Name,
                CreateIndexOperation createIndex => createIndex.Table,
                AddForeignKeyOperation foreignKey => foreignKey.Table,
                _ => string.Empty
            })
            .Where(item => item.Length > 0)
            .Distinct()
            .OrderBy(item => item)
            .ToArray();

        Assert.Equal(
            [
                "AttendanceAdjustments",
                "AttendanceShifts",
                "DailyAttendanceResults",
                "EmployeeShiftAssignments"
            ],
            touchedTables);
        Assert.DoesNotContain("AttendanceRawEvents", touchedTables);
        Assert.DoesNotContain("AttendanceSyncStates", touchedTables);
        Assert.DoesNotContain("BioWebPersonMappings", touchedTables);
        Assert.DoesNotContain("Employees", touchedTables);
    }

    [Theory]
    [InlineData("AttendanceShifts")]
    [InlineData("EmployeeShiftAssignments")]
    [InlineData("DailyAttendanceResults")]
    public void Mutable_Aggregates_Have_RowVersion(string table)
    {
        var rowVersion = Operations()
            .OfType<CreateTableOperation>()
            .Single(item => item.Name == table)
            .Columns
            .Single(item => item.Name == "RowVersion");

        Assert.True(rowVersion.IsRowVersion);
    }

    [Theory]
    [InlineData(
        "UX_AttendanceShifts_Code",
        "AttendanceShifts",
        "Code")]
    [InlineData(
        "UX_DailyAttendanceResults_Employee_WorkDate",
        "DailyAttendanceResults",
        "EmployeeId,WorkDate")]
    [InlineData(
        "UX_AttendanceAdjustments_Result_Revision",
        "AttendanceAdjustments",
        "DailyAttendanceResultId,RevisionNumber")]
    public void Authoritative_Indexes_Are_Unique(
        string name,
        string table,
        string columns)
    {
        var index = Operations()
            .OfType<CreateIndexOperation>()
            .Single(item => item.Name == name);

        Assert.True(index.IsUnique);
        Assert.Equal(table, index.Table);
        Assert.Equal(columns.Split(','), index.Columns);
    }

    [Theory]
    [InlineData("CK_AttendanceShifts_ExpectedWorkMinutes")]
    [InlineData("CK_EmployeeShiftAssignments_EffectivePeriod")]
    [InlineData("CK_DailyAttendanceResults_Durations")]
    [InlineData("CK_AttendanceAdjustments_RevisionNumber")]
    public void Approved_Check_Constraint_Exists(string name)
    {
        var constraints = Operations()
            .OfType<CreateTableOperation>()
            .SelectMany(item => item.CheckConstraints)
            .Select(item => item.Name);

        Assert.Contains(name, constraints);
    }

    [Theory]
    [InlineData(
        "FK_DailyAttendanceResults_AttendanceRawEvents_RawClockInEventId")]
    [InlineData(
        "FK_DailyAttendanceResults_AttendanceRawEvents_RawClockOutEventId")]
    [InlineData(
        "FK_DailyAttendanceResults_AttendanceShifts_ShiftId")]
    [InlineData(
        "FK_EmployeeShiftAssignments_Employees_EmployeeId")]
    [InlineData(
        "FK_EmployeeShiftAssignments_AttendanceShifts_ShiftId")]
    public void Evidence_And_Assignment_Foreign_Keys_Exist(string name)
    {
        var inlineKeys = Operations()
            .OfType<CreateTableOperation>()
            .SelectMany(item => item.ForeignKeys);

        Assert.Contains(inlineKeys, item => item.Name == name);
    }
}
