using HRSystem.Application.Attendance;
using HRSystem.Application.Common.Auditing;
using HRSystem.Application.Common.Exceptions;
using HRSystem.Application.Security;
using HRSystem.Domain.Attendance;
using HRSystem.Domain.Common;
using HRSystem.Domain.MasterData;
using Microsoft.EntityFrameworkCore;

namespace HRSystem.UnitTests;

public sealed class AttendanceReviewResolutionTests
{
    private static readonly DateOnly WorkDate = new(2026, 8, 20);
    private static readonly DateTimeOffset Now = new(2026, 8, 20, 2, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Fingerprint_Is_Deterministic_And_Anomaly_Specific()
    {
        var employeeId = Guid.NewGuid();
        var first = Fingerprint(employeeId, AttendanceReviewAnomalyType.Late);
        var second = Fingerprint(employeeId, AttendanceReviewAnomalyType.Late);
        var other = Fingerprint(employeeId, AttendanceReviewAnomalyType.EarlyLeave);

        Assert.Equal(32, first.Length);
        Assert.Equal(first, second);
        Assert.NotEqual(first, other);
    }

    [Fact]
    public void Other_Reason_Requires_Trimmed_Note()
    {
        var exception = Assert.Throws<DomainValidationException>(() => Create(
            AttendanceReviewAnomalyType.Late,
            AttendanceReviewResolutionReason.Other,
            "   "));

        Assert.Contains("必須填寫", exception.Message);
    }

    [Fact]
    public void Note_Over_1000_Characters_Is_Rejected()
    {
        var exception = Assert.Throws<DomainValidationException>(() => Create(
            AttendanceReviewAnomalyType.Late,
            AttendanceReviewResolutionReason.ConfirmedAttendance,
            new string('x', 1001)));

        Assert.Contains("1000", exception.Message);
    }

    [Fact]
    public void Overtime_Anomaly_Requires_Overtime_Reason()
    {
        Assert.Throws<DomainValidationException>(() => Create(
            AttendanceReviewAnomalyType.PotentialUnreportedOvertime,
            AttendanceReviewResolutionReason.ConfirmedAttendance));

        var resolution = Create(
            AttendanceReviewAnomalyType.PotentialUnreportedOvertime,
            AttendanceReviewResolutionReason.ConfirmedOvertimeWork);
        Assert.Equal(AttendanceReviewResolutionStatus.Resolved, resolution.Status);
    }

    [Fact]
    public void Resolution_Can_Reopen_And_Resolve_Again()
    {
        var resolution = Create();
        resolution.Reopen("重新確認", "actor", Now.AddMinutes(1));
        Assert.Equal(AttendanceReviewResolutionStatus.Reopened, resolution.Status);

        resolution.ResolveAgain(
            Fingerprint(resolution.EmployeeId, resolution.AnomalyType),
            AttendanceReviewResolutionReason.IncorrectPunch,
            "更正原因",
            "actor",
            Now.AddMinutes(2));

        Assert.Equal(AttendanceReviewResolutionStatus.Resolved, resolution.Status);
        Assert.Equal(AttendanceReviewResolutionReason.IncorrectPunch, resolution.Reason);
    }

    [Fact]
    public void Resolved_Resolution_Cannot_Be_Resolved_Twice()
    {
        var resolution = Create();
        Assert.Throws<DomainValidationException>(() => resolution.ResolveAgain(
            Fingerprint(resolution.EmployeeId, resolution.AnomalyType),
            AttendanceReviewResolutionReason.ConfirmedAttendance,
            null,
            "actor",
            Now.AddMinutes(1)));
    }

    [Fact]
    public async Task Resolve_Writes_Ledger_History_And_Safe_Audit()
    {
        await using var setup = await Setup.CreateAsync();
        var search = await setup.Service.SearchAsync(setup.Query());
        var anomaly = Assert.Single(Assert.Single(search.Items).ReviewItems,
            item => item.AnomalyType == AttendanceReviewAnomalyType.Late);

        var result = await setup.Service.ResolveAsync(new(
            setup.Daily.Id,
            anomaly.AnomalyType,
            anomaly.SourceFingerprint,
            AttendanceReviewResolutionReason.ConfirmedAttendance,
            " 已確認 ",
            null));

        Assert.Equal(AttendanceReviewResolutionStatus.Resolved, result.Status);
        var ledger = await setup.Db.AttendanceReviewResolutions.SingleAsync();
        Assert.Equal("已確認", ledger.Note);
        Assert.Equal(2, await setup.Db.AttendanceReviewResolutionHistories.CountAsync());
        var audit = await setup.Db.AuditLogs.SingleAsync(item =>
            item.Action == AuditActions.AttendanceReviewResolved);
        Assert.DoesNotContain("已確認", audit.NewValuesJson ?? string.Empty);
        Assert.Equal(0, await setup.Db.AttendanceRawEvents.CountAsync());
        Assert.Equal(0, await setup.Db.AttendanceAdjustments.CountAsync());
    }

    [Fact]
    public async Task Search_Projects_Resolved_And_NeedsReview_State()
    {
        await using var setup = await Setup.CreateAsync();
        var initial = await setup.Service.SearchAsync(setup.Query());
        var anomaly = initial.Items.Single().ReviewItems.Single(item =>
            item.AnomalyType == AttendanceReviewAnomalyType.Late);
        await setup.Service.ResolveAsync(new(
            setup.Daily.Id, anomaly.AnomalyType, anomaly.SourceFingerprint,
            AttendanceReviewResolutionReason.ConfirmedAttendance, null, null));

        var resolved = await setup.Service.SearchAsync(setup.Query());
        Assert.Equal(AttendanceReviewState.Resolved,
            resolved.Items.Single().ReviewItems.Single(item =>
                item.AnomalyType == AttendanceReviewAnomalyType.Late).ReviewState);

        setup.Daily.Recalculate(true, AttendanceCalendarClassification.WorkingDay,
            setup.Shift,
            AttendanceDailyCalculator.Calculate(WorkDate, true, setup.Snapshot(),
                [new(Guid.NewGuid(), WorkDate.ToDateTime(new TimeOnly(9, 0))),
                 new(Guid.NewGuid(), WorkDate.ToDateTime(new TimeOnly(17, 30)))]),
            "changed", Now.AddMinutes(1));
        await setup.Db.SaveChangesAsync();

        var stale = await setup.Service.SearchAsync(setup.Query());
        Assert.Equal(AttendanceReviewState.NeedsReview,
            stale.Items.Single().ReviewItems.Single(item =>
                item.AnomalyType == AttendanceReviewAnomalyType.Late).ReviewState);
    }

    [Fact]
    public async Task Reopen_Appends_History_And_Audit()
    {
        await using var setup = await Setup.CreateAsync();
        var anomaly = (await setup.Service.SearchAsync(setup.Query()))
            .Items.Single().ReviewItems.First();
        var saved = await setup.Service.ResolveAsync(new(
            setup.Daily.Id, anomaly.AnomalyType, anomaly.SourceFingerprint,
            AttendanceReviewResolutionReason.ConfirmedAttendance, null, null));

        await setup.Service.ReopenAsync(new(saved.ResolutionId, saved.RowVersion, "複核"));

        Assert.Equal(AttendanceReviewResolutionStatus.Reopened,
            (await setup.Db.AttendanceReviewResolutions.SingleAsync()).Status);
        Assert.Equal(3, await setup.Db.AttendanceReviewResolutionHistories.CountAsync());
        Assert.Single(await setup.Db.AuditLogs.Where(item =>
            item.Action == AuditActions.AttendanceReviewReopened).ToListAsync());
    }

    [Fact]
    public async Task Resolution_History_Is_Append_Only()
    {
        await using var setup = await Setup.CreateAsync();
        var anomaly = (await setup.Service.SearchAsync(setup.Query()))
            .Items.Single().ReviewItems.First();
        await setup.Service.ResolveAsync(new(
            setup.Daily.Id, anomaly.AnomalyType, anomaly.SourceFingerprint,
            AttendanceReviewResolutionReason.ConfirmedAttendance, null, null));
        var history = await setup.Db.AttendanceReviewResolutionHistories.FirstAsync();
        setup.Db.AttendanceReviewResolutionHistories.Remove(history);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            setup.Db.SaveChangesAsync());
    }

    [Fact]
    public async Task Resolved_And_Pending_Filters_Use_Current_Ledger_State()
    {
        await using var setup = await Setup.CreateAsync();
        var anomaly = (await setup.Service.SearchAsync(setup.Query()))
            .Items.Single().ReviewItems.First();
        await setup.Service.ResolveAsync(new(
            setup.Daily.Id, anomaly.AnomalyType, anomaly.SourceFingerprint,
            AttendanceReviewResolutionReason.ConfirmedAttendance, null, null));

        var resolvedQuery = setup.Query();
        resolvedQuery.QuickFilter = AttendanceReviewQuickFilter.ResolvedReview;
        Assert.Single((await setup.Service.SearchAsync(resolvedQuery)).Items);
        var pendingQuery = setup.Query();
        pendingQuery.OnlyPending = true;
        Assert.Empty((await setup.Service.SearchAsync(pendingQuery)).Items);
    }

    [Fact]
    public async Task Resolve_Rejects_Stale_Client_Fingerprint_Without_Writes()
    {
        await using var setup = await Setup.CreateAsync();
        var anomaly = (await setup.Service.SearchAsync(setup.Query()))
            .Items.Single().ReviewItems.First();
        var stale = Convert.ToBase64String(new byte[32]);

        await Assert.ThrowsAsync<ConcurrencyConflictException>(() =>
            setup.Service.ResolveAsync(new(
                setup.Daily.Id, anomaly.AnomalyType, stale,
                AttendanceReviewResolutionReason.ConfirmedAttendance,
                null, null)));

        Assert.Equal(0, await setup.Db.AttendanceReviewResolutions.CountAsync());
        Assert.Equal(0, await setup.Db.AuditLogs.CountAsync());
    }

    [Fact]
    public async Task Pending_Filter_Uses_Anomaly_Item_Unit()
    {
        await using var setup = await Setup.CreateAsync();
        var query = setup.Query();
        query.OnlyPending = true;
        var result = await setup.Service.SearchAsync(query);

        Assert.Single(result.Items);
        Assert.True(result.Summary.PendingReviewCount >= 1);
        Assert.Equal(0, result.Summary.ResolvedReviewCount);
    }

    [Fact]
    public async Task Non_Admin_Cannot_Search_Or_Resolve()
    {
        await using var setup = await Setup.CreateAsync(RoleNames.Employee);
        await Assert.ThrowsAsync<ForbiddenAccessException>(() =>
            setup.Service.SearchAsync(setup.Query()));
    }

    private static AttendanceReviewResolution Create(
        AttendanceReviewAnomalyType anomaly = AttendanceReviewAnomalyType.Late,
        AttendanceReviewResolutionReason reason = AttendanceReviewResolutionReason.ConfirmedAttendance,
        string? note = null)
    {
        var employeeId = Guid.NewGuid();
        return new(Guid.NewGuid(), employeeId, WorkDate, Guid.NewGuid(), anomaly,
            Fingerprint(employeeId, anomaly), reason, note, "actor", Now);
    }

    private static byte[] Fingerprint(Guid employeeId, AttendanceReviewAnomalyType anomaly) =>
        AttendanceReviewFingerprint.Compute(employeeId, WorkDate, anomaly,
            WorkDate.ToDateTime(new TimeOnly(8, 30)),
            WorkDate.ToDateTime(new TimeOnly(17, 30)),
            30, 0, 0, false, false);

    private sealed class Setup : IAsyncDisposable
    {
        private Setup(HRSystem.Infrastructure.Persistence.HRSystemDbContext db,
            AttendanceReviewService service, Employee employee,
            AttendanceShift shift, DailyAttendanceResult daily)
        {
            Db = db; Service = service; Employee = employee; Shift = shift; Daily = daily;
        }

        public HRSystem.Infrastructure.Persistence.HRSystemDbContext Db { get; }
        public AttendanceReviewService Service { get; }
        public Employee Employee { get; }
        public AttendanceShift Shift { get; }
        public DailyAttendanceResult Daily { get; }

        public static async Task<Setup> CreateAsync(string role = RoleNames.Admin)
        {
            var db = TestDb.Create();
            var department = new Department(Guid.NewGuid(), "ADM", "行政部", Now);
            var employee = new Employee(Guid.NewGuid(), "EMP9901", "測試員工",
                department.Id, new DateOnly(2025, 1, 1), Now);
            var shift = new AttendanceShift(Guid.NewGuid(), "NORMAL", "正常班",
                new TimeOnly(8, 0), new TimeOnly(8, 1), new TimeOnly(12, 0),
                new TimeOnly(13, 30), new TimeOnly(17, 30), 480, false, false, Now);
            var calculation = AttendanceDailyCalculator.Calculate(WorkDate, true,
                Snapshot(shift),
                [new(Guid.NewGuid(), WorkDate.ToDateTime(new TimeOnly(8, 30))),
                 new(Guid.NewGuid(), WorkDate.ToDateTime(new TimeOnly(17, 30)))]);
            var daily = new DailyAttendanceResult(Guid.NewGuid(), employee.Id, WorkDate, Now);
            daily.Recalculate(true, AttendanceCalendarClassification.WorkingDay,
                shift, calculation, "resolution-test", Now);
            db.AddRange(department, employee, shift, daily);
            await db.SaveChangesAsync();
            return new Setup(db,
                new AttendanceReviewService(db, new TestCurrentUser(role),
                    new FixedTimeProvider(Now)), employee, shift, daily);
        }

        public AttendanceReviewQuery Query() => new()
        {
            StartDate = WorkDate,
            EndDate = WorkDate,
            EmployeeIds = [Employee.Id]
        };

        public AttendanceShiftSnapshot Snapshot() => Snapshot(Shift);

        private static AttendanceShiftSnapshot Snapshot(AttendanceShift shift) => new(
            shift.Id, shift.Name, shift.ScheduledStartTime, shift.LateThresholdTime,
            shift.LunchBreakStartTime, shift.LunchBreakEndTime,
            shift.ScheduledEndTime, shift.ExpectedWorkMinutes,
            shift.IsLunchPunchRequired, shift.IsOvernightShift);

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
