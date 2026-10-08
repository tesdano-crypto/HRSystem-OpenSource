using System.Data.Common;
using HRSystem.Application.Abstractions.Security;
using HRSystem.Application.Attendance;
using HRSystem.Application.Security;
using HRSystem.Domain.Attendance;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace HRSystem.IntegrationTests;

public sealed class BioWebTaImportStatusSqlIntegrationTests
{
    [Fact]
    [Trait("Category", "SqlAttendance")]
    public async Task Status_query_orders_and_limits_entities_on_sql_server()
    {
        await using var database = await DisposableSqlServerDatabase.CreateAsync(
            "ImportStatus");
        var commands = new CommandCaptureInterceptor();
        await using var db = database.CreateDbContext(commands);
        var startedAtUtc = new DateTimeOffset(
            2026, 8, 13, 0, 0, 0, TimeSpan.Zero);
        var batches = Enumerable.Range(0, 18)
            .Select(index => CompletedBatch(
                index % 2 == 0
                    ? BioWebTaImportTriggerType.Scheduled
                    : BioWebTaImportTriggerType.Manual,
                startedAtUtc.AddMinutes(index)))
            .ToArray();
        db.BioWebTaImportBatches.AddRange(batches);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        commands.Clear();

        var result = await Service(db).GetAsync();

        Assert.Equal(15, result.RecentBatches.Count);
        Assert.Equal(
            batches.OrderByDescending(item => item.StartedAtUtc)
                .Take(15)
                .Select(item => item.Id),
            result.RecentBatches.Select(item => item.Id));
        Assert.Equal(batches[^2].Id, result.LastScheduled!.Id);
        Assert.Equal(batches[^1].Id, result.LastManual!.Id);
        Assert.Contains(commands.Commands, command =>
            command.Contains("TOP", StringComparison.OrdinalIgnoreCase) &&
            command.Contains("ORDER BY", StringComparison.OrdinalIgnoreCase) &&
            command.Contains("StartedAtUtc", StringComparison.Ordinal));
        Assert.DoesNotContain(commands.Commands, command =>
            command.TrimStart().StartsWith("INSERT", StringComparison.OrdinalIgnoreCase) ||
            command.TrimStart().StartsWith("UPDATE", StringComparison.OrdinalIgnoreCase) ||
            command.TrimStart().StartsWith("DELETE", StringComparison.OrdinalIgnoreCase));
    }

    private static BioWebTaImportStatusService Service(
        HRSystem.Infrastructure.Persistence.HRSystemDbContext db) =>
        new(
            db,
            new BioWebTaScheduledImportOptions
            {
                Enabled = true,
                OverlapDays = 3,
                PageSize = 500,
                MaxExecutionMinutes = 30,
                TimeZoneId = "Asia/Taipei"
            },
            new SqlAdminCurrentUser(),
            new FixedTimeProvider(
                new DateTimeOffset(2026, 8, 14, 0, 40, 0, TimeSpan.Zero)));

    private static BioWebTaImportBatch CompletedBatch(
        BioWebTaImportTriggerType trigger,
        DateTimeOffset startedAtUtc)
    {
        var batch = new BioWebTaImportBatch(
            Guid.NewGuid(),
            trigger,
            new DateTime(2026, 8, 12),
            new DateTime(2026, 8, 14, 8, 40, 0),
            "Asia/Taipei",
            startedAtUtc,
            "sql-integration-test",
            1,
            "1.0.0-test");
        batch.Complete(
            sourceRowCount: 1,
            insertedCount: 0,
            duplicateCount: 1,
            completedWithWarnings: false,
            startedAtUtc.AddSeconds(3));
        return batch;
    }

    private sealed class SqlAdminCurrentUser : ICurrentUser
    {
        public string? UserId => "biowebta-status-sql-test-admin";
        public Guid? EmployeeId => null;
        public string? DisplayName => "BioWebTA Status SQL Test Admin";
        public string? IpAddress => "127.0.0.1";
        public bool IsAuthenticated => true;
        public bool IsInRole(string role) => role == RoleNames.Admin;
        public bool HasPermission(string policy) =>
            RolePermissions.HasPermission([RoleNames.Admin], policy);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class CommandCaptureInterceptor : DbCommandInterceptor
    {
        public List<string> Commands { get; } = [];

        public void Clear() => Commands.Clear();

        public override ValueTask<InterceptionResult<DbDataReader>>
            ReaderExecutingAsync(
                DbCommand command,
                CommandEventData eventData,
                InterceptionResult<DbDataReader> result,
                CancellationToken cancellationToken = default)
        {
            Commands.Add(command.CommandText);
            return base.ReaderExecutingAsync(
                command,
                eventData,
                result,
                cancellationToken);
        }

        public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<object> result,
            CancellationToken cancellationToken = default)
        {
            Commands.Add(command.CommandText);
            return base.ScalarExecutingAsync(
                command,
                eventData,
                result,
                cancellationToken);
        }
    }
}
