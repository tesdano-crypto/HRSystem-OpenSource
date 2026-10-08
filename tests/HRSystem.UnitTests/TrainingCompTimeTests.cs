using HRSystem.Application.CompTime;
using HRSystem.Application.Attendance;
using HRSystem.Application.Common.Exceptions;
using HRSystem.Domain.CompTime;
using HRSystem.Domain.LeaveRequests;
using HRSystem.Domain.MasterData;
using HRSystem.Domain.Attendance;
using HRSystem.Domain.Overtime;
using HRSystem.Domain.CompanyCalendars;
using HRSystem.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HRSystem.UnitTests;

public sealed class TrainingCompTimeTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 30, 0, 0, 0, TimeSpan.Zero);
    [Fact]
    public async Task Published_Day_Snapshot_Is_Preserved_After_Calendar_Changes()
    {
        await using var f = await Fixture.Create();
        var year = new CompanyCalendarYear(Guid.NewGuid(), 2026, "test", "test", new(2025, 1, 1), "https://example.test/calendar", null, null, "test-v1", new string('a', 64), "test", Now);
        year.Publish("test", Now);
        var day = new CompanyCalendarDay(Guid.NewGuid(), year.Id, 2026, new(2026, 9, 27), CompanyCalendarDayType.Sunday, null, null, null, "test", Now);
        f.Db.AddRange(year, day); await f.Db.SaveChangesAsync();
        var draft = await f.Draft(8, null);
        Assert.Null(draft.Warning);
        await f.Service.ApproveTrainingAsync(new(draft.Id, draft.RowVersion, draft.EvidenceFingerprint, null));
        var grant = await f.Db.TrainingCompTimeGrants.SingleAsync();
        Assert.Equal(day.Id, grant.CalendarDayId); Assert.Equal(year.Id, grant.CalendarYearId);
        Assert.Equal(CompanyCalendarDayType.Sunday, grant.DayType); Assert.False(grant.UsedCalendarFallback);
        year.Archive("test", Now.AddDays(1)); await f.Db.SaveChangesAsync();
        Assert.Equal(CompanyCalendarDayType.Sunday, (await f.Db.TrainingCompTimeGrants.AsNoTracking().SingleAsync()).DayType);
        Assert.Equal(8, await f.Balance());
    }
    [Fact]
    public async Task New_Punch_Or_Overtime_Requires_Fresh_Explicit_Review_Without_Changing_Those_Sources()
    {
        await using var f = await Fixture.Create();
        var draft = await f.Draft(8, null);
        var punch = new AttendanceRawEvent(Guid.NewGuid(), AttendanceSourceSystems.BioWebTa, 1, f.Employee.Id,
            "test", null, new DateTime(2026, 9, 27, 8, 0, 0), 0, null, null, Now);
        var overtime = new OvertimeRequest(Guid.NewGuid(), f.Employee.Id, new DateTime(2026, 9, 27, 17, 0, 0), new DateTime(2026, 9, 27, 18, 0, 0), "other activity", "test", Now);
        f.Db.AddRange(punch, overtime); await f.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<ApplicationValidationException>(() => f.Approve(draft));
        var refreshed = Assert.Single(await f.Service.GetTrainingAsync());
        Assert.Contains("同日有打卡", refreshed.Warning);
        await Assert.ThrowsAsync<ApplicationValidationException>(() => f.Service.ApproveTrainingAsync(new(refreshed.Id, refreshed.RowVersion, refreshed.EvidenceFingerprint, "")));
        await f.Approve(refreshed);
        Assert.Single(f.Db.AttendanceRawEvents); Assert.Single(f.Db.OvertimeRequests);
        Assert.Equal(OvertimeRequestStatus.Draft, (await f.Db.OvertimeRequests.SingleAsync()).Status);
        Assert.Empty(f.Db.DailyAttendanceResults);
    }
    [Fact]
    public async Task Draft_Then_Approval_Exactly_Once_Without_Attendance_Overtime_Or_Payroll()
    {
        await using var f = await Fixture.Create();
        var draft = await f.Draft(8, new DateOnly(2026, 9, 28));
        Assert.Equal(0, await f.Balance()); Assert.Empty(f.Db.CompTimeTransactions);
        await Assert.ThrowsAsync<ApplicationValidationException>(() => f.Service.ApproveTrainingAsync(new(draft.Id, draft.RowVersion, draft.EvidenceFingerprint, null)));
        var approved = await f.Approve(draft);
        await f.Approve(draft);
        Assert.Equal(8, await f.Balance());
        Assert.Single(f.Db.CompTimeTransactions);
        Assert.Equal(TrainingCompTimeStatus.Approved, approved.Status);
        Assert.Equal(2, approved.Histories.Count);
        Assert.Equal(CompTimeSourceType.TrainingCompTimeGrant, f.Db.CompTimeTransactions.Single().SourceType);
        Assert.Empty(f.Db.AttendanceRawEvents); Assert.Empty(f.Db.DailyAttendanceResults);
        Assert.Empty(f.Db.OvertimeRequests); Assert.Empty(f.Db.PayrollRuns); Assert.Empty(f.Db.PayrollAdjustments);
        Assert.Equal(8, approved.RemainingHours); // A past expiration is metadata only.
    }
    [Fact]
    public async Task Duplicate_Course_And_Invalid_Day_Are_Rejected()
    {
        await using var f = await Fixture.Create();
        await f.Draft(8, null);
        await Assert.ThrowsAsync<ApplicationValidationException>(() => f.Draft(8, null));
        await Assert.ThrowsAsync<ApplicationValidationException>(() => f.Service.CreateTrainingAsync(new() {
            EmployeeId = f.Employee.Id, TrainingDate = new(2026, 9, 28), Hours = 8, CourseOrReason = "workday" }));
    }
    [Fact]
    public async Task Expiry_First_Null_Last_Multiple_Grants_And_Restore_Original_Targets()
    {
        await using var f = await Fixture.Create();
        await f.Opening(4);
        var nullExpiry = await f.Approve(await f.Draft(2, null, "null"));
        var later = await f.Approve(await f.Draft(2, new(2026, 12, 1), "later"));
        var earlier = await f.Approve(await f.Draft(2, new(2026, 10, 1), "earlier"));
        var leave = await f.Leave(5);
        await f.Consume(leave);
        var allocations = await f.Db.CompTimeAllocations.ToListAsync();
        Assert.Equal(3, allocations.Count);
        Guid Credit(Guid source) => f.Db.CompTimeTransactions.Single(x => x.SourceId == source).Id;
        Assert.Equal(2, allocations.Single(x => x.GrantTransactionId == Credit(earlier.Id)).AllocatedHours);
        Assert.Equal(2, allocations.Single(x => x.GrantTransactionId == Credit(later.Id)).AllocatedHours);
        var nullCandidates = f.Db.CompTimeTransactions.AsEnumerable().Where(x => x.TransactionType == CompTimeTransactionType.Grant &&
            (x.SourceType == CompTimeSourceType.LegacyOpeningBalance || x.SourceId == nullExpiry.Id)).OrderBy(x => x.CreatedAtUtc).ThenBy(x => x.Id).ToArray();
        Assert.Equal(1, allocations.Single(x => x.GrantTransactionId == nullCandidates[0].Id).AllocatedHours);
        Assert.DoesNotContain(allocations, x => x.GrantTransactionId == nullCandidates[1].Id);
        Assert.Equal(5, await f.Balance());
        await f.Consume(leave); Assert.Equal(3, f.Db.CompTimeAllocations.Count());
        await f.Restore(leave); await f.Restore(leave);
        Assert.Equal(10, await f.Balance()); Assert.Equal(3, f.Db.CompTimeAllocationReturns.Count());
        var training = await f.Service.GetTrainingAsync();
        Assert.All(training, x => { Assert.Equal(0, x.UsedHours); Assert.Equal(x.Hours, x.RemainingHours); });
    }
    [Fact]
    public async Task Legacy_Baseline_13_Does_Not_Add_Ledger_And_Old_Restore_Returns_To_Pool()
    {
        await using var f = await Fixture.Create();
        await f.Opening(8); await f.Opening(8, 1);
        var old = await f.Leave(3);
        f.Db.CompTimeTransactions.Add(new(Guid.NewGuid(), f.Employee.Id, CompTimeTransactionType.Consume, 3,
            new(2026, 9, 1), CompTimeSourceType.LeaveRequest, old.Id, "old", "test", Now.AddDays(-10)));
        await f.Db.SaveChangesAsync(); await f.Cutover();
        Assert.Equal(13, f.Db.CompTimeLegacyPools.Single().BaselineHours);
        Assert.Equal(3, f.Db.CompTimeTransactions.Count()); Assert.Equal(13, await f.Balance());
        await f.Restore(old); await f.Restore(old);
        Assert.Equal(16, await f.Balance()); Assert.Single(f.Db.CompTimeLegacyReturns);
        var next = await f.Leave(5); await f.Consume(next);
        Assert.Equal(f.Employee.Id, f.Db.CompTimeAllocations.Single().LegacyPoolId);
        await f.Restore(next); await f.Restore(next);
        Assert.Equal(16, await f.Balance()); Assert.Single(f.Db.CompTimeAllocationReturns);
    }
    [Fact]
    public async Task Mixed_Pool_Training_And_New_Opening_Do_Not_Double_Count()
    {
        await using var f = await Fixture.Create();
        await f.Opening(4); await f.Cutover();
        await f.Opening(2, 1);
        var training = await f.Approve(await f.Draft(2, new(2026, 10, 1)));
        var leave = await f.Leave(7); await f.Consume(leave);
        Assert.Equal(1, await f.Balance());
        Assert.Equal(3, f.Db.CompTimeAllocations.Count());
        Assert.Equal(4, f.Db.CompTimeLegacyPools.Single().BaselineHours);
        var credit = f.Db.CompTimeTransactions.Single(x => x.SourceId == training.Id);
        Assert.Equal(2, f.Db.CompTimeAllocations.Single(x => x.GrantTransactionId == credit.Id).AllocatedHours);
        await f.Restore(leave); Assert.Equal(8, await f.Balance());
    }
    [Fact]
    public async Task Insufficient_Allocation_And_Missing_Legacy_Baseline_Stop_Without_Repair()
    {
        await using var f = await Fixture.Create();
        await f.Opening(2);
        var leave = await f.Leave(3);
        await Assert.ThrowsAsync<ApplicationValidationException>(() => f.Consume(leave));
        Assert.Empty(f.Db.CompTimeAllocations); Assert.Single(f.Db.CompTimeTransactions);
        f.Db.CompTimeTransactions.Add(new(Guid.NewGuid(), f.Employee.Id, CompTimeTransactionType.Consume, 1,
            new(2026, 9, 1), CompTimeSourceType.LeaveRequest, leave.Id, "old", "test", Now.AddDays(-1)));
        await f.Db.SaveChangesAsync();
        var draft = await f.Draft(8, null);
        await Assert.ThrowsAsync<ApplicationValidationException>(() => f.Approve(draft));
        Assert.Equal(2, f.Db.CompTimeTransactions.Count()); Assert.Empty(f.Db.CompTimeLegacyPools);
    }
    [Fact]
    public async Task Zero_Balance_Pool_Can_Restore_Old_Consumption()
    {
        await using var f = await Fixture.Create();
        await f.Opening(3);
        var old = await f.Leave(3);
        f.Db.CompTimeTransactions.Add(new(Guid.NewGuid(), f.Employee.Id, CompTimeTransactionType.Consume, 3,
            new(2026, 9, 1), CompTimeSourceType.LeaveRequest, old.Id, "old", "test", Now));
        await f.Db.SaveChangesAsync(); await f.Cutover();
        await f.Restore(old); Assert.Equal(3, await f.Balance());
    }
    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => Now; }
    private sealed class Fixture : IAsyncDisposable
    {
        public required HRSystemDbContext Db { get; init; }
        public required Employee Employee { get; init; }
        public required LeaveType Type { get; init; }
        public CompTimeService Service => new(Db, new TestCurrentUser(), new Clock());
        public static async Task<Fixture> Create()
        {
            var db = TestDb.Create();
            var dep = new Department(Guid.NewGuid(), "TRAIN", "測試", Now);
            var emp = new Employee(Guid.NewGuid(), "TESTTRAIN", "測試", dep.Id, new(2020, 1, 1), Now);
            var type = new LeaveType(Guid.NewGuid(), "COMP_TIME", "補休", LeaveUnit.Hour, .5m, true, true, 100, Now);
            db.AddRange(dep, emp, type); await db.SaveChangesAsync();
            return new() { Db = db, Employee = emp, Type = type };
        }
        public Task<CompTimeBalanceDto> Opening(decimal hours, int offset = 0) => Service.CreateLegacyOpeningBalanceAsync(new() {
            EmployeeId = Employee.Id, Hours = hours, CutoverDate = new DateOnly(2026, 1, 1).AddDays(offset), Reason = "test" });
        public Task<TrainingCompTimeDto> Draft(decimal hours, DateOnly? expiry, string course = "training") => Service.CreateTrainingAsync(new() {
            EmployeeId = Employee.Id, TrainingDate = new(2026, 9, 27), Hours = hours, CourseOrReason = course, ExpirationDate = expiry });
        public Task<TrainingCompTimeDto> Approve(TrainingCompTimeDto dto) => Service.ApproveTrainingAsync(new(dto.Id, dto.RowVersion, dto.EvidenceFingerprint, "測試覆核"));
        public async Task<decimal> Balance() => (await Service.GetAdminBalanceAsync(Employee.Id)).AvailableHours;
        public async Task<LeaveRequest> Leave(decimal hours)
        {
            var leave = new LeaveRequest(Guid.NewGuid(), Guid.NewGuid().ToString(), Employee.Id, Type.Id, Now.AddDays(1), Now.AddDays(1).AddHours((double)hours), hours, "test", "test", Now);
            Db.LeaveRequests.Add(leave); await Db.SaveChangesAsync();
            return await Db.LeaveRequests.Include(x => x.LeaveType).SingleAsync(x => x.Id == leave.Id);
        }
        public async Task Consume(LeaveRequest leave) { await Service.ConsumeForApprovalAsync(leave, default); await Db.SaveChangesAsync(); }
        public async Task Restore(LeaveRequest leave) { await Service.RestoreAfterCancellationAsync(leave, default); await Db.SaveChangesAsync(); }
        public async Task Cutover()
        {
            var rows = await Db.CompTimeTransactions.ToArrayAsync();
            Db.CompTimeLegacyPools.Add(new(Employee.Id, rows.Where(x => x.TransactionType == CompTimeTransactionType.Grant).Sum(x => x.Hours),
                rows.Where(x => x.TransactionType == CompTimeTransactionType.Consume).Sum(x => x.Hours), rows.Where(x => x.TransactionType == CompTimeTransactionType.Restore).Sum(x => x.Hours), rows.Length, Now, "test-cutover"));
            Db.CompTimeLegacyMembers.AddRange(rows.Select(x => new CompTimeLegacyMember(x.Id, Employee.Id)));
            await Db.SaveChangesAsync();
        }
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }
}
