using HRSystem.Application.Abstractions.Security;
using HRSystem.Application.Attendance;
using HRSystem.Application.ParentalLeave;
using HRSystem.Application.Security;
using HRSystem.Domain.MasterData;
using HRSystem.Domain.ParentalLeave;
using Microsoft.EntityFrameworkCore;

namespace HRSystem.IntegrationTests;

[Collection(DisposableSqlServerCollection.Name)]
public sealed class ParentalLeaveTransactionSqlIntegrationTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 20, 2, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Cancellation_Approval_Rolls_Back_When_Recalculation_Fails()
    {
        await using var database = await DisposableSqlServerDatabase.CreateAsync(
            "ParentalLeaveCancellationRollback");
        var requestId = Guid.NewGuid();
        string rowVersion;

        await using (var db = database.CreateDbContext())
        {
            var department = new Department(
                Guid.NewGuid(),
                "PLRB",
                "育嬰交易測試部",
                Now);
            var employee = new Employee(
                Guid.NewGuid(),
                "EMP9901",
                "育嬰交易測試員工",
                department.Id,
                new DateOnly(2025, 1, 1),
                Now);
            var request = new ParentalLeaveRequest(
                requestId,
                "PL-ROLLBACK-001",
                employee.Id,
                Guid.NewGuid(),
                new DateOnly(2025, 1, 1),
                new DateOnly(2026, 9, 1),
                new DateOnly(2026, 9, 1),
                "測試地址",
                "0900000000",
                true,
                ParentalLeaveNoticeType.Standard,
                null,
                null,
                null,
                "employee",
                Now);
            request.Submit(new DateOnly(2026, 8, 20), "employee", Now);
            request.Approve("manager", Now);
            request.RequestCancellation("交易回滾測試", "employee", Now);
            db.AddRange(department, employee, request);
            await db.SaveChangesAsync();
            rowVersion = Convert.ToBase64String(request.RowVersion);

            var service = new ParentalLeaveService(
                db,
                new AdminCurrentUser(),
                new FailingRecalculationEngine(),
                new FixedTimeProvider(Now));

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.ApproveCancellationAsync(new()
                {
                    Id = requestId,
                    RowVersion = rowVersion,
                    Comment = "應完整回滾"
                }));
        }

        await using var verification = database.CreateDbContext();
        var persisted = await verification.ParentalLeaveRequests
            .AsNoTracking()
            .SingleAsync(item => item.Id == requestId);
        Assert.Equal(ParentalLeaveStatus.CancellationRequested, persisted.Status);
        Assert.Equal(0, await verification.ParentalLeaveApprovalHistories
            .CountAsync(item => item.ParentalLeaveRequestId == requestId));
        Assert.Equal(0, await verification.AuditLogs.CountAsync(item =>
            item.EntityId == requestId.ToString()));
    }

    private sealed class FailingRecalculationEngine : IAttendanceRecalculationEngine
    {
        public Task<AttendanceRecalculationOutcome> RecalculateRangeAsync(
            DateOnly dateFrom,
            DateOnly dateTo,
            Guid? employeeId = null,
            IReadOnlyCollection<PendingApprovedLeave>? pendingApprovedLeaves = null,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Controlled recalculation failure.");

        public Task<AttendanceRecalculationOutcome> RecalculateKeysAsync(
            IReadOnlyCollection<AttendanceRecalculationKey> keys,
            CancellationToken cancellationToken = default,
            IReadOnlyCollection<Guid>? excludedApprovedLeaveRequestIds = null) =>
            throw new InvalidOperationException("Controlled recalculation failure.");

        public Task<AttendanceRecalculationOutcome>
            RecalculateKeysWithEmploymentSuspensionsAsync(
            IReadOnlyCollection<AttendanceRecalculationKey> keys,
            IReadOnlyCollection<PendingEmploymentSuspension>?
                pendingEmploymentSuspensions = null,
            IReadOnlyCollection<Guid>?
                excludedParentalLeaveRequestIds = null,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Controlled recalculation failure.");
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class AdminCurrentUser : ICurrentUser
    {
        public string? UserId => "phase-c-test-admin";
        public Guid? EmployeeId => null;
        public string? DisplayName => "Phase C Test Admin";
        public string? IpAddress => "127.0.0.1";
        public bool IsAuthenticated => true;
        public bool IsInRole(string role) => role == RoleNames.Admin;
        public bool HasPermission(string policy) =>
            RolePermissions.HasPermission([RoleNames.Admin], policy);
    }
}
