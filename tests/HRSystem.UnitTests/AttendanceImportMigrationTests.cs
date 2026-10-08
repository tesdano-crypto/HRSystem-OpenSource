using HRSystem.Infrastructure.Persistence.Migrations;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace HRSystem.UnitTests;

public sealed class AttendanceImportMigrationTests
{
    private static IReadOnlyList<MigrationOperation> Operations()
    {
        var migration = new AddBioWebTaAttendanceImportFoundation();
        var builder = new MigrationBuilder("Microsoft.EntityFrameworkCore.SqlServer");
        typeof(AddBioWebTaAttendanceImportFoundation)
            .GetMethod("Up", System.Reflection.BindingFlags.Instance |
                             System.Reflection.BindingFlags.NonPublic)!
            .Invoke(migration, [builder]);
        return builder.Operations;
    }

    [Fact]
    public void Migration_Creates_Only_Expected_Additive_Tables()
    {
        var operations = Operations();
        var createdTables = operations
            .OfType<CreateTableOperation>()
            .Select(operation => operation.Name)
            .OrderBy(name => name)
            .ToArray();

        Assert.Equal(
            [
                "AttendanceRawEvents",
                "AttendanceSyncStates",
                "BioWebPersonMappings"
            ],
            createdTables);
        Assert.All(
            operations,
            operation => Assert.True(
                operation is CreateTableOperation or CreateIndexOperation,
                $"Unexpected migration operation: {operation.GetType().Name}"));
    }

    [Fact]
    public void Migration_Does_Not_Alter_Existing_Business_Tables()
    {
        var operations = Operations();

        Assert.DoesNotContain(operations, operation =>
            operation is AlterTableOperation or AlterColumnOperation or
                DropTableOperation or DropColumnOperation or SqlOperation);
        Assert.DoesNotContain(
            operations.Select(operation => operation.ToString()),
            value => value is not null &&
                     (value.Contains("LeaveRequest", StringComparison.OrdinalIgnoreCase) ||
                      value.Contains("CompanyCalendar", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void Migration_Has_Authoritative_Unique_Source_Event_Index()
    {
        var index = Operations()
            .OfType<CreateIndexOperation>()
            .Single(operation =>
                operation.Name ==
                "UX_AttendanceRawEvents_SourceSystem_ExternalEventId");

        Assert.True(index.IsUnique);
        Assert.Equal(
            [nameof(Domain.Attendance.AttendanceRawEvent.SourceSystem),
             nameof(Domain.Attendance.AttendanceRawEvent.ExternalEventId)],
            index.Columns);
    }
}
