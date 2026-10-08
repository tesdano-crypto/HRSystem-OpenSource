using System.Data;
using HRSystem.Application.Attendance;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace HRSystem.Infrastructure.Attendance;

public sealed class SqlBioWebTaImportExecutionLock(IConfiguration configuration)
    : IBioWebTaImportExecutionLock
{
    private const string ResourceName = "HRSystem:BioWebTA:ScheduledImport";

    public async ValueTask<IAsyncDisposable?> TryAcquireAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        if (timeout < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        var connectionString = configuration.GetConnectionString("HRSystemDb");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "HRSystem database connection is required for the import lock.");
        }

        var connection = new SqlConnection(connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                DECLARE @result int;
                EXEC @result = sys.sp_getapplock
                    @Resource = @Resource,
                    @LockMode = N'Exclusive',
                    @LockOwner = N'Session',
                    @LockTimeout = @LockTimeout;
                SELECT @result;
                """;
            command.CommandType = CommandType.Text;
            command.Parameters.Add(new SqlParameter("@Resource", SqlDbType.NVarChar, 255)
            {
                Value = ResourceName
            });
            command.Parameters.Add(new SqlParameter("@LockTimeout", SqlDbType.Int)
            {
                Value = checked((int)Math.Min(timeout.TotalMilliseconds, int.MaxValue))
            });
            var result = Convert.ToInt32(
                await command.ExecuteScalarAsync(cancellationToken));
            if (result < 0)
            {
                await connection.DisposeAsync();
                return null;
            }

            return new Lease(connection);
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    private sealed class Lease(SqlConnection connection) : IAsyncDisposable
    {
        private SqlConnection? _connection = connection;

        public async ValueTask DisposeAsync()
        {
            var current = Interlocked.Exchange(ref _connection, null);
            if (current is null)
            {
                return;
            }

            try
            {
                await using var command = current.CreateCommand();
                command.CommandText = """
                    EXEC sys.sp_releaseapplock
                        @Resource = @Resource,
                        @LockOwner = N'Session';
                    """;
                command.Parameters.Add(new SqlParameter(
                    "@Resource", SqlDbType.NVarChar, 255)
                {
                    Value = ResourceName
                });
                await command.ExecuteNonQueryAsync(CancellationToken.None);
            }
            finally
            {
                await current.DisposeAsync();
            }
        }
    }
}
