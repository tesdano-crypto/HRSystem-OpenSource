using System.Security.Cryptography;
using System.Text;
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

public sealed class LineUserBindingService(
    IApplicationDbContext db,
    ICurrentUser currentUser,
    ILinePairingUserDirectory userDirectory,
    ILinePrivateTestNotificationSender testSender,
    TimeProvider timeProvider) : ILineUserBindingService
{
    private static readonly TimeSpan PairingLifetime = TimeSpan.FromMinutes(20);
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<LinePairingStatusDto>> GetStatusesAsync(
        CancellationToken cancellationToken = default)
    {
        EnsureAuthenticated();
        IReadOnlyList<LinePairingUserDto> users;
        if (currentUser.HasPermission(PolicyNames.SystemAdmin))
        {
            users = await userDirectory.GetOwnerUsersAsync(cancellationToken);
        }
        else
        {
            if (!currentUser.HasPermission(PolicyNames.ApprovalAct))
                throw new ForbiddenAccessException("您沒有查看 LINE 私人帳號綁定的權限。");
            var own = await userDirectory.FindActiveOwnerAsync(
                currentUser.UserId!, cancellationToken);
            users = own is null ? [] : [own];
        }

        if (users.Count == 0) return [];
        var userIds = users.Select(x => x.UserId).ToArray();
        var bindings = await db.LineUserBindings.AsNoTracking()
            .Where(x => userIds.Contains(x.HrSystemUserId))
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToListAsync(cancellationToken);
        var now = timeProvider.GetUtcNow();
        var pending = await db.LinePairingRequests.AsNoTracking()
            .Where(x => userIds.Contains(x.HrSystemUserId) &&
                x.ConsumedAtUtc == null && x.RevokedAtUtc == null &&
                x.ExpiresAtUtc > now)
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToListAsync(cancellationToken);
        var canManage = currentUser.HasPermission(PolicyNames.SystemAdmin);

        return users.Select(user =>
        {
            var active = bindings.FirstOrDefault(x => x.HrSystemUserId == user.UserId &&
                x.IsActive && x.TargetType == LineBindingTargetType.User &&
                x.RevokedAtUtc == null);
            var latest = bindings.FirstOrDefault(x => x.HrSystemUserId == user.UserId);
            var request = pending.FirstOrDefault(x => x.HrSystemUserId == user.UserId);
            var status = active is not null ? LinePairingStatus.Verified :
                request is not null ? LinePairingStatus.Pending :
                latest?.RevokedAtUtc is not null ? LinePairingStatus.Revoked :
                LinePairingStatus.Unbound;
            return new LinePairingStatusDto(user.UserId, user.UserName,
                user.DisplayName, user.EmployeeNumber, user.EmployeeName,
                user.Roles, status, active?.VerifiedAtUtc,
                latest?.RevokedAtUtc, request?.ExpiresAtUtc,
                active is null ? null : Mask(active.LineUserId), canManage,
                canManage && active is not null && testSender.IsConfigured);
        }).ToArray();
    }

    public async Task<CreateLinePairingResult> CreatePairingRequestAsync(
        CreateLinePairingRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureManagePermission();
        var target = await RequireOwnerAsync(request.HrSystemUserId, cancellationToken);
        if (request.ReplaceExistingBinding &&
            string.IsNullOrWhiteSpace(request.ReplacementReason))
            throw new ApplicationValidationException("更換已驗證 LINE 綁定時必須填寫原因。");

        var rawToken = Base64UrlEncode(RandomNumberGenerator.GetBytes(24));
        var tokenHash = SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));
        var now = timeProvider.GetUtcNow();
        var expires = now.Add(PairingLifetime);
        Guid requestId = Guid.Empty;
        await db.ExecuteSerializableAsync(async token =>
        {
            var activeBinding = await db.LineUserBindings.AnyAsync(x =>
                x.HrSystemUserId == target.UserId && x.IsActive, token);
            if (activeBinding && !request.ReplaceExistingBinding)
                throw new ApplicationValidationException(
                    "此使用者已有已驗證 LINE 私人帳號；更換前必須明確選擇更換並填寫原因。");

            var prior = await db.LinePairingRequests.Where(x =>
                x.HrSystemUserId == target.UserId && x.ConsumedAtUtc == null &&
                x.RevokedAtUtc == null).ToListAsync(token);
            foreach (var item in prior) item.Revoke(now);

            var entity = new LinePairingRequest(Guid.NewGuid(), target.UserId,
                tokenHash, expires, currentUser.UserId!, now,
                request.ReplaceExistingBinding, request.ReplacementReason);
            requestId = entity.Id;
            db.LinePairingRequests.Add(entity);
            db.AuditLogs.Add(AuditLogFactory.Create(currentUser, timeProvider,
                AuditActions.LinePairingRequested, nameof(LinePairingRequest),
                entity.Id.ToString(), null, new
                {
                    entity.HrSystemUserId,
                    Purpose = entity.Purpose.ToString(),
                    entity.ExpiresAtUtc,
                    entity.ReplacesExistingBinding
                }));
            await db.SaveChangesAsync(token);
        }, cancellationToken);
        return new(requestId, $"綁定 HRSystem {rawToken}", expires);
    }

    public async Task<CompleteLinePairingResult> CompletePrivatePairingAsync(
        CompleteLinePairingRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(request.SourceType, "user", StringComparison.Ordinal))
            return new(false, "LINE 配對只允許私人聊天室。");
        if (string.IsNullOrWhiteSpace(request.PairingToken) ||
            string.IsNullOrWhiteSpace(request.LineUserId))
            return new(false, "LINE 配對碼無效。");

        var tokenHash = SHA256.HashData(Encoding.UTF8.GetBytes(
            request.PairingToken.Trim()));
        var lineUserId = request.LineUserId.Trim();
        var now = timeProvider.GetUtcNow();
        CompleteLinePairingResult result = new(false, "LINE 配對碼無效。");
        await db.ExecuteSerializableAsync(async token =>
        {
            var pairing = await db.LinePairingRequests.SingleOrDefaultAsync(x =>
                x.TokenHash == tokenHash, token);
            if (pairing is null || pairing.Purpose != LinePairingPurpose.PrivateUserBinding ||
                !CryptographicOperations.FixedTimeEquals(pairing.TokenHash, tokenHash) ||
                !pairing.IsValid(now))
            {
                result = new(false, "LINE 配對碼已失效或已使用。");
                return;
            }
            if (await userDirectory.FindActiveOwnerAsync(pairing.HrSystemUserId, token) is null)
            {
                result = new(false, "LINE 配對目標帳號已失效或不具 Owner 權限。");
                return;
            }
            if (await db.LineUserBindings.AnyAsync(x => x.IsActive &&
                x.LineUserId == lineUserId &&
                x.HrSystemUserId != pairing.HrSystemUserId, token))
            {
                result = new(false, "此 LINE 私人帳號已綁定其他 HRSystem 使用者。");
                return;
            }

            var existing = await db.LineUserBindings.SingleOrDefaultAsync(x =>
                x.HrSystemUserId == pairing.HrSystemUserId && x.IsActive, token);
            if (existing is not null && !pairing.ReplacesExistingBinding)
            {
                result = new(false, "此使用者已有已驗證 LINE 私人帳號，必須先明確申請更換。");
                return;
            }

            pairing.Consume(now);
            if (existing is not null && existing.LineUserId == lineUserId &&
                existing.TargetType == LineBindingTargetType.User)
            {
                db.AuditLogs.Add(BridgeAudit(AuditActions.LinePairingVerified,
                    nameof(LineUserBinding), existing.Id.ToString(), now,
                    new { existing.HrSystemUserId, AlreadyVerified = true }));
                await db.SaveChangesAsync(token);
                result = new(true, "LINE 私人帳號已完成驗證。");
                return;
            }

            if (existing is not null) existing.Deactivate(now);
            var binding = new LineUserBinding(Guid.NewGuid(), pairing.HrSystemUserId,
                lineUserId, now, now, LineBindingTargetType.User);
            db.LineUserBindings.Add(binding);
            db.AuditLogs.Add(BridgeAudit(existing is null
                    ? AuditActions.LinePairingVerified
                    : AuditActions.LineUserBindingReplaced,
                nameof(LineUserBinding), binding.Id.ToString(), now,
                new
                {
                    binding.HrSystemUserId,
                    TargetType = binding.TargetType.ToString(),
                    binding.VerifiedAtUtc,
                    ReplacedBindingId = existing?.Id,
                    pairing.ReplacementReason
                }));
            await db.SaveChangesAsync(token);
            result = new(true, "LINE 私人帳號已完成驗證。");
        }, cancellationToken);
        return result;
    }

    public async Task RevokeAsync(RevokeLineBindingRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureManagePermission();
        if (string.IsNullOrWhiteSpace(request.Reason))
            throw new ApplicationValidationException("解除 LINE 綁定必須填寫原因。");
        await RequireOwnerAsync(request.HrSystemUserId, cancellationToken);
        await db.ExecuteSerializableAsync(async token =>
        {
            var binding = await db.LineUserBindings.SingleOrDefaultAsync(x =>
                x.HrSystemUserId == request.HrSystemUserId && x.IsActive, token)
                ?? throw new ApplicationValidationException("目前沒有可解除的 LINE 私人帳號綁定。");
            var now = timeProvider.GetUtcNow();
            binding.Deactivate(now);
            var pending = await db.LinePairingRequests.Where(x =>
                x.HrSystemUserId == request.HrSystemUserId &&
                x.ConsumedAtUtc == null && x.RevokedAtUtc == null).ToListAsync(token);
            foreach (var item in pending) item.Revoke(now);
            db.AuditLogs.Add(AuditLogFactory.Create(currentUser, timeProvider,
                AuditActions.LineUserBindingRevoked, nameof(LineUserBinding),
                binding.Id.ToString(), null, new
                {
                    binding.HrSystemUserId,
                    binding.IsActive,
                    binding.RevokedAtUtc,
                    Reason = request.Reason.Trim()
                }));
            await db.SaveChangesAsync(token);
        }, cancellationToken);
    }

    public async Task SendTestNotificationAsync(string hrSystemUserId,
        CancellationToken cancellationToken = default)
    {
        EnsureManagePermission();
        if (!testSender.IsConfigured)
            throw new ApplicationValidationException("LINE 私訊 bridge 尚未設定，無法傳送測試通知。");
        var binding = await db.LineUserBindings.AsNoTracking()
            .SingleOrDefaultAsync(x => x.HrSystemUserId == hrSystemUserId &&
                x.IsActive && x.TargetType == LineBindingTargetType.User &&
                x.RevokedAtUtc == null, cancellationToken)
            ?? throw new ApplicationValidationException("尚未完成 LINE 私人帳號綁定。");
        db.AuditLogs.Add(AuditLogFactory.Create(currentUser, timeProvider,
            AuditActions.LineTestNotificationRequested, nameof(LineUserBinding),
            binding.Id.ToString(), null, new { binding.HrSystemUserId }));
        await db.SaveChangesAsync(cancellationToken);
        try
        {
            await testSender.SendAsync(new(binding.LineUserId,
                "HRSystem LINE 私人通知測試成功。"), cancellationToken);
            db.AuditLogs.Add(AuditLogFactory.Create(currentUser, timeProvider,
                AuditActions.LineTestNotificationSucceeded, nameof(LineUserBinding),
                binding.Id.ToString(), null, new { binding.HrSystemUserId }));
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            db.AuditLogs.Add(AuditLogFactory.Create(currentUser, timeProvider,
                AuditActions.LineTestNotificationFailed, nameof(LineUserBinding),
                binding.Id.ToString(), null,
                new { binding.HrSystemUserId, Error = "LINE 私訊 bridge 傳送失敗。" }));
            await db.SaveChangesAsync(cancellationToken);
            throw new ApplicationValidationException(
                "LINE 私訊測試傳送失敗，請查閱安全的伺服器紀錄。");
        }
    }

    private async Task<LinePairingUserDto> RequireOwnerAsync(string userId,
        CancellationToken cancellationToken) =>
        await userDirectory.FindActiveOwnerAsync(userId, cancellationToken)
        ?? throw new ApplicationValidationException("指定帳號不存在、已停用或未具 Owner 權限。");

    private void EnsureAuthenticated()
    {
        if (!currentUser.IsAuthenticated || string.IsNullOrWhiteSpace(currentUser.UserId))
            throw new ForbiddenAccessException("請先登入。");
    }

    private void EnsureManagePermission()
    {
        EnsureAuthenticated();
        if (!currentUser.HasPermission(PolicyNames.SystemAdmin))
            throw new ForbiddenAccessException("只有系統管理員可管理 LINE 私人帳號綁定。");
    }

    private static string Base64UrlEncode(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    internal static string Mask(string value) => value.Length <= 5
        ? "*****"
        : $"{value[..3]}{new string('*', Math.Min(8, value.Length - 5))}{value[^2..]}";

    private static AuditLog BridgeAudit(string action, string entityType,
        string entityId, DateTimeOffset now, object safeValues) => new(
            null, action, entityType, entityId, null,
            JsonSerializer.Serialize(safeValues, JsonOptions), null, now);
}

public sealed class LinePrivateTargetResolver(IApplicationDbContext db)
    : ILinePrivateTargetResolver
{
    public async Task<LinePrivateTargetResolution> ResolveAsync(
        string hrSystemUserId, CancellationToken cancellationToken = default)
    {
        var binding = await db.LineUserBindings.AsNoTracking()
            .SingleOrDefaultAsync(x => x.HrSystemUserId == hrSystemUserId &&
                x.IsActive && x.TargetType == LineBindingTargetType.User &&
                x.RevokedAtUtc == null, cancellationToken);
        return binding is null
            ? new(LinePrivateTargetStatus.MissingBinding)
            : new(LinePrivateTargetStatus.Found, binding.LineUserId);
    }
}
