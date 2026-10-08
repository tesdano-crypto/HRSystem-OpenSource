using HRSystem.Application.Attendance;
using HRSystem.Application.Dashboard;
using HRSystem.Application.Overtime;
using HRSystem.Application.Security;
using HRSystem.Domain.Attendance;
using HRSystem.Domain.LeaveRequests;
using HRSystem.Domain.MasterData;
using HRSystem.Domain.Overtime;

namespace HRSystem.UnitTests;

public sealed class EmployeeDashboardServiceTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 24, 2, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Unbound_Admin_Returns_Safe_Empty_Result()
    {
        await using var db = TestDb.Create();
        var service = Create(db, new TestCurrentUser(RoleNames.Admin));

        var result = await service.GetAsync();

        Assert.False(result.HasEmployeeBinding);
        Assert.Empty(result.RecentAttendance);
        Assert.Empty(result.NeedsAction);
    }

    [Fact]
    public async Task Dashboard_Query_Has_No_EmployeeId_Input()
    {
        Assert.DoesNotContain(typeof(IEmployeeDashboardService).GetMethods()
            .SelectMany(x => x.GetParameters()),
            x => x.ParameterType == typeof(Guid));
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Uses_Taipei_Today_And_Current_Month()
    {
        await using var setup = await Setup.CreateAsync();
        await setup.Service.GetAsync();

        var query = Assert.Single(setup.Attendance.Queries);
        Assert.Equal(new DateOnly(2026, 8, 1), query.StartDate);
        Assert.Equal(new DateOnly(2026, 8, 24), query.EndDate);
        Assert.Null(query.Limit);
    }

    [Fact]
    public async Task Recent_Attendance_Is_Limited_To_Seven_Workdays()
    {
        await using var setup = await Setup.CreateAsync(
            Enumerable.Range(0, 9).Select(index => Row(new DateOnly(2026, 8, 24).AddDays(-index))).ToArray());

        var result = await setup.Service.GetAsync();

        Assert.Equal(7, result.RecentAttendance.Count);
        Assert.Equal(new DateOnly(2026, 8, 24), result.RecentAttendance[0].WorkDate);
    }

    [Fact]
    public async Task Monthly_Summary_Uses_MyAttendance_Semantics()
    {
        var normal = Row(new DateOnly(2026, 8, 24));
        var anomaly = Row(new DateOnly(2026, 8, 23)) with
        {
            IsLate = true,
            LateMinutes = 30,
            IsAnomaly = true
        };
        var fullLeave = Row(new DateOnly(2026, 8, 22)) with
        {
            LeaveCoverageStatus = "Full"
        };
        var nonwork1 = Row(new DateOnly(2026, 8, 21)) with
        {
            IsRequiredWorkday = false
        };
        var nonwork2 = Row(new DateOnly(2026, 8, 20)) with
        {
            IsRequiredWorkday = false
        };
        await using var setup = await Setup.CreateAsync(
            normal, anomaly, fullLeave, nonwork1, nonwork2);

        var result = await setup.Service.GetAsync();

        Assert.Equal(3, result.MonthSummary.Workdays);
        Assert.Equal(4, result.MonthSummary.NormalDays);
        Assert.Equal(1, result.MonthSummary.AnomalyDays);
        Assert.Equal(1, result.MonthSummary.LateDays);
        Assert.Equal(setup.Attendance.LastSummary!.NormalCount,
            result.MonthSummary.NormalDays);
        Assert.Equal(setup.Attendance.LastSummary.AnomalyCount,
            result.MonthSummary.AnomalyDays);
        Assert.NotEqual(result.MonthSummary.Workdays,
            result.MonthSummary.NormalDays);
    }

    [Fact]
    public async Task Today_Before_Scheduled_End_Is_In_Progress_Not_Missing()
    {
        await using var setup = await Setup.CreateAsync(Row(new DateOnly(2026, 8, 24)) with
        {
            MissingClockOut = true,
            IsAnomaly = true
        });

        var result = await setup.Service.GetAsync();

        Assert.True(result.TodayAttendance!.IsInProgress);
        Assert.Equal("已上班，尚未下班", result.TodayAttendance.Status);
        Assert.DoesNotContain(result.NeedsAction, x => x.Title.Contains("缺卡", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Historical_Missing_Punch_Becomes_Action_Without_Request()
    {
        await using var setup = await Setup.CreateAsync(Row(new DateOnly(2026, 8, 22)) with
        {
            MissingClockOut = true,
            IsAnomaly = true
        });

        var result = await setup.Service.GetAsync();

        Assert.Contains(result.NeedsAction, x => x.Title == "缺卡待處理");
    }

    [Fact]
    public async Task Potential_Overtime_Without_Request_Becomes_Action()
    {
        await using var setup = await Setup.CreateAsync(Row(new DateOnly(2026, 8, 22)) with
        {
            OverstayLevel = AttendanceReviewOverstayLevel.PotentialUnreportedOvertime,
            OverstayMinutes = 90,
            IsAnomaly = true
        });

        var result = await setup.Service.GetAsync();

        Assert.Contains(result.NeedsAction, x => x.Title == "疑似未申報加班");
    }

    [Fact]
    public async Task Read_Does_Not_Save_Or_Add_Audit_Data()
    {
        await using var setup = await Setup.CreateAsync(Row(new DateOnly(2026, 8, 24)));
        setup.Db.ChangeTracker.Clear();

        await setup.Service.GetAsync();

        Assert.False(setup.Db.ChangeTracker.HasChanges());
        Assert.Empty(setup.Db.AuditLogs);
    }

    [Fact]
    public async Task Other_Employee_Workflow_Data_Is_Not_Visible()
    {
        await using var setup = await Setup.CreateAsync();
        setup.Db.OvertimeRequests.Add(NewOvertime(setup.OtherEmployee.Id));
        await setup.Db.SaveChangesAsync();

        var result = await setup.Service.GetAsync();

        Assert.Equal(0, result.WorkflowSummary.OvertimeDraft);
        Assert.DoesNotContain(result.NeedsAction, x => x.Category == "加班");
    }

    [Fact]
    public async Task Own_Submitted_Overtime_Appears_In_Waiting_Section()
    {
        await using var setup = await Setup.CreateAsync();
        var request = NewOvertime(setup.Employee.Id);
        request.Submit("unit-test-user", Now);
        setup.Db.OvertimeRequests.Add(request);
        await setup.Db.SaveChangesAsync();

        var result = await setup.Service.GetAsync();

        Assert.Equal(1, result.WorkflowSummary.OvertimeSubmitted);
        Assert.Contains(result.WaitingReview, x => x.Category == "加班" && x.Status == "待審核");
    }

    [Fact]
    public async Task Submitted_Correction_Is_Waiting_Not_Duplicate_Missing_Action()
    {
        var workDate = new DateOnly(2026, 8, 22);
        var row = Row(workDate) with
        {
            MissingClockOut = true,
            IsAnomaly = true,
            CorrectionRequests =
            [
                new(Guid.NewGuid(), AttendanceCorrectionRequestType.MissingClockOut,
                    AttendanceCorrectionRequestStatus.Submitted, "更正申請待審核")
            ]
        };
        await using var setup = await Setup.CreateAsync(row);
        var correction = NewCorrection(setup.Employee.Id, row.DailyAttendanceResultId, workDate);
        correction.Submit(Now);
        setup.Db.AttendanceCorrectionRequests.Add(correction);
        await setup.Db.SaveChangesAsync();

        var result = await setup.Service.GetAsync();

        Assert.DoesNotContain(result.NeedsAction, x => x.Title == "缺卡待處理");
        Assert.Contains(result.WaitingReview, x => x.Category == "出勤更正" && x.Status == "待審核");
    }

    [Fact]
    public async Task Rejected_Correction_Is_Needs_Action()
    {
        await using var setup = await Setup.CreateAsync();
        var correction = NewCorrection(setup.Employee.Id, Guid.NewGuid(), new DateOnly(2026, 8, 22));
        correction.Submit(Now);
        correction.Reject("請補充說明", Now.AddMinutes(1));
        setup.Db.AttendanceCorrectionRequests.Add(correction);
        await setup.Db.SaveChangesAsync();

        var result = await setup.Service.GetAsync();

        Assert.Contains(result.NeedsAction, x => x.Category == "出勤更正" && x.Status.Contains("已駁回", StringComparison.Ordinal));
        Assert.Equal(1, result.WorkflowSummary.CorrectionRejected);
    }

    [Fact]
    public async Task Approved_Leave_Today_Shows_Type_And_Period()
    {
        await using var setup = await Setup.CreateAsync();
        var request = NewLeave(setup.Employee.Id, setup.LeaveType.Id,
            LeaveRequestStatus.Approved);
        setup.Db.LeaveRequests.Add(request);
        await setup.Db.SaveChangesAsync();

        var result = await setup.Service.GetAsync();

        var status = Assert.Single(result.TodayStatuses);
        Assert.Equal("請假", status.Category);
        Assert.Contains("特休", status.Text, StringComparison.Ordinal);
        Assert.Contains("08:00–17:30", status.Text, StringComparison.Ordinal);
        Assert.Equal(1, result.WorkflowSummary.LeaveApprovedThisMonth);
    }

    [Theory]
    [InlineData(30)]
    [InlineData(0)]
    public async Task Current_Confirmed_Recognition_Is_Aggregated(int minutes)
    {
        await using var setup = await Setup.CreateAsync();
        await AddRecognitionAsync(setup, minutes, stale: false);

        var result = await setup.Service.GetAsync();

        Assert.Equal(minutes, result.WorkflowSummary.OvertimeRecognizedMinutes);
        Assert.Equal(0, result.WorkflowSummary.OvertimeNeedsReview);
        Assert.DoesNotContain(result.WaitingReview,
            item => item.Category == "加班");
    }

    [Fact]
    public async Task Stale_Confirmed_Recognition_Is_Not_Aggregated_And_Needs_Review()
    {
        await using var setup = await Setup.CreateAsync();
        await AddRecognitionAsync(setup, 30, stale: true);

        var dashboard = await setup.Service.GetAsync();
        var overtimeService = new OvertimeRequestService(setup.Db,
            new TestCurrentUser(RoleNames.Employee, setup.Employee.Id),
            new DashboardTimeProvider(Now));
        var myOvertime = Assert.Single(await overtimeService.GetMyRequestsAsync(new()));

        Assert.Equal(0, dashboard.WorkflowSummary.OvertimeRecognizedMinutes);
        Assert.Equal(1, dashboard.WorkflowSummary.OvertimeNeedsReview);
        Assert.Equal(OvertimeRecognitionStatus.NeedsReview,
            myOvertime.Recognition!.Status);
        Assert.True(myOvertime.Recognition.IsSourceStale);
        Assert.Contains(dashboard.WaitingReview, item =>
            item.Status == "出勤資料已變更，待重新確認");
    }

    [Fact]
    public async Task Pending_Recognition_Is_Not_Aggregated()
    {
        await using var setup = await Setup.CreateAsync();
        await AddRecognitionAsync(setup, 0, stale: false, pending: true);

        var result = await setup.Service.GetAsync();

        Assert.Equal(0, result.WorkflowSummary.OvertimeRecognizedMinutes);
        Assert.Equal(1,
            result.WorkflowSummary.OvertimeApprovedPendingRecognition);
        Assert.Equal(0, result.WorkflowSummary.OvertimeNeedsReview);
    }

    [Fact]
    public async Task Manager_With_Binding_Sees_Only_Own_Dashboard()
    {
        await using var setup = await Setup.CreateAsync();
        var managerService = Create(setup.Db,
            new TestCurrentUser(RoleNames.Manager, setup.Employee.Id), setup.Attendance);

        var result = await managerService.GetAsync();

        Assert.True(result.HasEmployeeBinding);
        Assert.Equal("測試員工", result.EmployeeName);
    }

    [Fact]
    public void Read_Models_Do_Not_Expose_Internal_Resolution_Data()
    {
        var names = typeof(EmployeeDashboardDto).Assembly.GetTypes()
            .Where(x => x.Namespace == typeof(EmployeeDashboardDto).Namespace && x.Name.StartsWith("EmployeeDashboard", StringComparison.Ordinal))
            .SelectMany(x => x.GetProperties()).Select(x => x.Name).ToHashSet(StringComparer.Ordinal);
        Assert.DoesNotContain("Fingerprint", names);
        Assert.DoesNotContain("RowVersion", names);
        Assert.DoesNotContain("ActorUserId", names);
        Assert.DoesNotContain("ResolutionNote", names);
    }

    private static EmployeeDashboardService Create(
        HRSystem.Infrastructure.Persistence.HRSystemDbContext db,
        TestCurrentUser user,
        IAttendanceReviewService? attendance = null) =>
        new(db, user, attendance ?? new FakeAttendanceService(),
            new DashboardTimeProvider(Now));

    private static MyAttendanceRowDto Row(DateOnly date) => new(
        Guid.NewGuid(), date, true, "Workday", "正常班",
        new TimeOnly(8, 0), new TimeOnly(17, 30),
        date.ToDateTime(new TimeOnly(8, 0)), date.ToDateTime(new TimeOnly(17, 30)),
        "Normal", false, false, false, false, 0, 0, 0, 480, 480, 0, 0,
        "None", false, false, 0, [],
        new AttendanceReviewOvertimeRequestDto(AttendanceReviewOvertimeRequestState.NoRequest, 0, 0, 0, null),
        [], 0, AttendanceReviewOverstayLevel.None, false);

    private static OvertimeRequest NewOvertime(Guid employeeId) => new(
        Guid.NewGuid(), employeeId,
        DateTime.SpecifyKind(new DateTime(2026, 8, 24, 18, 0, 0), DateTimeKind.Unspecified),
        DateTime.SpecifyKind(new DateTime(2026, 8, 24, 20, 0, 0), DateTimeKind.Unspecified),
        "測試加班", "unit-test-user", Now);

    private static async Task AddRecognitionAsync(
        Setup setup, int minutes, bool stale, bool pending = false)
    {
        var request = NewOvertime(setup.Employee.Id);
        request.Submit("unit-test-user", Now);
        request.Approve(OvertimeReviewReason.ApprovedAsRequested, null,
            "unit-test-admin", Now.AddMinutes(1));
        setup.Db.OvertimeRequests.Add(request);
        await setup.Db.SaveChangesAsync();

        var source = OvertimeRecognitionPolicy.Build(
            request.Id, request.EmployeeId, request.OvertimeDate,
            request.Status, request.RowVersion, request.PlannedStartAt,
            request.PlannedEndAt, null, null, null, false, null, true, true);
        var fingerprint = stale
            ? Enumerable.Repeat((byte)0xA5, 32).ToArray()
            : source.Fingerprint;
        var recognition = new OvertimeRecognition(Guid.NewGuid(), request.Id,
            request.EmployeeId, request.OvertimeDate, request.PlannedStartAt,
            request.PlannedEndAt, null, null, null, null,
            pending ? OvertimeRecognitionStatus.Pending : source.InitialStatus,
            fingerprint, Now);
        if (!pending)
        {
            recognition.Confirm(
                minutes == 0 ? null : request.PlannedStartAt,
                minutes == 0 ? null : request.PlannedStartAt.AddMinutes(minutes),
                minutes == 0
                    ? OvertimeRecognitionReason.NoActualOvertime
                    : OvertimeRecognitionReason.ActualAttendanceConfirmed,
                null, fingerprint, "unit-test-admin", Now.AddMinutes(2));
        }
        setup.Db.OvertimeRecognitions.Add(recognition);
        await setup.Db.SaveChangesAsync();
    }

    private static AttendanceCorrectionRequest NewCorrection(
        Guid employeeId, Guid resultId, DateOnly workDate) => new(
        Guid.NewGuid(), employeeId, workDate, resultId,
        AttendanceCorrectionRequestType.MissingClockOut,
        workDate.ToDateTime(new TimeOnly(8, 0)), null, null,
        workDate.ToDateTime(new TimeOnly(17, 30)),
        AttendanceCorrectionReason.ForgotPunch, "忘記打卡", new byte[32], Now);

    private static LeaveRequest NewLeave(
        Guid employeeId, Guid leaveTypeId, LeaveRequestStatus status)
    {
        var request = new LeaveRequest(Guid.NewGuid(), "UAT-LEAVE-001",
            employeeId, leaveTypeId,
            new DateTimeOffset(2026, 8, 24, 8, 0, 0, TimeSpan.FromHours(8)),
            new DateTimeOffset(2026, 8, 24, 17, 30, 0, TimeSpan.FromHours(8)),
            8m, "測試請假", "unit-test-user", Now);
        if (status != LeaveRequestStatus.Draft)
            request.Submit("unit-test-user", Now);
        if (status == LeaveRequestStatus.Approved)
            request.Approve("unit-test-admin", Now.AddMinutes(1));
        return request;
    }

    private sealed class FakeAttendanceService(params MyAttendanceRowDto[] rows) : IAttendanceReviewService
    {
        public List<MyAttendanceQuery> Queries { get; } = [];
        public MyAttendanceSummary? LastSummary { get; private set; }
        public Task<AttendanceReviewFilterOptions> GetFilterOptionsAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AttendanceReviewResult> SearchAsync(AttendanceReviewQuery query, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<MyAttendanceResult> SearchMineAsync(MyAttendanceQuery query, CancellationToken cancellationToken = default)
        {
            Queries.Add(query);
            var items = rows.OrderByDescending(x => x.WorkDate).ToArray();
            LastSummary = new MyAttendanceSummary(items.Length,
                items.Count(x => x.IsRequiredWorkday),
                items.Count(x => !x.IsAnomaly), items.Count(x => x.IsAnomaly),
                items.Count(x => x.IsLate), items.Count(x => x.IsEarlyLeave),
                items.Count(x => x.MissingClockIn || x.MissingClockOut), 0, 0, 0);
            return Task.FromResult(new MyAttendanceResult(true, items, LastSummary));
        }
    }

    private sealed class DashboardTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class Setup : IAsyncDisposable
    {
        private Setup(HRSystem.Infrastructure.Persistence.HRSystemDbContext db,
            FakeAttendanceService attendance, EmployeeDashboardService service,
            Employee employee, Employee otherEmployee)
        { Db = db; Attendance = attendance; Service = service; Employee = employee; OtherEmployee = otherEmployee; }
        public HRSystem.Infrastructure.Persistence.HRSystemDbContext Db { get; }
        public FakeAttendanceService Attendance { get; }
        public EmployeeDashboardService Service { get; }
        public Employee Employee { get; }
        public Employee OtherEmployee { get; }
        public LeaveType LeaveType { get; private set; } = null!;
        public static async Task<Setup> CreateAsync(params MyAttendanceRowDto[] rows)
        {
            var db = TestDb.Create();
            var department = new Department(Guid.NewGuid(), "D01", "測試部", Now);
            var employee = new Employee(Guid.NewGuid(), "EMP0099", "測試員工", department.Id,
                new DateOnly(2020, 1, 1), Now);
            var otherEmployee = new Employee(Guid.NewGuid(), "EMP0100", "其他員工", department.Id,
                new DateOnly(2020, 1, 1), Now);
            var leaveType = new LeaveType(Guid.NewGuid(), "ANNUAL", "特休",
                LeaveUnit.Hour, 1m, true, true, 1, Now);
            db.AddRange(department, employee, otherEmployee, leaveType);
            await db.SaveChangesAsync();
            var attendance = new FakeAttendanceService(rows);
            var setup = new Setup(db, attendance,
                Create(db, new TestCurrentUser(RoleNames.Employee, employee.Id), attendance),
                employee, otherEmployee);
            setup.LeaveType = leaveType;
            return setup;
        }
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }
}
