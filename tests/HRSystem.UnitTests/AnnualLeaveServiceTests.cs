using HRSystem.Application.AnnualLeave;
using HRSystem.Application.Common.Exceptions;
using HRSystem.Application.LeaveRequests;
using HRSystem.Application.Security;
using HRSystem.Application.Employees;
using HRSystem.Domain.AnnualLeave;
using HRSystem.Domain.LeaveRequests;
using HRSystem.Domain.MasterData;
using HRSystem.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HRSystem.UnitTests;

public sealed class AnnualLeaveServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 12, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Initializer_Is_Idempotent()
    {
        await using var setup = await Setup.CreateAsync();
        await setup.Service.InitializeEmployeeAsync(setup.Employee.Id, new DateOnly(2026, 8, 12));
        var first = await setup.Db.AnnualLeaveEntitlements.CountAsync();
        await setup.Service.InitializeEmployeeAsync(setup.Employee.Id, new DateOnly(2026, 8, 12));
        Assert.Equal(first, await setup.Db.AnnualLeaveEntitlements.CountAsync());
    }

    [Fact]
    public async Task Initializer_Uses_Hire_Date_And_Creates_Expected_Grants()
    {
        await using var setup = await Setup.CreateAsync();
        await setup.Service.InitializeEmployeeAsync(setup.Employee.Id, new DateOnly(2026, 8, 12));
        var grants = await setup.Db.AnnualLeaveEntitlements.OrderBy(x => x.GrantedDate).ToListAsync();
        Assert.Equal(7, grants.Count);
        Assert.Equal(AnnualLeaveMilestone.HalfYear, grants[0].Milestone);
        Assert.Equal(15m, grants[^1].GrantedDays);
    }

    [Fact]
    public async Task My_Balance_Requires_Employee_Binding()
    {
        await using var setup = await Setup.CreateAsync(withEmployeeBinding: false);
        await Assert.ThrowsAsync<ForbiddenAccessException>(() => setup.Service.GetMyBalanceAsync(new DateOnly(2026, 8, 12)));
    }

    [Fact]
    public async Task Submit_Reserves_Annual_Leave()
    {
        await using var setup = await Setup.CreateAsync();
        var request = setup.NewRequest();
        setup.Db.LeaveRequests.Add(request);
        await setup.Service.ReserveForSubmissionAsync(request, default);
        await setup.Db.SaveChangesAsync();
        Assert.Equal(480, await setup.Db.AnnualLeaveAllocations.SumAsync(x => x.AllocatedMinutes));
        Assert.Equal(480, await setup.Db.AnnualLeaveEntitlements.SumAsync(x => x.ReservedMinutes));
    }

    [Fact]
    public async Task Duplicate_Submit_Is_Rejected()
    {
        await using var setup = await Setup.CreateAsync();
        var request = setup.NewRequest(); setup.Db.LeaveRequests.Add(request);
        await setup.Service.ReserveForSubmissionAsync(request, default); await setup.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<ApplicationValidationException>(() => setup.Service.ReserveForSubmissionAsync(request, default));
    }

    [Fact]
    public async Task Approval_Consumes_Reservation()
    {
        await using var setup = await Setup.CreateAsync();
        var request = setup.NewRequest(); setup.Db.LeaveRequests.Add(request);
        await setup.Service.ReserveForSubmissionAsync(request, default); await setup.Db.SaveChangesAsync();
        await setup.Service.ConsumeForApprovalAsync(request.Id, default); await setup.Db.SaveChangesAsync();
        var item = await setup.Db.AnnualLeaveEntitlements.SingleAsync(x => x.ConsumedMinutes > 0);
        Assert.Equal(480, item.ConsumedMinutes); Assert.Equal(0, item.ReservedMinutes);
    }

    [Fact]
    public async Task Rejection_Releases_Reservation()
    {
        await using var setup = await Setup.CreateAsync();
        var request = setup.NewRequest(); setup.Db.LeaveRequests.Add(request);
        await setup.Service.ReserveForSubmissionAsync(request, default); await setup.Db.SaveChangesAsync();
        await setup.Service.ReleaseReservationAsync(request.Id, default); await setup.Db.SaveChangesAsync();
        Assert.Equal(0, await setup.Db.AnnualLeaveEntitlements.SumAsync(x => x.ReservedMinutes));
    }

    [Fact]
    public async Task Cancellation_Restores_Original_Allocation()
    {
        await using var setup = await Setup.CreateAsync();
        var request = setup.NewRequest(); setup.Db.LeaveRequests.Add(request);
        await setup.Service.ReserveForSubmissionAsync(request, default); await setup.Db.SaveChangesAsync();
        await setup.Service.ConsumeForApprovalAsync(request.Id, default); await setup.Db.SaveChangesAsync();
        await setup.Service.RestoreAfterCancellationAsync(request.Id, default); await setup.Db.SaveChangesAsync();
        var allocation = await setup.Db.AnnualLeaveAllocations.SingleAsync();
        Assert.Equal(AnnualLeaveAllocationStatus.Restored, allocation.Status);
        Assert.Equal(0, await setup.Db.AnnualLeaveEntitlements.SumAsync(x => x.ConsumedMinutes));
    }

    [Fact]
    public async Task Non_Annual_Request_Does_Not_Create_Allocation()
    {
        await using var setup = await Setup.CreateAsync();
        var other = new LeaveType(Guid.NewGuid(), "SICK", "病假", LeaveUnit.Hour, .5m, true, false, 2, Now);
        setup.Db.LeaveTypes.Add(other); await setup.Db.SaveChangesAsync();
        var request = setup.NewRequest(other.Id); setup.Db.LeaveRequests.Add(request);
        await setup.Service.ReserveForSubmissionAsync(request, default); await setup.Db.SaveChangesAsync();
        Assert.Empty(setup.Db.AnnualLeaveAllocations);
    }

    [Fact]
    public async Task Preview_Does_Not_Write_Entitlements()
    {
        await using var setup = await Setup.CreateAsync(role: RoleNames.Admin);
        var preview = await setup.Service.PreviewBackfillAsync(new DateOnly(2026, 8, 12));
        Assert.True(preview.MissingEntitlementCount > 0);
        Assert.Empty(setup.Db.AnnualLeaveEntitlements);
    }

    [Fact]
    public async Task Backfill_Submitted_Request_Creates_Reserved_Allocation_Without_Modifying_Request()
    {
        await using var setup = await Setup.CreateAsync(role: RoleNames.Admin);
        var request = setup.NewRequest(); request.Submit("historical-user", Now);
        setup.Db.LeaveRequests.Add(request); await setup.Db.SaveChangesAsync();
        var result = await setup.Service.ApplyBackfillForEmployeeAsync(
            setup.Employee.Id, new DateOnly(2026, 8, 12));
        Assert.Equal(1, result.RequestsAllocated);
        Assert.Equal(AnnualLeaveAllocationStatus.Reserved,
            (await setup.Db.AnnualLeaveAllocations.SingleAsync()).Status);
        Assert.Equal(LeaveRequestStatus.Submitted,
            (await setup.Db.LeaveRequests.AsNoTracking().SingleAsync()).Status);
    }

    [Fact]
    public async Task Backfill_Approved_Uses_Stored_Duration_When_Current_Calculator_Returns_Zero()
    {
        await using var setup = await Setup.CreateAsync(
            role: RoleNames.Admin,
            durationCalculator: new ZeroDurationCalculator());
        setup.Employee.Deactivate(Now);
        var request = setup.NewRequest();
        request.Submit("employee", Now);
        request.Approve("manager", Now);
        setup.Db.LeaveRequests.Add(request);
        await setup.Db.SaveChangesAsync();

        var preview = await setup.Service.PreviewBackfillAsync(new DateOnly(2026, 8, 12));
        var previewItem = Assert.Single(preview.Items);
        Assert.Equal(1, previewItem.AllocatableRequestCount);
        Assert.Equal(0, previewItem.ManualReviewRequestCount);

        await setup.Service.ApplyBackfillForEmployeeAsync(setup.Employee.Id, new DateOnly(2026, 8, 12));

        var allocation = await setup.Db.AnnualLeaveAllocations.SingleAsync();
        Assert.Equal(480, allocation.AllocatedMinutes);
        Assert.Equal(AnnualLeaveAllocationStatus.Consumed, allocation.Status);
        Assert.Equal(480, await setup.Db.AnnualLeaveEntitlements.SumAsync(x => x.ConsumedMinutes));
    }

    [Fact]
    public async Task Backfill_Does_Not_Invoke_Current_Duration_Calculator()
    {
        await using var setup = await Setup.CreateAsync(
            role: RoleNames.Admin,
            durationCalculator: new ThrowingDurationCalculator());
        var request = setup.NewRequest();
        request.Submit("employee", Now);
        request.Approve("manager", Now);
        setup.Db.LeaveRequests.Add(request);
        await setup.Db.SaveChangesAsync();

        await setup.Service.ApplyBackfillForEmployeeAsync(setup.Employee.Id, new DateOnly(2026, 8, 12));

        Assert.Equal(480, (await setup.Db.AnnualLeaveAllocations.SingleAsync()).AllocatedMinutes);
    }

    [Fact]
    public async Task Backfill_Withdrawn_Request_Creates_Released_Evidence_Without_Reducing_Balance()
    {
        await using var setup = await Setup.CreateAsync(role: RoleNames.Admin);
        var request = setup.NewRequest();
        request.Submit("employee", Now);
        request.Withdraw("employee", Now);
        setup.Db.LeaveRequests.Add(request);
        await setup.Db.SaveChangesAsync();

        await setup.Service.ApplyBackfillForEmployeeAsync(setup.Employee.Id, new DateOnly(2026, 8, 12));

        var allocation = await setup.Db.AnnualLeaveAllocations.SingleAsync();
        Assert.Equal(AnnualLeaveAllocationStatus.Released, allocation.Status);
        Assert.Equal(480, allocation.AllocatedMinutes);
        var entitlements = await setup.Db.AnnualLeaveEntitlements.ToListAsync();
        Assert.Equal(
            entitlements.Sum(x => x.GrantedMinutes),
            entitlements.Sum(x => x.AvailableMinutes));
    }

    [Fact]
    public async Task Backfill_Cancelled_Request_Restores_Allocation()
    {
        await using var setup = await Setup.CreateAsync(role: RoleNames.Admin);
        var request = setup.NewRequest(); request.Submit("employee", Now); request.Approve("manager", Now);
        request.RequestCancellation("日期錯誤", "employee", Now); request.ApproveCancellation("manager", Now);
        setup.Db.LeaveRequests.Add(request); await setup.Db.SaveChangesAsync();
        await setup.Service.ApplyBackfillForEmployeeAsync(setup.Employee.Id, new DateOnly(2026, 8, 12));
        Assert.Equal(AnnualLeaveAllocationStatus.Restored,
            (await setup.Db.AnnualLeaveAllocations.SingleAsync()).Status);
        Assert.Equal(0, await setup.Db.AnnualLeaveEntitlements.SumAsync(x => x.ConsumedMinutes));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(8.01)]
    public async Task Backfill_Invalid_Stored_Duration_Requires_Manual_Review_And_Refuses_Apply(
        decimal storedDurationHours)
    {
        await using var setup = await Setup.CreateAsync(role: RoleNames.Admin);
        var request = setup.NewRequest();
        request.Submit("employee", Now);
        setup.Db.LeaveRequests.Add(request);
        await setup.Db.SaveChangesAsync();
        setup.Db.Entry(request).Property(x => x.DurationHours).CurrentValue = storedDurationHours;
        await setup.Db.SaveChangesAsync();

        var preview = await setup.Service.PreviewBackfillAsync(new DateOnly(2026, 8, 12));
        Assert.Equal(1, preview.ManualReviewRequestCount);
        await Assert.ThrowsAsync<ApplicationValidationException>(() =>
            setup.Service.ApplyBackfillForEmployeeAsync(setup.Employee.Id, new DateOnly(2026, 8, 12)));
        Assert.Empty(setup.Db.AnnualLeaveEntitlements);
        Assert.Empty(setup.Db.AnnualLeaveAllocations);
    }

    [Fact]
    public async Task Backfill_Termination_Date_Caps_Preview_And_Apply_At_The_Same_Grant_Count()
    {
        await using var setup = await Setup.CreateAsync(role: RoleNames.Admin);
        var terminationDate = new DateOnly(2023, 8, 2);
        setup.Employee.Update(
            setup.Employee.ChineseName,
            setup.Employee.DepartmentId,
            setup.Employee.HireDate,
            setup.Employee.EnglishName,
            setup.Employee.JobTitle,
            terminationDate,
            setup.Employee.Email,
            setup.Employee.MobilePhone,
            Now);
        setup.Employee.Deactivate(Now);
        await setup.Db.SaveChangesAsync();

        var preview = await setup.Service.PreviewBackfillAsync(new DateOnly(2026, 8, 12));
        var planned = Assert.Single(preview.Items).MissingEntitlementCount;
        var applied = await setup.Service.ApplyBackfillForEmployeeAsync(
            setup.Employee.Id, new DateOnly(2026, 8, 12));

        Assert.Equal(3, planned);
        Assert.Equal(planned, applied.EntitlementsCreated);
        Assert.All(await setup.Db.AnnualLeaveEntitlements.ToListAsync(),
            entitlement => Assert.True(entitlement.GrantedDate <= terminationDate));
    }

    [Fact]
    public async Task Backfill_Inactive_Employee_Without_Termination_Uses_AsOf_Consistently()
    {
        await using var setup = await Setup.CreateAsync(role: RoleNames.Admin);
        setup.Employee.Deactivate(Now);
        await setup.Db.SaveChangesAsync();

        var preview = await setup.Service.PreviewBackfillAsync(new DateOnly(2026, 8, 12));
        var planned = Assert.Single(preview.Items).MissingEntitlementCount;
        var applied = await setup.Service.ApplyBackfillForEmployeeAsync(
            setup.Employee.Id, new DateOnly(2026, 8, 12));

        Assert.Equal(planned, applied.EntitlementsCreated);
        Assert.Equal(7, applied.EntitlementsCreated);
    }

    [Fact]
    public async Task Backfill_Second_Apply_Is_Idempotent()
    {
        await using var setup = await Setup.CreateAsync(role: RoleNames.Admin);
        var request = setup.NewRequest();
        request.Submit("employee", Now);
        request.Approve("manager", Now);
        setup.Db.LeaveRequests.Add(request);
        await setup.Db.SaveChangesAsync();

        await setup.Service.ApplyBackfillForEmployeeAsync(setup.Employee.Id, new DateOnly(2026, 8, 12));
        var entitlementCount = await setup.Db.AnnualLeaveEntitlements.CountAsync();
        var allocationCount = await setup.Db.AnnualLeaveAllocations.CountAsync();
        var second = await setup.Service.ApplyBackfillForEmployeeAsync(
            setup.Employee.Id, new DateOnly(2026, 8, 12));

        Assert.Equal(0, second.EntitlementsCreated);
        Assert.Equal(0, second.RequestsAllocated);
        Assert.Equal(0, second.AllocationRowsCreated);
        Assert.Equal(entitlementCount, await setup.Db.AnnualLeaveEntitlements.CountAsync());
        Assert.Equal(allocationCount, await setup.Db.AnnualLeaveAllocations.CountAsync());
    }

    [Fact]
    public async Task Carry_Forward_Is_Limited_To_Next_Period_And_Audited()
    {
        await using var setup = await Setup.CreateAsync(role: RoleNames.Admin);
        await setup.Service.InitializeEmployeeAsync(setup.Employee.Id, new DateOnly(2026, 8, 12));
        var rows = await setup.Db.AnnualLeaveEntitlements.OrderBy(x => x.PeriodStart).ToListAsync();
        await setup.Service.ApplyCarryForwardAsync(rows[^2].Id, rows[^1].Id, 60);
        Assert.Equal(60, rows[^2].SettledMinutes);
        Assert.Equal(60, rows[^1].CarriedInMinutes);
        Assert.Single(setup.Db.AnnualLeaveCarryForwards);
        Assert.Contains(setup.Db.AuditLogs, x => x.Action == "AnnualLeaveCarryForwardApplied");
    }

    [Fact]
    public async Task HireDate_Change_Is_Blocked_After_Entitlement_Exists()
    {
        await using var setup = await Setup.CreateAsync(role: RoleNames.Admin);
        await setup.Service.InitializeEmployeeAsync(setup.Employee.Id, new DateOnly(2026, 8, 12));
        var employeeService = TestEmployeeServices.Create(setup.Db, new TestCurrentUser(RoleNames.Admin));
        await Assert.ThrowsAsync<ApplicationValidationException>(() => employeeService.UpdateAsync(
            new UpdateEmployeeRequest
            {
                Id = setup.Employee.Id,
                ChineseName = setup.Employee.ChineseName,
                DepartmentId = setup.Employee.DepartmentId,
                HireDate = setup.Employee.HireDate.AddDays(1),
                RowVersion = Convert.ToBase64String(setup.Employee.RowVersion)
            }));
    }

    private sealed class Setup : IAsyncDisposable
    {
        public required HRSystemDbContext Db { get; init; }
        public required Employee Employee { get; init; }
        public required LeaveType AnnualType { get; init; }
        public required AnnualLeaveService Service { get; init; }

        public static async Task<Setup> CreateAsync(
            bool withEmployeeBinding = true,
            string role = RoleNames.Employee,
            ILeaveDurationCalculator? durationCalculator = null)
        {
            var db = TestDb.Create();
            var department = new Department(Guid.NewGuid(), "TEST", "測試部", Now);
            var employee = new Employee(Guid.NewGuid(), "EMP9001", "測試員工", department.Id,
                new DateOnly(2020, 8, 3), Now);
            var annual = new LeaveType(Guid.NewGuid(), AnnualLeavePolicy.LeaveTypeCode, "特休",
                LeaveUnit.Hour, .5m, true, true, 1, Now);
            db.AddRange(department, employee, annual);
            await db.SaveChangesAsync();
            var user = new TestCurrentUser(role, withEmployeeBinding ? employee.Id : null);
            return new Setup
            {
                Db = db, Employee = employee, AnnualType = annual,
                Service = new AnnualLeaveService(db, user, durationCalculator ?? new FixedDurationCalculator(),
                    new FixedTimeProvider(Now))
            };
        }

        public LeaveRequest NewRequest(Guid? leaveTypeId = null) => new(
            Guid.NewGuid(), $"TEST-{Guid.NewGuid():N}", Employee.Id, leaveTypeId ?? AnnualType.Id,
            new DateTimeOffset(2026, 8, 3, 8, 0, 0, TimeSpan.FromHours(8)),
            new DateTimeOffset(2026, 8, 3, 17, 30, 0, TimeSpan.FromHours(8)),
            8m, "測試", "unit-test-user", Now);
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private sealed class FixedDurationCalculator : ILeaveDurationCalculator
    {
        public Task<LeaveDurationEstimateDto> CalculateAsync(Guid employeeId, DateTimeOffset startAt,
            DateTimeOffset endAt, CancellationToken cancellationToken = default) =>
            Task.FromResult(new LeaveDurationEstimateDto(8m, true, []));
        public Task<LeaveDurationEstimateDto> CalculateAsync(Guid employeeId, DateTimeOffset startAt,
            DateTimeOffset endAt, LeaveCalculationMode calculationMode,
            CancellationToken cancellationToken = default) => CalculateAsync(employeeId, startAt, endAt, cancellationToken);
    }

    private sealed class ZeroDurationCalculator : ILeaveDurationCalculator
    {
        public Task<LeaveDurationEstimateDto> CalculateAsync(
            Guid employeeId,
            DateTimeOffset startAt,
            DateTimeOffset endAt,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new LeaveDurationEstimateDto(0m, false, ["No active assignment."]));

        public Task<LeaveDurationEstimateDto> CalculateAsync(
            Guid employeeId,
            DateTimeOffset startAt,
            DateTimeOffset endAt,
            LeaveCalculationMode calculationMode,
            CancellationToken cancellationToken = default) =>
            CalculateAsync(employeeId, startAt, endAt, cancellationToken);
    }

    private sealed class ThrowingDurationCalculator : ILeaveDurationCalculator
    {
        public Task<LeaveDurationEstimateDto> CalculateAsync(
            Guid employeeId,
            DateTimeOffset startAt,
            DateTimeOffset endAt,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Historical backfill must not call the current duration calculator.");

        public Task<LeaveDurationEstimateDto> CalculateAsync(
            Guid employeeId,
            DateTimeOffset startAt,
            DateTimeOffset endAt,
            LeaveCalculationMode calculationMode,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Historical backfill must not call the current duration calculator.");
    }

    private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }
}
