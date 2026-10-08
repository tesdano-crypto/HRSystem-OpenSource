using HRSystem.Domain.MasterData;
using HRSystem.Infrastructure.Persistence;
using HRSystem.Infrastructure.Persistence.Seed;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging.Abstractions;

namespace HRSystem.IntegrationTests;

[Collection(DisposableSqlServerCollection.Name)]
public sealed class LeaveTypeEmployeeRequestModeConstraintMigrationSqlIntegrationTests
{
    private const string PreviousMigration =
        "20260809073631_AddCalendarDaySpecialLeave";
    private const string CurrentMigration =
        "20260809083358_FixLeaveTypeEmployeeRequestModeConstraint";

    [Fact]
    [Trait("Category", "SqlLeave")]
    public async Task Migration_Up_Down_Up_Enforces_Requestable_Modes_And_Activation()
    {
        var database = await DisposableSqlServerDatabase.CreateAsync(
            "LeaveTypeRequestMode",
            applyMigrations: false);
        try
        {
            await using (var before = database.CreateDbContext())
            {
                await before.GetService<IMigrator>().MigrateAsync(
                    PreviousMigration);
                AssertOriginalConstraint(await ConstraintDefinitionAsync(before));
                await new StandardLeaveTypeSeedService(
                    before,
                    TimeProvider.System,
                    NullLogger<StandardLeaveTypeSeedService>.Instance)
                    .SeedAsync();

                // This test reconstructs the catalog as it existed at the
                // historical migration boundary. COMP_TIME is introduced by
                // the later AddCompTimeLedger migration under test elsewhere.
                await before.LeaveTypes
                    .Where(item => item.Code == "COMP_TIME")
                    .ExecuteDeleteAsync();
            }

            await using (var up = database.CreateDbContext())
            {
                await up.GetService<IMigrator>().MigrateAsync(CurrentMigration);
                AssertExpandedConstraint(await ConstraintDefinitionAsync(up));

                await SetEmployeeRequestEnabledAsync(up, "ANNUAL", false);
                await SetEmployeeRequestEnabledAsync(up, "ANNUAL", true);
                await SetEmployeeRequestEnabledAsync(up, "MATERNITY", false);
                await SetEmployeeRequestEnabledAsync(
                    up,
                    "PARENTAL_LEAVE_WITHOUT_PAY",
                    false);

                var activation = await new CalendarDayLeaveTypeActivationService(
                    up,
                    TimeProvider.System)
                    .ActivateAsync();
                Assert.Equal(3, activation.ActivatedCount);
                Assert.Equal(0, activation.AlreadyActiveCount);
                Assert.Equal(3, await up.LeaveTypes.CountAsync(item =>
                    item.IsEmployeeRequestEnabled &&
                    item.CalculationMode == LeaveCalculationMode.CalendarDays));
                Assert.False(await up.LeaveTypes
                    .Where(item => item.Code == "PARENTAL_LEAVE_WITHOUT_PAY")
                    .Select(item => item.IsEmployeeRequestEnabled)
                    .SingleAsync());

                await Assert.ThrowsAsync<SqlException>(() =>
                    SetEmployeeRequestEnabledAsync(
                        up,
                        "PARENTAL_LEAVE_WITHOUT_PAY",
                        true));
                up.ChangeTracker.Clear();
                Assert.False(await up.LeaveTypes
                    .Where(item => item.Code == "PARENTAL_LEAVE_WITHOUT_PAY")
                    .Select(item => item.IsEmployeeRequestEnabled)
                    .SingleAsync());

                var calendarTypes = await up.LeaveTypes
                    .Where(item => item.CalculationMode ==
                        LeaveCalculationMode.CalendarDays)
                    .ToListAsync();
                foreach (var leaveType in calendarTypes)
                {
                    leaveType.SetEmployeeRequestEnabled(
                        false,
                        TimeProvider.System.GetUtcNow());
                }

                await up.SaveChangesAsync();
            }

            await using (var down = database.CreateDbContext())
            {
                await down.GetService<IMigrator>().MigrateAsync(
                    PreviousMigration);
                AssertOriginalConstraint(await ConstraintDefinitionAsync(down));
                await Assert.ThrowsAsync<SqlException>(() =>
                    SetEmployeeRequestEnabledAsync(down, "MATERNITY", true));
                down.ChangeTracker.Clear();
            }

            await using (var final = database.CreateDbContext())
            {
                await final.GetService<IMigrator>().MigrateAsync();
                AssertExpandedConstraint(await ConstraintDefinitionAsync(final));
                var activation = await new CalendarDayLeaveTypeActivationService(
                    final,
                    TimeProvider.System)
                    .ActivateAsync();
                Assert.Equal(3, activation.ActivatedCount);
                Assert.False(await final.LeaveTypes
                    .Where(item => item.Code == "PARENTAL_LEAVE_WITHOUT_PAY")
                    .Select(item => item.IsEmployeeRequestEnabled)
                    .SingleAsync());
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

    private static Task SetEmployeeRequestEnabledAsync(
        HRSystemDbContext db,
        string code,
        bool enabled) => db.Database.ExecuteSqlInterpolatedAsync(
        $"""
        UPDATE [LeaveTypes]
        SET [IsEmployeeRequestEnabled] = {enabled}
        WHERE [Code] = {code}
        """);

    private static Task<string> ConstraintDefinitionAsync(
        HRSystemDbContext db) => db.Database.SqlQueryRaw<string>(
        """
        SELECT [definition] AS [Value]
        FROM [sys].[check_constraints]
        WHERE [parent_object_id] = OBJECT_ID(N'[dbo].[LeaveTypes]')
          AND [name] = N'CK_LeaveTypes_EmployeeRequestMode'
        """).SingleAsync();

    private static void AssertOriginalConstraint(string definition)
    {
        var normalized = definition.Replace(" ", string.Empty);
        Assert.Contains("[CalculationMode]=(1)", normalized);
        Assert.DoesNotContain("[CalculationMode]=(2)", normalized);
        Assert.DoesNotContain("[CalculationMode]=(3)", normalized);
    }

    private static void AssertExpandedConstraint(string definition)
    {
        var normalized = definition.Replace(" ", string.Empty);
        Assert.Contains("[CalculationMode]=(1)", normalized);
        Assert.Contains("[CalculationMode]=(2)", normalized);
        Assert.DoesNotContain("[CalculationMode]=(3)", normalized);
    }
}
