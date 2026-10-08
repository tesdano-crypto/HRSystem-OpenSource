using HRSystem.Infrastructure.Persistence.Migrations;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace HRSystem.UnitTests;

public sealed class ParentalLeaveMigrationTests
{
    private static IReadOnlyList<MigrationOperation> Operations()
    {
        var migration = new AddParentalLeaveOfAbsence();
        var builder = new MigrationBuilder(
            "Microsoft.EntityFrameworkCore.SqlServer");
        typeof(AddParentalLeaveOfAbsence)
            .GetMethod(
                "Up",
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic)!
            .Invoke(migration, [builder]);
        return builder.Operations;
    }

    [Fact]
    public void Migration_Touches_Only_Parental_Leave_And_Attendance_Projection()
    {
        var tables = Operations().Select(operation => operation switch
        {
            CreateTableOperation item => item.Name,
            AddColumnOperation item => item.Table,
            CreateIndexOperation item => item.Table,
            AddForeignKeyOperation item => item.Table,
            _ => string.Empty
        }).Where(item => item.Length > 0).Distinct().OrderBy(item => item).ToArray();
        Assert.Equal(
            [
                "DailyAttendanceResults",
                "ParentalLeaveApprovalHistories",
                "ParentalLeaveRequests"
            ],
            tables);
    }

    [Fact]
    public void Migration_Has_No_Business_Data_Mutation()
    {
        Assert.DoesNotContain(Operations(), operation =>
            operation is SqlOperation or InsertDataOperation or
                UpdateDataOperation or DeleteDataOperation or DropTableOperation);
    }

    [Fact]
    public void All_Foreign_Keys_Are_NoAction_Or_Restrict()
    {
        var foreignKeys = Operations()
            .OfType<CreateTableOperation>()
            .SelectMany(item => item.ForeignKeys)
            .Concat(Operations().OfType<AddForeignKeyOperation>())
            .ToArray();
        Assert.Equal(3, foreignKeys.Length);
        Assert.All(foreignKeys, item => Assert.Contains(
            item.OnDelete,
            new[] { ReferentialAction.NoAction, ReferentialAction.Restrict }));
    }
}
