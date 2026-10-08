using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace HRSystem.IntegrationTests;

[Collection(DisposableSqlServerCollection.Name)]
public sealed class LeaveCancellationMigrationSqlIntegrationTests
{
    [Fact]
    public async Task Migration_Adds_Only_Nullable_Cancellation_Metadata()
    {
        await using var database =
            await DisposableSqlServerDatabase.CreateAsync(
                "LeaveCancellationSchema");
        await using var connection = await database.OpenConnectionAsync();

        var columns = new Dictionary<string, (string Type, short MaxLength, bool Nullable)>(
            StringComparer.Ordinal);
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT c.name, t.name, c.max_length, c.is_nullable
                FROM sys.columns c
                JOIN sys.types t ON c.user_type_id = t.user_type_id
                WHERE c.object_id = OBJECT_ID(N'dbo.LeaveRequests')
                  AND c.name IN (
                    N'CancellationRequestedAtUtc',
                    N'CancellationReason',
                    N'CancellationRequestedByUserId')
                ORDER BY c.name;
                """;
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                columns.Add(
                    reader.GetString(0),
                    (reader.GetString(1), reader.GetInt16(2), reader.GetBoolean(3)));
            }
        }

        Assert.Equal(3, columns.Count);
        Assert.Equal(("nvarchar", (short)2000, true), columns["CancellationReason"]);
        Assert.Equal(("datetimeoffset", (short)10, true), columns["CancellationRequestedAtUtc"]);
        Assert.Equal(("nvarchar", (short)900, true), columns["CancellationRequestedByUserId"]);
    }

    [Fact]
    public async Task Latest_Migration_Is_Applied_Without_Pending_Migrations()
    {
        await using var database =
            await DisposableSqlServerDatabase.CreateAsync(
                "LeaveCancellationMigrations");
        await using var db = database.CreateDbContext();

        var applied = (await db.Database.GetAppliedMigrationsAsync()).ToArray();

        Assert.Equal(
            DisposableSqlServerDatabase.ExpectedMigrationCount,
            applied.Length);
        Assert.Equal(
            DisposableSqlServerDatabase.LatestExpectedMigration,
            applied[^1]);
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
    }
}
