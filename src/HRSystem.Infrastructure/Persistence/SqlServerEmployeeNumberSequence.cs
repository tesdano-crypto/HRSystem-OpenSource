using HRSystem.Application.Common.Exceptions;
using HRSystem.Application.Employees;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HRSystem.Infrastructure.Persistence;

public sealed class SqlServerEmployeeNumberSequence(
    HRSystemDbContext dbContext,
    ILogger<SqlServerEmployeeNumberSequence> logger) : IEmployeeNumberSequence
{
    private const int SequenceExhaustedErrorNumber = 11728;
    private const string NextValueSql =
        "SELECT NEXT VALUE FOR [dbo].[EmployeeNumberSequence] AS [Value]";

    public async Task<int> GetNextValueAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await foreach (var value in dbContext.Database
                .SqlQueryRaw<int>(NextValueSql)
                .AsAsyncEnumerable()
                .WithCancellation(cancellationToken))
            {
                return value;
            }

            throw new EmployeeNumberGenerationException();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (SqlException exception) when (exception.Number == SequenceExhaustedErrorNumber)
        {
            logger.LogError(
                "Employee number sequence capacity was exhausted with SQL error {ErrorNumber}.",
                exception.Number);
            throw new EmployeeNumberCapacityExceededException(exception);
        }
        catch (SqlException exception)
        {
            logger.LogError(
                "Employee number sequence allocation failed with SQL error {ErrorNumber}.",
                exception.Number);
            throw new EmployeeNumberGenerationException(innerException: exception);
        }
    }
}
