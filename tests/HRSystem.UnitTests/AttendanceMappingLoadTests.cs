using System.Reflection;
using HRSystem.Application.Attendance;
using HRSystem.Application.Common.Exceptions;
using HRSystem.Infrastructure.Attendance;
using HRSystem.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace HRSystem.UnitTests;

public sealed class AttendanceMappingLoadTests
{
    [Fact]
    public void Unmapped_pin_summary_query_translates_for_sql_server()
    {
        var options = new DbContextOptionsBuilder<HRSystemDbContext>()
            .UseSqlServer(
                "Server=localhost;Database=TranslationOnly;Integrated Security=true;TrustServerCertificate=true")
            .Options;
        using var dbContext = new HRSystemDbContext(options);

        var query = dbContext.AttendanceRawEvents
            .AsNoTracking()
            .Where(rawEvent =>
                rawEvent.SourceSystem == AttendanceSourceSystems.BioWebTa &&
                rawEvent.EmployeeId == null)
            .GroupBy(rawEvent => rawEvent.SourcePersonPin)
            .Select(group => new
            {
                BioWebPin = group.Key,
                EventCount = group.LongCount(),
                FirstEventLocalDateTime =
                    group.Min(rawEvent => rawEvent.EventLocalDateTime),
                LastEventLocalDateTime =
                    group.Max(rawEvent => rawEvent.EventLocalDateTime)
            })
            .OrderBy(item => item.BioWebPin);

        var sql = query.ToQueryString();

        Assert.Contains("GROUP BY", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("INSERT", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UPDATE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DELETE", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Missing_source_username_is_controlled_and_logged_without_secret_values()
    {
        const string password = "password-value-that-must-not-be-logged";
        var logger = new RecordingLogger<BioWebTaAttendanceSource>();
        var source = NewSource(
            logger,
            new Dictionary<string, string?>
            {
                ["BIOWEBTA_READONLY_PASSWORD"] = password
            });

        var exception = await Assert.ThrowsAsync<AttendanceSyncException>(
            () => source.ReadAfterAsync(0, 1));
        var log = Assert.Single(logger.Entries);

        Assert.DoesNotContain(password, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(password, log.Message, StringComparison.Ordinal);
        Assert.Contains("UsernameConfigured=False", log.Message, StringComparison.Ordinal);
        Assert.Contains("PasswordConfigured=True", log.Message, StringComparison.Ordinal);
        Assert.Null(log.Exception);
    }

    [Fact]
    public async Task Missing_source_password_is_controlled_and_logged_without_secret_values()
    {
        const string userName = "username-value-that-must-not-be-logged";
        var logger = new RecordingLogger<BioWebTaAttendanceSource>();
        var source = NewSource(
            logger,
            new Dictionary<string, string?>
            {
                ["BIOWEBTA_READONLY_USERNAME"] = userName
            });

        var exception = await Assert.ThrowsAsync<AttendanceSyncException>(
            () => source.ReadAfterAsync(0, 1));
        var log = Assert.Single(logger.Entries);

        Assert.DoesNotContain(userName, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(userName, log.Message, StringComparison.Ordinal);
        Assert.Contains("UsernameConfigured=True", log.Message, StringComparison.Ordinal);
        Assert.Contains("PasswordConfigured=False", log.Message, StringComparison.Ordinal);
        Assert.Null(log.Exception);
    }

    [Theory]
    [InlineData(18456, "SQL authentication was rejected.")]
    [InlineData(229, "Required database SELECT permission was denied.")]
    [InlineData(207, "Required source schema is unavailable or incompatible.")]
    [InlineData(208, "Required source schema is unavailable or incompatible.")]
    public void Sql_failures_are_safely_classified_and_logged(
        int sqlNumber,
        string expectedClassification)
    {
        const string secret = "Server=secret;User Id=secret;Password=secret";
        var logger = new RecordingLogger<BioWebTaAttendanceSource>();
        var classification = InvokeSqlClassification(sqlNumber);

        InvokeSourceFailureLog(
            logger,
            classification,
            sqlNumber,
            sqlState: 1,
            sqlClass: 14);
        var log = Assert.Single(logger.Entries);

        Assert.Equal(expectedClassification, classification);
        Assert.Contains($"SqlNumber={sqlNumber}", log.Message, StringComparison.Ordinal);
        Assert.Contains("TargetHost=configured BioWebTA server", log.Message, StringComparison.Ordinal);
        Assert.Contains("TargetDatabase=BioWebTA", log.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(secret, log.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Password=", log.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("User Id=", log.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Server=", log.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Null(log.Exception);
    }

    [Fact]
    public void Source_adapter_query_needs_no_server_level_or_write_permission()
    {
        var sql = (string)typeof(BioWebTaAttendanceSource)
            .GetField("ReadBatchSql", BindingFlags.NonPublic | BindingFlags.Static)!
            .GetRawConstantValue()!;

        Assert.Contains("FROM [dbo].[AttLog]", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("[dbo].[Person]", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("[master]", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("[sys]", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("VIEW SERVER STATE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("INSERT", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UPDATE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DELETE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("EXEC", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Mapping_query_failure_logs_only_safe_diagnostic_fields()
    {
        var logger = new RecordingLogger<BioWebPersonMappingService>();
        var dbContext = TestDb.Create();
        var service = new BioWebPersonMappingService(
            dbContext,
            new TestCurrentUser(),
            TimeProvider.System,
            new AttendanceRecalculationEngine(dbContext, TimeProvider.System),
            logger);
        await dbContext.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => service.GetUnmappedPinsAsync());
        var log = Assert.Single(logger.Entries);

        Assert.Contains(
            "OperationName=GetUnmappedPinsAsync",
            log.Message,
            StringComparison.Ordinal);
        Assert.Contains(
            "ExceptionType=ObjectDisposedException",
            log.Message,
            StringComparison.Ordinal);
        Assert.Contains(
            "SanitizedMessage=The unmapped PIN summary query could not be completed.",
            log.Message,
            StringComparison.Ordinal);
        Assert.DoesNotContain("Object name", log.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Null(log.Exception);
    }

    private static BioWebTaAttendanceSource NewSource(
        ILogger<BioWebTaAttendanceSource> logger,
        IReadOnlyDictionary<string, string?> values)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
        return new BioWebTaAttendanceSource(configuration, logger);
    }

    private static string InvokeSqlClassification(int sqlNumber)
    {
        var method = typeof(BioWebTaAttendanceSource).GetMethod(
            "ClassifySqlFailure",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("SQL classifier was not found.");
        return (string)method.Invoke(null, [sqlNumber])!;
    }

    private static void InvokeSourceFailureLog(
        ILogger logger,
        string classification,
        int sqlNumber,
        byte sqlState,
        byte sqlClass)
    {
        var method = typeof(BioWebTaAttendanceSource).GetMethod(
            "LogSourceFailure",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Source logger was not found.");
        method.Invoke(
            null,
            [
                logger,
                "ReadAttendanceEvents",
                "SqlException",
                classification,
                (int?)sqlNumber,
                (byte?)sqlState,
                (byte?)sqlClass,
                "SocketException",
                true,
                true
            ]);
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull =>
            null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Entries.Add(new LogEntry(
                logLevel,
                formatter(state, exception),
                exception));
    }

    private sealed record LogEntry(
        LogLevel Level,
        string Message,
        Exception? Exception);
}
