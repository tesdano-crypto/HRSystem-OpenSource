using System.Security.Cryptography;
using HRSystem.Domain.Approvals;
using HRSystem.Domain.Common;

namespace HRSystem.UnitTests;

public sealed class ApprovalWorkflowTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 28, 1, 0, 0, TimeSpan.Zero);

    [Fact]
    public void New_Approval_Is_Pending_With_Immutable_Source_Snapshot()
    {
        var fingerprint = SHA256.HashData([1, 2, 3]);
        var approval = Create(fingerprint);
        fingerprint[0] ^= 0xff;

        Assert.Equal(ApprovalStatus.Pending, approval.Status);
        Assert.Equal(ApprovalNotificationStatus.Pending, approval.NotificationStatus);
        Assert.True(approval.MatchesSource(1, SHA256.HashData([1, 2, 3])));
        Assert.False(approval.MatchesSource(2, SHA256.HashData([1, 2, 3])));
    }

    [Fact]
    public void Approve_Is_One_Way_And_Replay_Is_Rejected()
    {
        var approval = Create();
        approval.Approve("owner", ApprovalChannel.Web, Now.AddMinutes(1));

        Assert.Equal(ApprovalStatus.Approved, approval.Status);
        Assert.Equal("owner", approval.DecisionByUserId);
        Assert.Throws<DomainValidationException>(() =>
            approval.Approve("owner", ApprovalChannel.Line, Now.AddMinutes(2)));
    }

    [Fact]
    public void Return_Requires_Reason_And_Trims_It()
    {
        var approval = Create();
        Assert.Throws<DomainValidationException>(() =>
            approval.Return("owner", ApprovalChannel.Web, "   ", Now));

        approval.Return("owner", ApprovalChannel.Web, "  請補充說明  ", Now);
        Assert.Equal(ApprovalStatus.Returned, approval.Status);
        Assert.Equal("請補充說明", approval.DecisionReason);
    }

    [Fact]
    public void Superseded_Approval_Cannot_Be_Approved()
    {
        var approval = Create();
        approval.Supersede("system", ApprovalChannel.System, Now);
        Assert.Equal(ApprovalStatus.Superseded, approval.Status);
        Assert.Throws<DomainValidationException>(() =>
            approval.Approve("owner", ApprovalChannel.Web, Now));
    }

    [Fact]
    public void Line_Token_Is_One_Time_And_Expires()
    {
        var token = new ApprovalLineActionToken(Guid.NewGuid(), Guid.NewGuid(),
            SHA256.HashData([9]), ApprovalLineAction.Approve, "owner", "U123",
            Now.AddHours(1), Now);
        Assert.True(token.IsValid(Now));
        token.Consume(Now.AddMinutes(1));
        Assert.False(token.IsValid(Now.AddMinutes(2)));
        Assert.Throws<DomainValidationException>(() => token.Consume(Now.AddMinutes(2)));

        var expired = new ApprovalLineActionToken(Guid.NewGuid(), Guid.NewGuid(),
            SHA256.HashData([8]), ApprovalLineAction.Return, "owner", "U123",
            Now.AddMinutes(1), Now);
        Assert.False(expired.IsValid(Now.AddMinutes(1)));
    }

    [Fact]
    public void History_And_Display_Use_Typed_Values()
    {
        var history = new ApprovalHistory(Guid.NewGuid(), Guid.NewGuid(),
            ApprovalHistoryAction.Submitted, "accounting", Now,
            ApprovalChannel.Web);
        Assert.Equal(ApprovalHistoryAction.Submitted, history.Action);
        Assert.Equal("待簽核", HRSystem.Application.Approvals.ApprovalDisplay.Status(
            ApprovalStatus.Pending));
        Assert.Equal("LINE 私訊通知失敗",
            HRSystem.Application.Approvals.ApprovalDisplay.HistoryAction(
                ApprovalHistoryAction.NotificationFailed));
    }

    [Fact]
    public void Fingerprint_Must_Be_Exactly_Thirty_Two_Bytes() =>
        Assert.Throws<DomainValidationException>(() => Create([1, 2, 3]));

    [Fact]
    public async Task Approval_History_Is_Append_Only_In_Persistence()
    {
        await using var db = TestDb.Create();
        var approval = Create();
        var history = new ApprovalHistory(Guid.NewGuid(), approval.Id,
            ApprovalHistoryAction.Submitted, "accounting", Now,
            ApprovalChannel.Web);
        db.Approvals.Add(approval);
        db.ApprovalHistories.Add(history);
        await db.SaveChangesAsync();
        db.Entry(history).State = Microsoft.EntityFrameworkCore.EntityState.Modified;

        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    private static Approval Create(byte[]? fingerprint = null) => new(
        Guid.NewGuid(), ApprovalType.Payroll, "PayrollRun", Guid.NewGuid().ToString(),
        1, fingerprint ?? SHA256.HashData([1]), "2026 年 08 月薪資待核准",
        "[]", "accounting", "owner", Now);
}
