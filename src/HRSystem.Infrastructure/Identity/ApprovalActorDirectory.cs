using HRSystem.Application.Approvals;
using HRSystem.Application.Security;
using HRSystem.Domain.Approvals;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace HRSystem.Infrastructure.Identity;

public sealed class ApprovalActorDirectory(UserManager<ApplicationUser> userManager)
    : IApprovalActorDirectory
{
    public async Task<ApprovalActorDto?> FindActiveAsync(string userId,
        CancellationToken cancellationToken = default)
    {
        var user = await userManager.Users.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == userId && x.IsActive,
                cancellationToken);
        return user is null ? null : new(user.Id,
            string.IsNullOrWhiteSpace(user.DisplayName)
                ? user.UserName ?? "使用者" : user.DisplayName);
    }

    public async Task<bool> HasPermissionAsync(string userId, string permission,
        CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByIdAsync(userId);
        if (user is null || !user.IsActive) return false;
        var roles = await userManager.GetRolesAsync(user);
        return RolePermissions.HasPermission(roles, permission);
    }

    public async Task<IReadOnlyList<ApprovalActorDto>> GetEligibleApproversAsync(
        ApprovalType approvalType, CancellationToken cancellationToken = default)
    {
        var requiredPermission = approvalType switch
        {
            ApprovalType.Payroll => PolicyNames.PayrollApprove,
            _ => throw new InvalidOperationException("不支援的簽核類型。")
        };
        var users = await userManager.Users.AsNoTracking()
            .Where(x => x.IsActive).OrderBy(x => x.DisplayName)
            .Select(x => new { x.Id, x.DisplayName, x.UserName })
            .ToListAsync(cancellationToken);
        var result = new List<ApprovalActorDto>();
        foreach (var item in users)
        {
            var user = await userManager.FindByIdAsync(item.Id);
            if (user is null) continue;
            var roles = await userManager.GetRolesAsync(user);
            if (RolePermissions.HasPermission(roles, PolicyNames.ApprovalAct) &&
                RolePermissions.HasPermission(roles, requiredPermission))
            {
                result.Add(new(item.Id,
                    string.IsNullOrWhiteSpace(item.DisplayName)
                        ? item.UserName ?? "使用者" : item.DisplayName));
            }
        }
        return result;
    }
}
