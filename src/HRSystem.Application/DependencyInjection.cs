using HRSystem.Application.AuditLogs;
using HRSystem.Application.Attendance;
using HRSystem.Application.Dashboard;
using HRSystem.Application.CompanyCalendars;
using HRSystem.Application.Departments;
using HRSystem.Application.Employees;
using HRSystem.Application.LeaveTypes;
using HRSystem.Application.LeaveRequests;
using HRSystem.Application.ParentalLeave;
using HRSystem.Application.AttendanceExceptions;
using HRSystem.Application.AnnualLeave;
using HRSystem.Application.Overtime;
using HRSystem.Application.Payroll;
using HRSystem.Application.Approvals;
using HRSystem.Application.CompTime;
using Microsoft.Extensions.DependencyInjection;

namespace HRSystem.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<IDepartmentService, DepartmentService>();
        services.AddScoped<IDashboardService, DashboardService>();
        services.AddScoped<IEmployeeDashboardService, EmployeeDashboardService>();
        services.AddScoped<IEmployeeService, EmployeeService>();
        services.AddScoped<ILeaveTypeService, LeaveTypeService>();
        services.AddScoped<ILeaveDurationCalculator, LeaveDurationCalculator>();
        services.AddScoped<ILeaveRequestService, LeaveRequestService>();
        services.AddScoped<IAnnualLeaveService, AnnualLeaveService>();
        services.AddScoped<ICompTimeService, CompTimeService>();
        services.AddScoped<IParentalLeaveService, ParentalLeaveService>();
        services.AddScoped<IAttendanceExceptionService, AttendanceExceptionService>();
        services.AddScoped<IAuditLogService, AuditLogService>();
        services.AddScoped<ICompanyCalendarService, CompanyCalendarService>();
        services.AddScoped<ICompanyCalendarQueryService, CompanyCalendarQueryService>();
        services.AddScoped<IBioWebPersonMappingService, BioWebPersonMappingService>();
        services.AddScoped<IAttendanceImportService, AttendanceImportService>();
        services.AddScoped<IBioWebTaImportCoordinator, BioWebTaImportCoordinator>();
        services.AddScoped<IBioWebTaImportStatusService,
            BioWebTaImportStatusService>();
        services.AddScoped<IAttendancePunchRecordService, AttendancePunchRecordService>();
        services.AddScoped<IAttendanceRecalculationEngine, AttendanceRecalculationEngine>();
        services.AddScoped<IAttendanceManagementService, AttendanceManagementService>();
        services.AddScoped<IAttendanceReviewService, AttendanceReviewService>();
        services.AddScoped<IAttendanceExcelReportService, AttendanceExcelReportService>();
        services.AddScoped<IAttendanceCorrectionService,
            AttendanceCorrectionService>();
        services.AddScoped<IOvertimeRequestService, OvertimeRequestService>();
        services.AddScoped<IOvertimeRecognitionService, OvertimeRecognitionService>();
        services.AddScoped<IPayrollService, PayrollService>();
        services.AddScoped<IInsuranceManagementService, InsuranceManagementService>();
        services.AddScoped<IPayrollFinalizationService, PayrollFinalizationService>();
        services.AddScoped<IApprovalSourceProvider, PayrollApprovalSourceProvider>();
        services.AddScoped<IApprovalService, ApprovalService>();
        services.AddScoped<ILineUserBindingService, LineUserBindingService>();
        services.AddScoped<ILinePrivateTargetResolver, LinePrivateTargetResolver>();
        return services;
    }
}
