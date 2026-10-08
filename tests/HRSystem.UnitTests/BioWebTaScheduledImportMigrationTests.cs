using HRSystem.Infrastructure.Persistence.Migrations;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace HRSystem.UnitTests;

public sealed class BioWebTaScheduledImportMigrationTests
{
    private static IReadOnlyList<MigrationOperation> Operations()
    {
        var migration = new AddBioWebTaScheduledImportAndFingerprint();
        var builder = new MigrationBuilder("Microsoft.EntityFrameworkCore.SqlServer");
        migration.GetType()
            .GetMethod(
                "Up",
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic)!
            .Invoke(migration, [builder]);
        return builder.Operations;
    }

    [Fact]
    public void Migration_Adds_Only_Fingerprint_And_Import_Evidence_Schema()
    {
        var operations = Operations();

        Assert.Equal(
            ["BioWebTaImportBatches", "BioWebTaImportBatchIssues"],
            operations.OfType<CreateTableOperation>()
                .Select(operation => operation.Name)
                .ToArray());
        Assert.Equal(
            ["SourceFingerprint", "SourceFingerprintVersion"],
            operations.OfType<AddColumnOperation>()
                .Select(operation => operation.Name)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray());
        Assert.Empty(operations.OfType<DropTableOperation>());
        Assert.Empty(operations.OfType<DropColumnOperation>());
    }

    [Fact]
    public void Migration_Backfills_Before_NotNull_And_Unique_Index()
    {
        var operations = Operations();
        var sqlIndex = operations.ToList().FindIndex(operation =>
            operation is SqlOperation);
        var alterIndexes = operations
            .Select((operation, index) => (operation, index))
            .Where(pair => pair.operation is AlterColumnOperation)
            .Select(pair => pair.index)
            .ToArray();
        var uniqueIndex = Assert.Single(
            operations.OfType<CreateIndexOperation>(),
            operation => operation.Name ==
                "UX_AttendanceRawEvents_SourceSystem_FingerprintVersion_Fingerprint");

        Assert.All(alterIndexes, index => Assert.True(index > sqlIndex));
        Assert.True(uniqueIndex.IsUnique);
        Assert.Equal(
            ["SourceSystem", "SourceFingerprintVersion", "SourceFingerprint"],
            uniqueIndex.Columns);
    }

    [Fact]
    public void Backfill_Is_Guarded_And_Has_No_Destructive_Dml()
    {
        var sql = Assert.Single(Operations().OfType<SqlOperation>()).Sql;

        Assert.Contains("HASHBYTES", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SHA2_256", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("0x01000000027631", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "SET [SourceFingerprintVersion] = 1",
            sql,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("0x010000000131", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("THROW 51004", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("UPDATE raw_event", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DELETE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("MERGE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("AttendanceAdjustments", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Issue_Foreign_Key_Is_NoAction()
    {
        var issueTable = Assert.Single(
            Operations().OfType<CreateTableOperation>(),
            operation => operation.Name == "BioWebTaImportBatchIssues");
        var foreignKey = Assert.Single(issueTable.ForeignKeys);

        Assert.Equal(ReferentialAction.NoAction, foreignKey.OnDelete);
    }
}
