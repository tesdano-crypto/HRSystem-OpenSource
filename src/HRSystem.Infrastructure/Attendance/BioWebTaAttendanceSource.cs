using System.Data;
using HRSystem.Application.Attendance;
using HRSystem.Application.Common.Exceptions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace HRSystem.Infrastructure.Attendance;

public sealed class BioWebTaAttendanceSource(
    IConfiguration configuration,
    ILogger<BioWebTaAttendanceSource> logger)
    : IBioWebTaAttendanceSource
{
    private const string TargetHost = "configured BioWebTA server";
    private const string TargetDatabase = "BioWebTA";
    private const string ReadOperation = "ReadAttendanceEvents";

    private const string ReadBatchSql = """
        SELECT TOP (@BatchSize)
            [Id],
            [Pin],
            [SN],
            [AttLogTime],
            [Status],
            [Verify],
            [CreationTime]
        FROM [dbo].[AttLog]
        WHERE [Id] > @AfterId
        ORDER BY [Id] ASC;
        """;

    private const string ReadWindowPageSql = """
        SELECT TOP (@PageSize)
            [Id],
            [Pin],
            [SN],
            [AttLogTime],
            [Status],
            [Verify],
            [CreationTime]
        FROM [dbo].[AttLog]
        WHERE [AttLogTime] >= @QueryFromLocal
          AND [AttLogTime] < @QueryToLocal
          AND (
              @AfterEventLocalDateTime IS NULL
              OR [AttLogTime] > @AfterEventLocalDateTime
              OR ([AttLogTime] = @AfterEventLocalDateTime AND [Id] > @AfterExternalEventId)
          )
        ORDER BY [AttLogTime] ASC, [Id] ASC;
        """;

    public async Task<IReadOnlyList<BioWebAttendanceSourceRecord>> ReadAfterAsync(
        long lastExternalEventId,
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        if (lastExternalEventId < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(lastExternalEventId));
        }

        if (batchSize is < 1 or > 1000)
        {
            throw new ArgumentOutOfRangeException(nameof(batchSize));
        }

        var userName = configuration["BIOWEBTA_READONLY_USERNAME"];
        var password = configuration["BIOWEBTA_READONLY_PASSWORD"];
        var userNameConfigured = !string.IsNullOrWhiteSpace(userName);
        var passwordConfigured = !string.IsNullOrWhiteSpace(password);
        if (!userNameConfigured || !passwordConfigured)
        {
            LogSourceFailure(
                logger,
                ReadOperation,
                nameof(AttendanceSyncException),
                "Required read-only source configuration is unavailable.",
                null,
                null,
                null,
                null,
                userNameConfigured,
                passwordConfigured);
            throw new AttendanceSyncException(
                "BioWebTA 唯讀帳號尚未設定，匯入未執行。");
        }

        var connectionString = new SqlConnectionStringBuilder
        {
            DataSource = configuration["BioWebTA:Server"] ?? throw new AttendanceSyncException("BioWebTA server is not configured."),
            InitialCatalog = "BioWebTA",
            UserID = userName,
            Password = password,
            Encrypt = true,
            TrustServerCertificate = true,
            ApplicationIntent = ApplicationIntent.ReadOnly,
            ConnectTimeout = 10,
            PersistSecurityInfo = false,
            Pooling = true
        };

        try
        {
            await using var connection = new SqlConnection(
                connectionString.ConnectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = ReadBatchSql;
            command.CommandType = CommandType.Text;
            command.CommandTimeout = 30;
            command.Parameters.Add(
                new SqlParameter("@BatchSize", SqlDbType.Int)
                {
                    Value = batchSize
                });
            command.Parameters.Add(
                new SqlParameter("@AfterId", SqlDbType.BigInt)
                {
                    Value = lastExternalEventId
                });

            var records = new List<BioWebAttendanceSourceRecord>(batchSize);
            await using var reader = await command.ExecuteReaderAsync(
                CommandBehavior.SequentialAccess |
                CommandBehavior.SingleResult,
                cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                records.Add(new BioWebAttendanceSourceRecord(
                    reader.GetInt64(0),
                    reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetString(2),
                    AsSourceLocal(reader.GetDateTime(3)),
                    reader.IsDBNull(4) ? null : reader.GetInt32(4),
                    reader.IsDBNull(5) ? null : reader.GetInt32(5),
                    reader.IsDBNull(6)
                        ? null
                        : AsSourceLocal(reader.GetDateTime(6))));
            }

            return records;
        }
        catch (AttendanceSyncException)
        {
            throw;
        }
        catch (SqlException exception)
        {
            LogSourceFailure(
                logger,
                ReadOperation,
                exception.GetType().Name,
                ClassifySqlFailure(exception.Number),
                exception.Number,
                exception.State,
                exception.Class,
                exception.InnerException?.GetType().Name,
                userNameConfigured,
                passwordConfigured);
            throw new AttendanceSyncException(
                $"BioWebTA 唯讀查詢失敗（SQL 錯誤代碼 {exception.Number}）。");
        }
        catch (InvalidOperationException exception)
        {
            LogSourceFailure(
                logger,
                ReadOperation,
                exception.GetType().Name,
                "The read-only source operation could not be initialized.",
                null,
                null,
                null,
                exception.InnerException?.GetType().Name,
                userNameConfigured,
                passwordConfigured);
            throw new AttendanceSyncException(
                "BioWebTA 唯讀連線無法建立。");
        }
        finally
        {
            password = null;
            connectionString.Clear();
        }
    }

    public async Task<IReadOnlyList<BioWebAttendanceSourceRecord>> ReadWindowPageAsync(
        BioWebTaSourceWindowPageRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.QueryFromLocal.Kind != DateTimeKind.Unspecified ||
            request.QueryToLocal.Kind != DateTimeKind.Unspecified ||
            request.QueryToLocal <= request.QueryFromLocal ||
            request.AfterEventLocalDateTime is { Kind: not DateTimeKind.Unspecified } ||
            request.AfterExternalEventId < 0 ||
            request.PageSize is < 1 or > 1000)
        {
            throw new ArgumentException("BioWebTA source window is invalid.", nameof(request));
        }

        var userName = configuration["BIOWEBTA_READONLY_USERNAME"];
        var password = configuration["BIOWEBTA_READONLY_PASSWORD"];
        var userNameConfigured = !string.IsNullOrWhiteSpace(userName);
        var passwordConfigured = !string.IsNullOrWhiteSpace(password);
        if (!userNameConfigured || !passwordConfigured)
        {
            LogSourceFailure(
                logger,
                "ReadAttendanceWindow",
                nameof(AttendanceSyncException),
                "Required read-only source configuration is unavailable.",
                null, null, null, null,
                userNameConfigured,
                passwordConfigured);
            throw new AttendanceSyncException(
                "BioWebTA read-only source configuration is unavailable.");
        }

        var connectionString = new SqlConnectionStringBuilder
        {
            DataSource = configuration["BioWebTA:Server"] ?? throw new AttendanceSyncException("BioWebTA server is not configured."),
            InitialCatalog = "BioWebTA",
            UserID = userName,
            Password = password,
            Encrypt = true,
            TrustServerCertificate = true,
            ApplicationIntent = ApplicationIntent.ReadOnly,
            ConnectTimeout = 10,
            PersistSecurityInfo = false,
            Pooling = true
        };

        try
        {
            await using var connection = new SqlConnection(
                connectionString.ConnectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = ReadWindowPageSql;
            command.CommandType = CommandType.Text;
            command.CommandTimeout = 30;
            command.Parameters.Add(new SqlParameter("@PageSize", SqlDbType.Int)
            {
                Value = request.PageSize
            });
            command.Parameters.Add(new SqlParameter(
                "@QueryFromLocal", SqlDbType.DateTime2)
            {
                Value = request.QueryFromLocal
            });
            command.Parameters.Add(new SqlParameter(
                "@QueryToLocal", SqlDbType.DateTime2)
            {
                Value = request.QueryToLocal
            });
            command.Parameters.Add(new SqlParameter(
                "@AfterEventLocalDateTime", SqlDbType.DateTime2)
            {
                Value = request.AfterEventLocalDateTime is null
                    ? DBNull.Value
                    : request.AfterEventLocalDateTime.Value
            });
            command.Parameters.Add(new SqlParameter(
                "@AfterExternalEventId", SqlDbType.BigInt)
            {
                Value = request.AfterExternalEventId
            });

            var records = new List<BioWebAttendanceSourceRecord>(request.PageSize);
            await using var reader = await command.ExecuteReaderAsync(
                CommandBehavior.SequentialAccess | CommandBehavior.SingleResult,
                cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                records.Add(ReadRecord(reader));
            }

            return records;
        }
        catch (SqlException exception)
        {
            LogSourceFailure(
                logger,
                "ReadAttendanceWindow",
                exception.GetType().Name,
                ClassifySqlFailure(exception.Number),
                exception.Number,
                exception.State,
                exception.Class,
                exception.InnerException?.GetType().Name,
                userNameConfigured,
                passwordConfigured);
            throw new AttendanceSyncException(
                $"BioWebTA read-only source failed with SQL error {exception.Number}.");
        }
        finally
        {
            password = null;
            connectionString.Clear();
        }
    }

    private static BioWebAttendanceSourceRecord ReadRecord(SqlDataReader reader) =>
        new(
            reader.GetInt64(0),
            reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetString(2),
            AsSourceLocal(reader.GetDateTime(3)),
            reader.IsDBNull(4) ? null : reader.GetInt32(4),
            reader.IsDBNull(5) ? null : reader.GetInt32(5),
            reader.IsDBNull(6) ? null : AsSourceLocal(reader.GetDateTime(6)));

    private static DateTime AsSourceLocal(DateTime value) =>
        DateTime.SpecifyKind(value, DateTimeKind.Unspecified);

    private static string ClassifySqlFailure(int number) => number switch
    {
        18456 => "SQL authentication was rejected.",
        229 or 230 or 262 => "Required database SELECT permission was denied.",
        207 or 208 => "Required source schema is unavailable or incompatible.",
        -2 => "The SQL source operation timed out.",
        20 or 53 or 64 or 233 or 258 or 10053 or 10054 or 10060 =>
            "The SQL source connection could not be established.",
        _ => "The SQL source operation failed."
    };

    private static void LogSourceFailure(
        ILogger logger,
        string operationName,
        string exceptionType,
        string sanitizedMessage,
        int? sqlNumber,
        byte? sqlState,
        byte? sqlClass,
        string? innerExceptionType,
        bool userNameConfigured,
        bool passwordConfigured)
    {
        logger.LogError(
            "BioWebTA source failure. OperationName={OperationName} " +
            "ExceptionType={ExceptionType} SanitizedMessage={SanitizedMessage} " +
            "SqlNumber={SqlNumber} SqlState={SqlState} SqlClass={SqlClass} " +
            "InnerExceptionType={InnerExceptionType} TargetHost={TargetHost} " +
            "TargetDatabase={TargetDatabase} UsernameConfigured={UsernameConfigured} " +
            "PasswordConfigured={PasswordConfigured}",
            operationName,
            exceptionType,
            sanitizedMessage,
            sqlNumber,
            sqlState,
            sqlClass,
            innerExceptionType,
            TargetHost,
            TargetDatabase,
            userNameConfigured,
            passwordConfigured);
    }
}
