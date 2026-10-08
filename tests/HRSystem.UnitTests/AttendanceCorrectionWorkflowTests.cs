using HRSystem.Application.Abstractions.Security;
using HRSystem.Application.Attendance;
using HRSystem.Application.Common.Auditing;
using HRSystem.Application.Common.Exceptions;
using HRSystem.Application.Security;
using HRSystem.Domain.Attendance;
using HRSystem.Domain.Common;
using HRSystem.Domain.MasterData;
using HRSystem.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HRSystem.UnitTests;

public sealed class AttendanceCorrectionWorkflowTests
{
    [Theory]
    [InlineData(AttendanceCorrectionRequestType.MissingClockIn, true, false)]
    [InlineData(AttendanceCorrectionRequestType.MissingClockOut, false, true)]
    [InlineData(AttendanceCorrectionRequestType.MissingBoth, true, true)]
    [InlineData(AttendanceCorrectionRequestType.ClockInCorrection, true, false)]
    [InlineData(AttendanceCorrectionRequestType.ClockOutCorrection, false, true)]
    [InlineData(AttendanceCorrectionRequestType.LateExplanation, false, false)]
    [InlineData(AttendanceCorrectionRequestType.EarlyLeaveExplanation, false, false)]
    public void Domain_Enforces_Proposed_Time_Shape(
        AttendanceCorrectionRequestType type, bool clockIn, bool clockOut)
    {
        var entity = NewDomain(type,
            clockIn ? Local(8) : null,
            clockOut ? Local(17, 30) : null);

        Assert.Equal(type, entity.RequestType);
        Assert.Equal(clockIn, entity.ProposedClockInAt.HasValue);
        Assert.Equal(clockOut, entity.ProposedClockOutAt.HasValue);
    }

    [Fact]
    public void Domain_Rejects_Empty_Reason_And_Invalid_Time_Shape()
    {
        Assert.Throws<DomainValidationException>(() => NewDomain(
            AttendanceCorrectionRequestType.MissingClockIn, Local(8), null,
            "  "));
        Assert.Throws<DomainValidationException>(() => NewDomain(
            AttendanceCorrectionRequestType.MissingClockIn, null, Local(17)));
    }

    [Fact]
    public void Domain_Lifecycle_Is_Terminal_After_Approval()
    {
        var entity = NewDomain(AttendanceCorrectionRequestType.MissingClockOut,
            null, Local(17, 30));
        entity.Submit(Now);
        entity.Approve(Guid.NewGuid(), "核准", Now.AddMinutes(1));

        Assert.Equal(AttendanceCorrectionRequestStatus.Approved, entity.Status);
        Assert.Throws<DomainValidationException>(() =>
            entity.Withdraw(null, Now.AddMinutes(2)));
    }

    [Fact]
    public void Explanation_Approval_Cannot_Create_Adjustment()
    {
        var entity = NewDomain(AttendanceCorrectionRequestType.LateExplanation,
            null, null);
        entity.Submit(Now);
        Assert.Throws<DomainValidationException>(() =>
            entity.Approve(Guid.NewGuid(), null, Now));
        entity.Approve(null, null, Now);
        Assert.Equal(AttendanceCorrectionRequestStatus.Approved, entity.Status);
    }

    [Fact]
    public void Rejection_Requires_Reviewer_Note()
    {
        var entity = NewDomain(AttendanceCorrectionRequestType.LateExplanation,
            null, null);
        entity.Submit(Now);
        Assert.Throws<DomainValidationException>(() => entity.Reject(" ", Now));
    }

    [Fact]
    public void History_Validates_Append_Only_Transitions()
    {
        _ = new AttendanceCorrectionRequestHistory(Guid.NewGuid(), Guid.NewGuid(),
            AttendanceCorrectionRequestHistoryAction.Submitted,
            AttendanceCorrectionRequestStatus.Draft,
            AttendanceCorrectionRequestStatus.Submitted,
            "employee", Guid.NewGuid(), "送出", Now);
        Assert.Throws<DomainValidationException>(() =>
            new AttendanceCorrectionRequestHistory(Guid.NewGuid(), Guid.NewGuid(),
                AttendanceCorrectionRequestHistoryAction.Approved,
                AttendanceCorrectionRequestStatus.Draft,
                AttendanceCorrectionRequestStatus.Approved,
                "admin", null, null, Now));
    }

    [Fact]
    public async Task Employee_Can_Only_Create_From_Own_Attendance()
    {
        await using var setup = await Setup.CreateAsync();
        var otherResult = await setup.AddDailyAsync(setup.Other,
            [Candidate(8), Candidate(17, 30)]);

        await Assert.ThrowsAsync<EntityNotFoundException>(() =>
            setup.EmployeeService.CreateDraftAsync(new()
            {
                AttendanceResultId = otherResult.Id,
                RequestType = AttendanceCorrectionRequestType.ClockOutCorrection,
                ProposedClockOutAt = Local(17, 40),
                Reason = AttendanceCorrectionReason.IncorrectRecognizedTime,
                EmployeeReason = "非本人資料"
            }));
    }

    [Fact]
    public async Task User_Without_Employee_Binding_Is_Safely_Rejected()
    {
        await using var setup = await Setup.CreateAsync();
        await Assert.ThrowsAsync<ForbiddenAccessException>(() =>
            setup.AdminService.GetMyRequestsAsync(new()));
    }

    [Theory]
    [InlineData(RoleNames.Employee)]
    [InlineData(RoleNames.Manager)]
    public async Task Non_Admin_Cannot_Review(string role)
    {
        await using var setup = await Setup.CreateAsync();
        var service = setup.Service(new User(role, setup.Employee.Id, role));
        await Assert.ThrowsAsync<ForbiddenAccessException>(() =>
            service.SearchForReviewAsync(new()));
    }

    [Fact]
    public async Task Draft_Update_Submit_And_Withdraw_Appends_History()
    {
        await using var setup = await Setup.CreateAsync();
        var result = await setup.AddDailyAsync(setup.Employee,
            [Candidate(8, 10), Candidate(17, 30)]);
        var draft = await setup.EmployeeService.CreateDraftAsync(
            Explanation(result.Id));
        draft = await setup.EmployeeService.UpdateDraftAsync(new()
        {
            Id = draft.Id, RowVersion = draft.RowVersion,
            AttendanceResultId = result.Id,
            RequestType = AttendanceCorrectionRequestType.LateExplanation,
            Reason = AttendanceCorrectionReason.TrafficIncident,
            EmployeeReason = "交通事故更新"
        });
        var submitted = await setup.EmployeeService.SubmitAsync(new()
            { Id = draft.Id, RowVersion = draft.RowVersion });
        var withdrawn = await setup.EmployeeService.WithdrawAsync(new()
            { Id = submitted.Id, RowVersion = submitted.RowVersion });

        Assert.Equal(AttendanceCorrectionRequestStatus.Withdrawn,
            withdrawn.Status);
        Assert.Equal(4, withdrawn.Histories.Count);
    }

    [Fact]
    public async Task Duplicate_Submitted_Request_Is_Blocked()
    {
        await using var setup = await Setup.CreateAsync();
        var result = await setup.AddDailyAsync(setup.Employee,
            [Candidate(8, 10), Candidate(17, 30)]);
        var first = await setup.EmployeeService.CreateDraftAsync(
            Explanation(result.Id));
        await setup.EmployeeService.SubmitAsync(new()
            { Id = first.Id, RowVersion = first.RowVersion });
        var second = await setup.EmployeeService.CreateDraftAsync(
            Explanation(result.Id));
        await Assert.ThrowsAsync<ApplicationValidationException>(() =>
            setup.EmployeeService.SubmitAsync(new()
                { Id = second.Id, RowVersion = second.RowVersion }));
    }

    [Fact]
    public async Task Approved_Explanation_Does_Not_Adjust_Or_Recalculate()
    {
        await using var setup = await Setup.CreateAsync();
        var result = await setup.AddDailyAsync(setup.Employee,
            [Candidate(8, 10), Candidate(17, 30)]);
        var submitted = await setup.SubmitAsync(Explanation(result.Id));

        var approved = await setup.AdminService.ApproveAsync(new()
        {
            Id = submitted.Id, RowVersion = submitted.RowVersion,
            SourceFingerprint = submitted.SourceFingerprint,
            Note = "接受說明"
        });

        Assert.Equal(AttendanceCorrectionRequestStatus.Approved,
            approved.Status);
        Assert.Null(approved.AppliedAttendanceAdjustmentId);
        Assert.Empty(setup.Recalculation.Keys);
        Assert.Empty(await setup.Db.AttendanceAdjustments.ToListAsync());
        Assert.True((await setup.Db.DailyAttendanceResults.SingleAsync()).IsLate);
    }

    [Fact]
    public async Task Approved_Missing_Punch_Creates_Adjustment_And_Focused_Recalculation()
    {
        await using var setup = await Setup.CreateAsync();
        var result = await setup.AddDailyAsync(setup.Employee, []);
        var submitted = await setup.SubmitAsync(new()
        {
            AttendanceResultId = result.Id,
            RequestType = AttendanceCorrectionRequestType.MissingBoth,
            ProposedClockInAt = Local(8),
            ProposedClockOutAt = Local(17, 30),
            Reason = AttendanceCorrectionReason.ForgotPunch,
            EmployeeReason = "忘記上下班打卡"
        });

        var approved = await setup.AdminService.ApproveAsync(new()
        {
            Id = submitted.Id, RowVersion = submitted.RowVersion,
            SourceFingerprint = submitted.SourceFingerprint,
            Note = "查核完成"
        });

        var adjustment = await setup.Db.AttendanceAdjustments.SingleAsync();
        Assert.Equal(adjustment.Id, approved.AppliedAttendanceAdjustmentId);
        Assert.Equal(setup.Employee.Id, Assert.Single(setup.Recalculation.Keys).EmployeeId);
        Assert.Equal(WorkDate, setup.Recalculation.Keys[0].WorkDate);
        Assert.Equal(4, approved.Histories.Count);
    }

    [Fact]
    public async Task Cross_Midnight_ClockOut_Is_Accepted()
    {
        await using var setup = await Setup.CreateAsync();
        var result = await setup.AddDailyAsync(setup.Employee, []);
        var submitted = await setup.SubmitAsync(new()
        {
            AttendanceResultId = result.Id,
            RequestType = AttendanceCorrectionRequestType.MissingBoth,
            ProposedClockInAt = Local(8),
            ProposedClockOutAt = DateTime.SpecifyKind(
                WorkDate.AddDays(1).ToDateTime(new TimeOnly(0, 30)),
                DateTimeKind.Unspecified),
            Reason = AttendanceCorrectionReason.WorkRequirement,
            EmployeeReason = "跨午夜工作"
        });

        var approved = await setup.AdminService.ApproveAsync(new()
        {
            Id = submitted.Id, RowVersion = submitted.RowVersion,
            SourceFingerprint = submitted.SourceFingerprint
        });
        Assert.Equal(WorkDate.AddDays(1),
            DateOnly.FromDateTime(approved.ProposedClockOutAt!.Value));
    }

    [Fact]
    public async Task Stale_Fingerprint_Blocks_Approval()
    {
        await using var setup = await Setup.CreateAsync();
        var result = await setup.AddDailyAsync(setup.Employee,
            [Candidate(8, 10), Candidate(17, 30)]);
        var submitted = await setup.SubmitAsync(Explanation(result.Id));
        var tracked = await setup.Db.DailyAttendanceResults.SingleAsync();
        tracked.ApplyAdjustment(Guid.NewGuid(), Local(8), Local(17, 30),
            AttendanceDailyCalculator.ApplyEffectiveTimes(WorkDate,
                setup.Snapshot(), AttendanceDailyCalculator.Calculate(
                    WorkDate, true, setup.Snapshot(),
                    [Candidate(8, 10), Candidate(17, 30)]),
                Local(8), Local(17, 30)), true, Now.AddMinutes(1));
        await setup.Db.SaveChangesAsync();

        await Assert.ThrowsAsync<ConcurrencyConflictException>(() =>
            setup.AdminService.ApproveAsync(new()
            {
                Id = submitted.Id, RowVersion = submitted.RowVersion,
                SourceFingerprint = submitted.SourceFingerprint
            }));
        Assert.Equal(AttendanceCorrectionRequestStatus.Submitted,
            (await setup.Db.AttendanceCorrectionRequests.SingleAsync()).Status);
    }

    [Fact]
    public async Task Audit_Excludes_Free_Text_And_History_Is_Append_Only()
    {
        await using var setup = await Setup.CreateAsync();
        var result = await setup.AddDailyAsync(setup.Employee,
            [Candidate(8, 10), Candidate(17, 30)]);
        await setup.EmployeeService.CreateDraftAsync(new()
        {
            AttendanceResultId = result.Id,
            RequestType = AttendanceCorrectionRequestType.LateExplanation,
            Reason = AttendanceCorrectionReason.Other,
            EmployeeReason = "不得進入稽核 payload 的私人說明"
        });
        var audit = string.Join('\n', await setup.Db.AuditLogs
            .Select(item => item.NewValuesJson).ToListAsync());
        Assert.DoesNotContain("私人說明", audit, StringComparison.Ordinal);

        var history = await setup.Db.AttendanceCorrectionRequestHistories
            .FirstAsync();
        setup.Db.Remove(history);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            setup.Db.SaveChangesAsync());
    }

    private static readonly DateOnly WorkDate = new(2026, 8, 24);
    private static readonly DateTimeOffset Now =
        new(2026, 8, 23, 2, 0, 0, TimeSpan.Zero);

    private static AttendanceCorrectionRequest NewDomain(
        AttendanceCorrectionRequestType type, DateTime? clockIn,
        DateTime? clockOut, string reason = "測試申請") => new(
            Guid.NewGuid(), Guid.NewGuid(), WorkDate, Guid.NewGuid(), type,
            null, null, clockIn, clockOut, AttendanceCorrectionReason.Other,
            reason, new byte[32], Now);
    private static DateTime Local(int hour, int minute = 0) =>
        DateTime.SpecifyKind(WorkDate.ToDateTime(new TimeOnly(hour, minute)),
            DateTimeKind.Unspecified);
    private static AttendancePunchCandidate Candidate(int hour, int minute = 0) =>
        new(Guid.NewGuid(), Local(hour, minute));
    private static CreateAttendanceCorrectionDraftRequest Explanation(Guid id) =>
        new()
        {
            AttendanceResultId = id,
            RequestType = AttendanceCorrectionRequestType.LateExplanation,
            Reason = AttendanceCorrectionReason.TrafficIncident,
            EmployeeReason = "交通事故造成遲到"
        };

    private sealed class Setup : IAsyncDisposable
    {
        private Setup(HRSystemDbContext db, Department department,
            Employee employee, Employee other, AttendanceShift shift)
        {
            Db = db; Department = department; Employee = employee;
            Other = other; Shift = shift;
            Recalculation = new FakeRecalculation();
            EmployeeService = Service(new User(RoleNames.Employee,
                employee.Id, "employee"));
            AdminService = Service(new User(RoleNames.Admin, null, "admin"));
        }
        public HRSystemDbContext Db { get; }
        public Department Department { get; }
        public Employee Employee { get; }
        public Employee Other { get; }
        public AttendanceShift Shift { get; }
        public FakeRecalculation Recalculation { get; }
        public AttendanceCorrectionService EmployeeService { get; }
        public AttendanceCorrectionService AdminService { get; }

        public static async Task<Setup> CreateAsync()
        {
            var db = new HRSystemDbContext(
                new DbContextOptionsBuilder<HRSystemDbContext>()
                    .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            var department = new Department(Guid.NewGuid(), "G3", "更正測試部", Now);
            var employee = new Employee(Guid.NewGuid(), "EMP-G301", "測試員工",
                department.Id, new DateOnly(2025, 1, 1), Now);
            var other = new Employee(Guid.NewGuid(), "EMP-G302", "其他員工",
                department.Id, new DateOnly(2025, 1, 1), Now);
            var shift = new AttendanceShift(Guid.NewGuid(), "NORMAL", "正常班",
                new TimeOnly(8, 0), new TimeOnly(8, 1), new TimeOnly(12, 0),
                new TimeOnly(13, 30), new TimeOnly(17, 30), 480,
                false, false, Now);
            db.AddRange(department, employee, other, shift);
            await db.SaveChangesAsync();
            return new Setup(db, department, employee, other, shift);
        }

        public AttendanceCorrectionService Service(User user) => new(
            Db, user, new FixedTime(Now),
            new AttendanceManagementService(Db, user,
                new FixedTime(Now), Recalculation), Recalculation);

        public async Task<DailyAttendanceResult> AddDailyAsync(
            Employee employee, IReadOnlyCollection<AttendancePunchCandidate> punches)
        {
            var daily = new DailyAttendanceResult(Guid.NewGuid(), employee.Id,
                WorkDate, Now);
            daily.Recalculate(true,
                AttendanceCalendarClassification.WorkingDay, Shift,
                AttendanceDailyCalculator.Calculate(WorkDate, true,
                    Snapshot(), punches), "g3-test", Now);
            Db.Add(daily);
            await Db.SaveChangesAsync();
            return daily;
        }

        public async Task<AttendanceCorrectionRequestDto> SubmitAsync(
            CreateAttendanceCorrectionDraftRequest request)
        {
            var draft = await EmployeeService.CreateDraftAsync(request);
            return await EmployeeService.SubmitAsync(new()
                { Id = draft.Id, RowVersion = draft.RowVersion });
        }

        public AttendanceShiftSnapshot Snapshot() => new(
            Shift.Id, Shift.Name, Shift.ScheduledStartTime,
            Shift.LateThresholdTime, Shift.LunchBreakStartTime,
            Shift.LunchBreakEndTime, Shift.ScheduledEndTime,
            Shift.ExpectedWorkMinutes, Shift.IsLunchPunchRequired,
            Shift.IsOvernightShift);
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private sealed class FakeRecalculation : IAttendanceRecalculationEngine
    {
        public List<AttendanceRecalculationKey> Keys { get; } = [];
        public Task<AttendanceRecalculationOutcome> RecalculateRangeAsync(
            DateOnly dateFrom, DateOnly dateTo, Guid? employeeId = null,
            IReadOnlyCollection<PendingApprovedLeave>? pendingApprovedLeaves = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new AttendanceRecalculationOutcome(
                dateFrom, dateTo, employeeId.HasValue ? 1 : 0, 0, Now));
        public Task<AttendanceRecalculationOutcome> RecalculateKeysAsync(
            IReadOnlyCollection<AttendanceRecalculationKey> keys,
            CancellationToken cancellationToken = default,
            IReadOnlyCollection<Guid>? excludedApprovedLeaveRequestIds = null)
        {
            Keys.AddRange(keys);
            return Task.FromResult(new AttendanceRecalculationOutcome(
                keys.Min(item => item.WorkDate), keys.Max(item => item.WorkDate),
                keys.Select(item => item.EmployeeId).Distinct().Count(),
                keys.Count, Now));
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
