using HRSystem.Domain.MasterData;
using HRSystem.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace HRSystem.IntegrationTests;

[Collection(DisposableSqlServerCollection.Name)]
public sealed class LeaveTypeCatalogMigrationSqlIntegrationTests
{
    private const string PreviousMigration =
        "20260805084057_AddApprovedLeaveCancellation";
    private const string CurrentMigration =
        "20260806040217_AddLeaveTypeRulesAndStandardCatalog";

    [Fact]
    [Trait("Category", "SqlLeave")]
    public async Task Migration_Up_Down_Up_Backfills_Metadata_Without_Changing_Existing_Business_Data()
    {
        var database = await DisposableSqlServerDatabase.CreateAsync(
            "LeaveTypeCatalog",
            applyMigrations: false);
        try
        {
            await using (var before = database.CreateDbContext())
            {
                await before.GetService<IMigrator>().MigrateAsync(PreviousMigration);
                await before.Database.ExecuteSqlRawAsync(
                    """
                    INSERT INTO [LeaveTypes]
                        ([Id], [Code], [Name], [Unit], [MinimumUnit], [RequiresReason],
                         [IsPaid], [IsActive], [SortOrder], [CreatedAtUtc], [UpdatedAtUtc])
                    VALUES
                        ('61000000-0000-0000-0000-000000000001', N'LEGACY_HOUR', N'舊小時假', 1, 0.50, 1, 0, 1, 10, '2026-08-06T00:00:00+00:00', '2026-08-06T00:00:00+00:00'),
                        ('61000000-0000-0000-0000-000000000002', N'LEGACY_DAY', N'舊日假', 2, 1.00, 1, 1, 0, 20, '2026-08-06T00:00:00+00:00', '2026-08-06T00:00:00+00:00');
                    """);
                Assert.Equal(2, await ScalarAsync(before, "SELECT COUNT(*) AS [Value] FROM [LeaveTypes]"));
            }

            var beforeChecksum = 0;
            await using (var before = database.CreateDbContext())
            {
                beforeChecksum = await BusinessChecksumAsync(before);
            }

            await using (var up = database.CreateDbContext())
            {
                await up.GetService<IMigrator>().MigrateAsync(CurrentMigration);

                var hour = await up.LeaveTypes.AsNoTracking()
                    .SingleAsync(item => item.Code == "LEGACY_HOUR");
                var day = await up.LeaveTypes.AsNoTracking()
                    .SingleAsync(item => item.Code == "LEGACY_DAY");

                Assert.Equal(LeaveCategory.General, hour.Category);
                Assert.Equal(LeaveCalculationMode.WorkingSchedule, hour.CalculationMode);
                Assert.True(hour.AllowHourlyRequest);
                Assert.Equal(30, hour.MinimumRequestMinutes);
                Assert.True(hour.IsEmployeeRequestEnabled);
                Assert.False(day.AllowHourlyRequest);
                Assert.Equal(480, day.MinimumRequestMinutes);
                Assert.Equal(beforeChecksum, await BusinessChecksumAsync(up));
                Assert.Equal(100, await ScalarAsync(up,
                    "SELECT CONVERT(int, [max_length]) AS [Value] FROM [sys].[columns] WHERE [object_id] = OBJECT_ID(N'[dbo].[LeaveTypes]') AND [name] = N'Code'"));
                Assert.Equal(5, await ScalarAsync(up,
                    "SELECT COUNT(*) AS [Value] FROM [sys].[check_constraints] WHERE [parent_object_id] = OBJECT_ID(N'[dbo].[LeaveTypes]') AND [name] LIKE N'CK_LeaveTypes_%' AND [name] NOT IN (N'CK_LeaveTypes_MinimumUnit_Positive', N'CK_LeaveTypes_SortOrder_NonNegative')"));
                Assert.Equal(0, await ScalarAsync(up,
                    "SELECT COUNT(*) AS [Value] FROM [LeaveRequests]"));
                Assert.Equal(0, await ScalarAsync(up,
                    "SELECT COUNT(*) AS [Value] FROM [AttendanceRawEvents]"));
                Assert.Equal(0, await ScalarAsync(up,
                    "SELECT COUNT(*) AS [Value] FROM [AttendanceAdjustments]"));
            }

            await using (var down = database.CreateDbContext())
            {
                await down.GetService<IMigrator>().MigrateAsync(PreviousMigration);
                Assert.Equal(beforeChecksum, await BusinessChecksumAsync(down));
                Assert.Equal(40, await ScalarAsync(down,
                    "SELECT CONVERT(int, [max_length]) AS [Value] FROM [sys].[columns] WHERE [object_id] = OBJECT_ID(N'[dbo].[LeaveTypes]') AND [name] = N'Code'"));
            }

            await using (var final = database.CreateDbContext())
            {
                await final.GetService<IMigrator>().MigrateAsync();
                Assert.Equal(beforeChecksum, await BusinessChecksumAsync(final));
                Assert.Equal(
                    DisposableSqlServerDatabase.ExpectedMigrationCount,
                    (await final.Database.GetAppliedMigrationsAsync()).Count());
                Assert.Empty(await final.Database.GetPendingMigrationsAsync());
            }
        }
        finally
        {
            await database.DisposeAsync();
        }

        Assert.False(await DisposableSqlServerDatabase.DatabaseExistsAsync(
            database.DatabaseName));
    }

    private static Task<int> BusinessChecksumAsync(HRSystemDbContext db) =>
        ScalarAsync(db,
            "SELECT COALESCE(CHECKSUM_AGG(BINARY_CHECKSUM([Id], [Code], [Name], [Unit], [MinimumUnit], [RequiresReason], [IsPaid], [IsActive], [SortOrder], [CreatedAtUtc], [UpdatedAtUtc])), 0) AS [Value] FROM [LeaveTypes] WHERE [Code] IN (N'LEGACY_HOUR', N'LEGACY_DAY')");

    private static Task<int> ScalarAsync(HRSystemDbContext db, string sql) =>
        db.Database.SqlQueryRaw<int>(sql).SingleAsync();
}
