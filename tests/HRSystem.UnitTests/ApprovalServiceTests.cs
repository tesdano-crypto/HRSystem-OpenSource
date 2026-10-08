using System.Security.Cryptography;
using HRSystem.Application.Abstractions.Security;
using HRSystem.Application.Approvals;
using HRSystem.Application.Common.Exceptions;
using HRSystem.Application.Security;
using HRSystem.Domain.Approvals;
using HRSystem.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HRSystem.UnitTests;

public sealed class ApprovalServiceTests
{
    [Fact]
    public async Task Submit_Commits_Approval_When_Line_Notification_Fails()
    {
        await using var setup = Setup.Create(RoleNames.Accounting,
            sender: new ThrowingSender());
        setup.Db.LineUserBindings.Add(new LineUserBinding(Guid.NewGuid(), "owner",
            "U-owner", Setup.Now, Setup.Now));
        await setup.Db.SaveChangesAsync();

        var result = await setup.Service.SubmitAsync(new()
        {
            ApprovalType = ApprovalType.Payroll,
            SourceId = setup.Source.SourceId,
            ApproverUserId = "owner"
        });

        Assert.Equal(ApprovalStatus.Pending, result.Status);
        Assert.Equal(ApprovalNotificationStatus.Failed, result.NotificationStatus);
        Assert.Single(setup.Db.Approvals);
        Assert.Contains(setup.Db.ApprovalHistories,
            x => x.Action == ApprovalHistoryAction.NotificationFailed);
        Assert.DoesNotContain("secret", result.NotificationErrorSummary ?? string.Empty,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Missing_Binding_Fails_Safely_And_Retry_Does_Not_Duplicate_Approval()
    {
        await using var setup = Setup.Create(RoleNames.Accounting);
        var approval = await setup.Service.SubmitAsync(new()
        {
            ApprovalType = ApprovalType.Payroll,
            SourceId = setup.Source.SourceId,
            ApproverUserId = "owner"
        });

        Assert.Equal(ApprovalStatus.Pending, approval.Status);
        Assert.Equal(ApprovalNotificationStatus.Failed, approval.NotificationStatus);
        Assert.Contains("尚未完成 LINE 私人帳號綁定", approval.NotificationErrorSummary,
            StringComparison.Ordinal);

        setup.Db.LineUserBindings.Add(new LineUserBinding(Guid.NewGuid(), "owner",
            "U-owner", Setup.Now, Setup.Now));
        await setup.Db.SaveChangesAsync();
        var retried = await setup.Service.RetryNotificationAsync(approval.Id);

        Assert.Equal(approval.Id, retried.Id);
        Assert.Single(setup.Db.Approvals);
        Assert.Equal(ApprovalNotificationStatus.Sent, retried.NotificationStatus);
        Assert.Contains(retried.History,
            x => x.Action == ApprovalHistoryAction.NotificationFailed);
        Assert.Contains(retried.History,
            x => x.Action == ApprovalHistoryAction.NotificationSent);
    }

    [Fact]
    public async Task Duplicate_Same_Source_Returns_One_Pending_Approval()
    {
        var sender = new CapturingSender();
        await using var setup = Setup.Create(RoleNames.Accounting, sender);
        setup.Db.LineUserBindings.Add(new LineUserBinding(Guid.NewGuid(), "owner",
            "U-owner", Setup.Now, Setup.Now));
        await setup.Db.SaveChangesAsync();
        var request = new SubmitApprovalRequest { ApprovalType = ApprovalType.Payroll,
            SourceId = setup.Source.SourceId, ApproverUserId = "owner" };

        var first = await setup.Service.SubmitAsync(request);
        var second = await setup.Service.SubmitAsync(request);

        Assert.Equal(first.Id, second.Id);
        Assert.Single(setup.Db.Approvals);
        Assert.Equal(1, sender.SendCalls);
    }

    [Fact]
    public async Task Changed_Source_Supersedes_Old_Pending_And_Creates_New()
    {
        await using var setup = Setup.Create(RoleNames.Accounting);
        var request = new SubmitApprovalRequest { ApprovalType = ApprovalType.Payroll,
            SourceId = setup.Source.SourceId, ApproverUserId = "owner" };
        var first = await setup.Service.SubmitAsync(request);
        setup.Source.ChangeFingerprint();

        var second = await setup.Service.SubmitAsync(request);

        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal(2, setup.Db.Approvals.Count());
        Assert.Equal(ApprovalStatus.Superseded,
            (await setup.Db.Approvals.FindAsync(first.Id))!.Status);
    }

    [Fact]
    public async Task Accounting_Cannot_Approve_And_Owner_Cannot_Submit()
    {
        await using var accounting = Setup.Create(RoleNames.Accounting);
        var approval = await accounting.Service.SubmitAsync(new()
        {
            ApprovalType = ApprovalType.Payroll,
            SourceId = accounting.Source.SourceId,
            ApproverUserId = "owner"
        });
        await Assert.ThrowsAsync<ForbiddenAccessException>(() =>
            accounting.Service.ApproveAsync(new() { ApprovalId = approval.Id }));

        await using var owner = Setup.Create(RoleNames.Owner);
        await Assert.ThrowsAsync<ForbiddenAccessException>(() =>
            owner.Service.SubmitAsync(new() { ApprovalType = ApprovalType.Payroll,
                SourceId = owner.Source.SourceId, ApproverUserId = "owner" }));
    }

    [Fact]
    public async Task Decision_Rejects_Changed_Source_Without_Approving()
    {
        await using var setup = Setup.Create(RoleNames.Admin);
        var approval = await setup.Service.SubmitAsync(new()
        {
            ApprovalType = ApprovalType.Payroll,
            SourceId = setup.Source.SourceId,
            ApproverUserId = "owner"
        });
        setup.Source.ChangeFingerprint();

        var result = await setup.Service.ApproveAsync(new() { ApprovalId = approval.Id });

        Assert.Equal(ApprovalStatus.Superseded, result.Status);
    }

    [Fact]
    public async Task Employee_Cannot_View_Approval_Center()
    {
        await using var setup = Setup.Create(RoleNames.Employee);
        await Assert.ThrowsAsync<ForbiddenAccessException>(() =>
            setup.Service.GetListAsync());
    }

    [Fact]
    public async Task Line_Approve_Is_Private_One_Time_And_Audited()
    {
        var sender = new CapturingSender();
        await using var setup = Setup.Create(RoleNames.Accounting, sender);
        setup.Db.LineUserBindings.Add(new LineUserBinding(Guid.NewGuid(), "owner",
            "U-owner", Setup.Now, Setup.Now));
        await setup.Db.SaveChangesAsync();
        var approval = await setup.Service.SubmitAsync(new()
        {
            ApprovalType = ApprovalType.Payroll,
            SourceId = setup.Source.SourceId,
            ApproverUserId = "owner"
        });

        var wrongUser = await setup.Service.HandleLinePostbackAsync(new(
            "U-other", sender.Notification!.ApproveToken, null));
        Assert.False(wrongUser.Succeeded);
        var success = await setup.Service.HandleLinePostbackAsync(new(
            "U-owner", sender.Notification.ApproveToken, null));
        var replay = await setup.Service.HandleLinePostbackAsync(new(
            "U-owner", sender.Notification.ApproveToken, null));

        Assert.True(success.Succeeded);
        Assert.Equal(ApprovalStatus.Approved, success.Status);
        Assert.False(replay.Succeeded);
        Assert.Equal(ApprovalStatus.Approved,
            (await setup.Db.Approvals.FindAsync(approval.Id))!.Status);
        Assert.Contains(setup.Db.ApprovalHistories,
            x => x.Action == ApprovalHistoryAction.Approved &&
                x.Channel == ApprovalChannel.Line);
        var audit = Assert.Single(setup.Db.AuditLogs,
            x => x.Action == HRSystem.Application.Common.Auditing.AuditActions.ApprovalApproved);
        Assert.Equal("owner", audit.UserId);
        Assert.DoesNotContain(sender.Notification.ApproveToken,
            audit.NewValuesJson ?? string.Empty, StringComparison.Ordinal);
        Assert.DoesNotContain("U-owner", audit.NewValuesJson ?? string.Empty,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Line_Return_Requires_Reason_And_Does_Not_Consume_On_Validation_Failure()
    {
        var sender = new CapturingSender();
        await using var setup = Setup.Create(RoleNames.Accounting, sender);
        setup.Db.LineUserBindings.Add(new LineUserBinding(Guid.NewGuid(), "owner",
            "U-owner", Setup.Now, Setup.Now));
        await setup.Db.SaveChangesAsync();
        await setup.Service.SubmitAsync(new() { ApprovalType = ApprovalType.Payroll,
            SourceId = setup.Source.SourceId, ApproverUserId = "owner" });

        var missing = await setup.Service.HandleLinePostbackAsync(new(
            "U-owner", sender.Notification!.ReturnToken, "   "));
        var success = await setup.Service.HandleLinePostbackAsync(new(
            "U-owner", sender.Notification.ReturnToken, "  請補正  "));

        Assert.False(missing.Succeeded);
        Assert.True(success.Succeeded);
        Assert.Equal(ApprovalStatus.Returned, success.Status);
    }

    [Fact]
    public async Task Admin_Web_Approval_Appends_History_And_Revokes_Line_Tokens()
    {
        var sender = new CapturingSender();
        await using var setup = Setup.Create(RoleNames.Admin, sender);
        setup.Db.LineUserBindings.Add(new LineUserBinding(Guid.NewGuid(), "owner",
            "U-owner", Setup.Now, Setup.Now));
        await setup.Db.SaveChangesAsync();
        var approval = await setup.Service.SubmitAsync(new()
        {
            ApprovalType = ApprovalType.Payroll,
            SourceId = setup.Source.SourceId,
            ApproverUserId = "owner"
        });

        var result = await setup.Service.ApproveAsync(new() { ApprovalId = approval.Id });

        Assert.Equal(ApprovalStatus.Approved, result.Status);
        Assert.Contains(result.History, x => x.Action == ApprovalHistoryAction.Approved);
        Assert.All(setup.Db.ApprovalLineActionTokens,
            x => Assert.NotNull(x.RevokedAtUtc));
    }

    private sealed class Setup : IAsyncDisposable
    {
        public static readonly DateTimeOffset Now =
            new(2026, 8, 28, 2, 0, 0, TimeSpan.Zero);
        private Setup(HRSystemDbContext db, FakeSource source, ApprovalService service)
        { Db = db; Source = source; Service = service; }
        public HRSystemDbContext Db { get; }
        public FakeSource Source { get; }
        public ApprovalService Service { get; }

        public static Setup Create(string role, IApprovalPrivateNotificationSender? sender = null)
        {
            var db = TestDb.Create();
            var source = new FakeSource();
            var userId = role == RoleNames.Owner ? "owner" : "unit-test-user";
            var current = new RoleCurrentUser(role, userId);
            var service = new ApprovalService(db, current, [source],
                new FakeDirectory(), new LinePrivateTargetResolver(db),
                sender ?? new SuccessSender(),
                new FixedTimeProvider(Now));
            return new(db, source, service);
        }
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private sealed class FakeSource : IApprovalSourceProvider
    {
        private byte _version = 1;
        public Guid SourceId { get; } = Guid.NewGuid();
        public ApprovalType ApprovalType => ApprovalType.Payroll;
        public void ChangeFingerprint() => _version++;
        public Task<ApprovalSourceSnapshot> GetSnapshotAsync(Guid sourceId,
            CancellationToken cancellationToken = default) => Task.FromResult(new ApprovalSourceSnapshot(
                ApprovalType.Payroll, "PayrollRun", sourceId.ToString(), 1,
                SHA256.HashData([_version]), "2026 年 08 月薪資待核准",
                [new("員工人數", "1")]));
    }

    private sealed class FakeDirectory : IApprovalActorDirectory
    {
        public Task<ApprovalActorDto?> FindActiveAsync(string userId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<ApprovalActorDto?>(userId == "owner" ? new("owner", "老闆") : null);
        public Task<bool> HasPermissionAsync(string userId, string permission,
            CancellationToken cancellationToken = default) => Task.FromResult(
                userId == "owner" && (permission == PolicyNames.ApprovalAct ||
                    permission == PolicyNames.PayrollApprove));
        public Task<IReadOnlyList<ApprovalActorDto>> GetEligibleApproversAsync(
            ApprovalType approvalType, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ApprovalActorDto>>([new("owner", "老闆")]);
    }

    private sealed class SuccessSender : IApprovalPrivateNotificationSender
    {
        public Task SendAsync(ApprovalPrivateNotification notification,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class ThrowingSender : IApprovalPrivateNotificationSender
    {
        public Task SendAsync(ApprovalPrivateNotification notification,
            CancellationToken cancellationToken = default) =>
            throw new HttpRequestException("bridge down with secret details");
    }

    private sealed class CapturingSender : IApprovalPrivateNotificationSender
    {
        public ApprovalPrivateNotification? Notification { get; private set; }
        public int SendCalls { get; private set; }
        public Task SendAsync(ApprovalPrivateNotification notification,
            CancellationToken cancellationToken = default)
        {
            Notification = notification;
            SendCalls++;
            return Task.CompletedTask;
        }
    }

    private sealed class RoleCurrentUser(string role, string userId) : ICurrentUser
    {
        public string? UserId => userId;
        public Guid? EmployeeId => null;
        public string? DisplayName => role;
        public string? IpAddress => "127.0.0.1";
        public bool IsAuthenticated => true;
        public bool IsInRole(string expectedRole) => role == expectedRole;
        public bool HasPermission(string policy) => RolePermissions.HasPermission([role], policy);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
