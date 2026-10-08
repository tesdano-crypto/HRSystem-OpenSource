namespace HRSystem.Application.Security;

public sealed record PermissionDisplay(string Code, string Name, string Group);

public static class SecurityDisplayCatalog
{
    public static IReadOnlyList<PermissionDisplay> Permissions { get; } =
    [
        new(PolicyNames.EmployeeView, "查看員工資料", "人事"),
        new(PolicyNames.EmployeeManage, "員工資料管理", "人事"),
        new(PolicyNames.AttendanceViewAll, "查看全員出勤紀錄", "出勤"),
        new(PolicyNames.AttendanceDailyReport, "每日出勤報表", "出勤"),
        new(PolicyNames.AttendanceManage, "出勤資料管理", "出勤"),
        new(PolicyNames.LeaveView, "查看請假資料", "請假"),
        new(PolicyNames.LeaveManage, "請假管理", "請假"),
        new(PolicyNames.OvertimeView, "查看加班資料", "加班"),
        new(PolicyNames.OvertimeManage, "加班管理", "加班"),
        new(PolicyNames.InsuranceView, "查看勞健保資料", "勞健保"),
        new(PolicyNames.InsuranceManage, "勞健保管理", "勞健保"),
        new(PolicyNames.PayrollView, "薪資查看", "薪資"),
        new(PolicyNames.PayrollManage, "薪資管理", "薪資"),
        new(PolicyNames.PayrollSubmitApproval, "送出薪資簽核", "薪資"),
        new(PolicyNames.PayrollApprove, "薪資結算核准", "薪資"),
        new(PolicyNames.PayrollFinalize, "薪資正式結算", "薪資"),
        new(PolicyNames.PayslipViewSelf, "查看本人薪資單", "薪資"),
        new(PolicyNames.ApprovalView, "查看待簽核事項", "簽核"),
        new(PolicyNames.ApprovalAct, "執行簽核", "簽核"),
        new(PolicyNames.ApprovalHistoryView, "歷史簽核查詢", "簽核"),
        new(PolicyNames.UserAdmin, "使用者與權限管理", "系統管理"),
        new(PolicyNames.SystemAdmin, "系統管理", "系統管理")
    ];

    public static string RoleName(string role) => role switch
    {
        RoleNames.Admin => "系統管理員",
        RoleNames.HR => "人資",
        RoleNames.Accounting => "會計",
        RoleNames.Owner => "老闆",
        RoleNames.Manager => "主管",
        RoleNames.Employee => "一般員工",
        _ => "未知角色"
    };

    public static string PermissionName(string code) =>
        Permissions.FirstOrDefault(item => item.Code == code)?.Name ?? "未知權限";
}
