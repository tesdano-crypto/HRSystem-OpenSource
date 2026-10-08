using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using HRSystem.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace HRSystem.IntegrationTests;

internal sealed partial class DisposableSqlServerDatabase : IAsyncDisposable
{
    internal const string DatabasePrefix = "HRSystem_Test_";
    internal const int ExpectedMigrationCount = 38;
    internal const string LatestExpectedMigration =
        "20260930061925_AddHolidayTrainingCompTime";

    private static readonly ConcurrentDictionary<string, byte> CurrentRunDatabases =
        new(StringComparer.Ordinal);
    private readonly string _connectionString;
    private int _disposed;

    private DisposableSqlServerDatabase(string databaseName)
    {
        ValidateDatabaseName(databaseName);
        DatabaseName = databaseName;
        _connectionString = BuildDatabaseConnectionString(databaseName);
    }

    public string DatabaseName { get; }

    public static async Task<DisposableSqlServerDatabase> CreateAsync(
        string purpose,
        bool applyMigrations = true)
    {
        var databaseName = GenerateDatabaseName(purpose);
        var database = new DisposableSqlServerDatabase(databaseName);
        try
        {
            await database.CreateDatabaseAsync();
            CurrentRunDatabases.TryAdd(databaseName, 0);
            if (applyMigrations)
            {
                await database.ApplyAndVerifyMigrationsAsync();
            }

            return database;
        }
        catch (Exception originalFailure)
        {
            try
            {
                await database.DisposeAsync();
            }
            catch (Exception cleanupFailure)
            {
                throw new AggregateException(
                    $"Disposable database setup and cleanup failed for {databaseName}.",
                    originalFailure,
                    cleanupFailure);
            }

            throw;
        }
    }

    public HRSystemDbContext CreateDbContext(
        DbCommandInterceptor? interceptor = null)
    {
        ThrowIfDisposed();
        var options = new DbContextOptionsBuilder<HRSystemDbContext>()
            .UseSqlServer(
                _connectionString,
                sql => sql.MigrationsAssembly(
                    typeof(HRSystemDbContext).Assembly.FullName));
        if (interceptor is not null)
        {
            options.AddInterceptors(interceptor);
        }

        return new HRSystemDbContext(options.Options);
    }

    public async Task<SqlConnection> OpenConnectionAsync()
    {
        ThrowIfDisposed();
        var connection = new SqlConnection(_connectionString);
        try
        {
            await connection.OpenAsync();
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        Exception? cleanupFailure = null;
        try
        {
            SqlConnection.ClearAllPools();
            await using var connection =
                new SqlConnection(BuildMasterConnectionString());
            await connection.OpenAsync(CancellationToken.None);
            await using var command = connection.CreateCommand();
            var quotedName = QuoteDatabaseName(DatabaseName);
            command.CommandText =
                $"""
                IF DB_ID(N'{DatabaseName}') IS NOT NULL
                BEGIN
                    ALTER DATABASE {quotedName}
                        SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                    DROP DATABASE {quotedName};
                END
                """;
            await command.ExecuteNonQueryAsync(CancellationToken.None);

            if (await DatabaseExistsAsync(DatabaseName))
            {
                throw new InvalidOperationException(
                    $"Disposable database cleanup left residue: {DatabaseName}.");
            }
        }
        catch (Exception exception)
        {
            cleanupFailure = exception;
        }
        finally
        {
            CurrentRunDatabases.TryRemove(DatabaseName, out _);
        }

        if (cleanupFailure is not null)
        {
            throw new InvalidOperationException(
                $"Failed to clean disposable database {DatabaseName}.",
                cleanupFailure);
        }
    }

    public static string GenerateDatabaseName(string purpose)
    {
        if (string.IsNullOrWhiteSpace(purpose) ||
            !SafePurposePattern().IsMatch(purpose))
        {
            throw new InvalidOperationException(
                "Disposable database purpose must contain only letters, numbers, or underscores.");
        }

        var databaseName =
            $"{DatabasePrefix}{purpose}_{DateTime.UtcNow:yyyyMMddHHmmssfff}_{Guid.NewGuid():N}";
        ValidateDatabaseName(databaseName);
        return databaseName;
    }

    public static void ValidateDatabaseName(string databaseName)
    {
        if (string.IsNullOrWhiteSpace(databaseName) ||
            databaseName.Length > 128 ||
            !databaseName.StartsWith(DatabasePrefix, StringComparison.Ordinal) ||
            !SafeDatabaseNamePattern().IsMatch(databaseName) ||
            databaseName.Contains(
                "HRSystemDb",
                StringComparison.OrdinalIgnoreCase) ||
            databaseName.Contains("BioWebTA", StringComparison.OrdinalIgnoreCase) ||
            databaseName.Contains("PRODUCTION", StringComparison.OrdinalIgnoreCase) ||
            databaseName.Contains("EXTERNAL_ERP", StringComparison.OrdinalIgnoreCase) ||
            databaseName.Contains("EXTERNAL_ATTENDANCE_DB", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Rejected unsafe disposable database name.");
        }
    }

    public static async Task<bool> DatabaseExistsAsync(string databaseName)
    {
        ValidateDatabaseName(databaseName);
        await using var connection =
            new SqlConnection(BuildMasterConnectionString());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT COUNT(*) FROM [sys].[databases] WHERE [name] = @name";
        command.Parameters.AddWithValue("@name", databaseName);
        return Convert.ToInt32(await command.ExecuteScalarAsync()) == 1;
    }

    public static async Task<int> CurrentRunResidueCountAsync()
    {
        var trackedNames = CurrentRunDatabases.Keys.ToArray();
        var residueCount = 0;
        foreach (var databaseName in trackedNames)
        {
            if (await DatabaseExistsAsync(databaseName))
            {
                residueCount++;
            }
        }

        return residueCount;
    }

    public static async Task<int> AllProjectResidueCountAsync()
    {
        await using var connection =
            new SqlConnection(BuildMasterConnectionString());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT COUNT(*) FROM [sys].[databases] " +
            "WHERE [name] LIKE N'HRSystem[_]Test[_]%'";
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    public override string ToString() => DatabaseName;

    private async Task CreateDatabaseAsync()
    {
        ValidateDatabaseName(DatabaseName);
        await using var connection =
            new SqlConnection(BuildMasterConnectionString());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"CREATE DATABASE {QuoteDatabaseName(DatabaseName)}";
        await command.ExecuteNonQueryAsync();
    }

    private async Task ApplyAndVerifyMigrationsAsync()
    {
        await using var db = CreateDbContext();
        await db.Database.MigrateAsync();
        var applied = (await db.Database.GetAppliedMigrationsAsync()).ToArray();
        var pending = (await db.Database.GetPendingMigrationsAsync()).ToArray();
        if (applied.Length != ExpectedMigrationCount ||
            !string.Equals(
                applied[^1],
                LatestExpectedMigration,
                StringComparison.Ordinal) ||
            pending.Length != 0)
        {
            throw new InvalidOperationException(
                "Disposable database Migration verification failed.");
        }
    }

    private static string BuildDatabaseConnectionString(string databaseName)
    {
        ValidateDatabaseName(databaseName);
        var builder = NewLocalIntegratedBuilder(pooling: true);
        builder.InitialCatalog = databaseName;
        ValidateLocalIntegratedTarget(builder, databaseName);
        return builder.ConnectionString;
    }

    private static string BuildMasterConnectionString()
    {
        var builder = NewLocalIntegratedBuilder(pooling: false);
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

    private static SqlConnectionStringBuilder NewLocalIntegratedBuilder(
        bool pooling) =>
        new()
        {
            DataSource = @".\SQLEXPRESS",
            IntegratedSecurity = true,
            Encrypt = true,
            TrustServerCertificate = true,
            Pooling = pooling
        };

    private static void ValidateLocalIntegratedTarget(
        SqlConnectionStringBuilder builder,
        string expectedDatabaseName)
    {
        ValidateDatabaseName(expectedDatabaseName);
        if (!IsApprovedLocalServer(builder.DataSource) ||
            !string.Equals(
                builder.InitialCatalog,
                expectedDatabaseName,
                StringComparison.Ordinal) ||
            !builder.IntegratedSecurity ||
            !string.IsNullOrWhiteSpace(builder.UserID))
        {
            throw new InvalidOperationException(
                "Rejected unsafe disposable SQL target.");
        }
    }

    private static bool IsApprovedLocalServer(string server) =>
        string.Equals(
            server.Trim(),
            @".\SQLEXPRESS",
            StringComparison.OrdinalIgnoreCase) ||
        string.Equals(
            server.Trim(),
            $@"{Environment.MachineName}\SQLEXPRESS",
            StringComparison.OrdinalIgnoreCase);

    private static string QuoteDatabaseName(string databaseName)
    {
        ValidateDatabaseName(databaseName);
        using var builder = new SqlCommandBuilder();
        return builder.QuoteIdentifier(databaseName);
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(
            Volatile.Read(ref _disposed) != 0,
            this);
    }

    [GeneratedRegex("^[A-Za-z0-9_]+$", RegexOptions.CultureInvariant)]
    private static partial Regex SafePurposePattern();

    [GeneratedRegex("^HRSystem_Test_[A-Za-z0-9_]+$", RegexOptions.CultureInvariant)]
    private static partial Regex SafeDatabaseNamePattern();
}

public sealed class MasterDataConcurrencyDatabaseFixture : IAsyncLifetime
{
    private DisposableSqlServerDatabase? _database;

    public async Task InitializeAsync()
    {
        _database = await DisposableSqlServerDatabase.CreateAsync(
            "MasterDataConcurrency");
    }

    public HRSystemDbContext CreateDbContext(
        DbCommandInterceptor? interceptor = null) =>
        (_database ?? throw new InvalidOperationException(
            "Disposable SQL fixture has not been initialized."))
        .CreateDbContext(interceptor);

    public async Task DisposeAsync()
    {
        if (_database is not null)
        {
            await _database.DisposeAsync();
        }
    }
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class DisposableSqlServerCollection
{
    public const string Name = "DisposableSqlServer";
}
