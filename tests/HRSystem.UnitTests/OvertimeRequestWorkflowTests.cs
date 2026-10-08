using HRSystem.Application.Abstractions.Security;
using HRSystem.Application.Common.Auditing;
using HRSystem.Application.Common.Exceptions;
using HRSystem.Application.Overtime;
using HRSystem.Application.Security;
using HRSystem.Domain.Common;
using HRSystem.Domain.MasterData;
using HRSystem.Domain.Overtime;
using HRSystem.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HRSystem.UnitTests;

public sealed class OvertimeRequestWorkflowTests
{
    [Fact]
    public void Domain_Supports_Cross_Midnight_And_Derives_Minutes()
    {
        var request = NewDomain(new DateTime(2026, 8, 21, 22, 0, 0),
            new DateTime(2026, 8, 22, 1, 0, 0));

        Assert.Equal(new DateOnly(2026, 8, 21), request.OvertimeDate);
        Assert.Equal(180, request.RequestedMinutes);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(721)]
    public void Domain_Rejects_Invalid_Duration(int minutes)
    {
        var start = new DateTime(2026, 8, 21, 18, 0, 0);
        Assert.Throws<DomainValidationException>(() =>
            NewDomain(start, start.AddMinutes(minutes)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Domain_Rejects_Empty_Reason(string reason)
    {
        Assert.Throws<DomainValidationException>(() => new OvertimeRequest(
            Guid.NewGuid(), Guid.NewGuid(), Local(18), Local(20), reason,
            "employee", Now));
    }

    [Fact]
    public void Lifecycle_Enforces_Draft_Submitted_Approved()
    {
        var entity = NewDomain(Local(18), Local(20));
        entity.Submit("employee", Now);
        entity.Approve(OvertimeReviewReason.ApprovedAsRequested, null,
            "admin", Now.AddMinutes(1));

        Assert.Equal(OvertimeRequestStatus.Approved, entity.Status);
        Assert.Throws<DomainValidationException>(() =>
            entity.Withdraw(null, "employee", Now));
    }

    [Fact]
    public void Rejection_Requires_Structured_Reason_And_Other_Note()
    {
        var entity = NewDomain(Local(18), Local(20));
        entity.Submit("employee", Now);
        Assert.Throws<DomainValidationException>(() => entity.Reject(
            OvertimeReviewReason.Other, " ", "admin", Now));
        entity.Reject(OvertimeReviewReason.TimeRangeIncorrect, null,
            "admin", Now);
        Assert.Equal(OvertimeRequestStatus.Rejected, entity.Status);
    }

    [Fact]
    public async Task Employee_Creates_Only_Own_Request_And_Sees_Only_Own_Data()
    {
        await using var setup = await Setup.CreateAsync();
        var own = await setup.Employee.CreateDraftAsync(NewDraft());
        await setup.Other.CreateDraftAsync(NewDraft(21));

        var result = await setup.Employee.GetMyRequestsAsync(new());

        Assert.Equal(setup.EmployeeEntity.Id, own.EmployeeId);
        Assert.Single(result);
        Assert.Equal(own.Id, result[0].Id);
    }

    [Fact]
    public async Task User_Without_Employee_Binding_Cannot_Use_Self_Service()
    {
        await using var setup = await Setup.CreateAsync();
        await Assert.ThrowsAsync<ForbiddenAccessException>(() =>
            setup.AdminWithoutEmployee.CreateDraftAsync(NewDraft()));
    }

    [Fact]
    public async Task Admin_Cannot_Proxy_Create_Normal_Request()
    {
        await using var setup = await Setup.CreateAsync();
        var own = await setup.AdminBoundToOther.CreateDraftAsync(NewDraft());
        Assert.Equal(setup.OtherEntity.Id, own.EmployeeId);
        Assert.NotEqual(setup.EmployeeEntity.Id, own.EmployeeId);
    }

    [Fact]
    public async Task Submitted_Request_Cannot_Be_Updated()
    {
        await using var setup = await Setup.CreateAsync();
        var submitted = await setup.CreateSubmittedAsync();
        await Assert.ThrowsAsync<DomainValidationException>(() =>
            setup.Employee.UpdateDraftAsync(new()
            {
                Id = submitted.Id,
                RowVersion = submitted.RowVersion,
                PlannedStartAt = Local(19),
                PlannedEndAt = Local(21),
                Reason = "修改"
            }));
    }

    [Fact]
    public async Task Submitted_Overlap_Is_Rejected_But_Rejected_Does_Not_Block()
    {
        await using var setup = await Setup.CreateAsync();
        var first = await setup.CreateSubmittedAsync();
        var rejected = await setup.Admin.RejectAsync(new()
        {
            Id = first.Id,
            RowVersion = first.RowVersion,
            Reason = OvertimeReviewReason.DuplicateRequest
        });
        Assert.Equal(OvertimeRequestStatus.Rejected, rejected.Status);
        var second = await setup.Employee.CreateDraftAsync(NewDraft(19));
        var submitted = await setup.Employee.SubmitAsync(new()
            { Id = second.Id, RowVersion = second.RowVersion });
        Assert.Equal(OvertimeRequestStatus.Submitted, submitted.Status);

        var overlap = await setup.Employee.CreateDraftAsync(NewDraft(20));
        await Assert.ThrowsAsync<ApplicationValidationException>(() =>
            setup.Employee.SubmitAsync(new()
                { Id = overlap.Id, RowVersion = overlap.RowVersion }));
    }

    [Fact]
    public async Task Withdrawn_Request_Does_Not_Block_Replacement()
    {
        await using var setup = await Setup.CreateAsync();
        var first = await setup.CreateSubmittedAsync();
        await setup.Employee.WithdrawAsync(new()
            { Id = first.Id, RowVersion = first.RowVersion });
        var replacement = await setup.Employee.CreateDraftAsync(NewDraft());
        var submitted = await setup.Employee.SubmitAsync(new()
            { Id = replacement.Id, RowVersion = replacement.RowVersion });
        Assert.Equal(OvertimeRequestStatus.Submitted, submitted.Status);
    }

    [Fact]
    public async Task Only_Admin_Can_Review_And_History_Is_Append_Only()
    {
        await using var setup = await Setup.CreateAsync();
        var submitted = await setup.CreateSubmittedAsync();
        await Assert.ThrowsAsync<ForbiddenAccessException>(() =>
            setup.Manager.SearchForReviewAsync(new()));
        var approved = await setup.Admin.ApproveAsync(new()
        {
            Id = submitted.Id,
            RowVersion = submitted.RowVersion,
            Reason = OvertimeReviewReason.ApprovedAsRequested
        });
        Assert.Equal(3, approved.Histories.Count);
        var history = await setup.Db.OvertimeRequestHistories.FirstAsync();
        setup.Db.Remove(history);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            setup.Db.SaveChangesAsync());
    }

    [Fact]
    public async Task Audit_Uses_Safe_Snapshot_Without_Free_Text_Reason()
    {
        await using var setup = await Setup.CreateAsync();
        await setup.Employee.CreateDraftAsync(NewDraft(reason: "敏感自由文字原因"));
        var json = string.Join('\n', await setup.Db.AuditLogs
            .Select(item => item.NewValuesJson).ToListAsync());
        Assert.Contains(AuditActions.OvertimeRequestCreated,
            await setup.Db.AuditLogs.Select(item => item.Action).SingleAsync());
        Assert.DoesNotContain("敏感自由文字原因", json, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(RoleNames.Admin, true)]
    [InlineData(RoleNames.Manager, false)]
    [InlineData(RoleNames.Employee, false)]
    public void Overtime_Manage_Is_Admin_Only(string role, bool expected) =>
        Assert.Equal(expected,
            RolePermissions.HasPermission([role], PolicyNames.OvertimeManage));

    private static readonly DateTimeOffset Now =
        new(2026, 8, 21, 2, 0, 0, TimeSpan.Zero);
    private static DateTime Local(int hour) =>
        DateTime.SpecifyKind(new DateTime(2026, 8, 21, hour, 0, 0),
            DateTimeKind.Unspecified);
    private static OvertimeRequest NewDomain(DateTime start, DateTime end,
        string reason = "測試加班") => new(Guid.NewGuid(), Guid.NewGuid(),
            start, end, reason, "employee", Now);
    private static CreateOvertimeDraftRequest NewDraft(int hour = 18,
        string reason = "測試加班") => new()
        { PlannedStartAt = Local(hour), PlannedEndAt = Local(hour + 2), Reason = reason };

    private sealed class Setup : IAsyncDisposable
    {
        private Setup(HRSystemDbContext db, Employee employee, Employee other)
        {
            Db = db; EmployeeEntity = employee; OtherEntity = other;
            Employee = Service(RoleNames.Employee, employee.Id, "employee");
            Other = Service(RoleNames.Employee, other.Id, "other");
            Admin = Service(RoleNames.Admin, null, "admin");
            AdminWithoutEmployee = Admin;
            AdminBoundToOther = Service(RoleNames.Admin, other.Id, "admin-own");
            Manager = Service(RoleNames.Manager, null, "manager");
        }
        public HRSystemDbContext Db { get; }
        public Employee EmployeeEntity { get; }
        public Employee OtherEntity { get; }
        public OvertimeRequestService Employee { get; }
        public OvertimeRequestService Other { get; }
        public OvertimeRequestService Admin { get; }
        public OvertimeRequestService AdminWithoutEmployee { get; }
        public OvertimeRequestService AdminBoundToOther { get; }
        public OvertimeRequestService Manager { get; }
        public static async Task<Setup> CreateAsync()
        {
            var db = new HRSystemDbContext(new DbContextOptionsBuilder<HRSystemDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            var department = new Department(Guid.NewGuid(), "OT", "加班測試部", Now);
            var employee = new Employee(Guid.NewGuid(), "EMP-G001", "測試員工",
                department.Id, new DateOnly(2025, 1, 1), Now);
            var other = new Employee(Guid.NewGuid(), "EMP-G002", "其他員工",
                department.Id, new DateOnly(2025, 1, 1), Now);
            db.AddRange(department, employee, other);
            await db.SaveChangesAsync();
            return new Setup(db, employee, other);
        }
        public async Task<OvertimeRequestDto> CreateSubmittedAsync()
        {
            var draft = await Employee.CreateDraftAsync(NewDraft());
            return await Employee.SubmitAsync(new()
                { Id = draft.Id, RowVersion = draft.RowVersion });
        }
        private OvertimeRequestService Service(string role, Guid? employeeId, string id) =>
            new(Db, new User(role, employeeId, id), new FixedTimeProvider(Now));
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private sealed class User(string role, Guid? employeeId, string id) : ICurrentUser
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
    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    { public override DateTimeOffset GetUtcNow() => now; }
}
