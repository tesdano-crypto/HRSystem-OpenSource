using HRSystem.Infrastructure.Identity;
using HRSystem.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace HRSystem.IntegrationTests;

[Collection(DisposableSqlServerCollection.Name)]
public sealed class LinePairingMigrationSqlIntegrationTests
{
    private const string Previous =
        "20260830095141_AddPayrollLegacyAdjustmentComponents";
    private const string Current =
        "20260830141851_AddOwnerPrivateLinePairing";

    [Fact]
    public async Task Migration_Up_Down_Up_Backfills_Metadata_And_Preserves_Bindings()
    {
        await using var database = await DisposableSqlServerDatabase.CreateAsync(
            "OwnerLinePairing", applyMigrations: false);
        await using var db = database.CreateDbContext();
        var migrator = db.GetService<IMigrator>();

        await migrator.MigrateAsync(Previous);
        Assert.Equal(32, (await db.Database.GetAppliedMigrationsAsync()).Count());
        db.Set<ApplicationUser>().AddRange(
            User("owner-active"), User("owner-revoked"));
        await db.SaveChangesAsync();
        await db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO [LineUserBindings]
                ([Id], [HrSystemUserId], [LineUserId], [IsActive],
                 [VerifiedAtUtc], [CreatedAtUtc], [UpdatedAtUtc])
            VALUES
                ('11111111-1111-1111-1111-111111111111', 'owner-active',
                 'U-active', 1, '2026-08-30T06:00:00+00:00',
                 '2026-08-30T06:00:00+00:00', '2026-08-30T06:00:00+00:00'),
                ('22222222-2222-2222-2222-222222222222', 'owner-revoked',
                 'U-revoked', 0, '2026-08-29T06:00:00+00:00',
                 '2026-08-29T06:00:00+00:00', '2026-08-30T05:00:00+00:00');
            """);
        var before = await BindingBusinessFingerprintAsync(db);

        await migrator.MigrateAsync(Current);
        Assert.Equal(33, (await db.Database.GetAppliedMigrationsAsync()).Count());
        await AssertSchemaAndBackfillAsync(db);
        Assert.Equal(before, await BindingBusinessFingerprintAsync(db));

        await migrator.MigrateAsync(Previous);
        Assert.Equal(32, (await db.Database.GetAppliedMigrationsAsync()).Count());
        Assert.False(await TableExistsAsync(db, "LinePairingRequests"));
        Assert.Equal(before, await BindingBusinessFingerprintAsync(db));

        await migrator.MigrateAsync(Current);
        await AssertSchemaAndBackfillAsync(db);
        Assert.Equal(
            db.Database.GetMigrations().SkipWhile(x => x != Current).Skip(1),
            await db.Database.GetPendingMigrationsAsync());
    }

    private static ApplicationUser User(string id) => new()
    {
        Id = id,
        UserName = id,
        NormalizedUserName = id.ToUpperInvariant(),
        DisplayName = "Owner test",
        IsActive = true,
        CreatedAtUtc = new DateTimeOffset(2026, 8, 30, 6, 0, 0, TimeSpan.Zero)
    };

    private static async Task AssertSchemaAndBackfillAsync(HRSystemDbContext db)
    {
        Assert.True(await TableExistsAsync(db, "LinePairingRequests"));
        Assert.True(await ColumnExistsAsync(db, "LineUserBindings", "TargetType"));
        Assert.True(await ColumnExistsAsync(db, "LineUserBindings", "RevokedAtUtc"));
        Assert.True(await IndexExistsAsync(db, "LinePairingRequests",
            "UX_LinePairingRequests_TokenHash"));
        Assert.True(await IndexExistsAsync(db, "LineUserBindings",
            "UX_LineUserBindings_HRSystemUser_Active"));
        Assert.True(await IndexExistsAsync(db, "LineUserBindings",
            "UX_LineUserBindings_LineUser_Active"));
        Assert.Equal(2, await NoActionForeignKeyCountAsync(db));
        Assert.Equal(0, await ScalarIntAsync(db,
            "SELECT COUNT(*) FROM [LineUserBindings] WHERE [TargetType] <> 1"));
        Assert.Equal(0, await ScalarIntAsync(db,
            "SELECT COUNT(*) FROM [LineUserBindings] WHERE [IsActive] = 0 AND [RevokedAtUtc] IS NULL"));
        Assert.Equal(1, await ScalarIntAsync(db,
            "SELECT COUNT(*) FROM [LineUserBindings] WHERE [IsActive] = 1 AND [RevokedAtUtc] IS NULL"));
    }

    private static async Task<string> BindingBusinessFingerprintAsync(
        HRSystemDbContext db)
    {
        await using var command = db.Database.GetDbConnection().CreateCommand();
        if (command.Connection!.State != System.Data.ConnectionState.Open)
            await command.Connection.OpenAsync();
        command.CommandText =
            """
            SELECT CONVERT(varchar(64), HASHBYTES('SHA2_256',
                STRING_AGG(CONCAT(CONVERT(varchar(36), [Id]), '|',
                    [HrSystemUserId], '|', [LineUserId], '|', [IsActive], '|',
                    CONVERT(varchar(40), [VerifiedAtUtc], 127), '|',
                    CONVERT(varchar(40), [CreatedAtUtc], 127), '|',
                    CONVERT(varchar(40), [UpdatedAtUtc], 127)), CHAR(10))
                    WITHIN GROUP (ORDER BY [Id])), 2)
            FROM [LineUserBindings];
            """;
        return Convert.ToString(await command.ExecuteScalarAsync())!;
    }

    private static async Task<bool> TableExistsAsync(HRSystemDbContext db,
        string table) => await ScalarIntAsync(db,
        "SELECT COUNT(*) FROM sys.tables WHERE name = @name", ("@name", table)) == 1;

    private static async Task<bool> ColumnExistsAsync(HRSystemDbContext db,
        string table, string column) => await ScalarIntAsync(db,
        "SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(@table) AND name = @column",
        ("@table", table), ("@column", column)) == 1;

    private static async Task<bool> IndexExistsAsync(HRSystemDbContext db,
        string table, string index) => await ScalarIntAsync(db,
        "SELECT COUNT(*) FROM sys.indexes WHERE object_id = OBJECT_ID(@table) AND name = @index",
        ("@table", table), ("@index", index)) == 1;

    private static Task<int> NoActionForeignKeyCountAsync(HRSystemDbContext db) =>
        ScalarIntAsync(db,
            "SELECT COUNT(*) FROM sys.foreign_keys WHERE parent_object_id = OBJECT_ID('LinePairingRequests') AND delete_referential_action = 0");

    private static async Task<int> ScalarIntAsync(HRSystemDbContext db, string sql,
        params (string Name, object Value)[] parameters)
    {
        await using var command = db.Database.GetDbConnection().CreateCommand();
        if (command.Connection!.State != System.Data.ConnectionState.Open)
            await command.Connection.OpenAsync();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = name; parameter.Value = value;
            command.Parameters.Add(parameter);
        }
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }
}
