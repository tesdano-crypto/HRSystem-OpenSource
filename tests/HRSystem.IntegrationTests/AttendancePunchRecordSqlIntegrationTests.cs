using System.Data.Common;
using HRSystem.Application.Abstractions.Security;
using HRSystem.Application.Attendance;
using HRSystem.Application.Common.Exceptions;
using HRSystem.Application.Security;
using HRSystem.Domain.Attendance;
using HRSystem.Domain.MasterData;
using HRSystem.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace HRSystem.IntegrationTests;

public sealed class AttendancePunchRecordSqlIntegrationTests
{
    private const string DatabasePrefix = "HRSystem_Phase72_Punch_Test_";
    private static readonly DateTimeOffset Now =
        new(2026, 7, 28, 1, 0, 0, TimeSpan.Zero);

    [Fact]
    [Trait("Category", "SqlAttendance")]
    public async Task Complete_filtered_paged_query_translates_on_sql_server_without_writes()
    {
        var databaseName = DatabasePrefix + Guid.NewGuid().ToString("N");
        Exception? originalFailure = null;
        var databaseCreated = false;
        try
        {
            await CreateDatabaseAsync(databaseName);
            databaseCreated = true;
            var commandCapture = new CommandCaptureInterceptor();
            await using var db = CreateContext(
                BuildConnectionString(databaseName),
                commandCapture);
            await db.Database.MigrateAsync();
            var department = new Department(Guid.NewGuid(), "ATT", "Attendance", Now);
            var employee = new Employee(
                Guid.NewGuid(), "EMP9701", "SQL Employee", department.Id,
                new DateOnly(2026, 1, 1), Now);
            db.Departments.Add(department);
            db.Employees.Add(employee);
            db.AttendanceRawEvents.AddRange(
                Event(100, employee.Id, "05", Local(2026, 7, 28, 8, 30), "DEVICE-1", 7, 9),
                Event(99, employee.Id, "5", Local(2026, 7, 28, 8, 30), "DEVICE-1", 7, 9),
                Event(98, null, "UNMAPPED", Local(2026, 7, 28, 8), "DEVICE-2", 3, 4));
            await db.SaveChangesAsync();
            db.ClearTrackedChanges();
            commandCapture.Clear();
            var service = new AttendancePunchRecordService(
                db,
                new SqlAdminCurrentUser(),
                TimeProvider.System);

            var employeeOptions = await service.GetEmployeeOptionsAsync();
            var employeeOption = Assert.Single(employeeOptions);
            Assert.Equal(employee.Id, employeeOption.EmployeeId);
            Assert.Single(commandCapture.Commands);
            Assert.DoesNotContain(
                "INSERT",
                commandCapture.Commands[0],
                StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(
                "UPDATE",
                commandCapture.Commands[0],
                StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(
                "DELETE",
                commandCapture.Commands[0],
                StringComparison.OrdinalIgnoreCase);
            commandCapture.Clear();

            await Assert.ThrowsAsync<ApplicationValidationException>(() =>
                service.GetListAsync(new AttendancePunchRecordQuery
                {
                    DateFrom = new DateOnly(2026, 1, 1),
                    DateTo = new DateOnly(2027, 1, 2),
                    EmployeeId = employee.Id
                }));
            Assert.Empty(commandCapture.Commands);

            var result = await service.GetListAsync(new AttendancePunchRecordQuery
            {
                DateFrom = new DateOnly(2026, 1, 1),
                DateTo = new DateOnly(2027, 1, 1),
                EmployeeId = employee.Id,
                EmployeeKeyword = "EMP9701",
                BioWebPin = "05",
                MappingStatus = AttendancePunchMappingStatus.Mapped,
                DeviceSerialNumber = "DEVICE-1",
                StatusCode = 7,
                VerifyCode = 9,
                PageSize = 50
            });

            var item = Assert.Single(result.Items);
            Assert.Equal(100, item.ExternalEventId);
            Assert.Equal("05", item.SourcePersonPin);
            Assert.Equal(Local(2026, 7, 28, 8, 30), item.EventLocalDateTime);
            Assert.Empty(db.ChangeTracker.Entries());
            var serviceCommands = commandCapture.Commands.ToArray();
            Assert.Equal(2, serviceCommands.Length);
            Assert.Contains(serviceCommands, sql =>
                sql.Contains("OFFSET", StringComparison.OrdinalIgnoreCase));
            Assert.All(serviceCommands, sql =>
            {
                Assert.DoesNotContain("INSERT", sql, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("UPDATE", sql, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("DELETE", sql, StringComparison.OrdinalIgnoreCase);
            });
            commandCapture.Clear();
            Assert.Equal(3, await db.AttendanceRawEvents.CountAsync());
            Assert.Equal(0, await db.AttendanceSyncStates.CountAsync());
            Assert.Equal(0, await db.BioWebPersonMappings.CountAsync());
            Assert.Equal(0, await db.AuditLogs.CountAsync());

            var sql = db.AttendanceRawEvents
                .AsNoTracking()
                .Where(rawEvent =>
                    rawEvent.SourceSystem == AttendanceSourceSystems.BioWebTa &&
                    rawEvent.EventLocalDateTime >= Local(2026, 7, 28) &&
                    rawEvent.EventLocalDateTime < Local(2026, 7, 29) &&
                    rawEvent.SourcePersonPin == "05" &&
                    rawEvent.EmployeeId != null)
                .OrderByDescending(rawEvent => rawEvent.EventLocalDateTime)
                .ThenByDescending(rawEvent => rawEvent.ExternalEventId)
                .Skip(0)
                .Take(50)
                .Select(rawEvent => new
                {
                    rawEvent.EventLocalDateTime,
                    rawEvent.SourcePersonPin,
                    rawEvent.ExternalEventId,
                    rawEvent.ImportedAtUtc
                })
                .ToQueryString();

            Assert.Contains("ORDER BY", sql, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("OFFSET", sql, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("INSERT", sql, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("UPDATE", sql, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("DELETE", sql, StringComparison.OrdinalIgnoreCase);
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
        Guid? employeeId,
        string pin,
        DateTime localTime,
        string device,
        int? status,
        int? verify) =>
        new(
            Guid.NewGuid(),
            AttendanceSourceSystems.BioWebTa,
            externalEventId,
            employeeId,
            pin,
            device,
            localTime,
            status,
            verify,
            null,
            Now);

    private static DateTime Local(int year, int month, int day, int hour = 0, int minute = 0) =>
        new(year, month, day, hour, minute, 0, DateTimeKind.Unspecified);

    private static HRSystemDbContext CreateContext(
        string connectionString,
        DbCommandInterceptor commandInterceptor) =>
        new(new DbContextOptionsBuilder<HRSystemDbContext>()
            .UseSqlServer(connectionString, sql =>
                sql.MigrationsAssembly(typeof(HRSystemDbContext).Assembly.FullName))
            .AddInterceptors(commandInterceptor)
            .Options);

    private static async Task CreateDatabaseAsync(string databaseName)
    {
        _ = BuildConnectionString(databaseName);
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
            _ = BuildConnectionString(databaseName);
            SqlConnection.ClearAllPools();
            await using var connection = new SqlConnection(BuildMasterConnectionString());
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            var quotedName = QuoteDatabaseName(databaseName);
            command.CommandText = $"ALTER DATABASE {quotedName} SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE {quotedName};";
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
        command.CommandText = "SELECT COUNT(*) FROM [sys].[databases] WHERE [name] LIKE N'HRSystem[_]Phase72[_]Punch[_]Test[_]%'";
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static string BuildConnectionString(string databaseName)
    {
        ValidateDisposableDatabaseName(databaseName);
        var builder = NewLocalIntegratedBuilder();
        builder.InitialCatalog = databaseName;
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
            databaseName.Contains("BioWebTA", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Rejected unsafe Phase 7.2 disposable database name.");
        }
    }

    private static string QuoteDatabaseName(string databaseName)
    {
        ValidateDisposableDatabaseName(databaseName);
        using var builder = new SqlCommandBuilder();
        return builder.QuoteIdentifier(databaseName);
    }

    private sealed class SqlAdminCurrentUser : ICurrentUser
    {
        public string? UserId => "phase72-punch-sql-test-admin";
        public Guid? EmployeeId => null;
        public string? DisplayName => "Phase 7.2 SQL Test Admin";
        public string? IpAddress => "127.0.0.1";
        public bool IsAuthenticated => true;
        public bool IsInRole(string role) => role == RoleNames.Admin;
        public bool HasPermission(string policy) =>
            RolePermissions.HasPermission([RoleNames.Admin], policy);
    }

    private sealed class CommandCaptureInterceptor : DbCommandInterceptor
    {
        public List<string> Commands { get; } = [];

        public void Clear() => Commands.Clear();

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
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

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            Commands.Add(command.CommandText);
            return base.NonQueryExecutingAsync(
                command,
                eventData,
                result,
                cancellationToken);
        }
    }
}
