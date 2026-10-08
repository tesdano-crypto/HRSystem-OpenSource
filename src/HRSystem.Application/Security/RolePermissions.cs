using System.Security.Claims;

namespace HRSystem.Application.Security;

public static class RolePermissions
{
    private static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> Matrix =
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal)
        {
            [RoleNames.Admin] = PolicyNames.All,
            [RoleNames.HR] = Set(
                PolicyNames.DashboardAccess, PolicyNames.EmployeeSelfService,
                PolicyNames.EmployeeView, PolicyNames.EmployeeManage,
                PolicyNames.AttendanceViewAll, PolicyNames.AttendanceDailyReport,
                PolicyNames.AttendanceManage,
                PolicyNames.LeaveView, PolicyNames.LeaveManage,
                PolicyNames.OvertimeView, PolicyNames.OvertimeManage,
                PolicyNames.InsuranceView, PolicyNames.InsuranceManage,
                PolicyNames.PayrollView, PolicyNames.ApprovalView,
                PolicyNames.MasterDataRead, PolicyNames.MasterDataManage,
                PolicyNames.LeaveRequestSelfService,
                PolicyNames.AttendancePunchRecordRead, PolicyNames.AttendanceDailyRead,
                PolicyNames.AttendanceAdjust, PolicyNames.LeaveRequestReadAll,
                PolicyNames.LeaveRequestApprove, PolicyNames.ParentalLeaveApprove,
                PolicyNames.ParentalLeaveSelfService,
                PolicyNames.AttendanceExceptionSelfService,
                PolicyNames.AttendanceExceptionApprove,
                PolicyNames.OvertimeSelfService),
            [RoleNames.Accounting] = Set(
                PolicyNames.DashboardAccess, PolicyNames.EmployeeSelfService,
                PolicyNames.EmployeeView,
                PolicyNames.AttendanceViewAll, PolicyNames.AttendanceDailyReport,
                PolicyNames.AttendanceManage,
                PolicyNames.LeaveView, PolicyNames.LeaveManage,
                PolicyNames.OvertimeView, PolicyNames.OvertimeManage,
                PolicyNames.InsuranceView, PolicyNames.InsuranceManage,
                PolicyNames.PayrollView, PolicyNames.PayrollManage,
                PolicyNames.PayrollSubmitApproval, PolicyNames.ApprovalView,
                PolicyNames.MasterDataRead, PolicyNames.AttendancePunchRecordRead,
                PolicyNames.AttendanceDailyRead, PolicyNames.AttendanceAdjust,
                PolicyNames.LeaveRequestSelfService, PolicyNames.LeaveRequestReadAll,
                PolicyNames.LeaveRequestApprove, PolicyNames.ParentalLeaveSelfService,
                PolicyNames.ParentalLeaveApprove,
                PolicyNames.AttendanceExceptionSelfService,
                PolicyNames.AttendanceExceptionApprove,
                PolicyNames.OvertimeSelfService),
            [RoleNames.Owner] = Set(
                PolicyNames.DashboardAccess, PolicyNames.EmployeeSelfService,
                PolicyNames.PayrollView, PolicyNames.ApprovalView,
                PolicyNames.ApprovalAct, PolicyNames.ApprovalHistoryView,
                PolicyNames.PayrollApprove),
            [RoleNames.Manager] = Set(
                PolicyNames.DashboardAccess, PolicyNames.EmployeeView,
                PolicyNames.MasterDataRead,
                PolicyNames.EmployeeSelfService, PolicyNames.LeaveRequestSelfService,
                PolicyNames.LeaveRequestApprove, PolicyNames.ParentalLeaveSelfService,
                PolicyNames.ParentalLeaveApprove,
                PolicyNames.AttendanceExceptionSelfService,
                PolicyNames.AttendanceExceptionApprove,
                PolicyNames.CompanyCalendarRead, PolicyNames.AttendancePunchRecordRead,
                PolicyNames.AttendanceDailyRead, PolicyNames.AttendanceSelfService,
                PolicyNames.OvertimeSelfService, PolicyNames.PayslipViewSelf),
            [RoleNames.Employee] = Set(
                PolicyNames.DashboardAccess, PolicyNames.EmployeeSelfService,
                PolicyNames.LeaveRequestSelfService,
                PolicyNames.ParentalLeaveSelfService,
                PolicyNames.AttendanceExceptionSelfService,
                PolicyNames.CompanyCalendarRead, PolicyNames.AttendancePunchRecordRead,
                PolicyNames.AttendanceDailyRead, PolicyNames.AttendanceSelfService,
                PolicyNames.OvertimeSelfService, PolicyNames.PayslipViewSelf)
        };

    public static bool HasPermission(IEnumerable<string> roles, string permission) =>
        roles.Distinct(StringComparer.Ordinal).Any(role =>
            Matrix.TryGetValue(role, out var permissions) &&
            permissions.Contains(permission));

    public static bool HasPermission(ClaimsPrincipal user, string permission) =>
        user.Identity?.IsAuthenticated == true && HasPermission(
            user.FindAll(ClaimTypes.Role).Select(claim => claim.Value), permission);

    public static IReadOnlySet<string> ForRole(string role) =>
        Matrix.TryGetValue(role, out var permissions)
            ? permissions
            : new HashSet<string>(StringComparer.Ordinal);

    private static IReadOnlySet<string> Set(params string[] permissions) =>
        new HashSet<string>(permissions, StringComparer.Ordinal);
}
