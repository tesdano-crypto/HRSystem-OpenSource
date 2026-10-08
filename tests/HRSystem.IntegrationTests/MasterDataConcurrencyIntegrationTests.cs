using System.Data.Common;
using HRSystem.Application.Abstractions.Security;
using HRSystem.Application.Departments;
using HRSystem.Application.Employees;
using HRSystem.Application.Security;
using HRSystem.Domain.MasterData;
using HRSystem.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit.Abstractions;

namespace HRSystem.IntegrationTests;

[Collection(DisposableSqlServerCollection.Name)]
public sealed class MasterDataConcurrencyIntegrationTests :
    IClassFixture<MasterDataConcurrencyDatabaseFixture>
{
    private readonly ITestOutputHelper _output;
    private readonly MasterDataConcurrencyDatabaseFixture _database;
    private static readonly DateTimeOffset Now = new(2026, 7, 19, 0, 0, 0, TimeSpan.Zero);

    public MasterDataConcurrencyIntegrationTests(
        MasterDataConcurrencyDatabaseFixture database,
        ITestOutputHelper output)
    {
        _database = database;
        _output = output;
    }

    [Fact]
    [Trait("Category", "SqlConcurrency")]
    public async Task Department_Deactivation_And_Employee_Transfer_Cannot_Commit_Invalid_State()
    {
        var data = await CreateFixtureAsync();
        Exception? testFailure = null;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var barrier = new AsyncBarrier(2);
            await using var departmentDb = CreateContext(new PauseAfterReaderInterceptor(2, barrier));
            await using var employeeDb = CreateContext(new PauseAfterReaderInterceptor(3, barrier));
            var departmentService = new DepartmentService(departmentDb, new RelationalAdminUser(), TimeProvider.System);
            var employeeService = EmployeeNumberTestRegistration.CreateEmployeeService(
                employeeDb, new RelationalAdminUser());

            var outcomes = await Task.WhenAll(
                CaptureAsync(() => departmentService.SetActiveAsync(
                    data.TargetDepartmentId, false, data.TargetDepartmentRowVersion, timeout.Token)),
                CaptureAsync(() => employeeService.UpdateAsync(
                    UpdateEmployee(data, data.TargetDepartmentId), timeout.Token)));
            RecordRejectedOutcome(outcomes);

            await using var verificationDb = CreateContext();
            var targetIsActive = await verificationDb.Departments
                .Where(x => x.Id == data.TargetDepartmentId)
                .Select(x => x.IsActive)
                .SingleAsync(timeout.Token);
            var employeeDepartmentId = await verificationDb.Employees
                .Where(x => x.Id == data.EmployeeId)
                .Select(x => x.DepartmentId)
                .SingleAsync(timeout.Token);
            var auditCount = await RelevantAuditCountAsync(verificationDb, data, timeout.Token);

            Assert.Equal(1, outcomes.Count(x => x is null));
            Assert.True(targetIsActive || employeeDepartmentId != data.TargetDepartmentId,
                "An inactive Department must not contain the concurrently transferred active Employee.");
            Assert.Equal(1, auditCount);
        }
        catch (Exception exception)
        {
            testFailure = exception;
            throw;
        }
        finally
        {
            await CleanupPreservingFailureAsync(data, testFailure);
        }
    }

    [Fact]
    [Trait("Category", "SqlConcurrency")]
    public async Task Employee_Transfer_And_Manager_Assignment_Cannot_Commit_Invalid_State()
    {
        var data = await CreateFixtureAsync();
        Exception? testFailure = null;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var barrier = new AsyncBarrier(2);
            await using var departmentDb = CreateContext(new PauseAfterReaderInterceptor(3, barrier));
            await using var employeeDb = CreateContext(new PauseAfterReaderInterceptor(3, barrier));
            var departmentService = new DepartmentService(departmentDb, new RelationalAdminUser(), TimeProvider.System);
            var employeeService = EmployeeNumberTestRegistration.CreateEmployeeService(
                employeeDb, new RelationalAdminUser());

            var outcomes = await Task.WhenAll(
                CaptureAsync(() => departmentService.UpdateAsync(
                    AssignManager(data), timeout.Token)),
                CaptureAsync(() => employeeService.UpdateAsync(
                    UpdateEmployee(data, data.TargetDepartmentId), timeout.Token)));
            RecordRejectedOutcome(outcomes);

            await using var verificationDb = CreateContext();
            var managerId = await verificationDb.Departments
                .Where(x => x.Id == data.SourceDepartmentId)
                .Select(x => x.ManagerEmployeeId)
                .SingleAsync(timeout.Token);
            var employee = await verificationDb.Employees
                .Where(x => x.Id == data.EmployeeId)
                .Select(x => new { x.DepartmentId, x.IsActive })
                .SingleAsync(timeout.Token);
            var auditCount = await RelevantAuditCountAsync(verificationDb, data, timeout.Token);

            Assert.Equal(1, outcomes.Count(x => x is null));
            Assert.True(!managerId.HasValue ||
                (managerId == data.EmployeeId && employee.IsActive && employee.DepartmentId == data.SourceDepartmentId),
                "A Department Manager must remain active and assigned to the managed Department.");
            Assert.Equal(1, auditCount);
        }
        catch (Exception exception)
        {
            testFailure = exception;
            throw;
        }
        finally
        {
            await CleanupPreservingFailureAsync(data, testFailure);
        }
    }

    [Fact]
    [Trait("Category", "SqlConcurrency")]
    public async Task Employee_Deactivation_And_Manager_Assignment_Cannot_Commit_Invalid_State()
    {
        var data = await CreateFixtureAsync();
        Exception? testFailure = null;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var barrier = new AsyncBarrier(2);
            await using var departmentDb = CreateContext(new PauseAfterReaderInterceptor(3, barrier));
            await using var employeeDb = CreateContext(new PauseAfterReaderInterceptor(2, barrier));
            var departmentService = new DepartmentService(departmentDb, new RelationalAdminUser(), TimeProvider.System);
            var employeeService = EmployeeNumberTestRegistration.CreateEmployeeService(
                employeeDb, new RelationalAdminUser());

            var outcomes = await Task.WhenAll(
                CaptureAsync(() => departmentService.UpdateAsync(
                    AssignManager(data), timeout.Token)),
                CaptureAsync(() => employeeService.SetActiveAsync(
                    data.EmployeeId, false, data.EmployeeRowVersion, timeout.Token)));
            RecordRejectedOutcome(outcomes);

            await using var verificationDb = CreateContext();
            var managerId = await verificationDb.Departments
                .Where(x => x.Id == data.SourceDepartmentId)
                .Select(x => x.ManagerEmployeeId)
                .SingleAsync(timeout.Token);
            var employeeIsActive = await verificationDb.Employees
                .Where(x => x.Id == data.EmployeeId)
                .Select(x => x.IsActive)
                .SingleAsync(timeout.Token);
            var auditCount = await RelevantAuditCountAsync(verificationDb, data, timeout.Token);

            Assert.Equal(1, outcomes.Count(x => x is null));
            Assert.True(!managerId.HasValue || employeeIsActive,
                "An inactive Employee must not remain assigned as Department Manager.");
            Assert.Equal(1, auditCount);
        }
        catch (Exception exception)
        {
            testFailure = exception;
            throw;
        }
        finally
        {
            await CleanupPreservingFailureAsync(data, testFailure);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [Trait("Category", "SqlConcurrency")]
    [Trait("Category", "LoserContextReuse")]
    public async Task Rolled_Back_Loser_Context_Cannot_Resave_Failed_Manager_Or_Employee_Change(
        bool departmentOperationLoses)
    {
        var data = await CreateFixtureAsync();
        Exception? testFailure = null;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var barrier = new AsyncBarrier(2);
            await using var departmentDb = CreateContext(new PauseAfterReaderInterceptor(3, barrier));
            await using var employeeDb = CreateContext(new PauseAfterReaderInterceptor(3, barrier));
            if (departmentOperationLoses)
            {
                await SetDeadlockPriorityLowAsync(departmentDb, timeout.Token);
            }
            else
            {
                await SetDeadlockPriorityLowAsync(employeeDb, timeout.Token);
            }

            var departmentService = new DepartmentService(departmentDb, new RelationalAdminUser(), TimeProvider.System);
            var employeeService = EmployeeNumberTestRegistration.CreateEmployeeService(
                employeeDb, new RelationalAdminUser());
            var outcomes = await Task.WhenAll(
                CaptureAsync(() => departmentService.UpdateAsync(AssignManager(data), timeout.Token)),
                CaptureAsync(() => employeeService.UpdateAsync(
                    UpdateEmployee(data, data.TargetDepartmentId), timeout.Token)));

            var loserIndex = departmentOperationLoses ? 0 : 1;
            Assert.NotNull(outcomes[loserIndex]);
            Assert.Null(outcomes[1 - loserIndex]);
            Assert.Equal(1205, FindSqlException(outcomes[loserIndex]!)?.Number);

            var loserDb = departmentOperationLoses ? departmentDb : employeeDb;
            Assert.Empty(loserDb.ChangeTracker.Entries());
            await loserDb.SaveChangesAsync(timeout.Token);

            await using var verificationDb = CreateContext();
            var managerId = await verificationDb.Departments
                .Where(x => x.Id == data.SourceDepartmentId)
                .Select(x => x.ManagerEmployeeId)
                .SingleAsync(timeout.Token);
            var employeeDepartmentId = await verificationDb.Employees
                .Where(x => x.Id == data.EmployeeId)
                .Select(x => x.DepartmentId)
                .SingleAsync(timeout.Token);
            var audits = await verificationDb.AuditLogs
                .Where(x =>
                    (x.EntityType == nameof(Department) && x.EntityId == data.SourceDepartmentId.ToString()) ||
                    (x.EntityType == nameof(Employee) && x.EntityId == data.EmployeeId.ToString()))
                .ToListAsync(timeout.Token);

            var audit = Assert.Single(audits);
            Assert.Equal("Updated", audit.Action);
            if (departmentOperationLoses)
            {
                Assert.Null(managerId);
                Assert.Equal(data.TargetDepartmentId, employeeDepartmentId);
                Assert.Equal(nameof(Employee), audit.EntityType);
            }
            else
            {
                Assert.Equal(data.EmployeeId, managerId);
                Assert.Equal(data.SourceDepartmentId, employeeDepartmentId);
                Assert.Equal(nameof(Department), audit.EntityType);
            }
        }
        catch (Exception exception)
        {
            testFailure = exception;
            throw;
        }
        finally
        {
            await CleanupPreservingFailureAsync(data, testFailure);
        }
    }

    [Fact]
    [Trait("Category", "SqlConstraint")]
    public async Task Department_Code_Unique_Index_Is_Recognized()
    {
        var data = await CreateFixtureAsync();
        Exception? testFailure = null;
        try
        {
            await using var db = CreateContext();
            db.Departments.Add(new Department(
                Guid.NewGuid(), data.SourceDepartmentCode, "Duplicate Department Code", Now));

            var exception = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());

            Assert.True(db.IsUniqueConstraintViolation(exception, "UX_Departments_Code"));
        }
        catch (Exception exception)
        {
            testFailure = exception;
            throw;
        }
        finally
        {
            await CleanupPreservingFailureAsync(data, testFailure);
        }
    }

    [Fact]
    [Trait("Category", "SqlConstraint")]
    public async Task Employee_Number_Unique_Index_Is_Recognized()
    {
        var data = await CreateFixtureAsync();
        Exception? testFailure = null;
        try
        {
            await using var db = CreateContext();
            db.Employees.Add(new Employee(
                Guid.NewGuid(), data.EmployeeNumber, "Duplicate Employee Number",
                data.SourceDepartmentId, new DateOnly(2026, 1, 1), Now));

            var exception = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());

            Assert.True(db.IsUniqueConstraintViolation(exception, "UX_Employees_EmployeeNumber"));
        }
        catch (Exception exception)
        {
            testFailure = exception;
            throw;
        }
        finally
        {
            await CleanupPreservingFailureAsync(data, testFailure);
        }
    }

    [Fact]
    [Trait("Category", "SqlConstraint")]
    public async Task Primary_Key_Unique_Violation_Is_Not_Mislabeled_As_Department_Code_Duplicate()
    {
        var data = await CreateFixtureAsync();
        Exception? testFailure = null;
        try
        {
            await using var db = CreateContext();
            db.Departments.Add(new Department(
                data.SourceDepartmentId, $"PK{Guid.NewGuid():N}"[..10], "Duplicate Department Id", Now));

            var exception = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());

            Assert.Equal(2627, FindSqlException(exception)?.Number);
            Assert.False(db.IsUniqueConstraintViolation(exception, "UX_Departments_Code"));
        }
        catch (Exception exception)
        {
            testFailure = exception;
            throw;
        }
        finally
        {
            await CleanupPreservingFailureAsync(data, testFailure);
        }
    }

    private async Task<FixtureData> CreateFixtureAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var source = new Department(Guid.NewGuid(), $"CS{suffix}", $"Concurrency Source {suffix}", Now);
        var target = new Department(Guid.NewGuid(), $"CT{suffix}", $"Concurrency Target {suffix}", Now);
        var employee = new Employee(Guid.NewGuid(), $"CE{suffix}", $"Concurrency Employee {suffix}",
            source.Id, new DateOnly(2026, 1, 1), Now);

        await using var db = CreateContext();
        db.AddRange(source, target, employee);
        await db.SaveChangesAsync();
        return new FixtureData(
            source.Id,
            target.Id,
            employee.Id,
            source.Code,
            source.Name,
            employee.EmployeeNumber,
            employee.ChineseName,
            Convert.ToBase64String(source.RowVersion),
            Convert.ToBase64String(target.RowVersion),
            Convert.ToBase64String(employee.RowVersion));
    }

    private static UpdateDepartmentRequest AssignManager(FixtureData data) => new()
    {
        Id = data.SourceDepartmentId,
        Code = data.SourceDepartmentCode,
        Name = data.SourceDepartmentName,
        ManagerEmployeeId = data.EmployeeId,
        RowVersion = data.SourceDepartmentRowVersion
    };

    private static UpdateEmployeeRequest UpdateEmployee(FixtureData data, Guid departmentId) => new()
    {
        Id = data.EmployeeId,
        ChineseName = data.EmployeeName,
        DepartmentId = departmentId,
        HireDate = new DateOnly(2026, 1, 1),
        RowVersion = data.EmployeeRowVersion
    };

    private static async Task<Exception?> CaptureAsync(Func<Task> operation)
    {
        try
        {
            await operation();
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    private void RecordRejectedOutcome(IEnumerable<Exception?> outcomes)
    {
        var exception = outcomes.SingleOrDefault(x => x is not null);
        if (exception is null)
        {
            _output.WriteLine("Rejected operation: none");
            return;
        }

        var sqlException = FindSqlException(exception);
        var typeChain = string.Join(" -> ", ExceptionChain(exception).Select(x => x.GetType().Name));
        _output.WriteLine(sqlException is null
            ? $"Rejected operation: {typeChain}"
            : $"Rejected operation: {typeChain}; SQL error {sqlException.Number}");
    }

    private static SqlException? FindSqlException(Exception exception)
    {
        return ExceptionChain(exception).OfType<SqlException>().FirstOrDefault();
    }

    private static IEnumerable<Exception> ExceptionChain(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            yield return current;
        }
    }

    private static Task<int> RelevantAuditCountAsync(
        HRSystemDbContext db,
        FixtureData data,
        CancellationToken cancellationToken) =>
        db.AuditLogs.CountAsync(x =>
            (x.EntityType == nameof(Department) &&
                (x.EntityId == data.SourceDepartmentId.ToString() || x.EntityId == data.TargetDepartmentId.ToString())) ||
            (x.EntityType == nameof(Employee) && x.EntityId == data.EmployeeId.ToString()),
            cancellationToken);

    private async Task CleanupAsync(FixtureData data)
    {
        await using var db = CreateContext();
        await db.Departments
            .Where(x => x.Id == data.SourceDepartmentId || x.Id == data.TargetDepartmentId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.ManagerEmployeeId, (Guid?)null));
        await db.AuditLogs
            .Where(x =>
                (x.EntityType == nameof(Department) &&
                    (x.EntityId == data.SourceDepartmentId.ToString() || x.EntityId == data.TargetDepartmentId.ToString())) ||
                (x.EntityType == nameof(Employee) && x.EntityId == data.EmployeeId.ToString()))
            .ExecuteDeleteAsync();
        await db.Employees.Where(x => x.Id == data.EmployeeId).ExecuteDeleteAsync();
        await db.Departments
            .Where(x => x.Id == data.SourceDepartmentId || x.Id == data.TargetDepartmentId)
            .ExecuteDeleteAsync();
    }

    private async Task CleanupPreservingFailureAsync(
        FixtureData data,
        Exception? originalFailure)
    {
        try
        {
            await CleanupAsync(data);
        }
        catch (Exception cleanupFailure) when (originalFailure is not null)
        {
            throw new AggregateException(
                "The concurrency test and its exact-record cleanup both failed.",
                originalFailure,
                cleanupFailure);
        }
    }

    private static async Task SetDeadlockPriorityLowAsync(
        HRSystemDbContext db,
        CancellationToken cancellationToken)
    {
        await db.Database.OpenConnectionAsync(cancellationToken);
        await db.Database.ExecuteSqlRawAsync("SET DEADLOCK_PRIORITY LOW", cancellationToken);
    }

    private HRSystemDbContext CreateContext(
        DbCommandInterceptor? interceptor = null) =>
        _database.CreateDbContext(interceptor);

    private sealed class PauseAfterReaderInterceptor(int pauseAfterReader, AsyncBarrier barrier)
        : DbCommandInterceptor
    {
        private int _readerCount;
        private int _paused;

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            DbDataReader result,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _readerCount) == pauseAfterReader &&
                Interlocked.Exchange(ref _paused, 1) == 0)
            {
                await barrier.SignalAndWaitAsync(cancellationToken);
            }

            return result;
        }
    }

    private sealed class AsyncBarrier(int participants)
    {
        private readonly TaskCompletionSource _release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _arrived;

        public async Task SignalAndWaitAsync(CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _arrived) == participants)
            {
                _release.TrySetResult();
            }

            await _release.Task.WaitAsync(cancellationToken);
        }
    }

    private sealed class RelationalAdminUser : ICurrentUser
    {
        public string? UserId => "sql-concurrency-test-admin";
        public Guid? EmployeeId => null;
        public string? DisplayName => "SQL Concurrency Test Admin";
        public string? IpAddress => "127.0.0.1";
        public bool IsAuthenticated => true;
        public bool IsInRole(string role) => role == RoleNames.Admin;
        public bool HasPermission(string policy) => RolePermissions.HasPermission([RoleNames.Admin], policy);
    }

    private sealed record FixtureData(
        Guid SourceDepartmentId,
        Guid TargetDepartmentId,
        Guid EmployeeId,
        string SourceDepartmentCode,
        string SourceDepartmentName,
        string EmployeeNumber,
        string EmployeeName,
        string SourceDepartmentRowVersion,
        string TargetDepartmentRowVersion,
        string EmployeeRowVersion);
}
