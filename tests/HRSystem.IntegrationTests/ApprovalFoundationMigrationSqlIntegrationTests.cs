using HRSystem.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace HRSystem.IntegrationTests;

[Collection(DisposableSqlServerCollection.Name)]
public sealed class ApprovalFoundationMigrationSqlIntegrationTests
{
    private const string Previous = "20260827131618_AddPayrollTotalsCalculation";
    private const string Current = "20260828032342_AddApprovalWorkflowFoundation";

    [Fact]
    public async Task Migration_Up_Down_Up_Preserves_Chain_And_Approval_Schema()
    {
        await using var database = await DisposableSqlServerDatabase.CreateAsync(
            "ApprovalFoundation", applyMigrations: false);
        await using var db = database.CreateDbContext();
        var migrator = db.GetService<IMigrator>();

        await migrator.MigrateAsync(Previous);
        Assert.Equal(27, (await db.Database.GetAppliedMigrationsAsync()).Count());
        var preMigrationBusinessRows = await ExistingBusinessRowCountAsync(db);
        await migrator.MigrateAsync(Current);
        await AssertSchemaAsync(db);
        Assert.Equal(preMigrationBusinessRows, await ExistingBusinessRowCountAsync(db));
        Assert.Equal(28, (await db.Database.GetAppliedMigrationsAsync()).Count());

        await migrator.MigrateAsync(Previous);
        Assert.Equal(27, (await db.Database.GetAppliedMigrationsAsync()).Count());
        Assert.False(await TableExistsAsync(db, "Approvals"));

        await migrator.MigrateAsync(Current);
        await AssertSchemaAsync(db);
        Assert.Equal(
            db.Database.GetMigrations().SkipWhile(x => x != Current).Skip(1),
            await db.Database.GetPendingMigrationsAsync());
    }

    private static async Task AssertSchemaAsync(HRSystemDbContext db)
    {
        Assert.True(await TableExistsAsync(db, "Approvals"));
        Assert.True(await TableExistsAsync(db, "ApprovalHistories"));
        Assert.True(await TableExistsAsync(db, "ApprovalLineActionTokens"));
        Assert.True(await TableExistsAsync(db, "LineUserBindings"));
        Assert.True(await IndexExistsAsync(db, "Approvals",
            "UX_Approvals_PendingSourceVersion"));
        Assert.True(await IndexExistsAsync(db, "ApprovalLineActionTokens",
            "UX_ApprovalLineActionTokens_TokenHash"));
        Assert.True(await IndexExistsAsync(db, "LineUserBindings",
            "UX_LineUserBindings_LineUser"));
        Assert.Equal(8, await NoActionForeignKeyCountAsync(db));
        Assert.Equal(0, await ApprovalDefaultConstraintCountAsync(db));
    }

    private static async Task<bool> TableExistsAsync(HRSystemDbContext db, string table)
    {
        await using var command = db.Database.GetDbConnection().CreateCommand();
        if (command.Connection!.State != System.Data.ConnectionState.Open)
            await command.Connection.OpenAsync();
        command.CommandText = "SELECT COUNT(*) FROM sys.tables WHERE name = @name";
        var parameter = command.CreateParameter(); parameter.ParameterName = "@name";
        parameter.Value = table; command.Parameters.Add(parameter);
        return Convert.ToInt32(await command.ExecuteScalarAsync()) == 1;
    }

    private static async Task<bool> IndexExistsAsync(HRSystemDbContext db,
        string table, string index)
    {
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sys.indexes WHERE object_id = OBJECT_ID(@table) AND name = @index";
        var p1 = command.CreateParameter(); p1.ParameterName = "@table"; p1.Value = table;
        var p2 = command.CreateParameter(); p2.ParameterName = "@index"; p2.Value = index;
        command.Parameters.Add(p1); command.Parameters.Add(p2);
        return Convert.ToInt32(await command.ExecuteScalarAsync()) == 1;
    }

    private static async Task<int> NoActionForeignKeyCountAsync(HRSystemDbContext db)
    {
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sys.foreign_keys WHERE parent_object_id IN (OBJECT_ID('Approvals'), OBJECT_ID('ApprovalHistories'), OBJECT_ID('ApprovalLineActionTokens'), OBJECT_ID('LineUserBindings')) AND delete_referential_action = 0";
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static async Task<long> ExistingBusinessRowCountAsync(HRSystemDbContext db)
    {
        await using var command = db.Database.GetDbConnection().CreateCommand();
        if (command.Connection!.State != System.Data.ConnectionState.Open)
            await command.Connection.OpenAsync();
        command.CommandText = "SELECT COALESCE(SUM(p.rows), 0) FROM sys.tables t JOIN sys.partitions p ON p.object_id = t.object_id AND p.index_id IN (0, 1) WHERE t.name NOT IN ('__EFMigrationsHistory', 'Approvals', 'ApprovalHistories', 'ApprovalLineActionTokens', 'LineUserBindings')";
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private static async Task<int> ApprovalDefaultConstraintCountAsync(HRSystemDbContext db)
    {
        await using var command = db.Database.GetDbConnection().CreateCommand();
        if (command.Connection!.State != System.Data.ConnectionState.Open)
            await command.Connection.OpenAsync();
        command.CommandText = "SELECT COUNT(*) FROM sys.default_constraints d WHERE d.parent_object_id IN (OBJECT_ID('Approvals'), OBJECT_ID('ApprovalHistories'), OBJECT_ID('ApprovalLineActionTokens'), OBJECT_ID('LineUserBindings'))";
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }
}
