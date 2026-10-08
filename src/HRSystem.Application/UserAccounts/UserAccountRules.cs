using HRSystem.Application.Common.Exceptions;
using HRSystem.Application.Security;

namespace HRSystem.Application.UserAccounts;

public static class UserAccountRules
{
    public static void EnsureRoles(IReadOnlyCollection<string> roles)
    {
        if (roles.Count == 0)
        {
            throw new ApplicationValidationException("帳號至少需要一個角色。");
        }

        if (roles.Any(role => !RoleNames.All.Contains(role)))
        {
            throw new ApplicationValidationException("包含不支援的角色。");
        }
    }

    public static void EnsureCanDeactivate(string? actorUserId, string targetUserId)
    {
        if (string.Equals(actorUserId, targetUserId, StringComparison.Ordinal))
        {
            throw new ApplicationValidationException("不可停用自己的帳號。");
        }
    }

    public static void EnsureAdminRemains(bool targetIsAdmin, int activeAdminCount, bool remainsActiveAdmin)
    {
        if (targetIsAdmin && activeAdminCount <= 1 && !remainsActiveAdmin)
        {
            throw new ApplicationValidationException("系統必須保留至少一個有效的 Admin 帳號。");
        }
    }
}
