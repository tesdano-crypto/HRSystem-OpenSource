using Microsoft.EntityFrameworkCore;

namespace HRSystem.IntegrationTests;

[Collection(DisposableSqlServerCollection.Name)]
public sealed class DisposableSqlServerDatabaseTests
{
    [Fact]
    public void Generated_Database_Name_Uses_Approved_Prefix()
    {
        var name = DisposableSqlServerDatabase.GenerateDatabaseName("Naming");

        Assert.StartsWith(
            DisposableSqlServerDatabase.DatabasePrefix,
            name,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Generated_Database_Names_Are_Unique()
    {
        var first = DisposableSqlServerDatabase.GenerateDatabaseName("Unique");
        var second = DisposableSqlServerDatabase.GenerateDatabaseName("Unique");

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Shared_HRSystemDb_Name_Is_Rejected()
    {
        Assert.Throws<InvalidOperationException>(() =>
            DisposableSqlServerDatabase.ValidateDatabaseName(
                "HRSystem_Test_HRSystemDb"));
    }

    [Fact]
    public void BioWebTA_Name_Is_Rejected()
    {
        Assert.Throws<InvalidOperationException>(() =>
            DisposableSqlServerDatabase.ValidateDatabaseName(
                "HRSystem_Test_BioWebTA"));
    }

    [Fact]
    public async Task Database_Is_Created_Successfully()
    {
        await using var database =
            await DisposableSqlServerDatabase.CreateAsync(
                "Create",
                applyMigrations: false);

        Assert.True(await DisposableSqlServerDatabase.DatabaseExistsAsync(
            database.DatabaseName));
    }

    [Fact]
    public async Task All_Current_Migrations_Are_Applied()
    {
        await using var database =
            await DisposableSqlServerDatabase.CreateAsync("Migrations");
        await using var db = database.CreateDbContext();

        Assert.Equal(
            DisposableSqlServerDatabase.ExpectedMigrationCount,
            (await db.Database.GetAppliedMigrationsAsync()).Count());
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
    }

    [Fact]
    public async Task Multiple_DbContext_Instances_Can_Connect()
    {
        await using var database =
            await DisposableSqlServerDatabase.CreateAsync("MultipleContexts");
        await using var first = database.CreateDbContext();
        await using var second = database.CreateDbContext();

        Assert.True(await first.Database.CanConnectAsync());
        Assert.True(await second.Database.CanConnectAsync());
    }

    [Fact]
    public async Task Database_Is_Dropped_After_Successful_Test()
    {
        string databaseName;
        await using (var database =
            await DisposableSqlServerDatabase.CreateAsync(
                "DropSuccess",
                applyMigrations: false))
        {
            databaseName = database.DatabaseName;
        }

        Assert.False(await DisposableSqlServerDatabase.DatabaseExistsAsync(
            databaseName));
    }

    [Fact]
    public async Task Database_Is_Dropped_After_Failing_Test_Path()
    {
        var databaseName = string.Empty;

        await Assert.ThrowsAsync<SyntheticTestFailure>(async () =>
        {
            await using var database =
                await DisposableSqlServerDatabase.CreateAsync(
                    "DropFailure",
                    applyMigrations: false);
            databaseName = database.DatabaseName;
            throw new SyntheticTestFailure();
        });

        Assert.False(await DisposableSqlServerDatabase.DatabaseExistsAsync(
            databaseName));
    }

    [Fact]
    public async Task Residue_Verification_Detects_Undeleted_Database()
    {
        await using var database =
            await DisposableSqlServerDatabase.CreateAsync(
                "ResidueDetection",
                applyMigrations: false);

        Assert.True(
            await DisposableSqlServerDatabase.CurrentRunResidueCountAsync() >= 1);
    }

    [Fact]
    public async Task Full_Connection_String_Is_Never_Returned_By_Display()
    {
        await using var database =
            await DisposableSqlServerDatabase.CreateAsync(
                "SafeDisplay",
                applyMigrations: false);

        var display = database.ToString();
        Assert.Equal(database.DatabaseName, display);
        Assert.DoesNotContain("Data Source", display, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Integrated Security", display, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain('=', display);
    }

    [Fact]
    public async Task Cleanup_Handles_Pooled_Connections()
    {
        string databaseName;
        await using (var database =
            await DisposableSqlServerDatabase.CreateAsync(
                "PooledCleanup",
                applyMigrations: false))
        {
            databaseName = database.DatabaseName;
            await using var connection = await database.OpenConnectionAsync();
            Assert.Equal(
                System.Data.ConnectionState.Open,
                connection.State);
        }

        Assert.False(await DisposableSqlServerDatabase.DatabaseExistsAsync(
            databaseName));
    }

    private sealed class SyntheticTestFailure : Exception
    {
    }
}
