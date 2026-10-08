using HRSystem.Application.Abstractions.Security;
using HRSystem.Application.Employees;
using HRSystem.Application.Security;
using HRSystem.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HRSystem.UnitTests;

internal sealed class TestCurrentUser(string role = RoleNames.Admin, Guid? employeeId = null) : ICurrentUser
{
    public string? UserId => "unit-test-user";
    public Guid? EmployeeId => employeeId;
    public string? DisplayName => "單元測試使用者";
    public string? IpAddress => "127.0.0.1";
    public bool IsAuthenticated => true;
    public bool IsInRole(string expectedRole) => string.Equals(role, expectedRole, StringComparison.Ordinal);
    public bool HasPermission(string policy) => RolePermissions.HasPermission([role], policy);
}

internal static class TestDb
{
    public static HRSystemDbContext Create() => new(
        new DbContextOptionsBuilder<HRSystemDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
}

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

internal static class TestEmployeeServices
{
    public static EmployeeService Create(
        HRSystemDbContext dbContext,
        ICurrentUser? currentUser = null,
        TestEmployeeNumberSequence? sequence = null) =>
        new(
            dbContext,
            sequence ?? new TestEmployeeNumberSequence(),
            currentUser ?? new TestCurrentUser(),
            TimeProvider.System,
            NullLogger<EmployeeService>.Instance);
}
