using HRSystem.Infrastructure.Persistence.Migrations;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace HRSystem.UnitTests;

public sealed class LeaveAwareAttendanceMigrationTests
{
    private static IReadOnlyList<MigrationOperation> Operations()
    {
        var migration = new AddLeaveAwareDailyAttendance();
        var builder = new MigrationBuilder(
            "Microsoft.EntityFrameworkCore.SqlServer");
        typeof(AddLeaveAwareDailyAttendance)
            .GetMethod(
                "Up",
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic)!
            .Invoke(migration, [builder]);
        return builder.Operations;
    }

    [Fact]
    public void Migration_Adds_Only_Leave_Projection_Schema()
    {
        var touchedTables = Operations()
            .Select(operation => operation switch
            {
                CreateTableOperation table => table.Name,
                AddColumnOperation column => column.Table,
                CreateIndexOperation index => index.Table,
                DropCheckConstraintOperation constraint => constraint.Table,
                AddCheckConstraintOperation constraint => constraint.Table,
                _ => string.Empty
            })
            .Where(name => name.Length > 0)
            .Distinct()
            .OrderBy(name => name)
            .ToArray();

        Assert.Equal(
            ["DailyAttendanceLeaveSegments", "DailyAttendanceResults"],
            touchedTables);
    }

    [Fact]
    public void Migration_Has_No_Data_Mutation_Or_Destructive_Table_Operation()
    {
        Assert.DoesNotContain(Operations(), operation =>
            operation is DropTableOperation or
                AlterColumnOperation or
                SqlOperation or
                InsertDataOperation or
                UpdateDataOperation or
                DeleteDataOperation);
    }

    [Fact]
    public void Leave_Segment_Foreign_Keys_Are_NoAction()
    {
        var foreignKeys = Operations()
            .OfType<CreateTableOperation>()
            .Single(table => table.Name == "DailyAttendanceLeaveSegments")
            .ForeignKeys;

        Assert.Equal(3, foreignKeys.Count);
        Assert.All(foreignKeys, foreignKey =>
            Assert.Equal(ReferentialAction.NoAction, foreignKey.OnDelete));
    }

    [Fact]
    public void Leave_Segment_Period_Index_Is_Unique()
    {
        var index = Operations()
            .OfType<CreateIndexOperation>()
            .Single(item =>
                item.Name ==
                "UX_DailyAttendanceLeaveSegments_Result_Period");

        Assert.True(index.IsUnique);
        Assert.Equal(
            ["DailyAttendanceResultId", "StartAtUtc", "EndAtUtc"],
            index.Columns);
    }
}
