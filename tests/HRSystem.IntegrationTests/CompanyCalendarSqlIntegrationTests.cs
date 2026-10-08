using HRSystem.Application.Abstractions.Security;
using HRSystem.Application.Common.Exceptions;
using HRSystem.Application.CompanyCalendars;
using HRSystem.Application.Security;
using HRSystem.Domain.CompanyCalendars;
using HRSystem.Domain.LeaveRequests;
using HRSystem.Domain.MasterData;
using HRSystem.Infrastructure.CompanyCalendars;
using HRSystem.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit.Abstractions;

namespace HRSystem.IntegrationTests;

public sealed class CompanyCalendarSqlIntegrationTests(ITestOutputHelper output)
{
    private const string DatabasePrefix = "HRSystem_Phase6_Test_";
    private const string PreviousMigration = "20260725015454_AddEmployeeNumberSequence";
    private const string CalendarMigration = "20260725110446_AddCompanyCalendar";
    private static readonly DateTimeOffset Now =
        new(2026, 7, 25, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    [Trait("Category", "SqlCalendar")]
    public async Task Disposable_Database_Proves_Migration_Constraints_Concurrency_Audit_And_Cleanup()
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
            SyntheticBaseline baseline;
            await using (var setupDb = CreateContext(connectionString))
            {
                var migrator = setupDb.GetService<IMigrator>();
                await migrator.MigrateAsync(PreviousMigration);
                baseline = await SeedSyntheticBaselineAsync(setupDb);
                await migrator.MigrateAsync(CalendarMigration);
                Assert.Equal(5, await MigrationCountAsync(setupDb));
                Assert.Equal(0, await setupDb.CompanyCalendarYears.CountAsync());
                Assert.Equal(0, await setupDb.CompanyCalendarDays.CountAsync());
            }

            await AssertConcurrentInitializationAsync(connectionString, 2026);
            await AssertDatabaseConstraintsAsync(connectionString);
            await AssertPublicationConcurrencyAndAuditAsync(connectionString);
            await AssertRoleBoundariesAsync(connectionString);
            await AssertSyntheticBaselineUnchangedAsync(connectionString, baseline);

            await using (var downDb = CreateContext(connectionString))
            {
                await downDb.GetService<IMigrator>().MigrateAsync(PreviousMigration);
                Assert.Equal(0, await TableCountAsync(downDb, "CompanyCalendarYears"));
                Assert.Equal(0, await TableCountAsync(downDb, "CompanyCalendarDays"));
            }
            await AssertSyntheticBaselineUnchangedAsync(connectionString, baseline);
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

            Assert.Equal(0, await DisposableDatabaseCountAsync());
        }
    }

    private static async Task AssertConcurrentInitializationAsync(
        string connectionString,
        int year)
    {
        var bytes = await File.ReadAllBytesAsync(ManifestPath(year));
        await using var previewDb = CreateContext(connectionString);
        var previewService = NewService(previewDb, RoleNames.Admin);
        await using var stream = new MemoryStream(bytes);
        var preview = await previewService.PreviewInitializationAsync(stream);

        async Task<Exception?> InitializeAsync()
        {
            try
            {
                await using var db = CreateContext(connectionString);
                await NewService(db, RoleNames.Admin).InitializeDraftAsync(
                    new InitializeCompanyCalendarRequest
                    {
                        ManifestBytes = bytes,
                        ExpectedManifestHash = preview.ManifestHash
                    });
                return null;
            }
            catch (Exception exception)
            {
                return exception;
            }
        }

        var outcomes = await Task.WhenAll(InitializeAsync(), InitializeAsync());
        Assert.Single(outcomes, outcome => outcome is null);
        Assert.Single(outcomes, outcome => outcome is ApplicationValidationException);

        await using var verificationDb = CreateContext(connectionString);
        Assert.Equal(1, await verificationDb.CompanyCalendarYears.CountAsync(item => item.Year == year));
        Assert.Equal(
            DateTime.IsLeapYear(year) ? 366 : 365,
            await verificationDb.CompanyCalendarDays.CountAsync(item => item.CalendarYear == year));
        Assert.Equal(
            1,
            await verificationDb.AuditLogs.CountAsync(item =>
                item.Action == "CompanyCalendarInitialized"));
    }

    private static async Task AssertDatabaseConstraintsAsync(string connectionString)
    {
        await using var db = CreateContext(connectionString);
        var year = await db.CompanyCalendarYears.Include(item => item.Days).SingleAsync();
        var duplicateYear = NewYear(year.Year);
        db.CompanyCalendarYears.Add(duplicateYear);
        var yearFailure = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.True(db.IsUniqueConstraintViolation(
            yearFailure,
            "UX_CompanyCalendarYears_Year"));
        db.ChangeTracker.Clear();

        year = await db.CompanyCalendarYears.Include(item => item.Days).SingleAsync();
        var original = year.Days.First();
        db.CompanyCalendarDays.Add(new CompanyCalendarDay(
            Guid.NewGuid(),
            year.Id,
            year.Year,
            original.Date,
            original.BaseDayType,
            original.BaseName,
            original.SourceNote,
            original.SourceReference,
            "phase6-sql-test-admin",
            Now));
        var dayFailure = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.True(db.IsUniqueConstraintViolation(
            dayFailure,
            "UX_CompanyCalendarDays_Year_Date"));
        db.ChangeTracker.Clear();

        var command = """
            INSERT INTO [CompanyCalendarDays]
                ([Id], [CompanyCalendarYearId], [CalendarYear], [Date],
                 [BaseDayType], [DayType], [CreatedAtUtc], [CreatedBy],
                 [ModifiedAtUtc], [ModifiedBy])
            VALUES
                (@id, @yearId, 2026, '2027-01-01',
                 1, 1, @now, N'phase6-sql-test-admin',
                 @now, N'phase6-sql-test-admin')
            """;
        var checkFailure = await Assert.ThrowsAsync<SqlException>(() =>
            db.Database.ExecuteSqlRawAsync(
                command,
                new SqlParameter("@id", Guid.NewGuid()),
                new SqlParameter("@yearId", year.Id),
                new SqlParameter("@now", Now)));
        Assert.Contains(
            "CK_CompanyCalendarDays_DateYear",
            checkFailure.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    private static async Task AssertPublicationConcurrencyAndAuditAsync(
        string connectionString)
    {
        CompanyCalendarYearDetailDto draft;
        await using (var readDb = CreateContext(connectionString))
        {
            draft = await NewService(readDb, RoleNames.Admin).GetManagementYearAsync(2026);
        }

        async Task<Exception?> PublishAsync()
        {
            try
            {
                await using var db = CreateContext(connectionString);
                await NewService(db, RoleNames.Admin).PublishAsync(
                    new PublishCompanyCalendarRequest
                    {
                        Year = 2026,
                        RowVersion = draft.Year.RowVersion,
                        ExpectedManifestVersion = draft.Year.ManifestVersion,
                        ExpectedManifestHash = draft.Year.ManifestHash
                    });
                return null;
            }
            catch (Exception exception)
            {
                return exception;
            }
        }

        var outcomes = await Task.WhenAll(PublishAsync(), PublishAsync());
        Assert.Single(outcomes, outcome => outcome is null);
        Assert.Single(outcomes, outcome =>
            outcome is ConcurrencyConflictException ||
            outcome is HRSystem.Domain.Common.DomainValidationException);

        await using var db = CreateContext(connectionString);
        Assert.Equal(
            CompanyCalendarStatus.Published,
            await db.CompanyCalendarYears.Select(item => item.Status).SingleAsync());
        Assert.Equal(
            1,
            await db.AuditLogs.CountAsync(item =>
                item.Action == "CompanyCalendarPublished"));

        var service = NewService(db, RoleNames.Admin);
        var published = await service.GetManagementYearAsync(2026);
        var day = published.Days.Single(item => item.Date == new DateOnly(2026, 7, 6));
        await service.OverridePublishedDayAsync(new OverridePublishedCalendarDayRequest
        {
            Year = 2026,
            Date = day.Date,
            DayType = CompanyCalendarDayType.CompanyHoliday,
            Name = "合成公司假日",
            Reason = "Phase 6 SQL 原子性測試",
            YearRowVersion = published.Year.RowVersion,
            DayRowVersion = day.RowVersion
        });
        Assert.Equal(
            1,
            await db.AuditLogs.CountAsync(item =>
                item.Action == "CompanyCalendarDayOverridden"));
    }

    private static async Task AssertRoleBoundariesAsync(string connectionString)
    {
        foreach (var role in new[] { RoleNames.Manager, RoleNames.Employee })
        {
            await using var db = CreateContext(connectionString);
            var query = new CompanyCalendarQueryService(db, new SqlCurrentUser(role));
            Assert.NotNull(await query.GetDayAsync(new DateOnly(2026, 1, 1)));
            var service = NewService(db, role);
            await Assert.ThrowsAsync<ForbiddenAccessException>(() =>
                service.GetManagementYearsAsync());
        }
    }

    private static async Task<SyntheticBaseline> SeedSyntheticBaselineAsync(
        HRSystemDbContext db)
    {
        var department = new Department(
            Guid.NewGuid(), "P6", "Phase 6 合成部門", Now);
        var employee = new Employee(
            Guid.NewGuid(),
            "EMP9001",
            "Phase 6 合成員工",
            department.Id,
            new DateOnly(2026, 1, 1),
            Now);
        var leaveType = new LeaveType(
            Guid.NewGuid(),
            "P6",
            "Phase 6 合成假別",
            LeaveUnit.Day,
            1,
            true,
            true,
            900,
            Now);
        var leave = new LeaveRequest(
            Guid.NewGuid(),
            "P6-LEAVE-001",
            employee.Id,
            leaveType.Id,
            Now.AddDays(1),
            Now.AddDays(2),
            24m,
            "Phase 6 合成原因",
            "phase6-sql-test-admin",
            Now);
        db.AddRange(department, employee);
        await db.SaveChangesAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO [LeaveTypes] (
                [Id], [Code], [Name], [Unit], [MinimumUnit],
                [RequiresReason], [IsPaid], [IsActive], [SortOrder],
                [CreatedAtUtc], [UpdatedAtUtc])
            VALUES (
                {leaveType.Id}, {leaveType.Code}, {leaveType.Name}, {(byte)leaveType.Unit},
                {leaveType.MinimumUnit}, {leaveType.RequiresReason}, {leaveType.IsPaid},
                {leaveType.IsActive}, {leaveType.SortOrder}, {leaveType.CreatedAtUtc},
                {leaveType.UpdatedAtUtc});
            """);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO [LeaveRequests] (
                [Id], [RequestNumber], [EmployeeId], [LeaveTypeId],
                [StartAt], [EndAt], [DurationHours], [Reason], [Status],
                [SubmittedAtUtc], [ApprovedAtUtc], [RejectedAtUtc],
                [WithdrawnAtUtc], [CreatedAtUtc], [UpdatedAtUtc],
                [CreatedByUserId], [UpdatedByUserId])
            VALUES (
                {leave.Id}, {leave.RequestNumber}, {leave.EmployeeId},
                {leave.LeaveTypeId}, {leave.StartAt}, {leave.EndAt},
                {leave.DurationHours}, {leave.Reason}, {(byte)leave.Status},
                {leave.SubmittedAtUtc}, {leave.ApprovedAtUtc},
                {leave.RejectedAtUtc}, {leave.WithdrawnAtUtc},
                {leave.CreatedAtUtc}, {leave.UpdatedAtUtc},
                {leave.CreatedByUserId}, {leave.UpdatedByUserId});
            """);
        return new SyntheticBaseline(
            await db.Departments.CountAsync(),
            await db.Employees.CountAsync(),
            await db.LeaveTypes.CountAsync(),
            await db.LeaveRequests.CountAsync(),
            employee.Id,
            employee.EmployeeNumber,
            leave.Id,
            leave.DurationHours);
    }

    private static async Task AssertSyntheticBaselineUnchangedAsync(
        string connectionString,
        SyntheticBaseline baseline)
    {
        await using var db = CreateContext(connectionString);
        Assert.Equal(baseline.Departments, await db.Departments.CountAsync());
        Assert.Equal(baseline.Employees, await db.Employees.CountAsync());
        Assert.Equal(baseline.LeaveTypes, await db.LeaveTypes.CountAsync());
        Assert.Equal(baseline.LeaveRequests, await db.LeaveRequests.CountAsync());
        Assert.Equal(
            baseline.EmployeeNumber,
            await db.Employees.Where(item => item.Id == baseline.EmployeeId)
                .Select(item => item.EmployeeNumber)
                .SingleAsync());
        Assert.Equal(
            baseline.LeaveDurationHours,
            await db.LeaveRequests.Where(item => item.Id == baseline.LeaveRequestId)
                .Select(item => item.DurationHours)
                .SingleAsync());
    }

    private static CompanyCalendarService NewService(HRSystemDbContext db, string role) =>
        new(
            db,
            new JsonCompanyCalendarManifestReader(),
            new SqlCurrentUser(role),
            TimeProvider.System);

    private static CompanyCalendarYear NewYear(int year) =>
        new(
            Guid.NewGuid(),
            year,
            "合成來源機關",
            "合成來源標題",
            new DateOnly(2025, 6, 13),
            "https://www.dgpa.gov.tw/information?pid=12573&uid=41",
            "合成文號",
            new string('a', 64),
            "test",
            new string('b', 64),
            "phase6-sql-test-admin",
            Now);

    private static HRSystemDbContext CreateContext(string connectionString) =>
        new(
            new DbContextOptionsBuilder<HRSystemDbContext>()
                .UseSqlServer(
                    connectionString,
                    sql => sql.MigrationsAssembly(
                        typeof(HRSystemDbContext).Assembly.FullName))
                .Options);

    private static Task<int> MigrationCountAsync(HRSystemDbContext db) =>
        db.Database.SqlQueryRaw<int>(
                "SELECT COUNT(*) AS [Value] FROM [dbo].[__EFMigrationsHistory]")
            .SingleAsync();

    private static Task<int> TableCountAsync(HRSystemDbContext db, string table) =>
        db.Database.SqlQuery<int>(
                $"""
                SELECT COUNT(*) AS [Value]
                FROM [sys].[tables]
                WHERE [name] = {table}
                """)
            .SingleAsync();

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
                $"Test and cleanup failed for {databaseName}.",
                originalFailure,
                cleanupFailure);
        }
    }

    private static async Task<int> DisposableDatabaseCountAsync()
    {
        await using var connection = new SqlConnection(BuildMasterConnectionString());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT COUNT(*) FROM [sys].[databases] WHERE [name] LIKE N'HRSystem[_]Phase6[_]Test[_]%'";
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static string BuildValidatedConnectionString(string databaseName)
    {
        ValidateDisposableDatabaseName(databaseName);
        var builder = NewLocalIntegratedBuilder();
        builder.InitialCatalog = databaseName;
        if (!IsApprovedLocalServer(builder.DataSource) ||
            !builder.IntegratedSecurity ||
            !string.IsNullOrWhiteSpace(builder.UserID))
        {
            throw new InvalidOperationException("Rejected unsafe Phase 6 SQL target.");
        }

        return builder.ConnectionString;
    }

    private static string BuildMasterConnectionString()
    {
        var builder = NewLocalIntegratedBuilder();
        builder.InitialCatalog = "master";
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

    private static void ValidateDisposableDatabaseName(string databaseName)
    {
        var suffix = databaseName.StartsWith(DatabasePrefix, StringComparison.Ordinal)
            ? databaseName[DatabasePrefix.Length..]
            : string.Empty;
        if (!databaseName.StartsWith(DatabasePrefix, StringComparison.Ordinal) ||
            !Guid.TryParseExact(suffix, "N", out _) ||
            databaseName.Contains("HRSystemDb", StringComparison.OrdinalIgnoreCase) ||
            databaseName.Contains("PRODUCTION", StringComparison.OrdinalIgnoreCase) ||
            databaseName.Contains("EXTERNAL_ERP", StringComparison.OrdinalIgnoreCase) ||
            databaseName.Contains("EXTERNAL_ATTENDANCE_DB", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Rejected unsafe Phase 6 database name.");
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

    private static string ManifestPath(int year)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "HRSystem.slnx")))
        {
            directory = directory.Parent;
        }

        return Path.Combine(
            directory?.FullName ?? throw new InvalidOperationException("Repository Root not found."),
            "data",
            "company-calendar",
            $"{year}.json");
    }

    private sealed class SqlCurrentUser(string role) : ICurrentUser
    {
        public string? UserId => "phase6-sql-test-admin";
        public Guid? EmployeeId => null;
        public string? DisplayName => "Phase 6 SQL Test Admin";
        public string? IpAddress => "127.0.0.1";
        public bool IsAuthenticated => true;
        public bool IsInRole(string expectedRole) => role == expectedRole;
        public bool HasPermission(string policy) => RolePermissions.HasPermission([role], policy);
    }

    private sealed record SyntheticBaseline(
        int Departments,
        int Employees,
        int LeaveTypes,
        int LeaveRequests,
        Guid EmployeeId,
        string EmployeeNumber,
        Guid LeaveRequestId,
        decimal LeaveDurationHours);
}
