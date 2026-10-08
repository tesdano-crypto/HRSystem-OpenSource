using HRSystem.Application.Abstractions.Security;
using HRSystem.Application.Attendance;
using HRSystem.Application.Common.Exceptions;
using HRSystem.Application.Security;
using HRSystem.Domain.Attendance;
using HRSystem.Domain.MasterData;
using HRSystem.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HRSystem.IntegrationTests;

[Collection(DisposableSqlServerCollection.Name)]
public sealed class AttendanceCorrectionSqlIntegrationTests
{
    [Fact]
    public async Task Missing_Punch_Approval_Is_Atomic_And_Preserves_Raw_Events()
    {
        await using var database = await DisposableSqlServerDatabase.CreateAsync(
            "AttendanceCorrectionAtomic");
        var seed = await SeedAsync(database, []);
        await using var db = database.CreateDbContext();
        var recalc = new RecordingRecalculation();
        var employee = Service(db, RoleNames.Employee, seed.EmployeeId,
            "employee", recalc);
        var admin = Service(db, RoleNames.Admin, null, "admin", recalc);
        var draft = await employee.CreateDraftAsync(new()
        {
            AttendanceResultId = seed.ResultId,
            RequestType = AttendanceCorrectionRequestType.MissingBoth,
            ProposedClockInAt = Local(8),
            ProposedClockOutAt = Local(17, 30),
            Reason = AttendanceCorrectionReason.ForgotPunch,
            EmployeeReason = "SQL 補上下班卡"
        });
        var submitted = await employee.SubmitAsync(new()
            { Id = draft.Id, RowVersion = draft.RowVersion });
        var rawBefore = await db.AttendanceRawEvents.CountAsync();

        var approved = await admin.ApproveAsync(new()
        {
            Id = submitted.Id, RowVersion = submitted.RowVersion,
            SourceFingerprint = submitted.SourceFingerprint,
            Note = "SQL 核准"
        });

        Assert.Equal(AttendanceCorrectionRequestStatus.Approved,
            approved.Status);
        Assert.NotNull(approved.AppliedAttendanceAdjustmentId);
        Assert.Single(await db.AttendanceAdjustments.ToListAsync());
        Assert.Equal(rawBefore, await db.AttendanceRawEvents.CountAsync());
        Assert.Equal((seed.EmployeeId, WorkDate),
            (Assert.Single(recalc.Keys).EmployeeId,
             recalc.Keys[0].WorkDate));
        Assert.Equal(4,
            await db.AttendanceCorrectionRequestHistories.CountAsync());
    }

    [Fact]
    public async Task Recalculation_Failure_Rolls_Back_Adjustment_Request_And_Audit()
    {
        await using var database = await DisposableSqlServerDatabase.CreateAsync(
            "AttendanceCorrectionRollback");
        var seed = await SeedAsync(database, []);
        Guid requestId;
        string rowVersion;
        string fingerprint;
        await using (var createDb = database.CreateDbContext())
        {
            var recalc = new RecordingRecalculation();
            var employee = Service(createDb, RoleNames.Employee,
                seed.EmployeeId, "employee", recalc);
            var draft = await employee.CreateDraftAsync(new()
            {
                AttendanceResultId = seed.ResultId,
                RequestType = AttendanceCorrectionRequestType.MissingBoth,
                ProposedClockInAt = Local(8),
                ProposedClockOutAt = Local(17, 30),
                Reason = AttendanceCorrectionReason.ForgotPunch,
                EmployeeReason = "rollback test"
            });
            var submitted = await employee.SubmitAsync(new()
                { Id = draft.Id, RowVersion = draft.RowVersion });
            requestId = submitted.Id;
            rowVersion = submitted.RowVersion;
            fingerprint = submitted.SourceFingerprint;
        }
        int auditBefore;
        int historyBefore;
        await using (var baseline = database.CreateDbContext())
        {
            auditBefore = await baseline.AuditLogs.CountAsync();
            historyBefore = await baseline.AttendanceCorrectionRequestHistories
                .CountAsync();
        }

        await using (var actionDb = database.CreateDbContext())
        {
            var failing = new RecordingRecalculation { Fail = true };
            var admin = Service(actionDb, RoleNames.Admin, null, "admin", failing);
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                admin.ApproveAsync(new()
                {
                    Id = requestId, RowVersion = rowVersion,
                    SourceFingerprint = fingerprint
                }));
        }

        await using var verify = database.CreateDbContext();
        Assert.Equal(AttendanceCorrectionRequestStatus.Submitted,
            (await verify.AttendanceCorrectionRequests.SingleAsync()).Status);
        Assert.Empty(await verify.AttendanceAdjustments.ToListAsync());
        Assert.Equal(auditBefore, await verify.AuditLogs.CountAsync());
        Assert.Equal(historyBefore,
            await verify.AttendanceCorrectionRequestHistories.CountAsync());
    }

    [Fact]
    public async Task Concurrent_Approve_Reject_Allows_One_Terminal_Transition()
    {
        await using var database = await DisposableSqlServerDatabase.CreateAsync(
            "AttendanceCorrectionConcurrency");
        var seed = await SeedAsync(database,
            [new AttendancePunchCandidate(Guid.NewGuid(), Local(8, 10)),
             new AttendancePunchCandidate(Guid.NewGuid(), Local(17, 30))]);
        Guid id;
        string version;
        string fingerprint;
        await using (var createDb = database.CreateDbContext())
        {
            var recalc = new RecordingRecalculation();
            var service = Service(createDb, RoleNames.Employee,
                seed.EmployeeId, "employee", recalc);
            var draft = await service.CreateDraftAsync(new()
            {
                AttendanceResultId = seed.ResultId,
                RequestType = AttendanceCorrectionRequestType.LateExplanation,
                Reason = AttendanceCorrectionReason.TrafficIncident,
                EmployeeReason = "traffic"
            });
            var submitted = await service.SubmitAsync(new()
                { Id = draft.Id, RowVersion = draft.RowVersion });
            id = submitted.Id;
            version = submitted.RowVersion;
            fingerprint = submitted.SourceFingerprint;
        }

        await using var approveDb = database.CreateDbContext();
        await using var rejectDb = database.CreateDbContext();
        var approve = Attempt(Service(approveDb, RoleNames.Admin, null,
            "admin-a", new RecordingRecalculation()).ApproveAsync(new()
            { Id = id, RowVersion = version, SourceFingerprint = fingerprint }));
        var reject = Attempt(Service(rejectDb, RoleNames.Admin, null,
            "admin-b", new RecordingRecalculation()).RejectAsync(new()
            { Id = id, RowVersion = version, SourceFingerprint = fingerprint,
              Note = "reject" }));
        var outcomes = await Task.WhenAll(approve, reject);

        Assert.Single(outcomes, item => item);
        await using var verify = database.CreateDbContext();
        Assert.Contains((await verify.AttendanceCorrectionRequests.SingleAsync()).Status,
            new[] { AttendanceCorrectionRequestStatus.Approved,
                AttendanceCorrectionRequestStatus.Rejected });
        Assert.Equal(3,
            await verify.AttendanceCorrectionRequestHistories.CountAsync());
    }

    private static readonly DateOnly WorkDate = new(2026, 8, 24);
    private static readonly DateTimeOffset Now =
        new(2026, 8, 23, 2, 0, 0, TimeSpan.Zero);

    private static async Task<(Guid EmployeeId, Guid ResultId)> SeedAsync(
        DisposableSqlServerDatabase database,
        IReadOnlyCollection<AttendancePunchCandidate> punches)
    {
        await using var db = database.CreateDbContext();
        var department = new Department(Guid.NewGuid(), "G3SQL",
            "更正 SQL 測試部", Now);
        var employee = new Employee(Guid.NewGuid(), "EMP-G3SQL", "測試員工",
            department.Id, new DateOnly(2025, 1, 1), Now);
        var shift = new AttendanceShift(Guid.NewGuid(), "NORMAL-G3", "正常班",
            new TimeOnly(8, 0), new TimeOnly(8, 1), new TimeOnly(12, 0),
            new TimeOnly(13, 30), new TimeOnly(17, 30), 480,
            false, false, Now);
        var result = new DailyAttendanceResult(Guid.NewGuid(), employee.Id,
            WorkDate, Now);
        result.Recalculate(true, AttendanceCalendarClassification.WorkingDay,
            shift, AttendanceDailyCalculator.Calculate(WorkDate, true,
                Snapshot(shift), punches), "g3-sql", Now);
        var externalId = 1L;
        var rawEvents = punches.Select(punch => new AttendanceRawEvent(
            punch.EventId, AttendanceSourceSystems.BioWebTa, externalId++,
            employee.Id, "G3-PIN", "G3-DEVICE", punch.LocalTime,
            0, 1, punch.LocalTime, Now)).ToArray();
        db.AddRange(department, employee, shift);
        db.AddRange(rawEvents);
        db.Add(result);
        await db.SaveChangesAsync();
        return (employee.Id, result.Id);
    }

    private static AttendanceCorrectionService Service(HRSystemDbContext db,
        string role, Guid? employeeId, string id,
        RecordingRecalculation recalculation)
    {
        var user = new User(role, employeeId, id);
        return new AttendanceCorrectionService(db, user, new FixedTime(Now),
            new AttendanceManagementService(db, user, new FixedTime(Now),
                recalculation), recalculation);
    }

    private static AttendanceShiftSnapshot Snapshot(AttendanceShift shift) =>
        new(shift.Id, shift.Name, shift.ScheduledStartTime,
            shift.LateThresholdTime, shift.LunchBreakStartTime,
            shift.LunchBreakEndTime, shift.ScheduledEndTime,
            shift.ExpectedWorkMinutes, shift.IsLunchPunchRequired,
            shift.IsOvernightShift);
    private static DateTime Local(int hour, int minute = 0) =>
        DateTime.SpecifyKind(WorkDate.ToDateTime(new TimeOnly(hour, minute)),
            DateTimeKind.Unspecified);
    private static async Task<bool> Attempt(
        Task<AttendanceCorrectionRequestDto> operation)
    { try { await operation; return true; } catch { return false; } }

    private sealed class RecordingRecalculation : IAttendanceRecalculationEngine
    {
        public List<AttendanceRecalculationKey> Keys { get; } = [];
        public bool Fail { get; init; }
        public Task<AttendanceRecalculationOutcome> RecalculateRangeAsync(
            DateOnly dateFrom, DateOnly dateTo, Guid? employeeId = null,
            IReadOnlyCollection<PendingApprovedLeave>? pendingApprovedLeaves = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new AttendanceRecalculationOutcome(
                dateFrom, dateTo, 1, 0, Now));
        public Task<AttendanceRecalculationOutcome> RecalculateKeysAsync(
            IReadOnlyCollection<AttendanceRecalculationKey> keys,
            CancellationToken cancellationToken = default,
            IReadOnlyCollection<Guid>? excludedApprovedLeaveRequestIds = null)
        {
            if (Fail) throw new InvalidOperationException("controlled recalc failure");
            Keys.AddRange(keys);
            return Task.FromResult(new AttendanceRecalculationOutcome(
                keys.Min(item => item.WorkDate), keys.Max(item => item.WorkDate),
                1, keys.Count, Now));
        }
    }

    private sealed class User(string role, Guid? employeeId, string id) :
        ICurrentUser
    {
        public string? UserId => id;
        public Guid? EmployeeId => employeeId;
        public string? DisplayName => id;
        public string? IpAddress => "127.0.0.1";
        public bool IsAuthenticated => true;
        public bool IsInRole(string candidate) => candidate == role;
        public bool HasPermission(string policy) =>
            RolePermissions.HasPermission([role], policy);
    }
    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    { public override DateTimeOffset GetUtcNow() => now; }
}
