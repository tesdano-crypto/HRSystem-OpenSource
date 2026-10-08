using HRSystem.Application.Attendance;
using HRSystem.Application.Common.Auditing;
using HRSystem.Domain.Attendance;
using HRSystem.Domain.MasterData;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HRSystem.UnitTests;

public sealed class BioWebTaImportCoordinatorTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 13, 1, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Scheduled_Disabled_Does_Not_Read_Source_Or_Write_Batch()
    {
        await using var setup = await Setup.CreateAsync(enabled: false);

        var result = await setup.Coordinator.RunAsync(Scheduled());

        Assert.Equal(BioWebTaImportExecutionOutcome.Disabled, result.Outcome);
        Assert.Equal(0, setup.Source.ReadCount);
        Assert.Empty(await setup.Db.BioWebTaImportBatches.ToListAsync());
    }

    [Fact]
    public async Task Default_Window_Uses_Three_Local_Calendar_Days()
    {
        await using var setup = await Setup.CreateAsync(withMapping: true);

        var result = await setup.Coordinator.PreviewAsync(Scheduled());

        Assert.Equal(Local(2026, 8, 11), result.QueryFromLocal);
        Assert.Equal(Local(2026, 8, 13, 9), result.QueryToLocal);
        Assert.Equal(DateTimeKind.Unspecified, result.QueryFromLocal.Kind);
    }

    [Fact]
    public async Task Import_Is_Fingerprint_Idempotent_And_Second_Run_Does_Not_Recalculate()
    {
        await using var setup = await Setup.CreateAsync(withMapping: true);
        setup.Source.Records.Add(Event(10, "PIN-1"));

        var first = await setup.Coordinator.RunAsync(Scheduled());
        var second = await setup.Coordinator.RunAsync(Scheduled());

        Assert.Equal(BioWebTaImportExecutionOutcome.Completed, first.Outcome);
        Assert.Equal(1, first.InsertedCount);
        Assert.Equal(BioWebTaImportExecutionOutcome.NoChanges, second.Outcome);
        Assert.Equal(0, second.InsertedCount);
        Assert.Equal(1, second.DuplicateCount);
        Assert.Equal(1, await setup.Db.AttendanceRawEvents.CountAsync());
        Assert.Equal(1, setup.Recalculation.Calls);
        Assert.Equal(2, await setup.Db.BioWebTaImportBatches.CountAsync());
        Assert.All(
            await setup.Db.BioWebTaImportBatches.ToListAsync(),
            item => Assert.Equal(BioWebTaImportBatchStatus.Completed, item.Status));
    }

    [Fact]
    public async Task Existing_Unmapped_Duplicate_Is_Not_Automatically_Relinked()
    {
        await using var setup = await Setup.CreateAsync();
        setup.Source.Records.Add(Event(10, "PIN-1"));
        await setup.Coordinator.RunAsync(Scheduled());
        setup.Db.BioWebPersonMappings.Add(new BioWebPersonMapping(
            Guid.NewGuid(), setup.Employee.Id, "PIN-1",
            Local(2026, 1, 1), null, Now));
        await setup.Db.SaveChangesAsync();

        var result = await setup.Coordinator.RunAsync(Scheduled());

        Assert.Equal(BioWebTaImportExecutionOutcome.NoChanges, result.Outcome);
        Assert.Equal(1, result.DuplicateCount);
        Assert.Null((await setup.Db.AttendanceRawEvents.SingleAsync()).EmployeeId);
        Assert.Equal(0, setup.Recalculation.Calls);
    }

    [Fact]
    public async Task Different_External_Ids_With_Same_V1_Fingerprint_Keep_One_Event()
    {
        await using var setup = await Setup.CreateAsync();
        setup.Source.Records.AddRange([Event(10, "PIN-1"), Event(11, "PIN-1")]);

        var result = await setup.Coordinator.RunAsync(Scheduled());

        Assert.Equal(1, result.InsertedCount);
        Assert.Equal(1, result.DuplicateCount);
        Assert.Equal(1, await setup.Db.AttendanceRawEvents.CountAsync());
    }

    [Fact]
    public async Task Existing_External_Id_With_Different_Fingerprint_Fails_Whole_Plan()
    {
        await using var setup = await Setup.CreateAsync();
        setup.Db.AttendanceRawEvents.Add(new AttendanceRawEvent(
            Guid.NewGuid(), AttendanceSourceSystems.BioWebTa, 10, null,
            "OLD", "DEVICE", Local(2026, 8, 12, 8), 0, 1, null, Now));
        await setup.Db.SaveChangesAsync();
        setup.Source.Records.Add(Event(10, "PIN-1"));

        var result = await setup.Coordinator.RunAsync(Scheduled());

        Assert.Equal(BioWebTaImportExecutionOutcome.Failed, result.Outcome);
        Assert.Equal(0, result.InsertedCount);
        Assert.Equal(1, result.ConflictCount);
        Assert.Equal(1, await setup.Db.AttendanceRawEvents.CountAsync());
        Assert.Equal(BioWebTaImportBatchStatus.Failed,
            (await setup.Db.BioWebTaImportBatches.SingleAsync()).Status);
        Assert.Equal(BioWebTaImportIssueCode.SourceEventContentMismatch,
            (await setup.Db.BioWebTaImportBatchIssues.SingleAsync()).IssueCode);
    }

    [Fact]
    public async Task Effective_Mapping_Links_New_Event_And_Recalculates_Distinct_Key()
    {
        await using var setup = await Setup.CreateAsync(withMapping: true);
        setup.Source.Records.AddRange([
            Event(10, "PIN-1"),
            Event(11, "PIN-1", Local(2026, 8, 12, 17, 30))]);

        var result = await setup.Coordinator.RunAsync(Scheduled());

        Assert.Equal(2, result.InsertedCount);
        Assert.Equal(1, result.AffectedEmployeeDateCount);
        Assert.Equal(1, setup.Recalculation.Calls);
        Assert.Single(setup.Recalculation.Keys);
        Assert.All(await setup.Db.AttendanceRawEvents.ToListAsync(),
            item => Assert.Equal(setup.Employee.Id, item.EmployeeId));
    }

    [Fact]
    public async Task Unmapped_Pin_Is_Preserved_Without_Attendance_Recalculation()
    {
        await using var setup = await Setup.CreateAsync();
        setup.Source.Records.Add(Event(10, "UNKNOWN"));

        var result = await setup.Coordinator.RunAsync(Scheduled());

        Assert.Equal(1, result.UnmappedCount);
        Assert.Equal(0, result.AffectedEmployeeDateCount);
        Assert.Equal(0, setup.Recalculation.Calls);
        Assert.Null((await setup.Db.AttendanceRawEvents.SingleAsync()).EmployeeId);
    }

    [Fact]
    public async Task Already_Running_Safely_Skips_Without_Source_Read()
    {
        await using var setup = await Setup.CreateAsync(lockAvailable: false);

        var result = await setup.Coordinator.RunAsync(Scheduled());

        Assert.Equal(BioWebTaImportExecutionOutcome.SkippedAlreadyRunning,
            result.Outcome);
        Assert.Equal(0, setup.Source.ReadCount);
        Assert.Empty(await setup.Db.BioWebTaImportBatches.ToListAsync());
    }

    [Fact]
    public async Task Source_Failure_Records_Safe_Failed_Batch()
    {
        await using var setup = await Setup.CreateAsync();
        setup.Source.Failure = new InvalidOperationException(
            "Password=secret;Server=private");

        var result = await setup.Coordinator.RunAsync(Scheduled());

        Assert.Equal(BioWebTaImportExecutionOutcome.Failed, result.Outcome);
        Assert.Equal("BioWebTA import failed safely.", result.SafeErrorSummary);
        var batch = await setup.Db.BioWebTaImportBatches.SingleAsync();
        Assert.Equal(BioWebTaImportBatchStatus.Failed, batch.Status);
        Assert.DoesNotContain("secret", batch.ErrorSummary,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Manual_Run_Writes_One_Safe_Request_Audit()
    {
        await using var setup = await Setup.CreateAsync();

        await setup.Coordinator.RunAsync(new(
            BioWebTaImportTriggerType.Manual));

        var audit = await setup.Db.AuditLogs.SingleAsync(item =>
            item.Action == AuditActions.BioWebTaImportRunNowRequested);
        Assert.DoesNotContain("PIN", audit.NewValuesJson,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Password", audit.NewValuesJson,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Dry_Run_Is_Read_Only()
    {
        await using var setup = await Setup.CreateAsync(withMapping: true);
        setup.Source.Records.Add(Event(10, "PIN-1"));

        var result = await setup.Coordinator.PreviewAsync(new(
            BioWebTaImportTriggerType.Manual,
            DryRun: true));

        Assert.Equal(BioWebTaImportExecutionOutcome.Preview, result.Outcome);
        Assert.Equal(1, result.InsertedCount);
        Assert.Empty(await setup.Db.AttendanceRawEvents.ToListAsync());
        Assert.Empty(await setup.Db.BioWebTaImportBatches.ToListAsync());
        Assert.Empty(await setup.Db.AuditLogs.ToListAsync());
    }

    [Fact]
    public async Task Explicit_Window_Requires_Unspecified_Local_DateTimes()
    {
        await using var setup = await Setup.CreateAsync();

        await Assert.ThrowsAsync<HRSystem.Application.Common.Exceptions.ApplicationValidationException>(
            () => setup.Coordinator.PreviewAsync(new(
                BioWebTaImportTriggerType.Manual,
                new DateTime(2026, 8, 11, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 8, 13, 0, 0, 0, DateTimeKind.Utc),
                DryRun: true)));
        Assert.Equal(0, setup.Source.ReadCount);
    }

    private static BioWebTaImportExecutionRequest Scheduled() =>
        new(BioWebTaImportTriggerType.Scheduled);

    private static BioWebAttendanceSourceRecord Event(
        long id,
        string pin,
        DateTime? at = null) =>
        new(id, pin, "DEVICE", at ?? Local(2026, 8, 12, 8), 0, 1, null);

    private static DateTime Local(
        int year, int month, int day, int hour = 0, int minute = 0) =>
        new(year, month, day, hour, minute, 0, DateTimeKind.Unspecified);

    private sealed class Setup : IAsyncDisposable
    {
        private Setup(
            HRSystem.Infrastructure.Persistence.HRSystemDbContext db,
            Employee employee,
            FakeSource source,
            FakeRecalculation recalculation,
            BioWebTaImportCoordinator coordinator)
        {
            Db = db;
            Employee = employee;
            Source = source;
            Recalculation = recalculation;
            Coordinator = coordinator;
        }

        public HRSystem.Infrastructure.Persistence.HRSystemDbContext Db { get; }
        public Employee Employee { get; }
        public FakeSource Source { get; }
        public FakeRecalculation Recalculation { get; }
        public BioWebTaImportCoordinator Coordinator { get; }

        public static async Task<Setup> CreateAsync(
            bool enabled = true,
            bool withMapping = false,
            bool lockAvailable = true)
        {
            var db = TestDb.Create();
            var department = new Department(
                Guid.NewGuid(), "ATT", "Attendance", Now);
            var employee = new Employee(
                Guid.NewGuid(), "EMP9001", "Test Employee", department.Id,
                new DateOnly(2026, 1, 1), Now);
            db.AddRange(department, employee);
            if (withMapping)
            {
                db.BioWebPersonMappings.Add(new BioWebPersonMapping(
                    Guid.NewGuid(), employee.Id, "PIN-1",
                    Local(2026, 1, 1), null, Now));
            }
            await db.SaveChangesAsync();
            var source = new FakeSource();
            var recalculation = new FakeRecalculation();
            var options = new BioWebTaScheduledImportOptions
            {
                Enabled = enabled,
                OverlapDays = 3,
                PageSize = 2,
                MaxExecutionMinutes = 30,
                TimeZoneId = "Asia/Taipei"
            };
            return new Setup(
                db,
                employee,
                source,
                recalculation,
                new BioWebTaImportCoordinator(
                    db,
                    source,
                    new FakeLock(lockAvailable),
                    recalculation,
                    options,
                    new TestCurrentUser(),
                    new FixedTimeProvider(Now),
                    NullLogger<BioWebTaImportCoordinator>.Instance));
        }

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private sealed class FakeSource : IBioWebTaAttendanceSource
    {
        public List<BioWebAttendanceSourceRecord> Records { get; } = [];
        public Exception? Failure { get; set; }
        public int ReadCount { get; private set; }

        public Task<IReadOnlyList<BioWebAttendanceSourceRecord>> ReadAfterAsync(
            long lastExternalEventId, int batchSize,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

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

    private sealed class FakeLock(bool available) : IBioWebTaImportExecutionLock
    {
        public ValueTask<IAsyncDisposable?> TryAcquireAsync(
            TimeSpan timeout,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IAsyncDisposable?>(
                available ? new Lease() : null);

        private sealed class Lease : IAsyncDisposable
        {
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private sealed class FakeRecalculation : IAttendanceRecalculationEngine
    {
        public int Calls { get; private set; }
        public HashSet<AttendanceRecalculationKey> Keys { get; } = [];

        public Task<AttendanceRecalculationOutcome> RecalculateKeysAsync(
            IReadOnlyCollection<AttendanceRecalculationKey> keys,
            CancellationToken cancellationToken = default,
            IReadOnlyCollection<Guid>? excludedApprovedLeaveRequestIds = null)
        {
            Calls++;
            Keys.UnionWith(keys);
            var distinct = keys.Distinct().ToArray();
            return Task.FromResult(new AttendanceRecalculationOutcome(
                distinct.Min(item => item.WorkDate),
                distinct.Max(item => item.WorkDate),
                distinct.Select(item => item.EmployeeId).Distinct().Count(),
                distinct.Length,
                Now));
        }

        public Task<AttendanceRecalculationOutcome> RecalculateRangeAsync(
            DateOnly dateFrom, DateOnly dateTo, Guid? employeeId = null,
            IReadOnlyCollection<PendingApprovedLeave>? pendingApprovedLeaves = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
