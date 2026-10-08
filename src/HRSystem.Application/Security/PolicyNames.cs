namespace HRSystem.Application.Security;

public static class PolicyNames
{
    public const string EmployeeView = "EmployeeView";
    public const string EmployeeManage = "EmployeeManage";
    public const string AttendanceViewAll = "AttendanceViewAll";
    public const string AttendanceDailyReport = "AttendanceDailyReport";
    public const string LeaveView = "LeaveView";
    public const string LeaveManage = "LeaveManage";
    public const string OvertimeView = "OvertimeView";
    public const string InsuranceView = "InsuranceView";
    public const string InsuranceManage = "InsuranceManage";
    public const string PayrollView = "PayrollView";
    public const string PayrollSubmitApproval = "PayrollSubmitApproval";
    public const string PayrollApprove = "PayrollApprove";
    public const string PayrollFinalize = "PayrollFinalize";
    public const string PayslipViewSelf = "PayslipViewSelf";
    public const string ApprovalView = "ApprovalView";
    public const string ApprovalAct = "ApprovalAct";
    public const string ApprovalHistoryView = "ApprovalHistoryView";
    public const string UserAdmin = "UserAdmin";
    public const string SystemAdmin = "SystemAdmin";

    public const string DashboardAccess = "DashboardAccess";
    public const string MasterDataRead = "MasterDataRead";
    public const string MasterDataManage = "MasterDataManage";
    public const string UserAccountManage = "UserAccountManage";
    public const string AuditLogRead = "AuditLogRead";
    public const string EmployeeSelfService = "EmployeeSelfService";
    public const string LeaveRequestSelfService = "LeaveRequestSelfService";
    public const string LeaveRequestApprove = "LeaveRequestApprove";
    public const string LeaveRequestReadAll = "LeaveRequestReadAll";
    public const string ParentalLeaveSelfService = "ParentalLeaveSelfService";
    public const string ParentalLeaveApprove = "ParentalLeaveApprove";
    public const string AttendanceExceptionSelfService = "AttendanceExceptionSelfService";
    public const string AttendanceExceptionApprove = "AttendanceExceptionApprove";
    public const string CompanyCalendarRead = "CompanyCalendarRead";
    public const string CompanyCalendarManage = "CompanyCalendarManage";
    public const string AttendanceImportManage = "AttendanceImportManage";
    public const string AttendancePunchRecordRead = "AttendancePunchRecordRead";
    public const string AttendanceDailyRead = "AttendanceDailyRead";
    public const string AttendanceSelfService = "AttendanceSelfService";
    public const string AttendanceManage = "AttendanceManage";
    public const string AttendanceAdjust = "AttendanceAdjust";
    public const string OvertimeSelfService = "OvertimeSelfService";
    public const string OvertimeManage = "OvertimeManage";
    public const string PayrollManage = "PayrollManage";

    public static IReadOnlySet<string> All { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        EmployeeView, EmployeeManage,
        AttendanceViewAll, AttendanceDailyReport, AttendanceManage,
        LeaveView, LeaveManage,
        OvertimeView, OvertimeManage,
        InsuranceView, InsuranceManage,
        PayrollView, PayrollManage, PayrollSubmitApproval, PayrollApprove,
        PayrollFinalize, PayslipViewSelf,
        ApprovalView, ApprovalAct, ApprovalHistoryView,
        UserAdmin, SystemAdmin,
        DashboardAccess, MasterDataRead, MasterDataManage, UserAccountManage,
        AuditLogRead, EmployeeSelfService, LeaveRequestSelfService,
        LeaveRequestApprove, LeaveRequestReadAll, ParentalLeaveSelfService,
        ParentalLeaveApprove, AttendanceExceptionSelfService,
        AttendanceExceptionApprove, CompanyCalendarRead, CompanyCalendarManage,
        AttendanceImportManage, AttendancePunchRecordRead, AttendanceDailyRead,
        AttendanceSelfService, AttendanceAdjust,
        OvertimeSelfService
    };
}
