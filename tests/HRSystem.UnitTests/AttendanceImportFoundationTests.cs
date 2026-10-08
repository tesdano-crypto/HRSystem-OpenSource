using System.Reflection;
using HRSystem.Application.Attendance;
using HRSystem.Application.Common.Auditing;
using HRSystem.Application.Common.Exceptions;
using HRSystem.Application.Security;
using HRSystem.Domain.Attendance;
using HRSystem.Domain.MasterData;
using HRSystem.Infrastructure.Attendance;
using HRSystem.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HRSystem.UnitTests;

public sealed class AttendanceImportFoundationTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 27, 1, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Admin_Can_Create_Mapping()
    {
        await using var setup = await Setup.CreateAsync();

        var created = await setup.MappingService.CreateAsync(
            MappingRequest(setup.Employee1.Id, "00042"));

        Assert.Equal("00042", created.BioWebPin);
        Assert.Equal(setup.Employee1.Id, created.EmployeeId);
        Assert.True(created.IsActive);
    }

    [Theory]
    [InlineData(RoleNames.Manager)]
    [InlineData(RoleNames.Employee)]
    public async Task Non_Admin_Cannot_Manage_Mappings(string role)
    {
        await using var setup = await Setup.CreateAsync(role);

        await Assert.ThrowsAsync<ForbiddenAccessException>(() =>
            setup.MappingService.CreateAsync(
                MappingRequest(setup.Employee1.Id, "00042")));
    }

    [Fact]
    public void BioWebPin_Is_A_String()
    {
        var property = typeof(BioWebPersonMapping)
            .GetProperty(nameof(BioWebPersonMapping.BioWebPin));

        Assert.NotNull(property);
        Assert.Equal(typeof(string), property.PropertyType);
    }

    [Fact]
    public async Task Duplicate_Active_Pin_Mapping_Is_Rejected()
    {
        await using var setup = await Setup.CreateAsync();
        await setup.MappingService.CreateAsync(
            MappingRequest(setup.Employee1.Id, "PIN-1"));

        var exception = await Assert.ThrowsAsync<ApplicationValidationException>(() =>
            setup.MappingService.CreateAsync(
                MappingRequest(setup.Employee2.Id, "PIN-1")));

        Assert.Contains("BioWeb PIN", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Overlapping_Employee_Mapping_Is_Rejected()
    {
        await using var setup = await Setup.CreateAsync();
        await setup.MappingService.CreateAsync(
            MappingRequest(
                setup.Employee1.Id,
                "PIN-1",
                Local(2026, 1, 1),
                Local(2026, 6, 1)));

        await Assert.ThrowsAsync<ApplicationValidationException>(() =>
            setup.MappingService.CreateAsync(
                MappingRequest(
                    setup.Employee1.Id,
                    "PIN-2",
                    Local(2026, 5, 1),
                    Local(2026, 7, 1))));
    }

    [Fact]
    public async Task Import_Reads_After_Current_Id_Cursor()
    {
        await using var setup = await Setup.CreateAsync();
        var state = new AttendanceSyncState(
            AttendanceSourceSystems.BioWebTa,
            Now.AddMinutes(-10));
        state.MarkSucceeded(10, 2, Now.AddMinutes(-10));
        setup.Db.AttendanceSyncStates.Add(state);
        await setup.Db.SaveChangesAsync();
        setup.Source.Records.Add(Event(11, "PIN-1"));

        await setup.ImportService.SyncNowAsync();

        Assert.Equal(10, setup.Source.LastAfterId);
        Assert.Equal(11, (await setup.ImportService.GetStatusAsync()).LastExternalEventId);
    }

    [Fact]
    public async Task Import_Advances_Strictly_By_AttLog_Id()
    {
        await using var setup = await Setup.CreateAsync();
        setup.Source.Records.AddRange(
        [
            Event(8, "PIN-1", eventTime: Local(2026, 1, 3)),
            Event(6, "PIN-1", eventTime: Local(2026, 1, 5)),
            Event(7, "PIN-1", eventTime: Local(2026, 1, 1))
        ]);

        var result = await setup.ImportService.SyncNowAsync();
        var importedIds = await setup.Db.AttendanceRawEvents
            .OrderBy(item => item.ExternalEventId)
            .Select(item => item.ExternalEventId)
            .ToArrayAsync();

        Assert.Equal(8, result.LastExternalEventId);
        Assert.Equal([6L, 7L, 8L], importedIds);
    }

    [Fact]
    public async Task Source_CreatedTime_Is_Not_Used_As_Cursor()
    {
        await using var setup = await Setup.CreateAsync();
        setup.Source.Records.AddRange(
        [
            Event(20, "PIN-1", createdTime: Local(2026, 7, 27, 12)),
            Event(21, "PIN-1", createdTime: Local(2020, 1, 1))
        ]);

        var result = await setup.ImportService.SyncNowAsync();

        Assert.Equal(21, result.LastExternalEventId);
    }

    [Fact]
    public async Task Repeated_Import_Is_Idempotent()
    {
        await using var setup = await Setup.CreateAsync();
        setup.Source.Records.AddRange([Event(1, "PIN-1"), Event(2, "PIN-1")]);

        var first = await setup.ImportService.SyncNowAsync();
        var second = await setup.ImportService.SyncNowAsync();

        Assert.Equal(2, first.ImportedCount);
        Assert.Equal(0, second.ImportedCount);
        Assert.Equal(2, await setup.Db.AttendanceRawEvents.CountAsync());
    }

    [Fact]
    public void Concurrent_Import_Integrity_Has_Unique_Index_And_Cursor_RowVersion()
    {
        using var db = TestDb.Create();
        var eventType = db.Model.FindEntityType(typeof(AttendanceRawEvent))!;
        var index = eventType.GetIndexes().Single(item =>
            item.Properties.Select(property => property.Name).SequenceEqual(
                [
                    nameof(AttendanceRawEvent.SourceSystem),
                    nameof(AttendanceRawEvent.ExternalEventId)
                ]));
        var stateType = db.Model.FindEntityType(typeof(AttendanceSyncState))!;
        var rowVersion = stateType.FindProperty(nameof(AttendanceSyncState.RowVersion));

        Assert.True(index.IsUnique);
        Assert.True(rowVersion!.IsConcurrencyToken);
    }

    [Fact]
    public async Task Failed_Source_Read_Does_Not_Advance_Cursor()
    {
        await using var setup = await Setup.CreateAsync();
        setup.Source.Failure = new InvalidOperationException("sensitive source failure");

        await Assert.ThrowsAsync<AttendanceSyncException>(() =>
            setup.ImportService.SyncNowAsync());

        var status = await setup.ImportService.GetStatusAsync();
        Assert.Equal(0, status.LastExternalEventId);
        Assert.Null(status.LastSuccessfulSyncAtUtc);
    }

    [Fact]
    public async Task Unmapped_Events_Are_Retained()
    {
        await using var setup = await Setup.CreateAsync();
        setup.Source.Records.Add(Event(1, "UNMAPPED"));

        var result = await setup.ImportService.SyncNowAsync();
        var rawEvent = await setup.Db.AttendanceRawEvents.SingleAsync();

        Assert.Equal(1, result.UnmappedImportedCount);
        Assert.Null(rawEvent.EmployeeId);
    }

    [Fact]
    public async Task Explicit_Workflow_Relinks_Unmapped_Events()
    {
        await using var setup = await Setup.CreateAsync();
        setup.Source.Records.Add(Event(1, "PIN-1"));
        await setup.ImportService.SyncNowAsync();
        var mapping = await setup.MappingService.CreateAsync(
            MappingRequest(setup.Employee1.Id, "PIN-1"));

        var result = await setup.MappingService.RelinkUnmappedEventsAsync(
            mapping.Id,
            mapping.RowVersion);

        Assert.Equal(1, result.RelinkedCount);
        Assert.Equal(1, result.RecalculatedKeyCount);
        Assert.True(result.PunchRecordsRefreshRecommended);
        Assert.Equal(
            setup.Employee1.Id,
            (await setup.Db.AttendanceRawEvents.SingleAsync()).EmployeeId);
    }

    [Fact]
    public async Task Relink_Recalculates_Distinct_Eligible_Keys_And_Is_Idempotent()
    {
        await using var setup = await Setup.CreateAsync();
        setup.Source.Records.AddRange(
        [
            Event(1, "PIN-1", Local(2026, 7, 27, 8)),
            Event(2, "PIN-1", Local(2026, 7, 27, 17)),
            Event(3, "PIN-1", Local(2026, 7, 28, 8)),
            Event(4, "OTHER", Local(2026, 7, 27, 8)),
            Event(7, "PIN-1", Local(2026, 7, 26, 8))
        ]);
        await setup.ImportService.SyncNowAsync();
        var otherSource = new AttendanceRawEvent(
            Guid.NewGuid(), "OtherSource", 5, null, "PIN-1", "DEVICE-1",
            Local(2026, 7, 27, 9), 1, 2, Local(2026, 7, 27, 9, 1), Now);
        var alreadyMapped = new AttendanceRawEvent(
            Guid.NewGuid(), AttendanceSourceSystems.BioWebTa, 6,
            setup.Employee2.Id, "PIN-1", "DEVICE-1",
            Local(2026, 7, 27, 10), 1, 2, Local(2026, 7, 27, 10, 1), Now);
        setup.Db.AttendanceRawEvents.AddRange(otherSource, alreadyMapped);
        await setup.Db.SaveChangesAsync();
        var mapping = await setup.MappingService.CreateAsync(
            MappingRequest(
                setup.Employee1.Id,
                "PIN-1",
                Local(2026, 7, 27),
                Local(2026, 7, 29)));

        var preview = await setup.MappingService.GetRelinkPreviewAsync(mapping.Id);

        var first = await setup.MappingService.RelinkUnmappedEventsAsync(
            mapping.Id,
            mapping.RowVersion);
        var second = await setup.MappingService.RelinkUnmappedEventsAsync(
            mapping.Id,
            mapping.RowVersion);

        Assert.Equal(3, preview.RelinkCandidateCount);
        Assert.Equal(1, preview.UnableToRelinkCount);
        Assert.Equal(3, first.RelinkedCount);
        Assert.Equal(2, first.RecalculatedKeyCount);
        Assert.Equal(1, first.UnableToRelinkCount);
        Assert.Equal(2, setup.RecalculationEngine.Keys.Count);
        Assert.Equal(1, setup.RecalculationEngine.Calls);
        Assert.Equal(0, second.RelinkedCount);
        Assert.Equal(0, second.RecalculatedKeyCount);
        Assert.Equal(1, setup.RecalculationEngine.Calls);
        Assert.Null(otherSource.EmployeeId);
        Assert.Equal(setup.Employee2.Id, alreadyMapped.EmployeeId);
        Assert.Null((await setup.Db.AttendanceRawEvents.SingleAsync(item =>
            item.ExternalEventId == 7)).EmployeeId);
        Assert.Equal(
            setup.Employee1.Id,
            (await setup.Db.AttendanceRawEvents.SingleAsync(item =>
                item.ExternalEventId == 1)).EmployeeId);
    }

    [Fact]
    public async Task Relink_Preserves_Source_Fields_And_Punch_Query_Uses_Employee()
    {
        await using var setup = await Setup.CreateAsync();
        var rawEvent = new AttendanceRawEvent(
            Guid.NewGuid(), AttendanceSourceSystems.BioWebTa, 101, null,
            "PIN-1", "DEVICE-X", Local(2026, 7, 27, 8, 15), 7, 9,
            Local(2026, 7, 27, 8, 16), Now);
        var fingerprint = rawEvent.SourceFingerprint.ToArray();
        setup.Db.AttendanceRawEvents.Add(rawEvent);
        await setup.Db.SaveChangesAsync();
        var mapping = await setup.MappingService.CreateAsync(
            MappingRequest(setup.Employee1.Id, "PIN-1"));

        var result = await setup.MappingService.RelinkUnmappedEventsAsync(
            mapping.Id,
            mapping.RowVersion);
        var punchService = new AttendancePunchRecordService(
            setup.Db,
            new TestCurrentUser(RoleNames.Admin),
            new FixedTimeProvider(Now));
        var punches = await punchService.GetListAsync(new AttendancePunchRecordQuery
        {
            DateFrom = new DateOnly(2026, 7, 27),
            DateTo = new DateOnly(2026, 7, 27)
        });

        Assert.Equal(1, result.RelinkedCount);
        Assert.Equal("PIN-1", rawEvent.SourcePersonPin);
        Assert.Equal(101, rawEvent.ExternalEventId);
        Assert.Equal(Local(2026, 7, 27, 8, 15), rawEvent.EventLocalDateTime);
        Assert.Equal(AttendanceRawEventFingerprintV1.Version,
            rawEvent.SourceFingerprintVersion);
        Assert.Equal(fingerprint, rawEvent.SourceFingerprint);
        Assert.Equal("DEVICE-X", rawEvent.DeviceSerialNumber);
        Assert.Equal(7, rawEvent.StatusCode);
        Assert.Equal(9, rawEvent.VerifyCode);
        Assert.Equal(Local(2026, 7, 27, 8, 16), rawEvent.SourceCreatedTime);
        Assert.Equal(Now, rawEvent.ImportedAtUtc);
        var punch = Assert.Single(punches.Items);
        Assert.Equal(setup.Employee1.EmployeeNumber, punch.EmployeeNumber);
        Assert.Equal(setup.Employee1.ChineseName, punch.EmployeeName);
    }

    [Fact]
    public async Task Source_Status_Is_Retained_Without_Inference()
    {
        await using var setup = await Setup.CreateAsync();
        setup.Source.Records.Add(Event(1, "PIN-1", status: 37));

        await setup.ImportService.SyncNowAsync();

        Assert.Equal(37, (await setup.Db.AttendanceRawEvents.SingleAsync()).StatusCode);
    }

    [Fact]
    public async Task Source_Verify_Is_Retained_Without_Inference()
    {
        await using var setup = await Setup.CreateAsync();
        setup.Source.Records.Add(Event(1, "PIN-1", verify: 91));

        await setup.ImportService.SyncNowAsync();

        Assert.Equal(91, (await setup.Db.AttendanceRawEvents.SingleAsync()).VerifyCode);
    }

    [Fact]
    public async Task Source_Local_Timestamp_Is_Preserved_Without_Utc_Conversion()
    {
        await using var setup = await Setup.CreateAsync();
        var local = Local(2026, 7, 27, 8, 30);
        setup.Source.Records.Add(Event(1, "PIN-1", eventTime: local));

        await setup.ImportService.SyncNowAsync();
        var stored = (await setup.Db.AttendanceRawEvents.SingleAsync())
            .EventLocalDateTime;

        Assert.Equal(local, stored);
        Assert.Equal(DateTimeKind.Unspecified, stored.Kind);
    }

    [Fact]
    public async Task Imported_Raw_Event_Cannot_Be_Deleted_By_Normal_Data_Flow()
    {
        await using var setup = await Setup.CreateAsync();
        setup.Source.Records.Add(Event(1, "PIN-1"));
        await setup.ImportService.SyncNowAsync();
        var rawEvent = await setup.Db.AttendanceRawEvents.SingleAsync();

        setup.Db.AttendanceRawEvents.Remove(rawEvent);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            setup.Db.SaveChangesAsync());
    }

    [Fact]
    public void Source_Command_Is_Parameterized_Select_Only()
    {
        var sql = (string)typeof(BioWebTaAttendanceSource)
            .GetField("ReadBatchSql", BindingFlags.NonPublic | BindingFlags.Static)!
            .GetRawConstantValue()!;

        Assert.Contains("SELECT", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("@BatchSize", sql, StringComparison.Ordinal);
        Assert.Contains("@AfterId", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("INSERT", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UPDATE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DELETE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("MERGE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("EXEC", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Window_Source_Command_Is_Half_Open_Stable_And_Select_Only()
    {
        var sql = (string)typeof(BioWebTaAttendanceSource)
            .GetField("ReadWindowPageSql", BindingFlags.NonPublic | BindingFlags.Static)!
            .GetRawConstantValue()!;

        Assert.Contains("[AttLogTime] >= @QueryFromLocal", sql, StringComparison.Ordinal);
        Assert.Contains("[AttLogTime] < @QueryToLocal", sql, StringComparison.Ordinal);
        Assert.Contains("ORDER BY [AttLogTime] ASC, [Id] ASC", sql, StringComparison.Ordinal);
        Assert.Contains("@AfterExternalEventId", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("INSERT", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UPDATE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DELETE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("MERGE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("EXEC", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Successful_Import_Writes_Completion_Audit()
    {
        await using var setup = await Setup.CreateAsync();
        setup.Source.Records.Add(Event(1, "PIN-1"));

        await setup.ImportService.SyncNowAsync();

        Assert.Single(await setup.Db.AttendanceRawEvents.ToListAsync());
        Assert.Contains(
            await setup.Db.AuditLogs.ToListAsync(),
            item => item.Action == AuditActions.AttendanceSyncCompleted);
    }

    [Fact]
    public async Task Invalid_Batch_Writes_No_Partial_Events_And_No_Completion_Audit()
    {
        await using var setup = await Setup.CreateAsync();
        setup.Source.PreserveOrder = true;
        setup.Source.Records.AddRange([Event(2, "PIN-1"), Event(1, "PIN-1")]);

        await Assert.ThrowsAsync<AttendanceSyncException>(() =>
            setup.ImportService.SyncNowAsync());

        Assert.Empty(await setup.Db.AttendanceRawEvents.ToListAsync());
        Assert.DoesNotContain(
            await setup.Db.AuditLogs.ToListAsync(),
            item => item.Action == AuditActions.AttendanceSyncCompleted);
    }

    [Fact]
    public void Imported_Model_Has_No_Biometric_Or_Credential_Fields()
    {
        var prohibited = new[]
        {
            "Template", "Biometric", "Password",
            "Credential", "Face", "Photo", "Card"
        };
        var names = typeof(AttendanceRawEvent)
            .GetProperties()
            .Select(property => property.Name)
            .ToArray();

        Assert.DoesNotContain(
            names,
            name => prohibited.Any(term =>
                name.Contains(term, StringComparison.OrdinalIgnoreCase)));
        Assert.Contains(nameof(AttendanceRawEvent.SourceFingerprint), names);
    }

    [Theory]
    [InlineData(RoleNames.Manager)]
    [InlineData(RoleNames.Employee)]
    public async Task Non_Admin_Cannot_Invoke_Sync_Now(string role)
    {
        await using var setup = await Setup.CreateAsync(role);

        await Assert.ThrowsAsync<ForbiddenAccessException>(() =>
            setup.ImportService.SyncNowAsync());
        Assert.Equal(0, setup.Source.ReadCount);
    }

    [Fact]
    public async Task Mapping_Lifecycle_Writes_Required_Audits()
    {
        await using var setup = await Setup.CreateAsync();
        var mapping = await setup.MappingService.CreateAsync(
            MappingRequest(setup.Employee1.Id, "PIN-1"));
        var updated = await setup.MappingService.UpdateAsync(
            new UpdateBioWebPersonMappingRequest
            {
                Id = mapping.Id,
                EffectiveFrom = Local(2026, 1, 2),
                RowVersion = mapping.RowVersion
            });
        await setup.MappingService.DeactivateAsync(updated.Id, updated.RowVersion);
        var actions = await setup.Db.AuditLogs
            .Select(item => item.Action)
            .ToArrayAsync();

        Assert.Contains(AuditActions.BioWebMappingCreated, actions);
        Assert.Contains(AuditActions.BioWebMappingUpdated, actions);
        Assert.Contains(AuditActions.BioWebMappingDeactivated, actions);
    }

    [Fact]
    public async Task Inactive_Mapping_Does_Not_Block_Active_Period_Integrity()
    {
        await using var setup = await Setup.CreateAsync();
        var inactive = await setup.MappingService.CreateAsync(
            MappingRequest(
                setup.Employee1.Id,
                "OLD-PIN",
                Local(2026, 1, 1),
                Local(2026, 2, 1)));
        await setup.MappingService.DeactivateAsync(inactive.Id, inactive.RowVersion);
        await setup.MappingService.CreateAsync(
            MappingRequest(setup.Employee1.Id, "NEW-PIN", Local(2026, 3, 1)));
        var inactiveAfterDeactivate = (await setup.MappingService.GetMappingsAsync())
            .Single(item => item.Id == inactive.Id);

        var updated = await setup.MappingService.UpdateAsync(
            new UpdateBioWebPersonMappingRequest
            {
                Id = inactiveAfterDeactivate.Id,
                EffectiveFrom = Local(2026, 3, 1),
                EffectiveTo = Local(2026, 4, 1),
                RowVersion = inactiveAfterDeactivate.RowVersion
            });

        Assert.False(updated.IsActive);
    }

    [Fact]
    public async Task Relink_Writes_Audit()
    {
        await using var setup = await Setup.CreateAsync();
        setup.Source.Records.Add(Event(1, "PIN-1"));
        await setup.ImportService.SyncNowAsync();
        var mapping = await setup.MappingService.CreateAsync(
            MappingRequest(setup.Employee1.Id, "PIN-1"));

        await setup.MappingService.RelinkUnmappedEventsAsync(
            mapping.Id,
            mapping.RowVersion);

        var audit = await setup.Db.AuditLogs.SingleAsync(
            item => item.Action == AuditActions.AttendanceUnmappedEventsRelinked);
        Assert.Contains(mapping.Id.ToString(), audit.NewValuesJson, StringComparison.Ordinal);
        Assert.Contains("\"relinkedCount\":1", audit.NewValuesJson, StringComparison.Ordinal);
        Assert.Contains("\"affectedKeyCount\":1", audit.NewValuesJson, StringComparison.Ordinal);
        Assert.DoesNotContain("PIN-1", audit.NewValuesJson, StringComparison.Ordinal);
        Assert.DoesNotContain(setup.Employee1.ChineseName, audit.NewValuesJson, StringComparison.Ordinal);
        Assert.DoesNotContain("2026-", audit.NewValuesJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Failed_Sync_Writes_Safe_Audit_Without_Original_Detail()
    {
        await using var setup = await Setup.CreateAsync();
        setup.Source.Failure =
            new InvalidOperationException("Password=secret;Server=private");

        await Assert.ThrowsAsync<AttendanceSyncException>(() =>
            setup.ImportService.SyncNowAsync());
        var failed = await setup.Db.AuditLogs.SingleAsync(
            item => item.Action == AuditActions.AttendanceSyncFailed);

        Assert.DoesNotContain("Password", failed.NewValuesJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("private", failed.NewValuesJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Raw_Event_Source_Key_Is_Unique()
    {
        using var db = TestDb.Create();
        var entityType = db.Model.FindEntityType(typeof(AttendanceRawEvent))!;
        var sourceKey = entityType.GetIndexes().Single(index =>
            index.Properties.Select(property => property.Name).SequenceEqual(
                [
                    nameof(AttendanceRawEvent.SourceSystem),
                    nameof(AttendanceRawEvent.ExternalEventId)
                ]));

        Assert.True(sourceKey.IsUnique);
        Assert.Equal(
            "UX_AttendanceRawEvents_SourceSystem_ExternalEventId",
            sourceKey.GetDatabaseName());
    }

    [Fact]
    public void Source_Interface_Exposes_Only_Bounded_Reads()
    {
        var methods = typeof(IBioWebTaAttendanceSource).GetMethods();

        Assert.Equal(2, methods.Length);
        var method = Assert.Single(methods, item =>
            item.Name == nameof(IBioWebTaAttendanceSource.ReadAfterAsync));
        Assert.Contains(
            method.GetParameters(),
            parameter => parameter.Name == "batchSize" &&
                         parameter.ParameterType == typeof(int));
        Assert.Single(methods, item =>
            item.Name == nameof(IBioWebTaAttendanceSource.ReadWindowPageAsync));
    }

    private static CreateBioWebPersonMappingRequest MappingRequest(
        Guid employeeId,
        string pin,
        DateTime? effectiveFrom = null,
        DateTime? effectiveTo = null) =>
        new()
        {
            EmployeeId = employeeId,
            BioWebPin = pin,
            EffectiveFrom = effectiveFrom ?? Local(2026, 1, 1),
            EffectiveTo = effectiveTo
        };

    private static BioWebAttendanceSourceRecord Event(
        long id,
        string pin,
        DateTime? eventTime = null,
        int? status = null,
        int? verify = null,
        DateTime? createdTime = null) =>
        new(
            id,
            pin,
            "DEVICE-1",
            eventTime ?? Local(2026, 7, 27, 8),
            status,
            verify,
            createdTime ?? Local(2026, 7, 27, 8, 1));

    private static DateTime Local(
        int year,
        int month,
        int day,
        int hour = 0,
        int minute = 0) =>
        new(year, month, day, hour, minute, 0, DateTimeKind.Unspecified);

    private sealed class Setup : IAsyncDisposable
    {
        private Setup(
            HRSystemDbContext db,
            Employee employee1,
            Employee employee2,
            TestBioWebTaSource source,
            RecordingRecalculationEngine recalculationEngine,
            BioWebPersonMappingService mappingService,
            AttendanceImportService importService)
        {
            Db = db;
            Employee1 = employee1;
            Employee2 = employee2;
            Source = source;
            RecalculationEngine = recalculationEngine;
            MappingService = mappingService;
            ImportService = importService;
        }

        public HRSystemDbContext Db { get; }
        public Employee Employee1 { get; }
        public Employee Employee2 { get; }
        public TestBioWebTaSource Source { get; }
        public RecordingRecalculationEngine RecalculationEngine { get; }
        public BioWebPersonMappingService MappingService { get; }
        public AttendanceImportService ImportService { get; }

        public static async Task<Setup> CreateAsync(string role = RoleNames.Admin)
        {
            var db = TestDb.Create();
            var department = new Department(Guid.NewGuid(), "ATT", "出勤測試", Now);
            var employee1 = new Employee(
                Guid.NewGuid(),
                "EMP9001",
                "測試員工一",
                department.Id,
                new DateOnly(2026, 1, 1),
                Now);
            var employee2 = new Employee(
                Guid.NewGuid(),
                "EMP9002",
                "測試員工二",
                department.Id,
                new DateOnly(2026, 1, 1),
                Now);
            db.AddRange(department, employee1, employee2);
            await db.SaveChangesAsync();
            var currentUser = new TestCurrentUser(role);
            var timeProvider = new FixedTimeProvider(Now);
            var source = new TestBioWebTaSource();
            var recalculationEngine = new RecordingRecalculationEngine(timeProvider);
            return new Setup(
                db,
                employee1,
                employee2,
                source,
                recalculationEngine,
                new BioWebPersonMappingService(
                    db,
                    currentUser,
                    timeProvider,
                    recalculationEngine,
                    NullLogger<BioWebPersonMappingService>.Instance),
                new AttendanceImportService(
                    db,
                    source,
                    new AttendanceImportSettings(100),
                    currentUser,
                    timeProvider,
                    NullLogger<AttendanceImportService>.Instance));
        }

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private sealed class RecordingRecalculationEngine(TimeProvider timeProvider) :
        IAttendanceRecalculationEngine
    {
        public int Calls { get; private set; }
        public HashSet<AttendanceRecalculationKey> Keys { get; } = [];

        public Task<AttendanceRecalculationOutcome> RecalculateKeysAsync(
            IReadOnlyCollection<AttendanceRecalculationKey> keys,
            CancellationToken cancellationToken = default,
            IReadOnlyCollection<Guid>? excludedApprovedLeaveRequestIds = null)
        {
            Calls++;
            foreach (var key in keys)
            {
                Keys.Add(key);
            }

            var distinct = keys.Distinct().ToArray();
            return Task.FromResult(new AttendanceRecalculationOutcome(
                distinct.Min(item => item.WorkDate),
                distinct.Max(item => item.WorkDate),
                distinct.Select(item => item.EmployeeId).Distinct().Count(),
                distinct.Length,
                timeProvider.GetUtcNow()));
        }

        public Task<AttendanceRecalculationOutcome> RecalculateRangeAsync(
            DateOnly dateFrom,
            DateOnly dateTo,
            Guid? employeeId = null,
            IReadOnlyCollection<PendingApprovedLeave>? pendingApprovedLeaves = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class TestBioWebTaSource : IBioWebTaAttendanceSource
    {
        public List<BioWebAttendanceSourceRecord> Records { get; } = [];
        public Exception? Failure { get; set; }
        public bool PreserveOrder { get; set; }
        public long LastAfterId { get; private set; }
        public int ReadCount { get; private set; }

        public Task<IReadOnlyList<BioWebAttendanceSourceRecord>> ReadAfterAsync(
            long lastExternalEventId,
            int batchSize,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReadCount++;
            LastAfterId = lastExternalEventId;
            if (Failure is not null)
            {
                return Task.FromException<IReadOnlyList<BioWebAttendanceSourceRecord>>(
                    Failure);
            }

            var query = Records
                .Where(item => item.ExternalEventId > lastExternalEventId);
            if (!PreserveOrder)
            {
                query = query.OrderBy(item => item.ExternalEventId);
            }

            return Task.FromResult<IReadOnlyList<BioWebAttendanceSourceRecord>>(
                query.Take(batchSize).ToArray());
        }

        public Task<IReadOnlyList<BioWebAttendanceSourceRecord>> ReadWindowPageAsync(
            BioWebTaSourceWindowPageRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReadCount++;
            if (Failure is not null)
            {
                return Task.FromException<IReadOnlyList<BioWebAttendanceSourceRecord>>(
                    Failure);
            }

            return Task.FromResult<IReadOnlyList<BioWebAttendanceSourceRecord>>(
                Records
                    .Where(item =>
                        item.EventLocalDateTime >= request.QueryFromLocal &&
                        item.EventLocalDateTime < request.QueryToLocal &&
                        (!request.AfterEventLocalDateTime.HasValue ||
                         item.EventLocalDateTime > request.AfterEventLocalDateTime.Value ||
                         (item.EventLocalDateTime == request.AfterEventLocalDateTime.Value &&
                          item.ExternalEventId > request.AfterExternalEventId)))
                    .OrderBy(item => item.EventLocalDateTime)
                    .ThenBy(item => item.ExternalEventId)
                    .Take(request.PageSize)
                    .ToArray());
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
