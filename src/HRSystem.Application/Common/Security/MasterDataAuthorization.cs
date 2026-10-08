using HRSystem.Application.Abstractions.Security;
using HRSystem.Application.Common.Exceptions;
using HRSystem.Application.Security;

namespace HRSystem.Application.Common.Security;

internal static class MasterDataAuthorization
{
    public static void EnsureAdmin(ICurrentUser currentUser)
    {
        if (!currentUser.IsAuthenticated || !currentUser.HasPermission(PolicyNames.MasterDataManage))
        {
            throw new ForbiddenAccessException();
        }
    }

    public static void EnsureOrganizationReader(ICurrentUser currentUser)
    {
        if (!currentUser.IsAuthenticated || !currentUser.HasPermission(PolicyNames.MasterDataRead))
        {
            throw new ForbiddenAccessException("您沒有檢視基本資料的權限。");
        }
    }
}
