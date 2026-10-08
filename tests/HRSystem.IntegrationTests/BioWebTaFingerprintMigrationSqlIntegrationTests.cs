using System.Data;
using HRSystem.Domain.Attendance;
using HRSystem.Infrastructure.Persistence.Migrations;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace HRSystem.IntegrationTests;

[Collection(DisposableSqlServerCollection.Name)]
public sealed class BioWebTaFingerprintMigrationSqlIntegrationTests
{
    private const string PreviousMigration =
        "20260801071420_AddLeaveAwareDailyAttendance";
    private const string CurrentMigration =
        "20260803041912_AddBioWebTaScheduledImportAndFingerprint";

    public static TheoryData<string, string, string?, DateTime, int?, int?, string>
        GoldenVectors => new()
        {
            {
                "BioWebTA", "42", "DEVICE-01", Local(2026, 8, 3, 8, 15),
                0, 1, "A1BAE2DE591F3305BEDE43F3CC350D0A1674C0E0265EBC964D99DCCAF5797BCD"
            },
            {
                "BioWebTA", "00042", "DEVICE-01", Local(2026, 8, 3, 8, 15),
                0, 1, "C1E9740F35EFFD3CB0FC29C98A24805AF7A372A2D51E98B095DA05413CCAD272"
            },
            {
                "BioWebTA", "00042", null, Local(2026, 8, 3, 12, 15),
                null, null, "51909A3CD84F4281C64BD937034F6A30A47C77D3D676F1761D4E52CC9804106A"
            },
            {
                "BioWebTA", "00042", string.Empty, Local(2026, 8, 3, 12, 15),
                null, null, "9D74CAD1B8BF4D7965A9E167A091E2788FD1356743B217FEF152A082843DBA71"
            },
            {
                "來源系統", "員工０１２", "裝置甲", Local(2026, 8, 3, 17, 45, 1, 234),
                -1, -7, "07501A9D4D449D71389E9F53FF6E27DD0737B21F858DFBA8B27ABA682CBFF37A"
            }
        };

    [Theory]
    [MemberData(nameof(GoldenVectors))]
    [Trait("Category", "SqlAttendance")]
    public async Task TSql_And_CSharp_Match_Approved_Golden_Vector(
        string sourceSystem,
        string pin,
        string? deviceSerial,
        DateTime eventLocalDateTime,
        int? status,
        int? verify,
        string expectedHex)
    {
        await using var database = await DisposableSqlServerDatabase.CreateAsync(
            "RawHashVector",
            applyMigrations: false);
        await using var connection = await database.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = AttendanceRawEventFingerprintSqlV1.ScalarFingerprintSql;
        AddVectorParameters(
            command,
            sourceSystem,
            pin,
            deviceSerial,
            eventLocalDateTime,
            status,
            verify);

        var sqlFingerprint = Assert.IsType<byte[]>(await command.ExecuteScalarAsync());
        var csharpFingerprint = AttendanceRawEventFingerprintV1.Compute(
            sourceSystem,
            pin,
            deviceSerial,
            eventLocalDateTime,
            status,
            verify);

        Assert.Equal(expectedHex, Convert.ToHexString(sqlFingerprint));
        Assert.Equal(csharpFingerprint, sqlFingerprint);
    }

    [Fact]
    [Trait("Category", "SqlAttendance")]
    public async Task Migration_Backfills_Existing_Rows_And_Preserves_Business_Data()
    {
        await using var database = await DisposableSqlServerDatabase.CreateAsync(
            "RawHashMigration",
            applyMigrations: false);
        await using var before = database.CreateDbContext();
        await before.GetService<IMigrator>().MigrateAsync(PreviousMigration);
        await InsertOldSchemaRowsAsync(before);
        var original = await ReadBusinessRowsAsync(before);

        await before.GetService<IMigrator>().MigrateAsync(CurrentMigration);
        before.ChangeTracker.Clear();

        var migrated = await before.AttendanceRawEvents
            .AsNoTracking()
            .OrderBy(item => item.ExternalEventId)
            .ToArrayAsync();
        Assert.Equal(3, migrated.Length);
        foreach (var item in migrated)
        {
            Assert.Equal(AttendanceRawEventFingerprintV1.Version, item.SourceFingerprintVersion);
            Assert.Equal(AttendanceRawEventFingerprintV1.HashLength, item.SourceFingerprint.Length);
            var csharpFingerprint = AttendanceRawEventFingerprintV1.Compute(
                item.SourceSystem,
                item.SourcePersonPin,
                item.DeviceSerialNumber,
                item.EventLocalDateTime,
                item.StatusCode,
                item.VerifyCode);
            var sqlFingerprint = await ComputeSqlFingerprintAsync(before, item);
            Assert.Equal(
                Convert.ToHexString(csharpFingerprint),
                Convert.ToHexString(sqlFingerprint));
            Assert.Equal(
                Convert.ToHexString(sqlFingerprint),
                Convert.ToHexString(item.SourceFingerprint));
        }
        Assert.Equal(original, await ReadBusinessRowsAsync(before));
        var repositoryMigrations = before.Database.GetMigrations().ToArray();
        var currentIndex = Array.IndexOf(repositoryMigrations, CurrentMigration);
        Assert.True(currentIndex >= 0);
        Assert.Equal(
            repositoryMigrations.Take(currentIndex + 1),
            await before.Database.GetAppliedMigrationsAsync());
        Assert.Equal(
            repositoryMigrations.Skip(currentIndex + 1),
            await before.Database.GetPendingMigrationsAsync());
        Assert.False(before.Database.HasPendingModelChanges());
        Assert.Equal(0, await before.BioWebTaImportBatches.CountAsync());
        Assert.Equal(0, await before.BioWebTaImportBatchIssues.CountAsync());
        await AssertSchemaAsync(before);
    }

    [Fact]
    [Trait("Category", "SqlAttendance")]
    public async Task Fingerprint_Collision_Rolls_Back_Entire_Migration()
    {
        await using var database = await DisposableSqlServerDatabase.CreateAsync(
            "RawHashCollision",
            applyMigrations: false);
        await using var db = database.CreateDbContext();
        await db.GetService<IMigrator>().MigrateAsync(PreviousMigration);
        await InsertCollisionRowsAsync(db);

        var exception = await Assert.ThrowsAnyAsync<Exception>(() =>
            db.GetService<IMigrator>().MigrateAsync(CurrentMigration));

        Assert.Contains(
            "fingerprint collision",
            exception.ToString(),
            StringComparison.OrdinalIgnoreCase);
        Assert.Equal(8, (await db.Database.GetAppliedMigrationsAsync()).Count());
        Assert.Equal(2, await ScalarIntAsync(
            db,
            "SELECT COUNT(*) FROM [AttendanceRawEvents]"));
        Assert.Equal(0, await ScalarIntAsync(
            db,
            "SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N'[AttendanceRawEvents]') AND name IN (N'SourceFingerprintVersion', N'SourceFingerprint')"));
        Assert.Equal(0, await ScalarIntAsync(
            db,
            "SELECT COUNT(*) FROM sys.tables WHERE name IN (N'BioWebTaImportBatches', N'BioWebTaImportBatchIssues')"));
    }

    [Fact]
    [Trait("Category", "SqlAttendance")]
    public async Task Unnormalized_Source_Data_Rolls_Back_Entire_Migration()
    {
        await using var database = await DisposableSqlServerDatabase.CreateAsync(
            "RawHashPreflight",
            applyMigrations: false);
        await using var db = database.CreateDbContext();
        await db.GetService<IMigrator>().MigrateAsync(PreviousMigration);
        await db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO [AttendanceRawEvents]
                ([Id], [SourceSystem], [ExternalEventId], [EmployeeId],
                 [SourcePersonPin], [DeviceSerialNumber], [EventLocalDateTime],
                 [StatusCode], [VerifyCode], [SourceCreatedTime], [ImportedAtUtc],
                 [IsSourceMissing])
            VALUES
                (NEWID(), N'BioWebTA', 201, NULL, N' 00042 ', N'DEVICE-01',
                 '2026-08-03T08:15:00.0000000', 0, 1, NULL,
                 '2026-08-03T00:15:00+00:00', 0);
            """);

        var exception = await Assert.ThrowsAnyAsync<Exception>(() =>
            db.GetService<IMigrator>().MigrateAsync(CurrentMigration));

        Assert.Contains("preflight", exception.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(8, (await db.Database.GetAppliedMigrationsAsync()).Count());
        Assert.Equal(0, await ScalarIntAsync(
            db,
            "SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N'[AttendanceRawEvents]') AND name IN (N'SourceFingerprintVersion', N'SourceFingerprint')"));
    }

    private static async Task AssertSchemaAsync(
        HRSystem.Infrastructure.Persistence.HRSystemDbContext db)
    {
        Assert.Equal(2, await ScalarIntAsync(
            db,
            "SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N'[AttendanceRawEvents]') AND name IN (N'SourceFingerprintVersion', N'SourceFingerprint') AND is_nullable = 0"));
        Assert.Equal(1, await ScalarIntAsync(
            db,
            "SELECT COUNT(*) FROM sys.indexes WHERE object_id = OBJECT_ID(N'[AttendanceRawEvents]') AND name = N'UX_AttendanceRawEvents_SourceSystem_FingerprintVersion_Fingerprint' AND is_unique = 1"));
        Assert.Equal(1, await ScalarIntAsync(
            db,
            "SELECT COUNT(*) FROM sys.indexes WHERE object_id = OBJECT_ID(N'[AttendanceRawEvents]') AND name = N'UX_AttendanceRawEvents_SourceSystem_ExternalEventId' AND is_unique = 1"));
        Assert.Equal(2, await ScalarIntAsync(
            db,
            "SELECT COUNT(*) FROM sys.tables WHERE name IN (N'BioWebTaImportBatches', N'BioWebTaImportBatchIssues')"));
    }

    private static async Task InsertOldSchemaRowsAsync(
        HRSystem.Infrastructure.Persistence.HRSystemDbContext db)
    {
        await db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO [AttendanceRawEvents]
                ([Id], [SourceSystem], [ExternalEventId], [EmployeeId],
                 [SourcePersonPin], [DeviceSerialNumber], [EventLocalDateTime],
                 [StatusCode], [VerifyCode], [SourceCreatedTime], [ImportedAtUtc],
                 [IsSourceMissing])
            VALUES
                (NEWID(), N'BioWebTA', 1, NULL, N'00042', N'DEVICE-01',
                 '2026-08-03T08:15:00.0000000', 0, 1, NULL,
                 '2026-08-03T00:15:00+00:00', 0),
                (NEWID(), N'BioWebTA', 2, NULL, N'00042', NULL,
                 '2026-08-03T12:15:00.0000000', NULL, NULL, NULL,
                 '2026-08-03T04:15:00+00:00', 0),
                (NEWID(), N'來源系統', 3, NULL, N'員工０１２', N'裝置甲',
                 '2026-08-03T17:45:01.2340000', -1, -7, NULL,
                 '2026-08-03T09:45:01+00:00', 0);
            """);
    }

    private static async Task InsertCollisionRowsAsync(
        HRSystem.Infrastructure.Persistence.HRSystemDbContext db)
    {
        await db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO [AttendanceRawEvents]
                ([Id], [SourceSystem], [ExternalEventId], [EmployeeId],
                 [SourcePersonPin], [DeviceSerialNumber], [EventLocalDateTime],
                 [StatusCode], [VerifyCode], [SourceCreatedTime], [ImportedAtUtc],
                 [IsSourceMissing])
            VALUES
                (NEWID(), N'BioWebTA', 101, NULL, N'00042', N'DEVICE-01',
                 '2026-08-03T08:15:00.0000000', 0, 1, NULL,
                 '2026-08-03T00:15:00+00:00', 0),
                (NEWID(), N'BioWebTA', 102, NULL, N'00042', N'DEVICE-01',
                 '2026-08-03T08:15:00.0000000', 0, 1, NULL,
                 '2026-08-03T00:16:00+00:00', 0);
            """);
    }

    private static async Task<string[]> ReadBusinessRowsAsync(
        HRSystem.Infrastructure.Persistence.HRSystemDbContext db) =>
        await db.Database.SqlQueryRaw<string>(
                """
                SELECT CONCAT(
                    CONVERT(varchar(36), [Id]), N'|', [SourceSystem], N'|',
                    [ExternalEventId], N'|',
                    COALESCE(CONVERT(varchar(36), [EmployeeId]), '<NULL>'), N'|',
                    [SourcePersonPin], N'|', COALESCE([DeviceSerialNumber], N'<NULL>'), N'|',
                    CONVERT(varchar(27), [EventLocalDateTime], 126), N'|',
                    COALESCE(CONVERT(varchar(11), [StatusCode]), '<NULL>'), N'|',
                    COALESCE(CONVERT(varchar(11), [VerifyCode]), '<NULL>'), N'|',
                    COALESCE(CONVERT(varchar(27), [SourceCreatedTime], 126), '<NULL>'), N'|',
                    CONVERT(varchar(33), [ImportedAtUtc], 127), N'|', [IsSourceMissing]) AS [Value]
                FROM [AttendanceRawEvents]
                ORDER BY [ExternalEventId]
                """)
            .ToArrayAsync();

    private static async Task<int> ScalarIntAsync(
        HRSystem.Infrastructure.Persistence.HRSystemDbContext db,
        string sql)
    {
        var connection = db.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose)
        {
            await connection.OpenAsync();
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            return Convert.ToInt32(await command.ExecuteScalarAsync());
        }
        finally
        {
            if (shouldClose)
            {
                await connection.CloseAsync();
            }
        }
    }

    private static void AddVectorParameters(
        SqlCommand command,
        string sourceSystem,
        string pin,
        string? deviceSerial,
        DateTime eventLocalDateTime,
        int? status,
        int? verify)
    {
        command.Parameters.Add("@SourceSystem", SqlDbType.NVarChar, 50).Value = sourceSystem;
        command.Parameters.Add("@SourcePersonPin", SqlDbType.NVarChar, 20).Value = pin;
        command.Parameters.Add("@DeviceSerialNumber", SqlDbType.NVarChar, 20).Value =
            (object?)deviceSerial ?? DBNull.Value;
        command.Parameters.Add("@EventLocalDateTime", SqlDbType.DateTime2).Value =
            eventLocalDateTime;
        command.Parameters.Add("@StatusCode", SqlDbType.Int).Value =
            (object?)status ?? DBNull.Value;
        command.Parameters.Add("@VerifyCode", SqlDbType.Int).Value =
            (object?)verify ?? DBNull.Value;
    }

    private static async Task<byte[]> ComputeSqlFingerprintAsync(
        HRSystem.Infrastructure.Persistence.HRSystemDbContext db,
        AttendanceRawEvent item)
    {
        var connection = (SqlConnection)db.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose)
        {
            await connection.OpenAsync();
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = AttendanceRawEventFingerprintSqlV1.ScalarFingerprintSql;
            AddVectorParameters(
                command,
                item.SourceSystem,
                item.SourcePersonPin,
                item.DeviceSerialNumber,
                item.EventLocalDateTime,
                item.StatusCode,
                item.VerifyCode);
            return Assert.IsType<byte[]>(await command.ExecuteScalarAsync());
        }
        finally
        {
            if (shouldClose)
            {
                await connection.CloseAsync();
            }
        }
    }

    private static DateTime Local(
        int year,
        int month,
        int day,
        int hour,
        int minute,
        int second = 0,
        int millisecond = 0) =>
        new(
            year,
            month,
            day,
            hour,
            minute,
            second,
            millisecond,
            DateTimeKind.Unspecified);
}
