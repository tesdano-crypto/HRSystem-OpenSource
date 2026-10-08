using HRSystem.Application.Attendance;
using HRSystem.Application.Common.Auditing;
using HRSystem.Application.Common.Exceptions;
using HRSystem.Application.Overtime;
using HRSystem.Application.Security;
using HRSystem.Domain.Attendance;
using HRSystem.Domain.Common;
using HRSystem.Domain.CompanyCalendars;
using HRSystem.Domain.MasterData;
using HRSystem.Domain.Overtime;
using HRSystem.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HRSystem.UnitTests;

public sealed class NonWorkingDayPunchTests
{
    private static readonly DateOnly Saturday = new(2026, 8, 29);
    private static readonly DateTimeOffset Now = new(2026, 9, 3, 0, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public async Task Daily_Exact_Weekend_Evidence_Is_Visible_Without_Recognition_Or_Writes(bool admin, bool exceptions)
    {
        await using var s = await Setup.Create(Saturday, false);
        await s.Calendar(CompanyCalendarDayType.Saturday);
        await s.Punch(10, 9, 14);
        await s.Punch(12, 36, 12);
        var persisted = await s.RestResult();
        var rawBefore = await s.Db.AttendanceRawEvents.AsNoTracking().Select(x => x.SourceFingerprint).ToArrayAsync();
        s.Db.ChangeTracker.Clear();

        var row = Assert.Single((await s.Daily(exceptions, admin)).Items);
        Assert.Equal(persisted.Id, row.Id);
        Assert.True(row.HasNonWorkingPunch);
        Assert.True(row.NonWorkingPunchPending);
        Assert.Equal(CompanyCalendarDayType.Saturday, row.PunchEvidence!.DayType);
        Assert.Equal(Saturday.ToDateTime(new(10, 9, 14)), row.PunchEvidence.FirstPunch);
        Assert.Equal(Saturday.ToDateTime(new(12, 36, 12)), row.PunchEvidence.LastPunch);
        Assert.Equal(DateTimeKind.Unspecified, row.PunchEvidence.FirstPunch!.Value.Kind);
        Assert.Equal(2, row.PunchEvidence.PunchCount);
        Assert.Equal(new TimeSpan(2, 26, 58), row.PunchEvidence.PunchSpan);
        Assert.Null(row.RawClockInLocalTime);
        Assert.Null(row.RawClockOutLocalTime);
        Assert.Null(row.EffectiveClockInLocalTime);
        Assert.Null(row.EffectiveClockOutLocalTime);
        Assert.Equal(0, row.RecognizedWorkMinutes);
        Assert.Empty(row.OvertimeLinks);
        Assert.False(s.Db.ChangeTracker.HasChanges());
        var after = await s.Db.DailyAttendanceResults.AsNoTracking().SingleAsync();
        Assert.Equal(persisted.CalculatedAtUtc, after.CalculatedAtUtc);
        Assert.Null(after.RawClockInLocalTime);
        Assert.Null(after.EffectiveClockOutLocalTime);
        Assert.Equal(rawBefore, await s.Db.AttendanceRawEvents.AsNoTracking().Select(x => x.SourceFingerprint).ToArrayAsync());
        Assert.Empty(s.Db.PayrollRuns);
        Assert.Empty(s.Db.PayrollEmployeeSnapshots);
        Assert.Empty(s.Db.PayrollAdjustments);
        Assert.Empty(s.Db.AttendanceAdjustments);
        Assert.Empty(s.Db.OvertimeRequests);
        Assert.Empty(s.Db.OvertimeRecognitions);
        Assert.Empty(s.Db.AttendanceReviewResolutions);
        Assert.Empty(s.Db.AuditLogs);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Daily_Resolved_Evidence_Remains_In_History_But_Not_Exception_Queue(bool admin)
    {
        await using var s = await Setup.Create(Saturday);
        await s.RestResult();
        await s.Resolve();
        var row = Assert.Single((await s.Daily(false, admin)).Items);
        Assert.True(row.HasNonWorkingPunch);
        Assert.False(row.NonWorkingPunchPending);
        Assert.Equal(NonWorkingDayAttendanceDisplay.Resolved, Assert.Single(row.PunchReviewItems).PublicStatus);
        Assert.DoesNotContain("私人取物", System.Text.Json.JsonSerializer.Serialize(row));
        Assert.Empty((await s.Daily(true, admin)).Items);
        Assert.Equal(2, await s.Db.AttendanceReviewResolutionHistories.CountAsync());
    }

    [Fact]
    public async Task Daily_Stale_Resolution_Uses_Shared_Review_Semantics()
    {
        await using var s = await Setup.Create(Saturday);
        await s.RestResult();
        await s.Resolve();
        await s.Punch(11, 30);
        var daily = Assert.Single((await s.Daily(true)).Items);
        var review = Assert.Single((await s.Search(true)).Items);
        Assert.Equal(review.PunchEvidence!.Fingerprint, daily.PunchEvidence!.Fingerprint);
        Assert.Equal(AttendanceReviewState.NeedsReview, Assert.Single(daily.PunchReviewItems).State);
    }

    [Fact]
    public async Task Daily_Scope_And_Status_Filters_Do_Not_Leak_Other_Employees()
    {
        await using var s = await Setup.Create(Saturday);
        await s.RestResult();
        var service = new AttendanceManagementService(s.Db,
            new TestCurrentUser(RoleNames.Employee, Guid.NewGuid()), TimeProvider.System);
        Assert.Empty((await service.GetDailyResultsAsync(new() { EmployeeId = s.Employee.Id,
            DateFrom = Saturday, DateTo = Saturday })).Items);
        Assert.Empty((await s.Daily(query: new() { DateFrom = Saturday, DateTo = Saturday,
            Status = AttendanceDailyStatus.Normal })).Items);
    }

    [Fact]
    public async Task Daily_Pending_Filter_Is_Applied_Before_Count_And_Pagination()
    {
        await using var s = await Setup.Create(Saturday);
        var first = await s.RestResult();
        var sunday = Saturday.AddDays(1);
        var second = await s.RestResult(sunday);
        s.Db.Add(new AttendanceRawEvent(Guid.NewGuid(), AttendanceSourceSystems.BioWebTa, 30,
            s.Employee.Id, "TEST", null, sunday.ToDateTime(new(9, 0)), null, null, null, Now));
        await s.Db.SaveChangesAsync();
        var result = await s.Daily(query: new() { DateFrom = Saturday, DateTo = sunday,
            ExceptionsOnly = true, PageSize = 1, PageNumber = 2 });
        Assert.Equal(2, result.TotalCount);
        Assert.Equal(first.Id, Assert.Single(result.Items).Id);
        Assert.NotEqual(second.Id, result.Items[0].Id);
    }

    [Fact]
    public async Task Daily_Preserves_366_Day_Range_And_Raw_Only_Contract()
    {
        await using var s = await Setup.Create(Saturday);
        Assert.Empty((await s.Daily()).Items); // Daily still lists persisted results only.
        await s.RestResult();
        Assert.Single((await s.Daily(query: new() { DateFrom = new(2026, 1, 1), DateTo = new(2026, 12, 31) })).Items);
    }

    [Theory]
    [InlineData(29)]
    [InlineData(30)]
    public async Task Historical_Weekend_Raw_Only_Is_Visible_Without_Writes(int day)
    {
        await using var s = await Setup.Create(new(2026, 8, day));
        var row = Assert.Single((await s.Search()).Items);
        Assert.Equal(Guid.Empty, row.DailyAttendanceResultId);
        Assert.True(row.HasNonWorkingPunch);
        Assert.Equal(2, row.PunchEvidence!.PunchCount);
        Assert.Equal(TimeSpan.FromMinutes(243), row.PunchEvidence.PunchSpan);
        Assert.Equal(0, row.OverstayMinutes);
        Assert.False(row.IsAnomaly);
        Assert.Equal(0, row.RecognizedWorkMinutes);
        Assert.Empty(s.Db.DailyAttendanceResults);
        Assert.Empty(s.Db.OvertimeRequests);
        Assert.Empty(s.Db.OvertimeRecognitions);
        Assert.Empty(s.Db.PayrollRuns);
        Assert.Empty(s.Db.AttendanceAdjustments);
        Assert.Empty(s.Db.AuditLogs);
    }

    [Fact]
    public async Task Existing_Rest_Result_Is_Merged_And_Multiple_Punches_Are_Evidence_Only()
    {
        await using var s = await Setup.Create(Saturday);
        var daily = new DailyAttendanceResult(Guid.NewGuid(), s.Employee.Id, Saturday, Now);
        daily.Recalculate(false, AttendanceCalendarClassification.Weekend, null,
            AttendanceDailyCalculator.Calculate(Saturday, false, null, []), "test", Now);
        s.Db.Add(daily);
        await s.Punch(13, 20);
        await s.Punch(17, 36);
        var row = Assert.Single((await s.Search()).Items);
        Assert.Equal(daily.Id, row.DailyAttendanceResultId);
        Assert.Equal(4, row.PunchEvidence!.PunchCount);
        Assert.Equal(0, row.OvertimeRequest.ApprovedCoveredMinutes);
        Assert.Null(row.OvertimeRequest.RecognizedMinutes);
        Assert.Null(daily.EffectiveClockInLocalTime);
    }

    [Fact]
    public async Task Empty_Weekend_Is_Not_Projected()
    {
        await using var s = await Setup.Create(Saturday, false);
        Assert.Empty((await s.Search()).Items);
    }

    [Fact]
    public async Task Resolution_Persists_Append_Only_Evidence_And_Leaves_Unresolved_Queue()
    {
        await using var s = await Setup.Create(Saturday);
        Assert.Single((await s.Search(true)).Items);
        var before = await s.Db.AttendanceRawEvents.Select(x => x.SourceFingerprint).ToArrayAsync();
        await s.Resolve();
        var resolution = Assert.Single(s.Db.AttendanceReviewResolutions);
        Assert.Null(resolution.DailyAttendanceResultId);
        Assert.Equal(2, await s.Db.AttendanceReviewResolutionHistories.CountAsync());
        Assert.Equal(AuditActions.NonWorkingDayPunchMarkedNonOvertime, Assert.Single(s.Db.AuditLogs).Action);
        Assert.Empty((await s.Search(true)).Items);
        Assert.Equal(AttendanceReviewState.Resolved, Assert.Single(Assert.Single((await s.Search()).Items).ReviewItems).ReviewState);
        Assert.Equal(before, await s.Db.AttendanceRawEvents.Select(x => x.SourceFingerprint).ToArrayAsync());
        Assert.Empty(s.Db.DailyAttendanceResults);
        Assert.Empty(s.Db.PayrollRuns);
        var mine = await s.Mine().SearchMineAsync(new() { StartDate = Saturday, EndDate = Saturday });
        Assert.Equal(NonWorkingDayAttendanceDisplay.Resolved, Assert.Single(mine.Items).ReviewItems.Single().PublicStatus);
    }

    [Fact]
    public async Task Added_Middle_Punch_Makes_Resolution_Stale()
    {
        await using var s = await Setup.Create(Saturday);
        await s.Resolve();
        await s.Punch(10, 0);
        Assert.Equal(AttendanceReviewState.NeedsReview, Assert.Single(Assert.Single((await s.Search(true)).Items).ReviewItems).ReviewState);
    }

    [Fact]
    public async Task Calendar_Change_To_Working_Day_Retains_Stale_Review()
    {
        await using var s = await Setup.Create(Saturday);
        await s.Resolve();
        await s.Calendar(CompanyCalendarDayType.WorkingDay);
        var row = Assert.Single((await s.Search()).Items);
        Assert.False(row.HasNonWorkingPunch);
        Assert.Equal(AttendanceReviewState.NeedsReview, Assert.Single(row.ReviewItems).ReviewState);
    }

    [Theory]
    [InlineData(CompanyCalendarDayType.NationalHoliday)]
    [InlineData(CompanyCalendarDayType.CompanyHoliday)]
    public async Task Weekday_Holiday_Uses_Published_Calendar(CompanyCalendarDayType type)
    {
        await using var s = await Setup.Create(new(2026, 8, 28));
        await s.Calendar(type);
        Assert.True(Assert.Single((await s.Search()).Items).HasNonWorkingPunch);
    }

    [Fact]
    public async Task Make_Up_Saturday_Does_Not_Become_Nonworking_Evidence()
    {
        await using var s = await Setup.Create(Saturday);
        await s.Calendar(CompanyCalendarDayType.WorkingDay);
        Assert.Empty((await s.Search()).Items);
    }

    [Theory]
    [InlineData(CompanyCalendarDayType.WorkingDay)]
    [InlineData(CompanyCalendarDayType.ExceptionalWorkingDay)]
    public async Task Make_Up_Saturday_Retains_Normal_Attendance_Evaluation(CompanyCalendarDayType type)
    {
        await using var s = await Setup.Create(Saturday);
        await s.Calendar(type);
        await s.Punch(16, 30);
        var shift = new AttendanceShift(Guid.NewGuid(), "NW-SHIFT", "測試班別",
            new(8, 0), new(8, 1), new(12, 0), new(13, 30), new(17, 30), 480, false, false, Now);
        s.Db.AddRange(shift, new EmployeeShiftAssignment(Guid.NewGuid(), s.Employee.Id,
            shift.Id, new(2026, 1, 1), null, Now));
        await s.Db.SaveChangesAsync();
        await new AttendanceRecalculationEngine(s.Db, TimeProvider.System)
            .RecalculateKeysAsync([new(s.Employee.Id, Saturday)]);
        await s.Db.SaveChangesAsync();
        var row = Assert.Single((await s.Search()).Items);
        Assert.True(row.IsRequiredWorkday);
        Assert.False(row.HasNonWorkingPunch);
        Assert.True(row.IsLate);
        Assert.True(row.IsEarlyLeave);
        Assert.True(row.LateMinutes > 0);
        Assert.True(row.EarlyLeaveMinutes > 0);
    }

    [Theory]
    [InlineData(AttendanceReviewAnomalyType.Late)]
    [InlineData(AttendanceReviewAnomalyType.EarlyLeave)]
    [InlineData(AttendanceReviewAnomalyType.MissingClockIn)]
    [InlineData(AttendanceReviewAnomalyType.MissingClockOut)]
    [InlineData(AttendanceReviewAnomalyType.MissingBoth)]
    [InlineData(AttendanceReviewAnomalyType.ExtendedStay)]
    [InlineData(AttendanceReviewAnomalyType.PotentialUnreportedOvertime)]
    public void Existing_Types_Require_Result(AttendanceReviewAnomalyType type) =>
        Assert.Throws<DomainValidationException>(() => NewResolution(type, null));

    [Fact]
    public void New_Type_Allows_Null_Result_But_Not_Approved_Overtime_Reason()
    {
        Assert.Null(NewResolution(AttendanceReviewAnomalyType.NonWorkingDayPunch, null).DailyAttendanceResultId);
        Assert.Throws<DomainValidationException>(() => new AttendanceReviewResolution(Guid.NewGuid(), Guid.NewGuid(),
            Saturday, null, AttendanceReviewAnomalyType.NonWorkingDayPunch, new byte[32],
            AttendanceReviewResolutionReason.ConfirmedOvertimeWork, null, "reviewer", Now));
    }

    [Theory]
    [InlineData(RoleNames.Employee)]
    [InlineData(RoleNames.Manager)]
    [InlineData(RoleNames.Owner)]
    public async Task Unauthorized_Review_Is_Rejected(string role)
    {
        await using var s = await Setup.Create(Saturday);
        var service = new AttendanceReviewService(s.Db, new TestCurrentUser(role, s.Employee.Id));
        await Assert.ThrowsAsync<ForbiddenAccessException>(() => service.SearchAsync(s.Query()));
        await Assert.ThrowsAsync<ForbiddenAccessException>(() => service.ResolveAsync(s.Command("")));
    }

    [Fact]
    public async Task Employee_Only_Sees_Own_Raw_Evidence()
    {
        await using var s = await Setup.Create(Saturday);
        var other = new AttendanceReviewService(s.Db, new TestCurrentUser(RoleNames.Employee, Guid.NewGuid()));
        Assert.Empty((await other.SearchMineAsync(new() { StartDate = Saturday, EndDate = Saturday })).Items);
        Assert.Single((await s.Mine().SearchMineAsync(new() { StartDate = Saturday, EndDate = Saturday })).Items);
    }

    [Theory]
    [InlineData(RoleNames.HR)]
    [InlineData(RoleNames.Accounting)]
    [InlineData(RoleNames.Admin)]
    public async Task Existing_AttendanceManage_Matrix_Is_Preserved(string role)
    {
        await using var s = await Setup.Create(Saturday);
        Assert.True(RolePermissions.HasPermission([role], PolicyNames.AttendanceManage));
        Assert.Single((await new AttendanceReviewService(s.Db, new TestCurrentUser(role)).SearchAsync(s.Query())).Items);
    }

    [Fact]
    public async Task Draft_Only_And_Duplicate_Link_Without_Payroll_Or_Recognition()
    {
        await using var s = await Setup.Create(Saturday);
        var service = new OvertimeRequestService(s.Db, new TestCurrentUser(RoleNames.Employee, s.Employee.Id), TimeProvider.System);
        var command = new CreateOvertimeDraftRequest { NonWorkingDayEvidenceDate = Saturday,
            PlannedStartAt = Saturday.ToDateTime(new(8, 0)), PlannedEndAt = Saturday.ToDateTime(new(12, 0)), Reason = "受指示工作測試" };
        var draft = await service.CreateDraftAsync(command);
        Assert.Equal(OvertimeRequestStatus.Draft, draft.Status);
        var conflict = await Assert.ThrowsAsync<OvertimeDraftOverlapException>(() => service.CreateDraftAsync(command));
        Assert.Equal(draft.Id, conflict.ExistingRequestId);
        var link = Assert.Single(Assert.Single((await s.Search()).Items).OvertimeLinks);
        Assert.Equal(draft.Id, link.Id);
        Assert.Equal(240, link.RequestedMinutes);
        Assert.Null(link.RecognizedMinutes);
        Assert.Single(s.Db.OvertimeRequests);
        Assert.Empty(s.Db.OvertimeRecognitions);
        Assert.Empty(s.Db.PayrollRuns);
        Assert.Contains(s.Db.AuditLogs, x => x.Action == AuditActions.OvertimeDraftCreatedFromNonWorkingDayPunch);
    }

    [Fact]
    public async Task Changed_Evidence_Rejects_Stale_Resolve_Without_Writes()
    {
        await using var s = await Setup.Create(Saturday);
        var row = Assert.Single((await s.Search()).Items);
        await s.Punch(11, 0);
        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => s.Service.ResolveAsync(s.Command(row.PunchEvidence!.Fingerprint)));
        Assert.Empty(s.Db.AttendanceReviewResolutions);
        Assert.Empty(s.Db.AuditLogs);
    }

    private static AttendanceReviewResolution NewResolution(AttendanceReviewAnomalyType type, Guid? source) =>
        new(Guid.NewGuid(), Guid.NewGuid(), Saturday, source, type, new byte[32], AttendanceReviewResolutionReason.NonWorkActivity,
            "測試", "reviewer", Now);

    private sealed class Setup(HRSystemDbContext db, Employee employee, DateOnly date) : IAsyncDisposable
    {
        public HRSystemDbContext Db => db;
        public Employee Employee => employee;
        public AttendanceReviewService Service => new(db, new TestCurrentUser());
        public AttendanceReviewService Mine() => new(db, new TestCurrentUser(RoleNames.Employee, employee.Id));
        public AttendanceReviewQuery Query(bool anomalies = false) => new()
        { EmployeeIds = [employee.Id], StartDate = date, EndDate = date, OnlyAnomalies = anomalies };
        public Task<AttendanceReviewResult> Search(bool anomalies = false) => Service.SearchAsync(Query(anomalies));
        public ResolveAttendanceReviewRequest Command(string fingerprint) => new(Guid.Empty,
            AttendanceReviewAnomalyType.NonWorkingDayPunch, fingerprint, AttendanceReviewResolutionReason.NonWorkActivity, "私人取物", null)
        { EmployeeId = employee.Id, WorkDate = date };
        public async Task Resolve() => await Service.ResolveAsync(Command(Assert.Single((await Search()).Items).PunchEvidence!.Fingerprint));
        public Task<HRSystem.Application.Common.Models.PagedResult<DailyAttendanceResultDto>> Daily(
            bool exceptions = false, bool admin = true, DailyAttendanceQuery? query = null) =>
            new AttendanceManagementService(db, admin ? new TestCurrentUser() : new TestCurrentUser(RoleNames.Employee, employee.Id),
                TimeProvider.System).GetDailyResultsAsync(query ?? new()
                { DateFrom = date, DateTo = date, EmployeeId = employee.Id, ExceptionsOnly = exceptions });
        public async Task<DailyAttendanceResult> RestResult(DateOnly? workDate = null)
        {
            var day = workDate ?? date;
            var result = new DailyAttendanceResult(Guid.NewGuid(), employee.Id, day, Now);
            result.Recalculate(false, AttendanceCalendarClassification.Weekend, null,
                AttendanceDailyCalculator.Calculate(day, false, null, []), "test", Now);
            db.Add(result);
            await db.SaveChangesAsync();
            return result;
        }
        public async Task Punch(int hour, int minute, int second = 0)
        {
            db.Add(new AttendanceRawEvent(Guid.NewGuid(), AttendanceSourceSystems.BioWebTa,
                await db.AttendanceRawEvents.CountAsync() + 1, employee.Id, "TEST", null,
                date.ToDateTime(new(hour, minute, second)), null, null, null, Now));
            await db.SaveChangesAsync();
        }
        public async Task Calendar(CompanyCalendarDayType type)
        {
            var year = new CompanyCalendarYear(Guid.NewGuid(), date.Year, "test", "test", date, "https://example.test/calendar",
                null, null, "v1", new string('a', 64), "test", Now);
            year.Publish("test", Now);
            var manual = type is CompanyCalendarDayType.CompanyHoliday or CompanyCalendarDayType.ExceptionalWorkingDay;
            var day = new CompanyCalendarDay(Guid.NewGuid(), year.Id, date.Year, date,
                manual ? CompanyCalendarDayType.WorkingDay : type, "測試", null, null, "test", Now);
            if (manual) day.OverridePublishedDay(type, "測試", null, "test", "test", Now);
            db.AddRange(year, day);
            await db.SaveChangesAsync();
        }
        public static async Task<Setup> Create(DateOnly date, bool punches = true)
        {
            var db = TestDb.Create();
            var department = new Department(Guid.NewGuid(), "NW", "測試", Now);
            var employee = new Employee(Guid.NewGuid(), "NW001", "測試員工", department.Id, new(2025, 1, 1), Now);
            db.AddRange(department, employee);
            await db.SaveChangesAsync();
            var s = new Setup(db, employee, date);
            if (punches) { await s.Punch(8, 3); await s.Punch(12, 6); }
            return s;
        }
        public ValueTask DisposeAsync() => db.DisposeAsync();
    }
}
