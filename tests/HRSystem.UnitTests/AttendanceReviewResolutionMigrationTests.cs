using HRSystem.Infrastructure.Persistence.Migrations;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace HRSystem.UnitTests;

public sealed class AttendanceReviewResolutionMigrationTests
{
    private static IReadOnlyList<MigrationOperation> Up()
    {
        var migration = new AddAttendanceReviewResolutionLedger();
        var builder = new MigrationBuilder("Microsoft.EntityFrameworkCore.SqlServer");
        typeof(AddAttendanceReviewResolutionLedger)
            .GetMethod("Up", System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic)!
            .Invoke(migration, [builder]);
        return builder.Operations;
    }

    [Fact]
    public void Migration_Creates_Only_Ledger_And_History_Tables()
    {
        Assert.Equal(
            ["AttendanceReviewResolutionHistories", "AttendanceReviewResolutions"],
            Up().OfType<CreateTableOperation>().Select(item => item.Name)
                .OrderBy(item => item).ToArray());
    }

    [Fact]
    public void Migration_Has_No_Data_Mutation_Or_Backfill()
    {
        Assert.DoesNotContain(Up(), operation => operation is
            SqlOperation or InsertDataOperation or UpdateDataOperation or
            DeleteDataOperation or AddColumnOperation);
    }

    [Fact]
    public void All_Foreign_Keys_Are_NoAction_And_Unique_Key_Is_Scoped()
    {
        var foreignKeys = Up().OfType<CreateTableOperation>()
            .SelectMany(table => table.ForeignKeys).ToArray();
        Assert.Equal(3, foreignKeys.Length);
        Assert.All(foreignKeys, key =>
            Assert.Equal(ReferentialAction.NoAction, key.OnDelete));
        var unique = Assert.Single(Up().OfType<CreateIndexOperation>(), item =>
            item.Name == "UX_AttendanceReviewResolutions_Employee_WorkDate_Anomaly");
        Assert.True(unique.IsUnique);
        Assert.Equal(["EmployeeId", "WorkDate", "AnomalyType"], unique.Columns);
    }

    [Fact]
    public void Migration_Enforces_Fingerprint_Reason_And_Status_Checks()
    {
        var checks = Up().OfType<CreateTableOperation>()
            .SelectMany(table => table.CheckConstraints)
            .Select(item => item.Name).ToArray();
        Assert.Contains("CK_AttendanceReviewResolutions_FingerprintLength", checks);
        Assert.Contains("CK_AttendanceReviewResolutions_OtherNote", checks);
        Assert.Contains("CK_AttendanceReviewResolutions_Status", checks);
        Assert.Contains("CK_AttendanceReviewResolutionHistories_Action", checks);
    }
}
