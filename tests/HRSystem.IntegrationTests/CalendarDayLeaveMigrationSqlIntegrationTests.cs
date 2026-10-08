using HRSystem.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace HRSystem.IntegrationTests;

[Collection(DisposableSqlServerCollection.Name)]
public sealed class CalendarDayLeaveMigrationSqlIntegrationTests
{
    private const string PreviousMigration =
        "20260806040217_AddLeaveTypeRulesAndStandardCatalog";
    private const string CurrentMigration =
        "20260809073631_AddCalendarDaySpecialLeave";

    [Fact]
    [Trait("Category", "SqlLeave")]
    public async Task Migration_Up_Down_Up_Adds_Nullable_Category_And_Safe_Constraints()
    {
        var database = await DisposableSqlServerDatabase.CreateAsync(
            "CalendarDayLeave",
            applyMigrations: false);
        try
        {
            await using (var before = database.CreateDbContext())
            {
                await before.GetService<IMigrator>().MigrateAsync(
                    PreviousMigration);
                Assert.Equal(0, await ColumnCountAsync(
                    before,
                    "PregnancyDurationCategory"));
            }

            await using (var up = database.CreateDbContext())
            {
                await up.GetService<IMigrator>().MigrateAsync(
                    CurrentMigration);
                Assert.Equal(1, await ColumnCountAsync(
                    up,
                    "PregnancyDurationCategory"));
                Assert.Equal(1, await ConstraintCountAsync(
                    up,
                    "CK_LeaveRequests_DurationHours_NonNegative"));
                Assert.Equal(1, await ConstraintCountAsync(
                    up,
                    "CK_LeaveRequests_PregnancyDurationCategory"));
                Assert.Equal(0, await ConstraintCountAsync(
                    up,
                    "CK_LeaveRequests_DurationHours_Positive"));
                Assert.False(up.Database.HasPendingModelChanges());
            }

            await using (var down = database.CreateDbContext())
            {
                await down.GetService<IMigrator>().MigrateAsync(
                    PreviousMigration);
                Assert.Equal(0, await ColumnCountAsync(
                    down,
                    "PregnancyDurationCategory"));
                Assert.Equal(1, await ConstraintCountAsync(
                    down,
                    "CK_LeaveRequests_DurationHours_Positive"));
            }

            await using (var final = database.CreateDbContext())
            {
                await final.GetService<IMigrator>().MigrateAsync();
                Assert.Equal(
                    DisposableSqlServerDatabase.ExpectedMigrationCount,
                    (await final.Database.GetAppliedMigrationsAsync()).Count());
                Assert.Empty(await final.Database.GetPendingMigrationsAsync());
                Assert.False(final.Database.HasPendingModelChanges());
            }
        }
        finally
        {
            await database.DisposeAsync();
        }

        Assert.False(await DisposableSqlServerDatabase.DatabaseExistsAsync(
            database.DatabaseName));
    }

    private static Task<int> ColumnCountAsync(
        HRSystemDbContext db,
        string columnName) => db.Database.SqlQueryRaw<int>(
        """
        SELECT COUNT(*) AS [Value]
        FROM [sys].[columns]
        WHERE [object_id] = OBJECT_ID(N'[dbo].[LeaveRequests]')
          AND [name] = {0}
        """,
        columnName).SingleAsync();

    private static Task<int> ConstraintCountAsync(
        HRSystemDbContext db,
        string constraintName) => db.Database.SqlQueryRaw<int>(
        """
        SELECT COUNT(*) AS [Value]
        FROM [sys].[check_constraints]
        WHERE [parent_object_id] = OBJECT_ID(N'[dbo].[LeaveRequests]')
          AND [name] = {0}
        """,
        constraintName).SingleAsync();
}
