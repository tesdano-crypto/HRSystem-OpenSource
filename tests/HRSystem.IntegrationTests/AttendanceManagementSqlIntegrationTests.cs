using HRSystem.Application.Abstractions.Security;
using HRSystem.Application.Attendance;
using HRSystem.Application.Common.Exceptions;
using HRSystem.Application.Security;
using HRSystem.Domain.Attendance;
using HRSystem.Domain.MasterData;
using HRSystem.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace HRSystem.IntegrationTests;

public sealed class AttendanceManagementSqlIntegrationTests
{
    private const string DatabasePrefix = "HRSystem_Phase734_Attendance_Test_";
    private static readonly DateTimeOffset Now =
        new(2026, 7, 28, 2, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly WorkDate = new(2026, 7, 28);

    [Fact]
    [Trait("Category", "SqlAttendance")]
    public async Task Disposable_Database_Proves_Migration_Query_Recalculation_Adjustment_And_Cleanup()
    {
        var databaseName = DatabasePrefix + Guid.NewGuid().ToString("N");
        Exception? originalFailure = null;
        var databaseCreated = false;
        try
        {
            await CreateDatabaseAsync(databaseName);
            databaseCreated = true;
            var connectionString = BuildConnectionString(databaseName);
            await using var db = CreateContext(connectionString);
            await db.Database.MigrateAsync();
            Assert.Equal(
                DisposableSqlServerDatabase.ExpectedMigrationCount,
                await db.Database.SqlQueryRaw<int>(
                        "SELECT COUNT(*) AS [Value] FROM [dbo].[__EFMigrationsHistory]")
                    .SingleAsync());
            await AssertSchemaAsync(db);

            var department = new Department(
                Guid.NewGuid(), "SQL-ATT", "Attendance SQL", Now);
            var employee = new Employee(
                Guid.NewGuid(),
                "EMP9901",
                "SQL Attendance Employee",
                department.Id,
                new DateOnly(2026, 1, 1),
                Now);
            var secondEmployee = new Employee(
                Guid.NewGuid(),
                "EMP9902",
                "SQL Overlap Employee",
                department.Id,
                new DateOnly(2026, 1, 1),
                Now);
            var shift = NormalShift();
            var assignment = new EmployeeShiftAssignment(
                Guid.NewGuid(),
                employee.Id,
                shift.Id,
                new DateOnly(2026, 1, 1),
                null,
                Now);
            db.AddRange(
                department,
                employee,
                secondEmployee,
                shift,
                assignment);
            db.AttendanceRawEvents.AddRange(
                Raw(1, employee.Id, Local(7, 55)),
                Raw(2, employee.Id, Local(7, 56)),
                Raw(3, employee.Id, Local(12, 30)),
                Raw(4, employee.Id, Local(17, 31)),
                Raw(5, employee.Id, Local(17, 35)));
            await db.SaveChangesAsync();
            db.ClearTrackedChanges();
            var service = new AttendanceManagementService(
                db,
                new SqlAdminCurrentUser(),
                new FixedTimeProvider(Now));
            var request = new AttendanceRecalculationRequest
            {
                DateFrom = WorkDate,
                DateTo = WorkDate,
                EmployeeId = employee.Id
            };

            await service.RecalculateAsync(request);
            await service.RecalculateAsync(request);
            var daily = await db.DailyAttendanceResults
                .AsNoTracking()
                .SingleAsync();

            Assert.Equal(1, await db.DailyAttendanceResults.CountAsync());
            Assert.Equal(5, await db.AttendanceRawEvents.CountAsync());
            Assert.Equal(Local(7, 55), daily.RawClockInLocalTime);
            Assert.Equal(Local(17, 35), daily.RawClockOutLocalTime);
            Assert.Equal(AttendanceDailyStatus.Normal, daily.Status);
            Assert.Equal("NORMAL", daily.ShiftCodeSnapshot);
            Assert.Equal(new TimeOnly(8, 0),
                daily.ScheduledStartTimeSnapshot);

            var query = await service.GetDailyResultsAsync(new()
            {
                DateFrom = WorkDate,
                DateTo = WorkDate,
                EmployeeId = employee.Id,
                PageSize = 1000
            });
            Assert.Equal(100, query.PageSize);
            var item = Assert.Single(query.Items);
            var adjusted = await service.AdjustAsync(new()
            {
                DailyAttendanceResultId = item.Id,
                AdjustClockIn = true,
                RecognizedClockInLocalTime = Local(8, 0),
                AdjustClockOut = true,
                RecognizedClockOutLocalTime = Local(17, 30),
                Reason = AttendanceAdjustmentReason.ManagerApproved,
                Note = "Approved disposable SQL adjustment.",
                RowVersion = item.RowVersion
            });
            Assert.True(adjusted.IsAdjusted);
            Assert.Equal(Local(7, 55), adjusted.RawClockInLocalTime);
            Assert.Equal(Local(17, 35), adjusted.RawClockOutLocalTime);
            Assert.Equal(Local(8, 0), adjusted.EffectiveClockInLocalTime);
            Assert.Equal(Local(17, 30), adjusted.EffectiveClockOutLocalTime);
            Assert.Equal(5, await db.AttendanceRawEvents.CountAsync());

            await service.RecalculateAsync(request);
            var afterRecalculation = (await service.GetDailyResultsAsync(new()
            {
                DateFrom = WorkDate,
                DateTo = WorkDate,
                EmployeeId = employee.Id
            })).Items.Single();
            Assert.True(afterRecalculation.IsAdjusted);
            Assert.Equal(Local(8, 0),
                afterRecalculation.EffectiveClockInLocalTime);
            Assert.Equal(
                1,
                await db.AttendanceAdjustments.CountAsync());

            var reverted = await service.RevertAdjustmentAsync(new()
            {
                DailyAttendanceResultId = afterRecalculation.Id,
                Note = "Approved disposable SQL revert.",
                RowVersion = afterRecalculation.RowVersion
            });
            Assert.False(reverted.IsAdjusted);
            Assert.Equal(reverted.RawClockInLocalTime,
                reverted.EffectiveClockInLocalTime);
            Assert.Equal(2, await db.AttendanceAdjustments.CountAsync());

            db.EmployeeShiftAssignments.AddRange(
                new EmployeeShiftAssignment(
                    Guid.NewGuid(),
                    secondEmployee.Id,
                    shift.Id,
                    WorkDate,
                    null,
                    Now),
                new EmployeeShiftAssignment(
                    Guid.NewGuid(),
                    secondEmployee.Id,
                    shift.Id,
                    WorkDate,
                    null,
                    Now));
            await db.SaveChangesAsync();
            db.ClearTrackedChanges();
            await Assert.ThrowsAsync<ApplicationValidationException>(() =>
                service.RecalculateAsync(new()
                {
                    DateFrom = WorkDate,
                    DateTo = WorkDate,
                    EmployeeId = secondEmployee.Id
                }));
            Assert.Equal(
                0,
                await db.DailyAttendanceResults.CountAsync(
                    result => result.EmployeeId == secondEmployee.Id));

            var inactiveEmployee = new Employee(
                Guid.NewGuid(),
                "TEST001",
                "Inactive SQL Employee",
                department.Id,
                new DateOnly(2026, 1, 1),
                Now);
            inactiveEmployee.Deactivate(Now);
            var terminatingEmployee = new Employee(
                Guid.NewGuid(),
                "EMP9903",
                "Terminating SQL Employee",
                department.Id,
                new DateOnly(2026, 1, 1),
                Now,
                terminationDate: WorkDate);
            db.Employees.AddRange(inactiveEmployee, terminatingEmployee);
            db.DailyAttendanceResults.AddRange(
                CreateDailyResult(inactiveEmployee, WorkDate, shift),
                CreateDailyResult(terminatingEmployee, WorkDate, shift),
                CreateDailyResult(
                    terminatingEmployee,
                    WorkDate.AddDays(1),
                    shift));
            await db.SaveChangesAsync();
            db.ClearTrackedChanges();

            var defaultOptions = await service.GetDailyEmployeeOptionsAsync(
                WorkDate,
                WorkDate);
            Assert.DoesNotContain(
                defaultOptions,
                option => option.EmployeeNumber == "TEST001");
            Assert.Contains(
                defaultOptions,
                option => option.EmployeeNumber == "EMP9901");
            Assert.Contains(
                defaultOptions,
                option => option.EmployeeNumber == "EMP9903");

            var defaultDaily = await service.GetDailyResultsAsync(new()
            {
                DateFrom = WorkDate,
                DateTo = WorkDate
            });
            Assert.DoesNotContain(
                defaultDaily.Items,
                item => item.EmployeeNumber == "TEST001");
            Assert.Contains(
                defaultDaily.Items,
                item => item.EmployeeNumber == "EMP9901");
            Assert.Contains(
                defaultDaily.Items,
                item => item.EmployeeNumber == "EMP9903");

            var includedOptions = await service.GetDailyEmployeeOptionsAsync(
                WorkDate,
                WorkDate,
                includeInactiveEmployees: true);
            Assert.Contains(
                includedOptions,
                option => option.EmployeeNumber == "TEST001");
            Assert.Equal(
                "TEST001",
                Assert.Single((await service.GetDailyResultsAsync(new()
                {
                    DateFrom = WorkDate,
                    DateTo = WorkDate,
                    EmployeeId = inactiveEmployee.Id,
                    IncludeInactiveEmployees = true
                })).Items).EmployeeNumber);

            Assert.Empty((await service.GetDailyResultsAsync(new()
            {
                DateFrom = WorkDate.AddDays(1),
                DateTo = WorkDate.AddDays(1),
                EmployeeId = terminatingEmployee.Id
            })).Items);
            Assert.Equal(
                WorkDate.AddDays(1),
                Assert.Single((await service.GetDailyResultsAsync(new()
                {
                    DateFrom = WorkDate.AddDays(1),
                    DateTo = WorkDate.AddDays(1),
                    EmployeeId = terminatingEmployee.Id,
                    IncludeInactiveEmployees = true
                })).Items).WorkDate);

            await Assert.ThrowsAsync<ApplicationValidationException>(() =>
                service.RecalculateAsync(new()
                {
                    DateFrom = WorkDate.AddDays(1),
                    DateTo = WorkDate.AddDays(1),
                    EmployeeId = inactiveEmployee.Id
                }));
            Assert.Equal(
                0,
                await db.DailyAttendanceResults.CountAsync(result =>
                    result.EmployeeId == inactiveEmployee.Id &&
                    result.WorkDate == WorkDate.AddDays(1)));
            Assert.Equal(0, await db.AttendanceSyncStates.CountAsync());
            Assert.Equal(0, await db.BioWebPersonMappings.CountAsync());
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
                await DropDatabasePreservingFailureAsync(
                    databaseName,
                    originalFailure);
            }

            Assert.Equal(0, await DisposableDatabaseCountAsync());
        }
    }

    private static async Task AssertSchemaAsync(HRSystemDbContext db)
    {
        var tableCount = await db.Database.SqlQueryRaw<int>(
                """
                SELECT COUNT(*) AS [Value]
                FROM [sys].[tables]
                WHERE [name] IN (
                    N'AttendanceShifts',
                    N'EmployeeShiftAssignments',
                    N'DailyAttendanceResults',
                    N'AttendanceAdjustments')
                """)
            .SingleAsync();
        var uniqueDaily = await db.Database.SqlQueryRaw<int>(
                """
                SELECT COUNT(*) AS [Value]
                FROM [sys].[indexes]
                WHERE [name] = N'UX_DailyAttendanceResults_Employee_WorkDate'
                  AND [is_unique] = 1
                """)
            .SingleAsync();

        Assert.Equal(4, tableCount);
        Assert.Equal(1, uniqueDaily);
    }

    private static DailyAttendanceResult CreateDailyResult(
        Employee employee,
        DateOnly workDate,
        AttendanceShift shift)
    {
        var result = new DailyAttendanceResult(
            Guid.NewGuid(),
            employee.Id,
            workDate,
            Now);
        result.Recalculate(
            true,
            AttendanceCalendarClassification.FallbackWorkingDay,
            shift,
            AttendanceDailyCalculator.Calculate(
                workDate,
                true,
                new AttendanceShiftSnapshot(
                    shift.Id,
                    shift.Name,
                    shift.ScheduledStartTime,
                    shift.LateThresholdTime,
                    shift.LunchBreakStartTime,
                    shift.LunchBreakEndTime,
                    shift.ScheduledEndTime,
                    shift.ExpectedWorkMinutes,
                    shift.IsLunchPunchRequired,
                    shift.IsOvernightShift),
                []),
            "integration-filter-test",
            Now);
        return result;
    }

    private static AttendanceShift NormalShift() =>
        new(
            Guid.NewGuid(),
            "NORMAL",
            "Normal",
            new TimeOnly(8, 0),
            new TimeOnly(8, 1),
            new TimeOnly(12, 0),
            new TimeOnly(13, 30),
            new TimeOnly(17, 30),
            480,
            false,
            false,
            Now);

    private static AttendanceRawEvent Raw(
        long externalId,
        Guid employeeId,
        DateTime local) =>
        new(
            Guid.NewGuid(),
            AttendanceSourceSystems.BioWebTa,
            externalId,
            employeeId,
            "SQL-TEST-PIN",
            "SQL-TEST-DEVICE",
            local,
            99,
            88,
            local,
            Now);

    private static DateTime Local(int hour, int minute) =>
        DateTime.SpecifyKind(
            WorkDate.ToDateTime(new TimeOnly(hour, minute)),
            DateTimeKind.Unspecified);

    private static HRSystemDbContext CreateContext(string connectionString) =>
        new(
            new DbContextOptionsBuilder<HRSystemDbContext>()
                .UseSqlServer(
                    connectionString,
                    sql => sql.MigrationsAssembly(
                        typeof(HRSystemDbContext).Assembly.FullName))
                .Options);

    private static async Task CreateDatabaseAsync(string databaseName)
    {
        _ = BuildConnectionString(databaseName);
        await using var connection =
            new SqlConnection(BuildMasterConnectionString());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"CREATE DATABASE {QuoteDatabaseName(databaseName)}";
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
            await using var connection =
                new SqlConnection(BuildMasterConnectionString());
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
                $"Test and cleanup failed for {databaseName}.",
                originalFailure,
                cleanupFailure);
        }
    }

    private static async Task<int> DisposableDatabaseCountAsync()
    {
        await using var connection =
            new SqlConnection(BuildMasterConnectionString());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT COUNT(*) FROM [sys].[databases] " +
            "WHERE [name] LIKE N'HRSystem[_]Phase734[_]Attendance[_]Test[_]%'";
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

    private static SqlConnectionStringBuilder NewLocalIntegratedBuilder() =>
        new()
        {
            DataSource = @".\SQLEXPRESS",
            IntegratedSecurity = true,
            Encrypt = true,
            TrustServerCertificate = true,
            Pooling = false
        };

    private static void ValidateDisposableDatabaseName(string databaseName)
    {
        var suffix = databaseName.StartsWith(
            DatabasePrefix,
            StringComparison.Ordinal)
            ? databaseName[DatabasePrefix.Length..]
            : string.Empty;
        if (!databaseName.StartsWith(
                DatabasePrefix,
                StringComparison.Ordinal) ||
            !Guid.TryParseExact(suffix, "N", out _) ||
            databaseName.Contains(
                "HRSystemDb",
                StringComparison.OrdinalIgnoreCase) ||
            databaseName.Contains("PRODUCTION", StringComparison.OrdinalIgnoreCase) ||
            databaseName.Contains("EXTERNAL_ERP", StringComparison.OrdinalIgnoreCase) ||
            databaseName.Contains(
                "EXTERNAL_ATTENDANCE_DB",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Rejected unsafe Phase 7.3/7.4 disposable database name.");
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
        public string? UserId => "phase734-sql-test-admin";
        public Guid? EmployeeId => null;
        public string? DisplayName => "Phase 7.3 SQL Test Admin";
        public string? IpAddress => "127.0.0.1";
        public bool IsAuthenticated => true;
        public bool IsInRole(string role) => role == RoleNames.Admin;
        public bool HasPermission(string policy) =>
            RolePermissions.HasPermission([RoleNames.Admin], policy);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
