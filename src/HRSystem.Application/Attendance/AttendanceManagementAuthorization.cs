using HRSystem.Application.Abstractions.Security;
using HRSystem.Application.Common.Exceptions;
using HRSystem.Application.Security;

namespace HRSystem.Application.Attendance;

internal static class AttendanceManagementAuthorization
{
    public static void EnsureRead(ICurrentUser currentUser)
    {
        if (!currentUser.IsAuthenticated ||
            !currentUser.HasPermission(PolicyNames.AttendanceDailyRead))
        {
            throw new ForbiddenAccessException(
                "You are not authorized to view daily attendance.");
        }
    }

    public static void EnsureManage(ICurrentUser currentUser)
    {
        if (!currentUser.IsAuthenticated ||
            !currentUser.HasPermission(PolicyNames.AttendanceManage))
        {
            throw new ForbiddenAccessException(
                "You are not authorized to manage attendance settings.");
        }
    }

    public static void EnsureAdjust(ICurrentUser currentUser)
    {
        if (!currentUser.IsAuthenticated ||
            !currentUser.HasPermission(PolicyNames.AttendanceAdjust))
        {
            throw new ForbiddenAccessException(
                "You are not authorized to adjust attendance results.");
        }
    }

    public static string Actor(ICurrentUser currentUser) =>
        currentUser.UserId ??
        throw new ForbiddenAccessException("An authenticated user is required.");
}
