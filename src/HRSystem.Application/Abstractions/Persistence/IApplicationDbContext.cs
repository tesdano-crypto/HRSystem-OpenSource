using System.Data;
using HRSystem.Domain.CompanyCalendars;
using HRSystem.Domain.Auditing;
using HRSystem.Domain.Attendance;
using HRSystem.Domain.LeaveRequests;
using HRSystem.Domain.MasterData;
using HRSystem.Domain.ParentalLeave;
using HRSystem.Domain.AttendanceExceptions;
using HRSystem.Domain.AnnualLeave;
using HRSystem.Domain.Overtime;
using HRSystem.Domain.Payroll;
using HRSystem.Domain.Approvals;
using HRSystem.Domain.CompTime;
using Microsoft.EntityFrameworkCore;

namespace HRSystem.Application.Abstractions.Persistence;

public interface IApplicationDbContext
{
    DbSet<Department> Departments { get; }
    DbSet<Employee> Employees { get; }
    DbSet<LeaveType> LeaveTypes { get; }
    DbSet<LeaveRequest> LeaveRequests { get; }
    DbSet<LeaveApprovalHistory> LeaveApprovalHistories { get; }
    DbSet<ParentalLeaveRequest> ParentalLeaveRequests { get; }
    DbSet<ParentalLeaveApprovalHistory> ParentalLeaveApprovalHistories { get; }
    DbSet<AttendanceException> AttendanceExceptions { get; }
    DbSet<AttendanceExceptionHistory> AttendanceExceptionHistories { get; }
    DbSet<AuditLog> AuditLogs { get; }
    DbSet<CompanyCalendarYear> CompanyCalendarYears { get; }
    DbSet<CompanyCalendarDay> CompanyCalendarDays { get; }
    DbSet<BioWebPersonMapping> BioWebPersonMappings { get; }
    DbSet<AttendanceRawEvent> AttendanceRawEvents { get; }
    DbSet<BioWebTaImportBatch> BioWebTaImportBatches { get; }
    DbSet<BioWebTaImportBatchIssue> BioWebTaImportBatchIssues { get; }
    DbSet<AttendanceSyncState> AttendanceSyncStates { get; }
    DbSet<AttendanceShift> AttendanceShifts { get; }
    DbSet<EmployeeShiftAssignment> EmployeeShiftAssignments { get; }
    DbSet<DailyAttendanceResult> DailyAttendanceResults { get; }
    DbSet<DailyAttendanceLeaveSegment> DailyAttendanceLeaveSegments { get; }
    DbSet<AttendanceReviewResolution> AttendanceReviewResolutions { get; }
    DbSet<AttendanceReviewResolutionHistory> AttendanceReviewResolutionHistories { get; }
    DbSet<AttendanceAdjustment> AttendanceAdjustments { get; }
    DbSet<AttendanceCorrectionRequest> AttendanceCorrectionRequests { get; }
    DbSet<AttendanceCorrectionRequestHistory> AttendanceCorrectionRequestHistories { get; }
    DbSet<AnnualLeaveEntitlement> AnnualLeaveEntitlements { get; }
    DbSet<AnnualLeaveAllocation> AnnualLeaveAllocations { get; }
    DbSet<AnnualLeaveCarryForward> AnnualLeaveCarryForwards { get; }
    DbSet<CompTimeTransaction> CompTimeTransactions { get; }
    DbSet<TrainingCompTimeGrant> TrainingCompTimeGrants { get; }
    DbSet<TrainingCompTimeHistory> TrainingCompTimeHistories { get; }
    DbSet<CompTimeLegacyPool> CompTimeLegacyPools { get; }
    DbSet<CompTimeLegacyMember> CompTimeLegacyMembers { get; }
    DbSet<CompTimeLegacyReturn> CompTimeLegacyReturns { get; }
    DbSet<CompTimeAllocation> CompTimeAllocations { get; }
    DbSet<CompTimeAllocationReturn> CompTimeAllocationReturns { get; }
    DbSet<OvertimeRequest> OvertimeRequests { get; }
    DbSet<OvertimeRequestHistory> OvertimeRequestHistories { get; }
    DbSet<OvertimeRecognition> OvertimeRecognitions { get; }
    DbSet<OvertimeRecognitionHistory> OvertimeRecognitionHistories { get; }
    DbSet<PayrollComponentDefinition> PayrollComponentDefinitions { get; }
    DbSet<PayrollPlan> PayrollPlans { get; }
    DbSet<PayrollPlanComponent> PayrollPlanComponents { get; }
    DbSet<PayrollSeniorityTier> PayrollSeniorityTiers { get; }
    DbSet<EmployeePayrollAssignment> EmployeePayrollAssignments { get; }
    DbSet<EmployeePayrollComponentOverride> EmployeePayrollComponentOverrides { get; }
    DbSet<EmployeePayrollPayCycle> EmployeePayrollPayCycles { get; }
    DbSet<PayrollPeriod> PayrollPeriods { get; }
    DbSet<PayrollRun> PayrollRuns { get; }
    DbSet<PayrollAdjustment> PayrollAdjustments { get; }
    DbSet<PayrollEmployeeSnapshot> PayrollEmployeeSnapshots { get; }
    DbSet<PayrollPeriodEmployeeCurrentSnapshot> PayrollPeriodEmployeeCurrentSnapshots { get; }
    DbSet<PayrollEmployeeSnapshotComponent> PayrollEmployeeSnapshotComponents { get; }
    DbSet<PayrollTotalBlockingEvidence> PayrollTotalBlockingEvidence { get; }
    DbSet<PayrollFinalization> PayrollFinalizations { get; }
    DbSet<PayrollFinalEmployeeSnapshot> PayrollFinalEmployeeSnapshots { get; }
    DbSet<PayrollLeaveDeductionPolicy> PayrollLeaveDeductionPolicies { get; }
    DbSet<PayrollAttendanceAllowanceSnapshot> PayrollAttendanceAllowanceSnapshots { get; }
    DbSet<PayrollAttendanceAllowanceEvidence> PayrollAttendanceAllowanceEvidence { get; }
    DbSet<PayrollLeaveDeductionSnapshot> PayrollLeaveDeductionSnapshots { get; }
    DbSet<PayrollLeaveDeductionEvidence> PayrollLeaveDeductionEvidence { get; }
    DbSet<OvertimePayRatePolicy> OvertimePayRatePolicies { get; }
    DbSet<PayrollOvertimePaySnapshot> PayrollOvertimePaySnapshots { get; }
    DbSet<PayrollOvertimeBaseComponentEvidence> PayrollOvertimeBaseComponentEvidence { get; }
    DbSet<PayrollOvertimeBucketEvidence> PayrollOvertimeBucketEvidence { get; }
    DbSet<PayrollOvertimeDayEvidence> PayrollOvertimeDayEvidence { get; }
    DbSet<PayrollOvertimeRecognitionEvidence> PayrollOvertimeRecognitionEvidence { get; }
    DbSet<EmployeeLaborInsuranceEnrollment> EmployeeLaborInsuranceEnrollments { get; }
    DbSet<EmployeeOccupationalInsuranceEnrollment> EmployeeOccupationalInsuranceEnrollments { get; }
    DbSet<LaborInsuranceRatePolicy> LaborInsuranceRatePolicies { get; }
    DbSet<PayrollLaborInsuranceSnapshot> PayrollLaborInsuranceSnapshots { get; }
    DbSet<PayrollLaborInsuranceContributionEvidence> PayrollLaborInsuranceContributionEvidence { get; }
    DbSet<PayrollPeriodicAccrualSnapshot> PayrollPeriodicAccrualSnapshots { get; }
    DbSet<PayrollPeriodicAccrualMonthEvidence> PayrollPeriodicAccrualMonthEvidence { get; }
    DbSet<EmployeeHealthInsuranceEnrollment> EmployeeHealthInsuranceEnrollments { get; }
    DbSet<HealthInsuranceRatePolicy> HealthInsuranceRatePolicies { get; }
    DbSet<PayrollHealthInsuranceSnapshot> PayrollHealthInsuranceSnapshots { get; }
    DbSet<PayrollHealthInsuranceEvidence> PayrollHealthInsuranceEvidence { get; }
    DbSet<Approval> Approvals { get; }
    DbSet<ApprovalHistory> ApprovalHistories { get; }
    DbSet<ApprovalLineActionToken> ApprovalLineActionTokens { get; }
    DbSet<LineUserBinding> LineUserBindings { get; }
    DbSet<LinePairingRequest> LinePairingRequests { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    void ClearTrackedChanges();

    bool IsUniqueConstraintViolation(DbUpdateException exception, string expectedConstraintOrIndexName);

    Task ExecuteSerializableAsync(
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken = default);

    Task<T> ExecuteSerializableAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken = default);

    Task ExecuteTransactionAsync(
        IsolationLevel isolationLevel,
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken = default);

    Task<T> ExecuteTransactionAsync<T>(
        IsolationLevel isolationLevel,
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken = default);
}
