using HRSystem.Application.Abstractions.Security;
using HRSystem.Application.Common.Exceptions;
using HRSystem.Application.Security;

namespace HRSystem.Application.Attendance;

internal static class AttendanceAuthorization
{
    public static void EnsureManage(ICurrentUser currentUser)
    {
        if (!currentUser.IsAuthenticated ||
            !currentUser.HasPermission(PolicyNames.AttendanceImportManage))
        {
            throw new ForbiddenAccessException("您沒有管理 BioWebTA 出勤匯入的權限。");
        }
    }
}
