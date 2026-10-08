using HRSystem.Application.Abstractions.Security;
using HRSystem.Application.Common.Auditing;
using HRSystem.Application.Overtime;
using HRSystem.Application.Security;
using HRSystem.Domain.MasterData;
using HRSystem.Domain.Overtime;
using Microsoft.EntityFrameworkCore;

namespace HRSystem.IntegrationTests;

[Collection(DisposableSqlServerCollection.Name)]
public sealed class OvertimeRequestSqlIntegrationTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 21, 2, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Employee_Submit_And_Admin_Approve_Persist_Atomic_Lifecycle()
    {
        await using var database = await DisposableSqlServerDatabase.CreateAsync(
            "OvertimeWorkflowLifecycle");
        var employeeId = await SeedEmployeeAsync(database);
        await using var db = database.CreateDbContext();
        var employee = Service(db, RoleNames.Employee, employeeId, "employee");
        var admin = Service(db, RoleNames.Admin, null, "admin");

        var draft = await employee.CreateDraftAsync(Draft());
        var submitted = await employee.SubmitAsync(new()
            { Id = draft.Id, RowVersion = draft.RowVersion });
        var approved = await admin.ApproveAsync(new()
        {
            Id = submitted.Id,
            RowVersion = submitted.RowVersion,
            Reason = OvertimeReviewReason.ApprovedAsRequested
        });

        Assert.Equal(OvertimeRequestStatus.Approved, approved.Status);
        Assert.Equal(3, await db.OvertimeRequestHistories.CountAsync());
        Assert.Contains(await db.AuditLogs.Select(item => item.Action).ToListAsync(),
            item => item == AuditActions.OvertimeRequestApproved);
        Assert.DoesNotContain("SQL workflow reason", string.Join('\n',
            await db.AuditLogs.Select(item => item.NewValuesJson).ToListAsync()),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Database_Side_Overlap_Rejects_Submitted_But_Not_Withdrawn()
    {
        await using var database = await DisposableSqlServerDatabase.CreateAsync(
            "OvertimeWorkflowOverlap");
        var employeeId = await SeedEmployeeAsync(database);
        await using var db = database.CreateDbContext();
        var service = Service(db, RoleNames.Employee, employeeId, "employee");
        var first = await service.CreateDraftAsync(Draft());
        var submitted = await service.SubmitAsync(new()
            { Id = first.Id, RowVersion = first.RowVersion });
        var overlap = await service.CreateDraftAsync(Draft(19));

        await Assert.ThrowsAsync<HRSystem.Application.Common.Exceptions.ApplicationValidationException>(
            () => service.SubmitAsync(new()
                { Id = overlap.Id, RowVersion = overlap.RowVersion }));

        await service.WithdrawAsync(new()
            { Id = submitted.Id, RowVersion = submitted.RowVersion });
        var replacement = await service.SubmitAsync(new()
            { Id = overlap.Id, RowVersion = overlap.RowVersion });
        Assert.Equal(OvertimeRequestStatus.Submitted, replacement.Status);
    }

    [Fact]
    public async Task Concurrent_Admin_Approve_Reject_Allows_Exactly_One_Transition()
    {
        await using var database = await DisposableSqlServerDatabase.CreateAsync(
            "OvertimeWorkflowConcurrency");
        var employeeId = await SeedEmployeeAsync(database);
        Guid requestId;
        string rowVersion;
        await using (var seedDb = database.CreateDbContext())
        {
            var employee = Service(seedDb, RoleNames.Employee, employeeId, "employee");
            var draft = await employee.CreateDraftAsync(Draft());
            var submitted = await employee.SubmitAsync(new()
                { Id = draft.Id, RowVersion = draft.RowVersion });
            requestId = submitted.Id;
            rowVersion = submitted.RowVersion;
        }

        await using var firstDb = database.CreateDbContext();
        await using var secondDb = database.CreateDbContext();
        var approve = Attempt(Service(firstDb, RoleNames.Admin, null, "admin-a")
            .ApproveAsync(new()
            {
                Id = requestId,
                RowVersion = rowVersion,
                Reason = OvertimeReviewReason.ApprovedAsRequested
            }));
        var reject = Attempt(Service(secondDb, RoleNames.Admin, null, "admin-b")
            .RejectAsync(new()
            {
                Id = requestId,
                RowVersion = rowVersion,
                Reason = OvertimeReviewReason.BusinessNeedNotConfirmed
            }));

        var outcomes = await Task.WhenAll(approve, reject);
        Assert.Single(outcomes, item => item);
        await using var verify = database.CreateDbContext();
        Assert.Contains((await verify.OvertimeRequests.SingleAsync()).Status,
            new[] { OvertimeRequestStatus.Approved, OvertimeRequestStatus.Rejected });
        Assert.Equal(3, await verify.OvertimeRequestHistories.CountAsync());
    }

    private static async Task<Guid> SeedEmployeeAsync(
        DisposableSqlServerDatabase database)
    {
        await using var db = database.CreateDbContext();
        var department = new Department(Guid.NewGuid(), "OTSQL", "SQL 加班測試部", Now);
        var employee = new Employee(Guid.NewGuid(), "EMP-SQL-G", "SQL 測試員工",
            department.Id, new DateOnly(2025, 1, 1), Now);
        db.AddRange(department, employee);
        await db.SaveChangesAsync();
        return employee.Id;
    }

    private static OvertimeRequestService Service(
        HRSystem.Infrastructure.Persistence.HRSystemDbContext db,
        string role, Guid? employeeId, string userId) =>
        new(db, new CurrentUser(role, employeeId, userId), new FixedTime(Now));

    private static CreateOvertimeDraftRequest Draft(int hour = 18) => new()
    {
        PlannedStartAt = DateTime.SpecifyKind(
            new DateTime(2026, 8, 21, hour, 0, 0), DateTimeKind.Unspecified),
        PlannedEndAt = DateTime.SpecifyKind(
            new DateTime(2026, 8, 21, hour + 2, 0, 0), DateTimeKind.Unspecified),
        Reason = "SQL workflow reason"
    };

    private static async Task<bool> Attempt(Task<OvertimeRequestDto> operation)
    {
        try { await operation; return true; }
        catch { return false; }
    }

    private sealed class CurrentUser(string role, Guid? employeeId, string userId) :
        ICurrentUser
    {
        public string? UserId => userId;
        public Guid? EmployeeId => employeeId;
        public string? DisplayName => userId;
        public string? IpAddress => "127.0.0.1";
        public bool IsAuthenticated => true;
        public bool IsInRole(string candidate) => candidate == role;
        public bool HasPermission(string policy) =>
            RolePermissions.HasPermission([role], policy);
    }
    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    { public override DateTimeOffset GetUtcNow() => now; }
}
