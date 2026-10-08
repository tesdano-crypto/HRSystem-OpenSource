using HRSystem.Application.Abstractions.Security;
using HRSystem.Application.Common.Exceptions;
using HRSystem.Application.Employees;
using HRSystem.Application.Security;
using HRSystem.Domain.MasterData;
using HRSystem.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit.Abstractions;

namespace HRSystem.IntegrationTests;

public sealed class EmployeeNumberSequenceSqlIntegrationTests(ITestOutputHelper output)
{
    private const string DatabasePrefix = "HRSystem_Phase51_Test_";
    private const string PreviousMigration = "20260719015020_AddLeaveRequestWorkflow";
    private const string EmployeeNumberMigration = "20260725015454_AddEmployeeNumberSequence";
    private static readonly DateTimeOffset Now =
        new(2026, 7, 25, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    [Trait("Category", "SqlSequence")]
    public async Task Disposable_Database_Proves_Migration_Sequence_Concurrency_Gap_And_Cleanup()
    {
        var databaseName = $"{DatabasePrefix}{Guid.NewGuid():N}";
        output.WriteLine($"Disposable database: {databaseName}");
        Exception? testFailure = null;
        var databaseCreated = false;
        try
        {
            await CreateDatabaseAsync(databaseName);
            databaseCreated = true;
            var connectionString = BuildValidatedConnectionString(databaseName);
            await using var setupDb = CreateContext(connectionString);
            var migrator = setupDb.GetService<IMigrator>();
            await migrator.MigrateAsync(PreviousMigration);

            var baseline = await SeedSyntheticBaselineAsync(setupDb);
            await migrator.MigrateAsync(EmployeeNumberMigration);

            await AssertSequenceMetadataAsync(setupDb);
            Assert.Equal(4, await setupDb.Database
                .SqlQueryRaw<int>("SELECT COUNT(*) AS [Value] FROM [dbo].[__EFMigrationsHistory]")
                .SingleAsync());

            var first = await CreateEmployeeAsync(connectionString, baseline.DepartmentId, "新進員工");
            Assert.Equal("EMP0015", first.EmployeeNumber);

            var concurrent = await Task.WhenAll(
                CreateEmployeeAsync(connectionString, baseline.DepartmentId, "併發員工甲"),
                CreateEmployeeAsync(connectionString, baseline.DepartmentId, "併發員工乙"));
            Assert.Equal(2, concurrent.Select(x => x.EmployeeNumber).Distinct().Count());
            Assert.Equal(
                ["EMP0016", "EMP0017"],
                concurrent.Select(x => x.EmployeeNumber).Order().ToArray());
            await AssertConcurrentRowsAndAuditsAsync(connectionString, concurrent);

            await AssertFailedCreateConsumesGapAsync(connectionString, baseline.DepartmentId);

            var afterGap = await CreateEmployeeAsync(connectionString, baseline.DepartmentId, "缺號後員工");
            Assert.Equal("EMP0019", afterGap.EmployeeNumber);

            await AssertSyntheticBaselineUnchangedAsync(connectionString, baseline);
            await AssertUniqueIndexAsync(connectionString);
            await AssertForcedDuplicateIsControlledAsync(connectionString, baseline.DepartmentId);

            await migrator.MigrateAsync(PreviousMigration);
            Assert.Equal(0, await SequenceCountAsync(setupDb));
            await Assert.ThrowsAsync<EmployeeNumberGenerationException>(() =>
                new SqlServerEmployeeNumberSequence(
                    setupDb,
                    NullLogger<SqlServerEmployeeNumberSequence>.Instance)
                    .GetNextValueAsync());
        }
        catch (Exception exception)
        {
            testFailure = exception;
            throw;
        }
        finally
        {
            if (databaseCreated)
            {
                await DropDatabasePreservingFailureAsync(databaseName, testFailure);
            }
        }
    }

    private static async Task<SyntheticBaseline> SeedSyntheticBaselineAsync(
        HRSystemDbContext db)
    {
        var department = new Department(
            Guid.NewGuid(), "P51", "Phase 5.1 合成部門", Now);
        var employees = Enumerable.Range(1, 14)
            .Select(value => NewEmployee($"EMP{value:D4}", department.Id))
            .Concat(
            [
                NewEmployee("TEST001", department.Id),
                NewEmployee("TEST002", department.Id),
                NewEmployee("LEGACY03", department.Id)
            ])
            .ToArray();
        db.Add(department);
        db.AddRange(employees);
        await db.SaveChangesAsync();
        return new SyntheticBaseline(
            department.Id,
            employees.ToDictionary(
                employee => employee.Id,
                employee => new EmployeeFingerprint(
                    employee.EmployeeNumber,
                    Convert.ToBase64String(employee.RowVersion))));
    }

    private static async Task<EmployeeDto> CreateEmployeeAsync(
        string connectionString,
        Guid departmentId,
        string name)
    {
        await using var db = CreateContext(connectionString);
        var service = new EmployeeService(
            db,
            new SqlServerEmployeeNumberSequence(
                db,
                NullLogger<SqlServerEmployeeNumberSequence>.Instance),
            new SqlAdminCurrentUser(),
            TimeProvider.System,
            NullLogger<EmployeeService>.Instance);
        return await service.CreateAsync(new CreateEmployeeRequest
        {
            ChineseName = name,
            DepartmentId = departmentId,
            HireDate = new DateOnly(2026, 7, 25)
        });
    }

    private static async Task AssertFailedCreateConsumesGapAsync(
        string connectionString,
        Guid departmentId)
    {
        await using var failingDb = CreateContext(
            connectionString,
            new ThrowBeforeSaveInterceptor());
        var service = new EmployeeService(
            failingDb,
            new SqlServerEmployeeNumberSequence(
                failingDb,
                NullLogger<SqlServerEmployeeNumberSequence>.Instance),
            new SqlAdminCurrentUser(),
            TimeProvider.System,
            NullLogger<EmployeeService>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateAsync(new CreateEmployeeRequest
            {
                ChineseName = "預期失敗員工",
                DepartmentId = departmentId,
                HireDate = new DateOnly(2026, 7, 25)
            }));

        await using var verificationDb = CreateContext(connectionString);
        Assert.False(await verificationDb.Employees
            .AnyAsync(x => x.ChineseName == "預期失敗員工"));
        Assert.False(await verificationDb.AuditLogs
            .AnyAsync(x => x.EntityType == nameof(Employee) &&
                x.NewValuesJson != null &&
                x.NewValuesJson.Contains("預期失敗員工")));
    }

    private static async Task AssertConcurrentRowsAndAuditsAsync(
        string connectionString,
        IReadOnlyCollection<EmployeeDto> concurrent)
    {
        var ids = concurrent.Select(x => x.Id).ToArray();
        var entityIds = ids.Select(x => x.ToString()).ToArray();
        await using var db = CreateContext(connectionString);
        Assert.Equal(2, await db.Employees.CountAsync(x => ids.Contains(x.Id)));
        Assert.Equal(
            2,
            await db.AuditLogs.CountAsync(x =>
                x.EntityType == nameof(Employee) &&
                x.Action == "Created" &&
                x.EntityId != null &&
                entityIds.Contains(x.EntityId)));
    }

    private static async Task AssertForcedDuplicateIsControlledAsync(
        string connectionString,
        Guid departmentId)
    {
        await using (var resetDb = CreateContext(connectionString))
        {
            await resetDb.Database.ExecuteSqlRawAsync(
                "ALTER SEQUENCE [dbo].[EmployeeNumberSequence] RESTART WITH 15");
        }

        var exception = await Assert.ThrowsAsync<EmployeeNumberGenerationException>(() =>
            CreateEmployeeAsync(connectionString, departmentId, "強制碰撞員工"));
        Assert.Contains("唯一", exception.Message, StringComparison.Ordinal);

        await using var verificationDb = CreateContext(connectionString);
        Assert.Equal(
            1,
            await verificationDb.Employees.CountAsync(x => x.EmployeeNumber == "EMP0015"));
        Assert.False(await verificationDb.Employees.AnyAsync(
            x => x.ChineseName == "強制碰撞員工"));
        Assert.False(await verificationDb.AuditLogs.AnyAsync(
            x => x.EntityType == nameof(Employee) &&
                x.NewValuesJson != null &&
                x.NewValuesJson.Contains("強制碰撞員工")));
    }

    private static async Task AssertSyntheticBaselineUnchangedAsync(
        string connectionString,
        SyntheticBaseline baseline)
    {
        await using var db = CreateContext(connectionString);
        var employeeIds = baseline.Employees.Keys.ToArray();
        var actual = await db.Employees
            .Where(x => employeeIds.Contains(x.Id))
            .ToDictionaryAsync(
                x => x.Id,
                x => new EmployeeFingerprint(
                    x.EmployeeNumber,
                    Convert.ToBase64String(x.RowVersion)));
        Assert.Equal(baseline.Employees.Count, actual.Count);
        foreach (var expected in baseline.Employees)
        {
            Assert.True(actual.TryGetValue(expected.Key, out var fingerprint));
            Assert.Equal(expected.Value, fingerprint);
        }
    }

    private static async Task AssertSequenceMetadataAsync(HRSystemDbContext db)
    {
        const string sql = """
            SELECT COUNT(*) AS [Value]
            FROM [sys].[sequences] AS [sequence]
            INNER JOIN [sys].[schemas] AS [schema] ON [schema].[schema_id] = [sequence].[schema_id]
            INNER JOIN [sys].[types] AS [type] ON [type].[user_type_id] = [sequence].[user_type_id]
            WHERE [schema].[name] = N'dbo'
              AND [sequence].[name] = N'EmployeeNumberSequence'
              AND [type].[name] = N'int'
              AND [sequence].[start_value] = 15
              AND [sequence].[minimum_value] = 15
              AND [sequence].[increment] = 1
              AND [sequence].[maximum_value] = 9999
              AND [sequence].[is_cycling] = 0
            """;
        Assert.Equal(1, await db.Database.SqlQueryRaw<int>(sql).SingleAsync());
    }

    private static async Task AssertUniqueIndexAsync(string connectionString)
    {
        await using var db = CreateContext(connectionString);
        const string sql = """
            SELECT COUNT(*) AS [Value]
            FROM [sys].[indexes] AS [index]
            INNER JOIN [sys].[tables] AS [table] ON [table].[object_id] = [index].[object_id]
            WHERE [table].[name] = N'Employees'
              AND [index].[name] = N'UX_Employees_EmployeeNumber'
              AND [index].[is_unique] = 1
              AND [index].[is_disabled] = 0
            """;
        Assert.Equal(1, await db.Database.SqlQueryRaw<int>(sql).SingleAsync());
    }

    private static Task<int> SequenceCountAsync(HRSystemDbContext db) =>
        db.Database.SqlQueryRaw<int>(
                """
                SELECT COUNT(*) AS [Value]
                FROM [sys].[sequences] AS [sequence]
                INNER JOIN [sys].[schemas] AS [schema] ON [schema].[schema_id] = [sequence].[schema_id]
                WHERE [schema].[name] = N'dbo'
                  AND [sequence].[name] = N'EmployeeNumberSequence'
                """)
            .SingleAsync();

    private static Employee NewEmployee(string number, Guid departmentId) =>
        new(
            Guid.NewGuid(),
            number,
            $"合成員工 {number}",
            departmentId,
            new DateOnly(2026, 1, 1),
            Now);

    private static HRSystemDbContext CreateContext(
        string connectionString,
        SaveChangesInterceptor? interceptor = null)
    {
        var options = new DbContextOptionsBuilder<HRSystemDbContext>()
            .UseSqlServer(
                connectionString,
                sql => sql.MigrationsAssembly(typeof(HRSystemDbContext).Assembly.FullName));
        if (interceptor is not null)
        {
            options.AddInterceptors(interceptor);
        }

        return new HRSystemDbContext(options.Options);
    }

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
                $"Test failure and cleanup failure for disposable database {databaseName}.",
                originalFailure,
                cleanupFailure);
        }
        catch (Exception cleanupFailure)
        {
            throw new InvalidOperationException(
                $"Failed to clean disposable database {databaseName}.",
                cleanupFailure);
        }
    }

    private static string BuildValidatedConnectionString(string databaseName)
    {
        ValidateDisposableDatabaseName(databaseName);
        var builder = NewLocalIntegratedBuilder();
        builder.InitialCatalog = databaseName;
        ValidateLocalIntegratedTarget(builder, databaseName);
        return builder.ConnectionString;
    }

    private static string BuildMasterConnectionString()
    {
        var builder = NewLocalIntegratedBuilder();
        builder.InitialCatalog = "master";
        if (!IsApprovedLocalServer(builder.DataSource) ||
            !builder.IntegratedSecurity ||
            !string.IsNullOrWhiteSpace(builder.UserID))
        {
            throw new InvalidOperationException(
                "Disposable database administration requires local SQLEXPRESS and Windows Integrated authentication.");
        }

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

    private static void ValidateLocalIntegratedTarget(
        SqlConnectionStringBuilder builder,
        string expectedDatabaseName)
    {
        ValidateDisposableDatabaseName(expectedDatabaseName);
        if (!IsApprovedLocalServer(builder.DataSource) ||
            !string.Equals(
                builder.InitialCatalog,
                expectedDatabaseName,
                StringComparison.Ordinal) ||
            !builder.IntegratedSecurity ||
            !string.IsNullOrWhiteSpace(builder.UserID))
        {
            throw new InvalidOperationException(
                "Rejected unsafe Phase 5.1 disposable SQL target.");
        }
    }

    private static void ValidateDisposableDatabaseName(string databaseName)
    {
        var suffix = databaseName.StartsWith(DatabasePrefix, StringComparison.Ordinal)
            ? databaseName[DatabasePrefix.Length..]
            : string.Empty;
        var forbidden = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "HRSystemDb",
            "master",
            "model",
            "msdb",
            "tempdb",
            "PRODUCTION",
            "EXTERNAL_ERP",
            "EXTERNAL_ATTENDANCE_DB"
        };
        if (!databaseName.StartsWith(DatabasePrefix, StringComparison.Ordinal) ||
            suffix.Length == 0 ||
            !Guid.TryParseExact(suffix, "N", out _) ||
            forbidden.Contains(databaseName))
        {
            throw new InvalidOperationException(
                "Rejected unsafe Phase 5.1 disposable database name.");
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

    private sealed class ThrowBeforeSaveInterceptor : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException<InterceptionResult<int>>(
                new InvalidOperationException("Synthetic save failure after sequence allocation."));
    }

    private sealed class SqlAdminCurrentUser : ICurrentUser
    {
        public string? UserId => "phase51-sql-test-admin";
        public Guid? EmployeeId => null;
        public string? DisplayName => "Phase 5.1 SQL Test Admin";
        public string? IpAddress => "127.0.0.1";
        public bool IsAuthenticated => true;
        public bool IsInRole(string role) => role == RoleNames.Admin;
        public bool HasPermission(string policy) =>
            RolePermissions.HasPermission([RoleNames.Admin], policy);
    }

    private sealed record SyntheticBaseline(
        Guid DepartmentId,
        Dictionary<Guid, EmployeeFingerprint> Employees);

    private sealed record EmployeeFingerprint(string EmployeeNumber, string RowVersion);
}
