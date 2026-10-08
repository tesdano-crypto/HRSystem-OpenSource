using HRSystem.Application.Security;

namespace HRSystem.Web.Components.Layout;

public enum AppNavigationIcon
{
    Home,
    Attendance,
    Overtime,
    Leave,
    People,
    Settings
}

public sealed record AppNavigationItem(
    string Label,
    string Route,
    string RequiredPermission,
    bool RequiresEmployeeBinding = false);

public sealed record AppNavigationSection(
    string Id,
    string Label,
    AppNavigationIcon Icon,
    IReadOnlyList<AppNavigationItem> Items);

public static class AppNavigationCatalog
{
    public static IReadOnlyList<AppNavigationSection> Sections { get; } =
    [
        new(
            "workspace",
            "我的工作台",
            AppNavigationIcon.Home,
            [
                new("工作儀表板", "/", PolicyNames.DashboardAccess),
                new("我的首頁", "/my", PolicyNames.EmployeeSelfService, true),
                new("我的出勤", "/my-attendance", PolicyNames.AttendanceSelfService, true),
                new("我的加班", "/my-overtime", PolicyNames.OvertimeSelfService, true),
                new("我的薪資單", "/my-payslips", PolicyNames.PayslipViewSelf, true),
                new("我的出勤申請", "/my-attendance-requests", PolicyNames.AttendanceSelfService, true),
                new("我的請假", "/leave-requests", PolicyNames.LeaveRequestSelfService, true),
                new("我的特休", "/annual-leave", PolicyNames.LeaveRequestSelfService, true),
                new("我的補休", "/my-comp-time", PolicyNames.LeaveRequestSelfService, true),
                new("育嬰留職停薪", "/parental-leave", PolicyNames.ParentalLeaveSelfService, true),
                new("天然災害出勤豁免", "/attendance-exceptions", PolicyNames.AttendanceExceptionSelfService, true),
                new("打卡紀錄", "/attendance/punch-records", PolicyNames.AttendancePunchRecordRead),
                new("每日出勤", "/attendance/daily", PolicyNames.AttendanceDailyRead),
                new("公司行事曆", "/company-calendar", PolicyNames.CompanyCalendarRead),
                new("我的資料", "/account/profile", PolicyNames.EmployeeSelfService),
                new("變更密碼", "/account/change-password", PolicyNames.EmployeeSelfService)
            ]),
        new(
            "attendance",
            "出勤管理",
            AppNavigationIcon.Attendance,
            [
                new("出勤檢核", "/attendance-review", PolicyNames.AttendanceManage),
                new("出勤異常申請審核", "/admin/attendance-requests", PolicyNames.AttendanceManage),
                new("班別設定", "/admin/attendance/shifts", PolicyNames.AttendanceManage),
                new("員工班別指派", "/admin/attendance/shift-assignments", PolicyNames.AttendanceManage),
                new("BioWebTA 對照", "/admin/biowebta-mappings", PolicyNames.AttendanceImportManage),
                new("BioWebTA 出勤匯入", "/admin/attendance-import", PolicyNames.AttendanceImportManage),
            ]),
        new(
            "overtime",
            "加班管理",
            AppNavigationIcon.Overtime,
            [
                new("加班管理", "/admin/overtime", PolicyNames.OvertimeManage)
            ]),
        new(
            "leave",
            "請假管理",
            AppNavigationIcon.Leave,
            [
                new("請假審核", "/approvals/leave", PolicyNames.LeaveRequestApprove),
                new("育嬰留停審核", "/parental-leave/approvals", PolicyNames.ParentalLeaveApprove),
                new("出勤豁免審核", "/attendance-exceptions/approvals", PolicyNames.AttendanceExceptionApprove),
                new("全部請假", "/admin/leave-requests", PolicyNames.LeaveView),
                new("特休額度管理", "/admin/annual-leave-entitlements", PolicyNames.LeaveManage),
                new("補休管理", "/admin/comp-time", PolicyNames.LeaveManage)
            ]),
        new(
            "master-data",
            "人員與基本資料",
            AppNavigationIcon.People,
            [
                new("部門管理", "/departments", PolicyNames.EmployeeView),
                new("員工資料", "/employees", PolicyNames.EmployeeView),
                new("行事曆管理", "/admin/company-calendar", PolicyNames.CompanyCalendarManage),
                new("假別設定", "/leave-types", PolicyNames.LeaveManage)
            ]),
        new(
            "payroll",
            "薪資管理",
            AppNavigationIcon.Settings,
            [
                new("薪資月份", "/admin/payroll", PolicyNames.PayrollView),
                new("薪資歷史", "/admin/payroll/history", PolicyNames.PayrollView),
                new("員工薪資設定", "/admin/payroll/employees", PolicyNames.PayrollView),
                new("保險投保設定", "/admin/insurance", PolicyNames.InsuranceView),
                new("薪資規則", "/admin/payroll/settings", PolicyNames.PayrollView),
                new("簽核中心", "/approvals", PolicyNames.ApprovalView),
                new("LINE 私人帳號", "/admin/line-bindings", PolicyNames.ApprovalAct)
            ]),
        new(
            "system",
            "系統管理",
            AppNavigationIcon.Settings,
            [
                new("帳號管理", "/admin/users", PolicyNames.UserAdmin),
                new("稽核紀錄", "/audit-logs", PolicyNames.SystemAdmin)
            ])
    ];
}
