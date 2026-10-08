using HRSystem.Application.Abstractions.Security;
using HRSystem.Application.Common.Exceptions;
using HRSystem.Application.Security;

namespace HRSystem.Application.CompanyCalendars;

internal static class CompanyCalendarAuthorization
{
    public static void EnsureManage(ICurrentUser currentUser)
    {
        if (!currentUser.IsAuthenticated ||
            !currentUser.HasPermission(PolicyNames.CompanyCalendarManage))
        {
            throw new ForbiddenAccessException("您沒有管理公司行事曆的權限。");
        }
    }

    public static void EnsureRead(ICurrentUser currentUser)
    {
        if (!currentUser.IsAuthenticated ||
            !currentUser.HasPermission(PolicyNames.CompanyCalendarRead))
        {
            throw new ForbiddenAccessException("您沒有檢視公司行事曆的權限。");
        }
    }

    public static string Actor(ICurrentUser currentUser) =>
        currentUser.UserId ??
        throw new ForbiddenAccessException("無法確認目前使用者。");
}
