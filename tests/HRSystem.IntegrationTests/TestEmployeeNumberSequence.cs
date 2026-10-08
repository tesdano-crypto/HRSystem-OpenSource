using HRSystem.Application.Abstractions.Security;
using HRSystem.Application.Employees;
using HRSystem.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;

namespace HRSystem.IntegrationTests;

internal sealed class TestEmployeeNumberSequence(int firstValue = 15) : IEmployeeNumberSequence
{
    private int _nextValue = firstValue - 1;
    private int _consumedCount;

    public Exception? Failure { get; init; }
    public int ConsumedCount => Volatile.Read(ref _consumedCount);

    public Task<int> GetNextValueAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Interlocked.Increment(ref _consumedCount);
        if (Failure is not null)
        {
            return Task.FromException<int>(Failure);
        }

        return Task.FromResult(Interlocked.Increment(ref _nextValue));
    }
}

internal static class EmployeeNumberTestRegistration
{
    public static void ReplaceEmployeeNumberSequence(
        this IServiceCollection services,
        TestEmployeeNumberSequence sequence)
    {
        services.RemoveAll<IEmployeeNumberSequence>();
        services.AddSingleton<IEmployeeNumberSequence>(sequence);
    }

    public static EmployeeService CreateEmployeeService(
        HRSystemDbContext dbContext,
        ICurrentUser currentUser) =>
        new(
            dbContext,
            new TestEmployeeNumberSequence(5000),
            currentUser,
            TimeProvider.System,
            NullLogger<EmployeeService>.Instance);
}
