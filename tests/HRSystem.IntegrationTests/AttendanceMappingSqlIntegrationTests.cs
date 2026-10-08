using HRSystem.Application.Abstractions.Security;
using HRSystem.Application.Attendance;
using HRSystem.Application.Common.Auditing;
using HRSystem.Application.Security;
using HRSystem.Domain.Attendance;
using HRSystem.Domain.MasterData;
using HRSystem.Infrastructure.Persistence;
using HRSystem.Infrastructure.Attendance;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace HRSystem.IntegrationTests;

public sealed class AttendanceMappingSqlIntegrationTests
{
    private const string DatabasePrefix = "HRSystem_Phase71_Mapping_Test_";

    [Fact]
    [Trait("Category", "SqlAttendance")]
    public async Task Unmapped_pin_summary_executes_on_sql_server_without_writes()
    {
        var databaseName = $"{DatabasePrefix}{Guid.NewGuid():N}";
        Exception? originalFailure = null;
        var databaseCreated = false;
        try
        {
            await CreateDatabaseAsync(databaseName);
            databaseCreated = true;
            var connectionString = BuildValidatedConnectionString(databaseName);
            await using var dbContext = CreateContext(connectionString);
            await dbContext.Database.MigrateAsync();
            var service = new BioWebPersonMappingService(
                dbContext,
                new SqlAdminCurrentUser(),
                TimeProvider.System,
                new AttendanceRecalculationEngine(dbContext, TimeProvider.System),
                NullLogger<BioWebPersonMappingService>.Instance);

            var empty = await service.GetUnmappedPinsAsync();

            Assert.Empty(empty);
            Assert.Equal(0, await dbContext.AttendanceRawEvents.CountAsync());
            Assert.Equal(0, await dbContext.AttendanceSyncStates.CountAsync());
            Assert.Equal(0, await dbContext.BioWebPersonMappings.CountAsync());
            Assert.Equal(0, await dbContext.AuditLogs.CountAsync());

            dbContext.AttendanceRawEvents.AddRange(
                Event(1, "00042", new DateTime(2026, 7, 27, 8, 0, 0)),
                Event(2, "00042", new DateTime(2026, 7, 27, 17, 0, 0)),
                Event(3, "00010", new DateTime(2026, 7, 27, 9, 0, 0)));
            await dbContext.SaveChangesAsync();
            dbContext.ClearTrackedChanges();

            var summaries = await service.GetUnmappedPinsAsync();

            Assert.Collection(
                summaries,
                summary => Assert.Equal("00010", summary.BioWebPin),
                summary => Assert.Equal("00042", summary.BioWebPin));
            var summary = summaries[1];
            Assert.IsType<string>(summary.BioWebPin);
            Assert.Equal(2L, summary.EventCount);
            Assert.Equal(new DateTime(2026, 7, 27, 8, 0, 0), summary.FirstEventLocalDateTime);
            Assert.Equal(new DateTime(2026, 7, 27, 17, 0, 0), summary.LastEventLocalDateTime);
            Assert.Equal(3, await dbContext.AttendanceRawEvents.CountAsync());
            Assert.Equal(0, await dbContext.AttendanceSyncStates.CountAsync());
            Assert.Equal(0, await dbContext.BioWebPersonMappings.CountAsync());
            Assert.Equal(0, await dbContext.AuditLogs.CountAsync());
            Assert.Empty(dbContext.ChangeTracker.Entries());
        }
        catch (Exception exception)
        {
            originalFailure = exception;
            throw;
        }
        finally
        {
            if (databaseCreated)
            {
                await DropDatabasePreservingFailureAsync(databaseName, originalFailure);
            }

            Assert.Equal(0, await DisposableDatabaseCountAsync());
        }
    }

    [Fact]
    [Trait("Category", "SqlAttendance")]
    public async Task Recalculation_failure_rolls_back_raw_event_relink_and_audit()
    {
        var databaseName = $"{DatabasePrefix}{Guid.NewGuid():N}";
        Exception? originalFailure = null;
        var databaseCreated = false;
        try
        {
            await CreateDatabaseAsync(databaseName);
            databaseCreated = true;
            var connectionString = BuildValidatedConnectionString(databaseName);
            await using var dbContext = CreateContext(connectionString);
            await dbContext.Database.MigrateAsync();
            var now = new DateTimeOffset(
                2026, 8, 4, 0, 0, 0, TimeSpan.Zero);
            var department = new Department(
                Guid.NewGuid(), "SQL-REL", "SQL Relink", now);
            var employee = new Employee(
                Guid.NewGuid(), "EMP9901", "SQL Relink Employee",
                department.Id, new DateOnly(2026, 1, 1), now);
            dbContext.AddRange(department, employee);
            await dbContext.SaveChangesAsync();
            var service = new BioWebPersonMappingService(
                dbContext,
                new SqlAdminCurrentUser(),
                TimeProvider.System,
                new FailingRecalculationEngine(),
                NullLogger<BioWebPersonMappingService>.Instance);
            var mapping = await service.CreateAsync(new CreateBioWebPersonMappingRequest
            {
                EmployeeId = employee.Id,
                BioWebPin = "ROLLBACK-PIN",
                EffectiveFrom = new DateTime(
                    2026, 8, 4, 0, 0, 0, DateTimeKind.Unspecified)
            });
            dbContext.AttendanceRawEvents.Add(Event(
                1001,
                "ROLLBACK-PIN",
                new DateTime(2026, 8, 4, 8, 0, 0)));
            await dbContext.SaveChangesAsync();
            dbContext.ClearTrackedChanges();

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.RelinkUnmappedEventsAsync(mapping.Id, mapping.RowVersion));

            Assert.Null((await dbContext.AttendanceRawEvents
                .AsNoTracking()
                .SingleAsync()).EmployeeId);
            Assert.Equal(0, await dbContext.DailyAttendanceResults.CountAsync());
            Assert.Equal(0, await dbContext.AuditLogs.CountAsync(item =>
                item.Action == AuditActions.AttendanceUnmappedEventsRelinked));
        }
        catch (Exception exception)
        {
            originalFailure = exception;
            throw;
        }
        finally
        {
            if (databaseCreated)
            {
                await DropDatabasePreservingFailureAsync(databaseName, originalFailure);
            }

            Assert.Equal(0, await DisposableDatabaseCountAsync());
        }
    }

    [Fact]
    [Trait("Category", "SqlAttendance")]
    public async Task Scheduled_import_recalculation_failure_rolls_back_batch_raw_event_and_audit()
    {
        var databaseName = $"{DatabasePrefix}{Guid.NewGuid():N}";
        Exception? originalFailure = null;
        var databaseCreated = false;
        try
        {
            await CreateDatabaseAsync(databaseName);
            databaseCreated = true;
            var connectionString = BuildValidatedConnectionString(databaseName);
            await using var dbContext = CreateContext(connectionString);
            await dbContext.Database.MigrateAsync();
            var now = new DateTimeOffset(2026, 8, 13, 1, 0, 0, TimeSpan.Zero);
            var department = new Department(
                Guid.NewGuid(), "SQL-IMP", "SQL Import", now);
            var employee = new Employee(
                Guid.NewGuid(), "EMP9902", "SQL Import Employee",
                department.Id, new DateOnly(2026, 1, 1), now);
            var mapping = new BioWebPersonMapping(
                Guid.NewGuid(), employee.Id, "ROLLBACK-IMPORT",
                new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Unspecified),
                null, now);
            dbContext.AddRange(department, employee, mapping);
            await dbContext.SaveChangesAsync();
            dbContext.ClearTrackedChanges();
            var source = new WindowSource(new BioWebAttendanceSourceRecord(
                2001,
                "ROLLBACK-IMPORT",
                "TEST-DEVICE",
                new DateTime(2026, 8, 12, 8, 0, 0, DateTimeKind.Unspecified),
                0,
                1,
                null));
            var coordinator = new BioWebTaImportCoordinator(
                dbContext,
                source,
                new AvailableExecutionLock(),
                new FailingRecalculationEngine(),
                new BioWebTaScheduledImportOptions
                {
                    Enabled = true,
                    OverlapDays = 3,
                    PageSize = 500,
                    MaxExecutionMinutes = 30,
                    TimeZoneId = "Asia/Taipei"
                },
                new SqlAdminCurrentUser(),
                TimeProvider.System,
                NullLogger<BioWebTaImportCoordinator>.Instance);

            var result = await coordinator.RunAsync(new(
                BioWebTaImportTriggerType.Scheduled,
                new DateTime(2026, 8, 11, 0, 0, 0, DateTimeKind.Unspecified),
                new DateTime(2026, 8, 13, 0, 0, 0, DateTimeKind.Unspecified)));

            Assert.Equal(BioWebTaImportExecutionOutcome.Failed, result.Outcome);
            Assert.Equal(0, await dbContext.AttendanceRawEvents.CountAsync());
            Assert.Equal(BioWebTaImportBatchStatus.Failed,
                (await dbContext.BioWebTaImportBatches.SingleAsync()).Status);
            Assert.Equal(0, await dbContext.BioWebTaImportBatchIssues.CountAsync());
            Assert.Equal(0, await dbContext.AuditLogs.CountAsync());
            Assert.Equal(0, await dbContext.DailyAttendanceResults.CountAsync());
        }
        catch (Exception exception)
        {
            originalFailure = exception;
            throw;
        }
        finally
        {
            if (databaseCreated)
            {
                await DropDatabasePreservingFailureAsync(databaseName, originalFailure);
            }

            Assert.Equal(0, await DisposableDatabaseCountAsync());
        }
    }

    [Fact]
    [Trait("Category", "SqlAttendance")]
    public async Task Sql_application_lock_allows_only_one_import_execution()
    {
        var databaseName = $"{DatabasePrefix}{Guid.NewGuid():N}";
        Exception? originalFailure = null;
        var databaseCreated = false;
        try
        {
            await CreateDatabaseAsync(databaseName);
            databaseCreated = true;
            var connectionString = BuildValidatedConnectionString(databaseName);
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:HRSystemDb"] = connectionString
                })
                .Build();
            var firstLock = new SqlBioWebTaImportExecutionLock(configuration);
            var secondLock = new SqlBioWebTaImportExecutionLock(configuration);

            await using (var first = await firstLock.TryAcquireAsync(TimeSpan.Zero))
            {
                Assert.NotNull(first);
                Assert.Null(await secondLock.TryAcquireAsync(TimeSpan.Zero));
            }

            await using var afterRelease = await secondLock.TryAcquireAsync(TimeSpan.Zero);
            Assert.NotNull(afterRelease);
        }
        catch (Exception exception)
        {
            originalFailure = exception;
            throw;
        }
        finally
        {
            if (databaseCreated)
            {
                await DropDatabasePreservingFailureAsync(databaseName, originalFailure);
            }

            Assert.Equal(0, await DisposableDatabaseCountAsync());
        }
    }

    private static AttendanceRawEvent Event(
        long externalEventId,
        string pin,
        DateTime eventTime) =>
        new(
            Guid.NewGuid(),
            AttendanceSourceSystems.BioWebTa,
            externalEventId,
            null,
            pin,
            "TEST-DEVICE",
            DateTime.SpecifyKind(eventTime, DateTimeKind.Unspecified),
            null,
            null,
            null,
            new DateTimeOffset(2026, 7, 28, 0, 0, 0, TimeSpan.Zero));

    private static HRSystemDbContext CreateContext(string connectionString) =>
        new(
            new DbContextOptionsBuilder<HRSystemDbContext>()
                .UseSqlServer(
                    connectionString,
                    sql => sql.MigrationsAssembly(
                        typeof(HRSystemDbContext).Assembly.FullName))
                .Options);

    private static async Task CreateDatabaseAsync(string databaseName)
    {
        _ = BuildValidatedConnectionString(databaseName);
        await using var connection = new SqlConnection(BuildMasterConnectionString());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"CREATE DATABASE {QuoteDatabaseName(databaseName)}";
        await command.ExecuteNonQueryAsync();
    }

    private static async Task DropDatabasePreservingFailureAsync(
        string databaseName,
        Exception? originalFailure)
    {
        try
        {
            _ = BuildValidatedConnectionString(databaseName);
            SqlConnection.ClearAllPools();
            await using var connection = new SqlConnection(BuildMasterConnectionString());
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            var quotedName = QuoteDatabaseName(databaseName);
            command.CommandText =
                $"ALTER DATABASE {quotedName} SET SINGLE_USER WITH ROLLBACK IMMEDIATE; " +
                $"DROP DATABASE {quotedName};";
            await command.ExecuteNonQueryAsync();
        }
        catch (Exception cleanupFailure) when (originalFailure is not null)
        {
            throw new AggregateException(
                $"Test and cleanup failed for {databaseName}.",
                originalFailure,
                cleanupFailure);
        }
    }

    private static async Task<int> DisposableDatabaseCountAsync()
    {
        await using var connection = new SqlConnection(BuildMasterConnectionString());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT COUNT(*) FROM [sys].[databases] " +
            "WHERE [name] LIKE N'HRSystem[_]Phase71[_]Mapping[_]Test[_]%'";
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static string BuildValidatedConnectionString(string databaseName)
    {
        ValidateDisposableDatabaseName(databaseName);
        var builder = NewLocalIntegratedBuilder();
        builder.InitialCatalog = databaseName;
        if (!IsApprovedLocalServer(builder.DataSource) ||
            !builder.IntegratedSecurity ||
            !string.IsNullOrWhiteSpace(builder.UserID))
        {
            throw new InvalidOperationException(
                "Rejected unsafe Phase 7.1 disposable SQL target.");
        }

        return builder.ConnectionString;
    }

    private static string BuildMasterConnectionString()
    {
        var builder = NewLocalIntegratedBuilder();
        builder.InitialCatalog = "master";
        return builder.ConnectionString;
    }

    private static SqlConnectionStringBuilder NewLocalIntegratedBuilder() => new()
    {
        DataSource = @".\SQLEXPRESS",
        IntegratedSecurity = true,
        Encrypt = true,
        TrustServerCertificate = true,
        Pooling = false
    };

    private static void ValidateDisposableDatabaseName(string databaseName)
    {
        var suffix = databaseName.StartsWith(DatabasePrefix, StringComparison.Ordinal)
            ? databaseName[DatabasePrefix.Length..]
            : string.Empty;
        if (!databaseName.StartsWith(DatabasePrefix, StringComparison.Ordinal) ||
            !Guid.TryParseExact(suffix, "N", out _) ||
            databaseName.Contains("HRSystemDb", StringComparison.OrdinalIgnoreCase) ||
            databaseName.Contains("PRODUCTION", StringComparison.OrdinalIgnoreCase) ||
            databaseName.Contains("EXTERNAL_ERP", StringComparison.OrdinalIgnoreCase) ||
            databaseName.Contains("EXTERNAL_ATTENDANCE_DB", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Rejected unsafe Phase 7.1 disposable database name.");
        }
    }

    private static bool IsApprovedLocalServer(string server) =>
        string.Equals(server.Trim(), @".\SQLEXPRESS", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(
            server.Trim(),
            $@"{Environment.MachineName}\SQLEXPRESS",
            StringComparison.OrdinalIgnoreCase);

    private static string QuoteDatabaseName(string databaseName)
    {
        ValidateDisposableDatabaseName(databaseName);
        using var builder = new SqlCommandBuilder();
        return builder.QuoteIdentifier(databaseName);
    }

    private sealed class SqlAdminCurrentUser : ICurrentUser
    {
        public string? UserId => "phase71-mapping-sql-test-admin";
        public Guid? EmployeeId => null;
        public string? DisplayName => "Phase 7.1 SQL Test Admin";
        public string? IpAddress => "127.0.0.1";
        public bool IsAuthenticated => true;
        public bool IsInRole(string role) => role == RoleNames.Admin;
        public bool HasPermission(string policy) =>
            RolePermissions.HasPermission([RoleNames.Admin], policy);
    }

    private sealed class FailingRecalculationEngine : IAttendanceRecalculationEngine
    {
        public Task<AttendanceRecalculationOutcome> RecalculateKeysAsync(
            IReadOnlyCollection<AttendanceRecalculationKey> keys,
            CancellationToken cancellationToken = default,
            IReadOnlyCollection<Guid>? excludedApprovedLeaveRequestIds = null) =>
            throw new InvalidOperationException("Controlled recalculation failure.");

        public Task<AttendanceRecalculationOutcome> RecalculateRangeAsync(
            DateOnly dateFrom,
            DateOnly dateTo,
            Guid? employeeId = null,
            IReadOnlyCollection<PendingApprovedLeave>? pendingApprovedLeaves = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class AvailableExecutionLock : IBioWebTaImportExecutionLock
    {
        public ValueTask<IAsyncDisposable?> TryAcquireAsync(
            TimeSpan timeout,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IAsyncDisposable?>(new Lease());

        private sealed class Lease : IAsyncDisposable
        {
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private sealed class WindowSource(params BioWebAttendanceSourceRecord[] records)
        : IBioWebTaAttendanceSource
    {
        public Task<IReadOnlyList<BioWebAttendanceSourceRecord>> ReadAfterAsync(
            long lastExternalEventId,
            int batchSize,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<BioWebAttendanceSourceRecord>> ReadWindowPageAsync(
            BioWebTaSourceWindowPageRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<BioWebAttendanceSourceRecord>>(
                records.Where(item =>
                        item.EventLocalDateTime >= request.QueryFromLocal &&
                        item.EventLocalDateTime < request.QueryToLocal)
                    .OrderBy(item => item.EventLocalDateTime)
                    .ThenBy(item => item.ExternalEventId)
                    .Take(request.PageSize)
                    .ToArray());
    }
}
