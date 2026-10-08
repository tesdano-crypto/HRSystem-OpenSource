using HRSystem.Application.Attendance;
using HRSystem.Application.Common.Exceptions;
using HRSystem.Application.CompTime;
using HRSystem.Application.LeaveRequests;
using HRSystem.Application.Security;
using HRSystem.Domain.Attendance;
using HRSystem.Domain.Common;
using HRSystem.Domain.CompTime;
using HRSystem.Domain.LeaveRequests;
using HRSystem.Domain.MasterData;
using HRSystem.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HRSystem.UnitTests;

public sealed class CompTimeServiceTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 31, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Start =
        new(2026, 9, 1, 8, 0, 0, TimeSpan.FromHours(8));
    private static readonly DateTimeOffset End =
        new(2026, 9, 1, 17, 30, 0, TimeSpan.FromHours(8));

    [Theory]
    [InlineData(0.5)]
    [InlineData(1)]
    [InlineData(1.5)]
    [InlineData(15.5)]
    public void Half_Hour_Increments_Are_Allowed(decimal hours) =>
        Assert.Equal(hours, CompTimePolicy.ValidateHours(hours));

    [Theory]
    [InlineData(0)]
    [InlineData(-0.5)]
    [InlineData(0.25)]
    [InlineData(0.75)]
    [InlineData(1.2)]
    [InlineData(15.25)]
    public void Zero_Negative_And_Non_Half_Hour_Values_Are_Rejected(decimal hours) =>
        Assert.Throws<DomainValidationException>(() => CompTimePolicy.ValidateHours(hours));

    [Fact]
    public async Task Legacy_Opening_Balance_Is_Auditable_And_Duplicate_Protected()
    {
        await using var setup = await Setup.CreateAsync();

        var result = await setup.AdminCompTime.CreateLegacyOpeningBalanceAsync(
            Opening(setup.Employee.Id, 16m));

        Assert.Equal(16m, result.AvailableHours);
        var transaction = Assert.Single(result.Transactions);
        Assert.Equal(CompTimeTransactionType.Grant, transaction.TransactionType);
        Assert.Equal(CompTimeSourceType.LegacyOpeningBalance, transaction.SourceType);
        Assert.Null(transaction.SourceId);
        Assert.Contains(setup.Db.AuditLogs, x =>
            x.Action == "CompTimeLegacyOpeningBalanceCreated");
        await Assert.ThrowsAsync<ApplicationValidationException>(() =>
            setup.AdminCompTime.CreateLegacyOpeningBalanceAsync(
                Opening(setup.Employee.Id, 16m)));
        Assert.Single(setup.Db.CompTimeTransactions);
    }

    [Fact]
    public async Task Employee_Cannot_Create_Opening_Balance_Or_View_Others()
    {
        await using var setup = await Setup.CreateAsync();
        await Assert.ThrowsAsync<ForbiddenAccessException>(() =>
            setup.EmployeeCompTime.CreateLegacyOpeningBalanceAsync(
                Opening(setup.Employee.Id, 16m)));
        var own = await setup.EmployeeCompTime.GetMyBalanceAsync();
        Assert.Equal(setup.Employee.Id, own.EmployeeId);
        await Assert.ThrowsAsync<ForbiddenAccessException>(() =>
            setup.EmployeeCompTime.GetAdminBalanceAsync(setup.Manager.Id));
    }

    [Fact]
    public async Task Approved_Comp_Time_Consumes_And_Cancellation_Restores()
    {
        await using var setup = await Setup.CreateAsync();
        await setup.AdminCompTime.CreateLegacyOpeningBalanceAsync(
            Opening(setup.Employee.Id, 16m));

        var approved = await setup.CreateSubmitAndApproveAsync(8m);
        Assert.Equal(8m, (await setup.AdminCompTime.GetAdminBalanceAsync(
            setup.Employee.Id)).AvailableHours);

        var cancellationRequested = await setup.EmployeeLeave.RequestCancellationAsync(new()
        {
            Id = approved.Id,
            RowVersion = approved.RowVersion,
            Comment = "測試返還"
        });
        await setup.ManagerLeave.ApproveCancellationAsync(new()
        {
            Id = approved.Id,
            RowVersion = cancellationRequested.RowVersion
        });

        var result = await setup.AdminCompTime.GetAdminBalanceAsync(setup.Employee.Id);
        Assert.Equal(16m, result.AvailableHours);
        Assert.Equal(3, result.Transactions.Count);
        Assert.Single(result.Transactions, x =>
            x.TransactionType == CompTimeTransactionType.Grant);
        Assert.Single(result.Transactions, x =>
            x.TransactionType == CompTimeTransactionType.Consume);
        Assert.Single(result.Transactions, x =>
            x.TransactionType == CompTimeTransactionType.Restore);
        Assert.Contains(setup.Db.AuditLogs, x => x.Action == "CompTimeConsumed");
        Assert.Contains(setup.Db.AuditLogs, x => x.Action == "CompTimeRestored");
    }

    [Fact]
    public async Task Two_Approved_Leaves_Consume_Opening_To_Zero()
    {
        await using var setup = await Setup.CreateAsync();
        await setup.AdminCompTime.CreateLegacyOpeningBalanceAsync(
            Opening(setup.Employee.Id, 16m));

        await setup.CreateSubmitAndApproveAsync(8m, 0);
        await setup.CreateSubmitAndApproveAsync(8m, 1);

        var balance = await setup.AdminCompTime.GetAdminBalanceAsync(setup.Employee.Id);
        Assert.Equal(0m, balance.AvailableHours);
        Assert.Equal(2, balance.Transactions.Count(x =>
            x.TransactionType == CompTimeTransactionType.Consume));
    }

    [Fact]
    public async Task Insufficient_Balance_Rolls_Back_Approval_History_And_Audit()
    {
        await using var setup = await Setup.CreateAsync();
        await setup.AdminCompTime.CreateLegacyOpeningBalanceAsync(
            Opening(setup.Employee.Id, 8m));
        var submitted = await setup.CreateAndSubmitAsync(8.5m);
        var historyBefore = await setup.Db.LeaveApprovalHistories.CountAsync();
        var auditBefore = await setup.Db.AuditLogs.CountAsync();

        await Assert.ThrowsAsync<ApplicationValidationException>(() =>
            setup.ManagerLeave.ApproveAsync(new()
            {
                Id = submitted.Id,
                RowVersion = submitted.RowVersion
            }));

        Assert.Equal(LeaveRequestStatus.Submitted,
            (await setup.Db.LeaveRequests.AsNoTracking()
                .SingleAsync(x => x.Id == submitted.Id)).Status);
        Assert.Equal(8m, (await setup.AdminCompTime.GetAdminBalanceAsync(
            setup.Employee.Id)).AvailableHours);
        Assert.Equal(historyBefore, await setup.Db.LeaveApprovalHistories.CountAsync());
        Assert.Equal(auditBefore, await setup.Db.AuditLogs.CountAsync());
    }

    [Fact]
    public async Task Duplicate_Approval_Does_Not_Duplicate_Consumption()
    {
        await using var setup = await Setup.CreateAsync();
        await setup.AdminCompTime.CreateLegacyOpeningBalanceAsync(
            Opening(setup.Employee.Id, 16m));
        var approved = await setup.CreateSubmitAndApproveAsync(8m);

        await Assert.ThrowsAsync<DomainValidationException>(() =>
            setup.ManagerLeave.ApproveAsync(new()
            {
                Id = approved.Id,
                RowVersion = approved.RowVersion
            }));

        Assert.Single(setup.Db.CompTimeTransactions.Where(x =>
            x.SourceId == approved.Id &&
            x.TransactionType == CompTimeTransactionType.Consume));
    }

    [Fact]
    public async Task Rejected_And_Non_Comp_Time_Leave_Do_Not_Change_Ledger()
    {
        await using var setup = await Setup.CreateAsync();
        await setup.AdminCompTime.CreateLegacyOpeningBalanceAsync(
            Opening(setup.Employee.Id, 16m));
        var submitted = await setup.CreateAndSubmitAsync(8m);
        await setup.ManagerLeave.RejectAsync(new()
        {
            Id = submitted.Id,
            RowVersion = submitted.RowVersion,
            Comment = "退回測試"
        });
        await setup.CreateSubmitAndApproveAsync(8m, 2, setup.PersonalType.Id);

        var balance = await setup.AdminCompTime.GetAdminBalanceAsync(setup.Employee.Id);
        Assert.Equal(16m, balance.AvailableHours);
        Assert.Single(balance.Transactions);
    }

    [Fact]
    public async Task Comp_Time_Request_Rejects_Quarter_Hour_Even_With_Balance()
    {
        await using var setup = await Setup.CreateAsync();
        await setup.AdminCompTime.CreateLegacyOpeningBalanceAsync(
            Opening(setup.Employee.Id, 16m));

        setup.Duration.Hours = 0.75m;
        await Assert.ThrowsAsync<DomainValidationException>(() =>
            setup.EmployeeLeave.CreateDraftAsync(setup.Draft(0.75m)));
        Assert.Empty(setup.Db.LeaveRequests);
    }

    private static CreateLegacyCompTimeOpeningBalanceRequest Opening(
        Guid employeeId,
        decimal hours) => new()
        {
            EmployeeId = employeeId,
            Hours = hours,
            CutoverDate = new DateOnly(2026, 7, 1),
            Reason = "HRSystem 上線前補休歷史期初餘額"
        };

    private sealed class Setup : IAsyncDisposable
    {
        public required HRSystemDbContext Db { get; init; }
        public required Employee Employee { get; init; }
        public required Employee Manager { get; init; }
        public required LeaveType CompType { get; init; }
        public required LeaveType PersonalType { get; init; }
        public required CompTimeService AdminCompTime { get; init; }
        public required CompTimeService EmployeeCompTime { get; init; }
        public required LeaveRequestService EmployeeLeave { get; init; }
        public required LeaveRequestService ManagerLeave { get; init; }
        public required RequestDurationCalculator Duration { get; init; }

        public static async Task<Setup> CreateAsync()
        {
            var db = TestDb.Create();
            var department = new Department(Guid.NewGuid(), "TEST", "測試部", Now);
            var employee = new Employee(Guid.NewGuid(), "EMP9001", "測試員工",
                department.Id, new DateOnly(2020, 1, 1), Now);
            var manager = new Employee(Guid.NewGuid(), "EMP9002", "測試主管",
                department.Id, new DateOnly(2020, 1, 1), Now);
            department.Update("TEST", "測試部", manager.Id, Now);
            var compType = new LeaveType(
                Guid.NewGuid(),
                CompTimePolicy.LeaveTypeCode,
                CompTimePolicy.LeaveTypeName,
                LeaveUnit.Hour,
                0.5m,
                true,
                true,
                105,
                Now);
            var personal = new LeaveType(Guid.NewGuid(), "PERSONAL", "事假",
                LeaveUnit.Hour, 0.5m, true, false, 10, Now);
            db.AddRange(department, employee, manager, compType, personal);
            await db.SaveChangesAsync();
            var time = new FixedTimeProvider(Now);
            var employeeUser = new TestCurrentUser(RoleNames.Employee, employee.Id);
            var managerUser = new TestCurrentUser(RoleNames.Manager, manager.Id);
            var adminCompTime = new CompTimeService(
                db, new TestCurrentUser(RoleNames.Admin), time);
            var employeeCompTime = new CompTimeService(db, employeeUser, time);
            var duration = new RequestDurationCalculator();
            return new Setup
            {
                Db = db,
                Employee = employee,
                Manager = manager,
                CompType = compType,
                PersonalType = personal,
                AdminCompTime = adminCompTime,
                EmployeeCompTime = employeeCompTime,
                Duration = duration,
                EmployeeLeave = new LeaveRequestService(
                    db, employeeUser, duration, time, new NoOpRecalculation()),
                ManagerLeave = new LeaveRequestService(
                    db, managerUser, duration, time, new NoOpRecalculation())
            };
        }

        public CreateLeaveDraftRequest Draft(decimal hours, int dayOffset = 0,
            Guid? leaveTypeId = null) => new()
        {
            LeaveTypeId = leaveTypeId ?? CompType.Id,
            StartAt = Start.AddDays(dayOffset),
            EndAt = End.AddDays(dayOffset),
            Reason = $"測試補休 {hours:0.##}",
            CalendarStartDate = null,
            CalendarEndDate = null
        };

        public async Task<LeaveRequestDto> CreateAndSubmitAsync(
            decimal hours,
            int dayOffset = 0,
            Guid? leaveTypeId = null)
        {
            Duration.Hours = hours;
            var draft = await EmployeeLeave.CreateDraftAsync(
                Draft(hours, dayOffset, leaveTypeId));
            return await EmployeeLeave.SubmitAsync(new()
            {
                Id = draft.Id,
                RowVersion = draft.RowVersion
            });
        }

        public async Task<LeaveRequestDto> CreateSubmitAndApproveAsync(
            decimal hours,
            int dayOffset = 0,
            Guid? leaveTypeId = null)
        {
            var submitted = await CreateAndSubmitAsync(hours, dayOffset, leaveTypeId);
            return await ManagerLeave.ApproveAsync(new()
            {
                Id = submitted.Id,
                RowVersion = submitted.RowVersion
            });
        }

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private sealed class RequestDurationCalculator : ILeaveDurationCalculator
    {
        public decimal Hours { get; set; } = 8m;
        public Task<LeaveDurationEstimateDto> CalculateAsync(
            Guid employeeId,
            DateTimeOffset startAt,
            DateTimeOffset endAt,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new LeaveDurationEstimateDto(Hours, true, []));
        public Task<LeaveDurationEstimateDto> CalculateAsync(
            Guid employeeId,
            DateTimeOffset startAt,
            DateTimeOffset endAt,
            LeaveCalculationMode calculationMode,
            CancellationToken cancellationToken = default) =>
            CalculateAsync(employeeId, startAt, endAt, cancellationToken);
    }

    private sealed class NoOpRecalculation : IAttendanceRecalculationEngine
    {
        public Task<AttendanceRecalculationOutcome> RecalculateRangeAsync(
            DateOnly dateFrom,
            DateOnly dateTo,
            Guid? employeeId = null,
            IReadOnlyCollection<PendingApprovedLeave>? pendingApprovedLeaves = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new AttendanceRecalculationOutcome(
                dateFrom, dateTo, employeeId.HasValue ? 1 : 0, 0, Now));

        public Task<AttendanceRecalculationOutcome> RecalculateKeysAsync(
            IReadOnlyCollection<AttendanceRecalculationKey> keys,
            CancellationToken cancellationToken = default,
            IReadOnlyCollection<Guid>? excludedApprovedLeaveRequestIds = null) =>
            Task.FromResult(new AttendanceRecalculationOutcome(
                keys.Min(x => x.WorkDate),
                keys.Max(x => x.WorkDate),
                keys.Select(x => x.EmployeeId).Distinct().Count(),
                0,
                Now));
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
