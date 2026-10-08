using System.Data;
using System.Runtime.ExceptionServices;
using HRSystem.Application.Abstractions.Persistence;
using HRSystem.Domain.Auditing;
using HRSystem.Domain.Attendance;
using HRSystem.Domain.CompanyCalendars;
using HRSystem.Domain.LeaveRequests;
using HRSystem.Domain.MasterData;
using HRSystem.Domain.ParentalLeave;
using HRSystem.Domain.AttendanceExceptions;
using HRSystem.Domain.AnnualLeave;
using HRSystem.Domain.Overtime;
using HRSystem.Domain.Payroll;
using HRSystem.Domain.Approvals;
using HRSystem.Domain.CompTime;
using HRSystem.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage;

namespace HRSystem.Infrastructure.Persistence;

public sealed class HRSystemDbContext(DbContextOptions<HRSystemDbContext> options)
    : IdentityDbContext<ApplicationUser>(options), IApplicationDbContext
{
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<LeaveType> LeaveTypes => Set<LeaveType>();
    public DbSet<LeaveRequest> LeaveRequests => Set<LeaveRequest>();
    public DbSet<LeaveApprovalHistory> LeaveApprovalHistories => Set<LeaveApprovalHistory>();
    public DbSet<ParentalLeaveRequest> ParentalLeaveRequests =>
        Set<ParentalLeaveRequest>();
    public DbSet<ParentalLeaveApprovalHistory> ParentalLeaveApprovalHistories =>
        Set<ParentalLeaveApprovalHistory>();
    public DbSet<AttendanceException> AttendanceExceptions => Set<AttendanceException>();
    public DbSet<AttendanceExceptionHistory> AttendanceExceptionHistories => Set<AttendanceExceptionHistory>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<CompanyCalendarYear> CompanyCalendarYears => Set<CompanyCalendarYear>();
    public DbSet<CompanyCalendarDay> CompanyCalendarDays => Set<CompanyCalendarDay>();
    public DbSet<BioWebPersonMapping> BioWebPersonMappings => Set<BioWebPersonMapping>();
    public DbSet<AttendanceRawEvent> AttendanceRawEvents => Set<AttendanceRawEvent>();
    public DbSet<BioWebTaImportBatch> BioWebTaImportBatches =>
        Set<BioWebTaImportBatch>();
    public DbSet<BioWebTaImportBatchIssue> BioWebTaImportBatchIssues =>
        Set<BioWebTaImportBatchIssue>();
    public DbSet<AttendanceSyncState> AttendanceSyncStates => Set<AttendanceSyncState>();
    public DbSet<AttendanceShift> AttendanceShifts => Set<AttendanceShift>();
    public DbSet<EmployeeShiftAssignment> EmployeeShiftAssignments =>
        Set<EmployeeShiftAssignment>();
    public DbSet<DailyAttendanceResult> DailyAttendanceResults =>
        Set<DailyAttendanceResult>();
    public DbSet<DailyAttendanceLeaveSegment> DailyAttendanceLeaveSegments =>
        Set<DailyAttendanceLeaveSegment>();
    public DbSet<AttendanceReviewResolution> AttendanceReviewResolutions =>
        Set<AttendanceReviewResolution>();
    public DbSet<AttendanceReviewResolutionHistory> AttendanceReviewResolutionHistories =>
        Set<AttendanceReviewResolutionHistory>();
    public DbSet<AttendanceAdjustment> AttendanceAdjustments =>
        Set<AttendanceAdjustment>();
    public DbSet<AttendanceCorrectionRequest> AttendanceCorrectionRequests =>
        Set<AttendanceCorrectionRequest>();
    public DbSet<AttendanceCorrectionRequestHistory> AttendanceCorrectionRequestHistories =>
        Set<AttendanceCorrectionRequestHistory>();
    public DbSet<AnnualLeaveEntitlement> AnnualLeaveEntitlements =>
        Set<AnnualLeaveEntitlement>();
    public DbSet<AnnualLeaveAllocation> AnnualLeaveAllocations =>
        Set<AnnualLeaveAllocation>();
    public DbSet<AnnualLeaveCarryForward> AnnualLeaveCarryForwards =>
        Set<AnnualLeaveCarryForward>();
    public DbSet<CompTimeTransaction> CompTimeTransactions =>
        Set<CompTimeTransaction>();
    public DbSet<TrainingCompTimeGrant> TrainingCompTimeGrants => Set<TrainingCompTimeGrant>();
    public DbSet<TrainingCompTimeHistory> TrainingCompTimeHistories => Set<TrainingCompTimeHistory>();
    public DbSet<CompTimeLegacyPool> CompTimeLegacyPools => Set<CompTimeLegacyPool>();
    public DbSet<CompTimeLegacyMember> CompTimeLegacyMembers => Set<CompTimeLegacyMember>();
    public DbSet<CompTimeLegacyReturn> CompTimeLegacyReturns => Set<CompTimeLegacyReturn>();
    public DbSet<CompTimeAllocation> CompTimeAllocations => Set<CompTimeAllocation>();
    public DbSet<CompTimeAllocationReturn> CompTimeAllocationReturns => Set<CompTimeAllocationReturn>();
    public DbSet<OvertimeRequest> OvertimeRequests => Set<OvertimeRequest>();
    public DbSet<OvertimeRequestHistory> OvertimeRequestHistories =>
        Set<OvertimeRequestHistory>();
    public DbSet<OvertimeRecognition> OvertimeRecognitions =>
        Set<OvertimeRecognition>();
    public DbSet<OvertimeRecognitionHistory> OvertimeRecognitionHistories =>
        Set<OvertimeRecognitionHistory>();
    public DbSet<PayrollComponentDefinition> PayrollComponentDefinitions => Set<PayrollComponentDefinition>();
    public DbSet<PayrollPlan> PayrollPlans => Set<PayrollPlan>();
    public DbSet<PayrollPlanComponent> PayrollPlanComponents => Set<PayrollPlanComponent>();
    public DbSet<PayrollSeniorityTier> PayrollSeniorityTiers => Set<PayrollSeniorityTier>();
    public DbSet<EmployeePayrollAssignment> EmployeePayrollAssignments => Set<EmployeePayrollAssignment>();
    public DbSet<EmployeePayrollComponentOverride> EmployeePayrollComponentOverrides => Set<EmployeePayrollComponentOverride>();
    public DbSet<EmployeePayrollPayCycle> EmployeePayrollPayCycles => Set<EmployeePayrollPayCycle>();
    public DbSet<PayrollPeriod> PayrollPeriods => Set<PayrollPeriod>();
    public DbSet<PayrollRun> PayrollRuns => Set<PayrollRun>();
    public DbSet<PayrollAdjustment> PayrollAdjustments => Set<PayrollAdjustment>();
    public DbSet<PayrollEmployeeSnapshot> PayrollEmployeeSnapshots => Set<PayrollEmployeeSnapshot>();
    public DbSet<PayrollPeriodEmployeeCurrentSnapshot> PayrollPeriodEmployeeCurrentSnapshots => Set<PayrollPeriodEmployeeCurrentSnapshot>();
    public DbSet<PayrollEmployeeSnapshotComponent> PayrollEmployeeSnapshotComponents => Set<PayrollEmployeeSnapshotComponent>();
    public DbSet<PayrollTotalBlockingEvidence> PayrollTotalBlockingEvidence => Set<PayrollTotalBlockingEvidence>();
    public DbSet<PayrollFinalization> PayrollFinalizations => Set<PayrollFinalization>();
    public DbSet<PayrollFinalEmployeeSnapshot> PayrollFinalEmployeeSnapshots => Set<PayrollFinalEmployeeSnapshot>();
    public DbSet<PayrollLeaveDeductionPolicy> PayrollLeaveDeductionPolicies => Set<PayrollLeaveDeductionPolicy>();
    public DbSet<PayrollAttendanceAllowanceSnapshot> PayrollAttendanceAllowanceSnapshots => Set<PayrollAttendanceAllowanceSnapshot>();
    public DbSet<PayrollAttendanceAllowanceEvidence> PayrollAttendanceAllowanceEvidence => Set<PayrollAttendanceAllowanceEvidence>();
    public DbSet<PayrollLeaveDeductionSnapshot> PayrollLeaveDeductionSnapshots => Set<PayrollLeaveDeductionSnapshot>();
    public DbSet<PayrollLeaveDeductionEvidence> PayrollLeaveDeductionEvidence => Set<PayrollLeaveDeductionEvidence>();
    public DbSet<OvertimePayRatePolicy> OvertimePayRatePolicies => Set<OvertimePayRatePolicy>();
    public DbSet<PayrollOvertimePaySnapshot> PayrollOvertimePaySnapshots => Set<PayrollOvertimePaySnapshot>();
    public DbSet<PayrollOvertimeBaseComponentEvidence> PayrollOvertimeBaseComponentEvidence => Set<PayrollOvertimeBaseComponentEvidence>();
    public DbSet<PayrollOvertimeBucketEvidence> PayrollOvertimeBucketEvidence => Set<PayrollOvertimeBucketEvidence>();
    public DbSet<PayrollOvertimeDayEvidence> PayrollOvertimeDayEvidence => Set<PayrollOvertimeDayEvidence>();
    public DbSet<PayrollOvertimeRecognitionEvidence> PayrollOvertimeRecognitionEvidence => Set<PayrollOvertimeRecognitionEvidence>();
    public DbSet<EmployeeLaborInsuranceEnrollment> EmployeeLaborInsuranceEnrollments => Set<EmployeeLaborInsuranceEnrollment>();
    public DbSet<EmployeeOccupationalInsuranceEnrollment> EmployeeOccupationalInsuranceEnrollments => Set<EmployeeOccupationalInsuranceEnrollment>();
    public DbSet<LaborInsuranceRatePolicy> LaborInsuranceRatePolicies => Set<LaborInsuranceRatePolicy>();
    public DbSet<PayrollLaborInsuranceSnapshot> PayrollLaborInsuranceSnapshots => Set<PayrollLaborInsuranceSnapshot>();
    public DbSet<PayrollLaborInsuranceContributionEvidence> PayrollLaborInsuranceContributionEvidence => Set<PayrollLaborInsuranceContributionEvidence>();
    public DbSet<PayrollPeriodicAccrualSnapshot> PayrollPeriodicAccrualSnapshots => Set<PayrollPeriodicAccrualSnapshot>();
    public DbSet<PayrollPeriodicAccrualMonthEvidence> PayrollPeriodicAccrualMonthEvidence => Set<PayrollPeriodicAccrualMonthEvidence>();
    public DbSet<EmployeeHealthInsuranceEnrollment> EmployeeHealthInsuranceEnrollments => Set<EmployeeHealthInsuranceEnrollment>();
    public DbSet<HealthInsuranceRatePolicy> HealthInsuranceRatePolicies => Set<HealthInsuranceRatePolicy>();
    public DbSet<PayrollHealthInsuranceSnapshot> PayrollHealthInsuranceSnapshots => Set<PayrollHealthInsuranceSnapshot>();
    public DbSet<PayrollHealthInsuranceEvidence> PayrollHealthInsuranceEvidence => Set<PayrollHealthInsuranceEvidence>();
    public DbSet<Approval> Approvals => Set<Approval>();
    public DbSet<ApprovalHistory> ApprovalHistories => Set<ApprovalHistory>();
    public DbSet<ApprovalLineActionToken> ApprovalLineActionTokens => Set<ApprovalLineActionToken>();
    public DbSet<LineUserBinding> LineUserBindings => Set<LineUserBinding>();
    public DbSet<LinePairingRequest> LinePairingRequests => Set<LinePairingRequest>();

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        if (ChangeTracker.Entries<ApprovalHistory>()
            .Any(x => x.State is EntityState.Modified or EntityState.Deleted))
        {
            throw new InvalidOperationException(
                "共用簽核歷程只能新增，不可修改或刪除。");
        }

        if (ChangeTracker.Entries<ApprovalLineActionToken>()
            .Any(x => x.State == EntityState.Deleted))
        {
            throw new InvalidOperationException("LINE 簽核 token 不可刪除。");
        }

        if (ChangeTracker.Entries<LinePairingRequest>()
            .Any(x => x.State == EntityState.Deleted))
        {
            throw new InvalidOperationException("LINE 配對要求不可刪除。");
        }

        if (ChangeTracker.Entries<LineUserBinding>()
            .Any(x => x.State == EntityState.Deleted))
        {
            throw new InvalidOperationException("LINE 綁定不可刪除。");
        }

        if (ChangeTracker.Entries<LeaveApprovalHistory>()
            .Any(x => x.State is EntityState.Modified or EntityState.Deleted))
        {
            throw new InvalidOperationException("簽核歷程只能新增，不可修改或刪除。");
        }

        if (ChangeTracker.Entries<ParentalLeaveApprovalHistory>()
            .Any(x => x.State is EntityState.Modified or EntityState.Deleted))
        {
            throw new InvalidOperationException(
                "育嬰留停簽核歷程只能新增，不可修改或刪除。");
        }

        if (ChangeTracker.Entries<AttendanceExceptionHistory>()
            .Any(entry => entry.State is EntityState.Modified or EntityState.Deleted))
        {
            throw new InvalidOperationException("出勤豁免歷程只能新增，不得修改或刪除。");
        }

        if (ChangeTracker.Entries<AttendanceReviewResolutionHistory>()
            .Any(entry => entry.State is EntityState.Modified or EntityState.Deleted))
        {
            throw new InvalidOperationException(
                "出勤檢核結案歷程只能新增，不得修改或刪除。");
        }

        if (ChangeTracker.Entries<OvertimeRequestHistory>()
            .Any(entry => entry.State is EntityState.Modified or EntityState.Deleted))
        {
            throw new InvalidOperationException(
                "加班申請歷程只能新增，不得修改或刪除。");
        }

        if (ChangeTracker.Entries<OvertimeRecognitionHistory>()
            .Any(entry => entry.State is EntityState.Modified or EntityState.Deleted))
        {
            throw new InvalidOperationException(
                "加班認列歷程只能新增，不得修改或刪除。");
        }

        if (ChangeTracker.Entries().Any(e =>
            e.Entity is CompTimeLegacyPool or CompTimeLegacyMember or CompTimeLegacyReturn or
                CompTimeAllocation or CompTimeAllocationReturn or TrainingCompTimeHistory &&
            e.State is EntityState.Modified or EntityState.Deleted))
            throw new InvalidOperationException("補休配置、基準與歷程只能新增。");
        if (ChangeTracker.Entries<TrainingCompTimeGrant>().Any(e => e.State == EntityState.Deleted ||
            e.State == EntityState.Modified && e.OriginalValues.GetValue<TrainingCompTimeStatus>(nameof(TrainingCompTimeGrant.Status)) == TrainingCompTimeStatus.Approved))
            throw new InvalidOperationException("已核准上課補休不得修改或刪除。");
        if (ChangeTracker.Entries<CompTimeTransaction>()
            .Any(entry => entry.State is EntityState.Modified or EntityState.Deleted))
        {
            throw new InvalidOperationException(
                "補休帳本只能新增，不可修改或刪除。");
        }

        if (ChangeTracker.Entries<AttendanceCorrectionRequestHistory>()
            .Any(entry => entry.State is EntityState.Modified or EntityState.Deleted))
        {
            throw new InvalidOperationException(
                "出勤更正申請歷程只能新增，不得修改或刪除。");
        }

        if (ChangeTracker.Entries<AttendanceRawEvent>()
            .Any(entry =>
                entry.State == EntityState.Deleted ||
                entry.State == EntityState.Modified &&
                !IsPermittedRawEventRelink(entry)))
        {
            throw new InvalidOperationException(
                "出勤原始事件不可由一般資料流程刪除。");
        }

        if (ChangeTracker.Entries<AttendanceAdjustment>()
            .Any(x => x.State is EntityState.Modified or EntityState.Deleted))
        {
            throw new InvalidOperationException(
                "Attendance adjustment history is append-only.");
        }


        if (ChangeTracker.Entries<PayrollEmployeeSnapshot>()
                .Any(x => x.State is EntityState.Modified or EntityState.Deleted) ||
            ChangeTracker.Entries<PayrollEmployeeSnapshotComponent>()
                .Any(x => x.State is EntityState.Modified or EntityState.Deleted) ||
            ChangeTracker.Entries<PayrollTotalBlockingEvidence>()
                .Any(x => x.State is EntityState.Modified or EntityState.Deleted) ||
            ChangeTracker.Entries<PayrollAttendanceAllowanceSnapshot>()
                .Any(x => x.State is EntityState.Modified or EntityState.Deleted) ||
            ChangeTracker.Entries<PayrollAttendanceAllowanceEvidence>()
                .Any(x => x.State is EntityState.Modified or EntityState.Deleted) ||
            ChangeTracker.Entries<PayrollLeaveDeductionSnapshot>()
                .Any(x => x.State is EntityState.Modified or EntityState.Deleted) ||
            ChangeTracker.Entries<PayrollLeaveDeductionEvidence>()
                .Any(x => x.State is EntityState.Modified or EntityState.Deleted) ||
            ChangeTracker.Entries<PayrollOvertimePaySnapshot>()
                .Any(x => x.State is EntityState.Modified or EntityState.Deleted) ||
            ChangeTracker.Entries<PayrollOvertimeBaseComponentEvidence>()
                .Any(x => x.State is EntityState.Modified or EntityState.Deleted) ||
            ChangeTracker.Entries<PayrollOvertimeBucketEvidence>()
                .Any(x => x.State is EntityState.Modified or EntityState.Deleted) ||
            ChangeTracker.Entries<PayrollOvertimeDayEvidence>()
                .Any(x => x.State is EntityState.Modified or EntityState.Deleted) ||
            ChangeTracker.Entries<PayrollOvertimeRecognitionEvidence>()
                .Any(x => x.State is EntityState.Modified or EntityState.Deleted) ||
            ChangeTracker.Entries<PayrollLaborInsuranceSnapshot>()
                .Any(x => x.State is EntityState.Modified or EntityState.Deleted) ||
            ChangeTracker.Entries<PayrollLaborInsuranceContributionEvidence>()
                .Any(x => x.State is EntityState.Modified or EntityState.Deleted) ||
            ChangeTracker.Entries<PayrollPeriodicAccrualSnapshot>()
                .Any(x => x.State is EntityState.Modified or EntityState.Deleted) ||
            ChangeTracker.Entries<PayrollPeriodicAccrualMonthEvidence>()
                .Any(x => x.State is EntityState.Modified or EntityState.Deleted) ||
            ChangeTracker.Entries<PayrollHealthInsuranceSnapshot>()
                .Any(x => x.State is EntityState.Modified or EntityState.Deleted) ||
            ChangeTracker.Entries<PayrollHealthInsuranceEvidence>()
                .Any(x => x.State is EntityState.Modified or EntityState.Deleted) ||
            ChangeTracker.Entries<PayrollFinalization>()
                .Any(x => x.State is EntityState.Modified or EntityState.Deleted) ||
            ChangeTracker.Entries<PayrollFinalEmployeeSnapshot>()
                .Any(x => x.State is EntityState.Modified or EntityState.Deleted))
        {
            throw new InvalidOperationException("薪資歷史快照只能新增，不可修改或刪除。");
        }

        return base.SaveChangesAsync(cancellationToken);
    }

    private static bool IsPermittedRawEventRelink(
        EntityEntry<AttendanceRawEvent> entry)
    {
        var employeeId = entry.Property(item => item.EmployeeId);
        return employeeId.IsModified &&
            employeeId.OriginalValue is null &&
            employeeId.CurrentValue.HasValue &&
            entry.Properties
                .Where(property => property.IsModified)
                .All(property =>
                    property.Metadata.Name ==
                    nameof(AttendanceRawEvent.EmployeeId));
    }

    public void ClearTrackedChanges() => ChangeTracker.Clear();

    public bool IsUniqueConstraintViolation(
        DbUpdateException exception,
        string expectedConstraintOrIndexName)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedConstraintOrIndexName);
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is SqlException sqlException && sqlException.Errors.Cast<SqlError>().Any(error =>
                    error.Number is 2601 or 2627 &&
                    error.Message.Contains(
                        $"'{expectedConstraintOrIndexName}'",
                        StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        return false;
    }

    public async Task ExecuteSerializableAsync(
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken = default)
        => await ExecuteTransactionAsync(
            IsolationLevel.Serializable,
            operation,
            cancellationToken);

    public Task<T> ExecuteSerializableAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken = default) =>
        ExecuteTransactionAsync(
            IsolationLevel.Serializable,
            operation,
            cancellationToken);

    public async Task ExecuteTransactionAsync(
        IsolationLevel isolationLevel,
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (!Database.IsRelational())
        {
            await operation(cancellationToken);
            return;
        }

        var transaction = await Database.BeginTransactionAsync(
            isolationLevel, cancellationToken);
        try
        {
            await operation(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            await RecoverFailedTransactionAsync(transaction, exception);
            throw;
        }

        await transaction.DisposeAsync();
    }

    public async Task<T> ExecuteTransactionAsync<T>(
        IsolationLevel isolationLevel,
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (!Database.IsRelational())
        {
            return await operation(cancellationToken);
        }

        var transaction = await Database.BeginTransactionAsync(
            isolationLevel, cancellationToken);
        T result;
        try
        {
            result = await operation(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            await RecoverFailedTransactionAsync(transaction, exception);
            throw;
        }

        await transaction.DisposeAsync();
        return result;
    }

    private async Task RecoverFailedTransactionAsync(
        IDbContextTransaction transaction,
        Exception originalException)
    {
        try
        {
            await transaction.RollbackAsync(CancellationToken.None);
        }
        catch (Exception rollbackException)
        {
            originalException.Data["HRSystem.TransactionRollbackFailure"] = rollbackException;
        }
        finally
        {
            ChangeTracker.Clear();
        }

        try
        {
            await transaction.DisposeAsync();
        }
        catch (Exception disposeException)
        {
            originalException.Data["HRSystem.TransactionDisposeFailure"] = disposeException;
        }

        ExceptionDispatchInfo.Capture(originalException).Throw();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasSequence<int>("EmployeeNumberSequence", "dbo")
            .StartsAt(15)
            .IncrementsBy(1)
            .HasMin(15)
            .HasMax(9999)
            .IsCyclic(false);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(HRSystemDbContext).Assembly);
    }
}
