using System.Data;
using System.Security.Cryptography;
using System.Text.Json;
using HRSystem.Application.Abstractions.Persistence;
using HRSystem.Application.Abstractions.Security;
using HRSystem.Application.Common.Auditing;
using HRSystem.Application.Common.Exceptions;
using HRSystem.Application.Security;
using HRSystem.Domain.Approvals;
using HRSystem.Domain.Auditing;
using Microsoft.EntityFrameworkCore;

namespace HRSystem.Application.Approvals;

public sealed class ApprovalService(
    IApplicationDbContext db,
    ICurrentUser currentUser,
    IEnumerable<IApprovalSourceProvider> sourceProviders,
    IApprovalActorDirectory actorDirectory,
    ILinePrivateTargetResolver privateTargetResolver,
    IApprovalPrivateNotificationSender notificationSender,
    TimeProvider timeProvider) : IApprovalService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan LineTokenLifetime = TimeSpan.FromHours(48);

    public Task<IReadOnlyList<ApprovalActorDto>> GetApproversAsync(
        ApprovalType type, CancellationToken cancellationToken = default)
    {
        EnsureSubmitPermission(type);
        return actorDirectory.GetEligibleApproversAsync(type, cancellationToken);
    }

    public async Task<ApprovalDto> SubmitAsync(
        SubmitApprovalRequest request, CancellationToken cancellationToken = default)
    {
        EnsureAuthenticated();
        EnsureSubmitPermission(request.ApprovalType);
        if (request.SourceId == Guid.Empty)
            throw new ApplicationValidationException("簽核來源不可空白。");
        var approver = await ValidateApproverAsync(
            request.ApprovalType, request.ApproverUserId, cancellationToken);
        var source = await Provider(request.ApprovalType)
            .GetSnapshotAsync(request.SourceId, cancellationToken);
        var now = timeProvider.GetUtcNow();

        var submission = await db.ExecuteSerializableAsync(async token =>
        {
            var existing = await db.Approvals.SingleOrDefaultAsync(x =>
                x.ApprovalType == request.ApprovalType &&
                x.SourceEntityType == source.SourceEntityType &&
                x.SourceEntityId == source.SourceEntityId &&
                x.SourceFingerprintVersion == source.FingerprintVersion &&
                x.Status == ApprovalStatus.Pending, token);
            if (existing is not null)
            {
                if (existing.MatchesSource(source.FingerprintVersion, source.Fingerprint))
                    return (ApprovalId: existing.Id, ShouldNotify: false);
                existing.Supersede(currentUser.UserId!, ApprovalChannel.System, now);
                await RevokeTokensAsync(existing.Id, now, token);
                db.ApprovalHistories.Add(new ApprovalHistory(Guid.NewGuid(), existing.Id,
                    ApprovalHistoryAction.Superseded, currentUser.UserId, now,
                    ApprovalChannel.System,
                    "來源資料已變更，由重新送簽取代。"));
                AddAudit(AuditActions.ApprovalSuperseded, existing,
                    new { Status = existing.Status.ToString(), ReplacedByNewSubmission = true });
            }

            var approval = new Approval(Guid.NewGuid(), source.ApprovalType,
                source.SourceEntityType, source.SourceEntityId,
                source.FingerprintVersion, source.Fingerprint, source.Title,
                JsonSerializer.Serialize(source.Summary, JsonOptions),
                currentUser.UserId!, approver.UserId, now);
            db.Approvals.Add(approval);
            db.ApprovalHistories.Add(new ApprovalHistory(Guid.NewGuid(), approval.Id,
                ApprovalHistoryAction.Submitted, currentUser.UserId, now,
                ApprovalChannel.Web));
            AddAudit(AuditActions.ApprovalSubmitted, approval, new
            {
                approval.ApprovalType,
                approval.SourceEntityType,
                approval.SourceEntityId,
                approval.SourceFingerprintVersion,
                approval.ApproverUserId,
                Status = approval.Status.ToString()
            });
            await db.SaveChangesAsync(token);
            return (ApprovalId: approval.Id, ShouldNotify: true);
        }, cancellationToken);

        if (submission.ShouldNotify)
            await TrySendNotificationAsync(submission.ApprovalId, cancellationToken);
        return await GetAsync(submission.ApprovalId, cancellationToken);
    }

    public async Task<IReadOnlyList<ApprovalDto>> GetListAsync(
        CancellationToken cancellationToken = default)
    {
        EnsureViewPermission();
        var userId = currentUser.UserId!;
        var query = db.Approvals.AsNoTracking()
            .Include(x => x.Histories)
            .AsQueryable();
        if (!currentUser.HasPermission(PolicyNames.SystemAdmin))
            query = query.Where(x => x.RequestedByUserId == userId ||
                x.ApproverUserId == userId);
        var items = await query.OrderByDescending(x => x.RequestedAtUtc)
            .Take(200).ToListAsync(cancellationToken);
        return items.Select(ToDto).ToArray();
    }

    public async Task<ApprovalDto> GetAsync(
        Guid approvalId, CancellationToken cancellationToken = default)
    {
        EnsureViewPermission();
        var approval = await LoadAsync(approvalId, true, cancellationToken);
        EnsureCanView(approval);
        return ToDto(approval);
    }

    public async Task<ApprovalDto> MarkViewedAsync(
        Guid approvalId, CancellationToken cancellationToken = default)
    {
        EnsureViewPermission();
        await db.ExecuteTransactionAsync(IsolationLevel.ReadCommitted, async token =>
        {
            var approval = await LoadAsync(approvalId, false, token);
            EnsureCanView(approval);
            var exists = await db.ApprovalHistories.AnyAsync(x =>
                x.ApprovalId == approvalId &&
                x.Action == ApprovalHistoryAction.Viewed &&
                x.ActorUserId == currentUser.UserId, token);
            if (!exists)
            {
                var now = timeProvider.GetUtcNow();
                db.ApprovalHistories.Add(new ApprovalHistory(Guid.NewGuid(), approval.Id,
                    ApprovalHistoryAction.Viewed, currentUser.UserId, now,
                    ApprovalChannel.Web));
                AddAudit(AuditActions.ApprovalViewed, approval,
                    new { approval.Status });
                await db.SaveChangesAsync(token);
            }
        }, cancellationToken);
        return await GetAsync(approvalId, cancellationToken);
    }

    public Task<ApprovalDto> ApproveAsync(
        ApprovalDecisionRequest request, CancellationToken cancellationToken = default) =>
        DecideWebAsync(request, ApprovalLineAction.Approve, cancellationToken);

    public Task<ApprovalDto> ReturnAsync(
        ApprovalDecisionRequest request, CancellationToken cancellationToken = default) =>
        DecideWebAsync(request, ApprovalLineAction.Return, cancellationToken);

    public async Task<ApprovalDto> CancelAsync(
        ApprovalDecisionRequest request, CancellationToken cancellationToken = default)
    {
        EnsureAuthenticated();
        await db.ExecuteSerializableAsync(async token =>
        {
            var approval = await LoadAsync(request.ApprovalId, false, token);
            if (approval.RequestedByUserId != currentUser.UserId &&
                !currentUser.HasPermission(PolicyNames.SystemAdmin))
                throw new ForbiddenAccessException("只有送簽人可取消此簽核。");
            var now = timeProvider.GetUtcNow();
            approval.Cancel(currentUser.UserId!, ApprovalChannel.Web, request.Reason, now);
            await RevokeTokensAsync(approval.Id, now, token);
            db.ApprovalHistories.Add(new ApprovalHistory(Guid.NewGuid(), approval.Id,
                ApprovalHistoryAction.Cancelled, currentUser.UserId, now,
                ApprovalChannel.Web, request.Reason));
            AddAudit(AuditActions.ApprovalCancelled, approval,
                new { Status = approval.Status.ToString() });
            await db.SaveChangesAsync(token);
        }, cancellationToken);
        return await GetAsync(request.ApprovalId, cancellationToken);
    }

    public async Task<ApprovalDto> RetryNotificationAsync(
        Guid approvalId, CancellationToken cancellationToken = default)
    {
        EnsureAuthenticated();
        var approval = await LoadAsync(approvalId, false, cancellationToken);
        if (approval.RequestedByUserId != currentUser.UserId &&
            !currentUser.HasPermission(PolicyNames.SystemAdmin))
            throw new ForbiddenAccessException("只有送簽人可重試通知。");
        if (approval.Status != ApprovalStatus.Pending)
            throw new ApplicationValidationException("已完成的簽核不能重試通知。");
        await TrySendNotificationAsync(approvalId, cancellationToken);
        return await GetAsync(approvalId, cancellationToken);
    }

    public async Task<ApprovalLinePostbackResult> HandleLinePostbackAsync(
        ApprovalLinePostbackRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.LineUserId) ||
            string.IsNullOrWhiteSpace(request.Token))
            return new(false, "此簽核連結無效。");
        byte[] raw;
        try { raw = Convert.FromBase64String(request.Token); }
        catch (FormatException) { return new(false, "此簽核連結無效。"); }
        var tokenHash = SHA256.HashData(raw);
        var now = timeProvider.GetUtcNow();
        ApprovalLinePostbackResult? result = null;
        await db.ExecuteSerializableAsync(async token =>
        {
            var actionToken = await db.ApprovalLineActionTokens
                .Include(x => x.Approval)
                .SingleOrDefaultAsync(x => x.TokenHash == tokenHash, token);
            if (actionToken is null ||
                !CryptographicOperations.FixedTimeEquals(
                    System.Text.Encoding.UTF8.GetBytes(actionToken.LineUserId),
                    System.Text.Encoding.UTF8.GetBytes(request.LineUserId.Trim())) ||
                !actionToken.IsValid(now))
            {
                result = new(false, "此簽核連結已失效或已使用。");
                return;
            }
            var privateTarget = await privateTargetResolver.ResolveAsync(
                actionToken.IntendedApproverUserId, token);
            var bindingValid = privateTarget.Status == LinePrivateTargetStatus.Found &&
                privateTarget.LineUserId is not null &&
                CryptographicOperations.FixedTimeEquals(
                    System.Text.Encoding.UTF8.GetBytes(privateTarget.LineUserId),
                    System.Text.Encoding.UTF8.GetBytes(request.LineUserId.Trim()));
            if (!bindingValid ||
                !await CanApproveAsync(actionToken.IntendedApproverUserId,
                    actionToken.Approval.ApprovalType, token))
            {
                result = new(false, "此 LINE 帳號沒有處理此簽核的權限。");
                return;
            }
            var approval = actionToken.Approval;
            if (approval.Status != ApprovalStatus.Pending)
            {
                result = new(false, "此簽核已完成，無法重複處理。",
                    approval.Id, approval.Status);
                return;
            }
            var source = await Provider(approval.ApprovalType)
                .GetSnapshotAsync(Guid.Parse(approval.SourceEntityId), token);
            if (!approval.MatchesSource(source.FingerprintVersion, source.Fingerprint))
            {
                approval.Supersede(actionToken.IntendedApproverUserId,
                    ApprovalChannel.Line, now);
                actionToken.Consume(now);
                await RevokeTokensAsync(approval.Id, now, token, actionToken.Id);
                AddHistoryAndAudit(approval, ApprovalHistoryAction.Superseded,
                    AuditActions.ApprovalSuperseded,
                    actionToken.IntendedApproverUserId, ApprovalChannel.Line, null);
                await db.SaveChangesAsync(token);
                result = new(false, "來源資料已變更，請由送簽人重新送出。",
                    approval.Id, approval.Status);
                return;
            }
            if (actionToken.Action == ApprovalLineAction.Return &&
                string.IsNullOrWhiteSpace(request.Reason))
            {
                result = new(false, "退回原因為必填欄位。", approval.Id,
                    approval.Status);
                return;
            }
            actionToken.Consume(now);
            if (actionToken.Action == ApprovalLineAction.Approve)
                approval.Approve(actionToken.IntendedApproverUserId,
                    ApprovalChannel.Line, now);
            else
                approval.Return(actionToken.IntendedApproverUserId,
                    ApprovalChannel.Line, request.Reason!, now);
            await RevokeTokensAsync(approval.Id, now, token, actionToken.Id);
            AddHistoryAndAudit(approval,
                actionToken.Action == ApprovalLineAction.Approve
                    ? ApprovalHistoryAction.Approved
                    : ApprovalHistoryAction.Returned,
                actionToken.Action == ApprovalLineAction.Approve
                    ? AuditActions.ApprovalApproved
                    : AuditActions.ApprovalReturned,
                actionToken.IntendedApproverUserId, ApprovalChannel.Line,
                request.Reason);
            await db.SaveChangesAsync(token);
            result = new(true,
                actionToken.Action == ApprovalLineAction.Approve
                    ? "簽核已核准。" : "簽核已退回。",
                approval.Id, approval.Status);
        }, cancellationToken);
        return result ?? new(false, "簽核處理失敗。");
    }

    private async Task<ApprovalDto> DecideWebAsync(ApprovalDecisionRequest request,
        ApprovalLineAction action, CancellationToken cancellationToken)
    {
        EnsureAuthenticated();
        await db.ExecuteSerializableAsync(async token =>
        {
            var approval = await LoadAsync(request.ApprovalId, false, token);
            await EnsureCanApproveCurrentAsync(approval, token);
            if (action == ApprovalLineAction.Return &&
                string.IsNullOrWhiteSpace(request.Reason))
                throw new ApplicationValidationException("退回原因為必填欄位。");
            var source = await Provider(approval.ApprovalType)
                .GetSnapshotAsync(Guid.Parse(approval.SourceEntityId), token);
            var now = timeProvider.GetUtcNow();
            if (!approval.MatchesSource(source.FingerprintVersion, source.Fingerprint))
            {
                approval.Supersede(currentUser.UserId!, ApprovalChannel.Web, now);
                await RevokeTokensAsync(approval.Id, now, token);
                AddHistoryAndAudit(approval, ApprovalHistoryAction.Superseded,
                    AuditActions.ApprovalSuperseded, currentUser.UserId!,
                    ApprovalChannel.Web, null);
                await db.SaveChangesAsync(token);
                return;
            }
            if (action == ApprovalLineAction.Approve)
                approval.Approve(currentUser.UserId!, ApprovalChannel.Web, now);
            else
                approval.Return(currentUser.UserId!, ApprovalChannel.Web,
                    request.Reason!, now);
            await RevokeTokensAsync(approval.Id, now, token);
            AddHistoryAndAudit(approval,
                action == ApprovalLineAction.Approve
                    ? ApprovalHistoryAction.Approved
                    : ApprovalHistoryAction.Returned,
                action == ApprovalLineAction.Approve
                    ? AuditActions.ApprovalApproved
                    : AuditActions.ApprovalReturned,
                currentUser.UserId!, ApprovalChannel.Web, request.Reason);
            await db.SaveChangesAsync(token);
        }, cancellationToken);
        return await GetAsync(request.ApprovalId, cancellationToken);
    }

    private async Task TrySendNotificationAsync(Guid approvalId,
        CancellationToken cancellationToken)
    {
        ApprovalPrivateNotification? notification = null;
        try
        {
            await db.ExecuteSerializableAsync(async token =>
            {
                var approval = await LoadAsync(approvalId, false, token);
                if (approval.Status != ApprovalStatus.Pending) return;
                var privateTarget = await privateTargetResolver.ResolveAsync(
                    approval.ApproverUserId, token);
                if (privateTarget.Status != LinePrivateTargetStatus.Found ||
                    string.IsNullOrWhiteSpace(privateTarget.LineUserId))
                    throw new ApplicationValidationException(
                        "尚未完成 LINE 私人帳號綁定。");
                var lineUserId = privateTarget.LineUserId;
                var now = timeProvider.GetUtcNow();
                await RevokeTokensAsync(approval.Id, now, token);
                var approveRaw = RandomNumberGenerator.GetBytes(32);
                var returnRaw = RandomNumberGenerator.GetBytes(32);
                var expires = now.Add(LineTokenLifetime);
                db.ApprovalLineActionTokens.Add(new ApprovalLineActionToken(
                    Guid.NewGuid(), approval.Id, SHA256.HashData(approveRaw),
                    ApprovalLineAction.Approve, approval.ApproverUserId,
                    lineUserId, expires, now));
                db.ApprovalLineActionTokens.Add(new ApprovalLineActionToken(
                    Guid.NewGuid(), approval.Id, SHA256.HashData(returnRaw),
                    ApprovalLineAction.Return, approval.ApproverUserId,
                    lineUserId, expires, now));
                await db.SaveChangesAsync(token);
                notification = new(lineUserId, approval.Id, approval.Title,
                    DeserializeSummary(approval.SummaryJson),
                    Convert.ToBase64String(approveRaw),
                    Convert.ToBase64String(returnRaw), expires);
            }, cancellationToken);
            if (notification is null) return;
            await notificationSender.SendAsync(notification, cancellationToken);
            await RecordNotificationResultAsync(approvalId, true, null, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await RecordNotificationResultAsync(approvalId, false,
                SafeNotificationError(ex), cancellationToken);
        }
    }

    private async Task RecordNotificationResultAsync(Guid approvalId, bool sent,
        string? safeError, CancellationToken cancellationToken)
    {
        await db.ExecuteTransactionAsync(IsolationLevel.ReadCommitted, async token =>
        {
            var approval = await LoadAsync(approvalId, false, token);
            if (approval.Status != ApprovalStatus.Pending) return;
            var now = timeProvider.GetUtcNow();
            if (sent) approval.MarkNotificationSent(now);
            else approval.MarkNotificationFailed(safeError ?? "LINE 私訊通知失敗。", now);
            db.ApprovalHistories.Add(new ApprovalHistory(Guid.NewGuid(), approval.Id,
                sent ? ApprovalHistoryAction.NotificationSent :
                    ApprovalHistoryAction.NotificationFailed,
                currentUser.UserId, now, ApprovalChannel.System,
                sent ? null : approval.NotificationErrorSummary));
            AddAudit(sent ? AuditActions.ApprovalNotificationSent :
                AuditActions.ApprovalNotificationFailed, approval,
                new { approval.NotificationStatus, approval.NotificationErrorSummary });
            await db.SaveChangesAsync(token);
        }, cancellationToken);
    }

    private async Task EnsureCanApproveCurrentAsync(Approval approval,
        CancellationToken cancellationToken)
    {
        if (approval.ApproverUserId != currentUser.UserId &&
            !currentUser.HasPermission(PolicyNames.SystemAdmin))
            throw new ForbiddenAccessException("您不是此簽核的指定簽核人。");
        if (!currentUser.HasPermission(PolicyNames.ApprovalAct) ||
            !currentUser.HasPermission(ApprovalPermission(approval.ApprovalType)))
            throw new ForbiddenAccessException("您沒有此類型的簽核權限。");
        if (currentUser.HasPermission(PolicyNames.SystemAdmin)) return;
        if (!await CanApproveAsync(currentUser.UserId!, approval.ApprovalType,
            cancellationToken))
            throw new ForbiddenAccessException("簽核帳號已停用或權限已變更。");
    }

    private async Task<ApprovalActorDto> ValidateApproverAsync(ApprovalType type,
        string userId, CancellationToken cancellationToken)
    {
        var actor = await actorDirectory.FindActiveAsync(userId.Trim(), cancellationToken)
            ?? throw new ApplicationValidationException("找不到有效簽核人。");
        if (!await CanApproveAsync(actor.UserId, type, cancellationToken))
            throw new ApplicationValidationException("所選帳號沒有此類型的簽核權限。");
        return actor;
    }

    private async Task<bool> CanApproveAsync(string userId, ApprovalType type,
        CancellationToken cancellationToken) =>
        await actorDirectory.HasPermissionAsync(userId, PolicyNames.ApprovalAct,
            cancellationToken) &&
        await actorDirectory.HasPermissionAsync(userId, ApprovalPermission(type),
            cancellationToken);

    private IApprovalSourceProvider Provider(ApprovalType type) =>
        sourceProviders.SingleOrDefault(x => x.ApprovalType == type)
        ?? throw new ApplicationValidationException("尚未支援此簽核類型。");

    private async Task<Approval> LoadAsync(Guid id, bool includeHistory,
        CancellationToken cancellationToken)
    {
        var query = db.Approvals.AsQueryable();
        if (includeHistory) query = query.Include(x => x.Histories);
        return await query.SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new ApplicationValidationException("找不到簽核資料。");
    }

    private void EnsureCanView(Approval approval)
    {
        if (!currentUser.HasPermission(PolicyNames.SystemAdmin) &&
            approval.RequestedByUserId != currentUser.UserId &&
            approval.ApproverUserId != currentUser.UserId)
            throw new ForbiddenAccessException("您沒有檢視此簽核的權限。");
    }

    private void EnsureViewPermission()
    {
        EnsureAuthenticated();
        if (!currentUser.HasPermission(PolicyNames.ApprovalView))
            throw new ForbiddenAccessException("您沒有簽核檢視權限。");
    }

    private void EnsureSubmitPermission(ApprovalType type)
    {
        EnsureAuthenticated();
        if (!currentUser.HasPermission(SubmitPermission(type)))
            throw new ForbiddenAccessException("您沒有此類型的送簽權限。");
    }

    private void EnsureAuthenticated()
    {
        if (!currentUser.IsAuthenticated || string.IsNullOrWhiteSpace(currentUser.UserId))
            throw new ForbiddenAccessException("請先登入。");
    }

    private static string SubmitPermission(ApprovalType type) => type switch
    {
        ApprovalType.Payroll => PolicyNames.PayrollSubmitApproval,
        _ => throw new ApplicationValidationException("尚未支援此簽核類型。")
    };

    private static string ApprovalPermission(ApprovalType type) => type switch
    {
        ApprovalType.Payroll => PolicyNames.PayrollApprove,
        _ => throw new ApplicationValidationException("尚未支援此簽核類型。")
    };

    private async Task RevokeTokensAsync(Guid approvalId, DateTimeOffset now,
        CancellationToken cancellationToken, Guid? exceptTokenId = null)
    {
        var tokens = await db.ApprovalLineActionTokens.Where(x =>
            x.ApprovalId == approvalId && x.ConsumedAtUtc == null &&
            x.RevokedAtUtc == null && (!exceptTokenId.HasValue || x.Id != exceptTokenId))
            .ToListAsync(cancellationToken);
        foreach (var item in tokens) item.Revoke(now);
    }

    private void AddHistoryAndAudit(Approval approval, ApprovalHistoryAction action,
        string auditAction, string actorUserId, ApprovalChannel channel,
        string? comment)
    {
        db.ApprovalHistories.Add(new ApprovalHistory(Guid.NewGuid(), approval.Id,
            action, actorUserId, timeProvider.GetUtcNow(), channel, comment));
        var values = new
        {
            Status = approval.Status.ToString(),
            Channel = channel.ToString(),
            HasReason = !string.IsNullOrWhiteSpace(comment)
        };
        if (channel == ApprovalChannel.Line)
        {
            db.AuditLogs.Add(new AuditLog(actorUserId, auditAction,
                nameof(Approval), approval.Id.ToString(), null,
                JsonSerializer.Serialize(values, JsonOptions), null,
                timeProvider.GetUtcNow()));
        }
        else
        {
            AddAudit(auditAction, approval, values);
        }
    }

    private void AddAudit(string action, Approval approval, object values) =>
        db.AuditLogs.Add(AuditLogFactory.Create(currentUser, timeProvider, action,
            nameof(Approval), approval.Id.ToString(), null, values));

    private static string SafeNotificationError(Exception ex) => ex switch
    {
        ApplicationValidationException => ex.Message,
        _ => "LINE 私訊通知失敗，請由送簽人稍後重試。"
    };

    private static IReadOnlyList<ApprovalSummaryItemDto> DeserializeSummary(string json) =>
        JsonSerializer.Deserialize<ApprovalSummaryItemDto[]>(json, JsonOptions) ?? [];

    private static ApprovalDto ToDto(Approval item) => new(
        item.Id, item.ApprovalType, item.SourceEntityType, item.SourceEntityId,
        item.SourceFingerprintVersion, item.Title,
        DeserializeSummary(item.SummaryJson), item.RequestedByUserId,
        item.RequestedAtUtc, item.ApproverUserId, item.Status,
        item.DecisionAtUtc, item.DecisionByUserId, item.DecisionChannel,
        item.DecisionReason, item.NotificationStatus,
        item.NotificationErrorSummary,
        item.Histories.OrderBy(x => x.ActionAtUtc).Select(x =>
            new ApprovalHistoryDto(x.Id, x.Action, x.ActorUserId,
                x.ActionAtUtc, x.Channel, x.Comment)).ToArray(),
        Convert.ToHexString(item.SourceFingerprint));
}
