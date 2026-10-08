namespace HRSystem.Application.Common.Auditing;

public static class AuditActions
{
    public const string Created = "Created";
    public const string Updated = "Updated";
    public const string Activated = "Activated";
    public const string Deactivated = "Deactivated";
    public const string PasswordReset = "PasswordReset";
    public const string PasswordChanged = "PasswordChanged";
    public const string LoginSucceeded = "LoginSucceeded";
    public const string LeaveDraftCreated = "LeaveDraftCreated";
    public const string LeaveDraftUpdated = "LeaveDraftUpdated";
    public const string LeaveDraftDeleted = "LeaveDraftDeleted";
    public const string LeaveSubmitted = "LeaveSubmitted";
    public const string LeaveWithdrawn = "LeaveWithdrawn";
    public const string LeaveApproved = "LeaveApproved";
    public const string LeaveRejected = "LeaveRejected";
    public const string LeaveCancellationRequested = "LeaveCancellationRequested";
    public const string LeaveCancellationApproved = "LeaveCancellationApproved";
    public const string LeaveCancellationRejected = "LeaveCancellationRejected";
    public const string CompTimeLegacyOpeningBalanceCreated =
        "CompTimeLegacyOpeningBalanceCreated";
    public const string CompTimeConsumed = "CompTimeConsumed";
    public const string CompTimeRestored = "CompTimeRestored";
    public const string ParentalLeaveCreated = "ParentalLeaveCreated";
    public const string ParentalLeaveSubmitted = "ParentalLeaveSubmitted";
    public const string ParentalLeaveApproved = "ParentalLeaveApproved";
    public const string ParentalLeaveRejected = "ParentalLeaveRejected";
    public const string ParentalLeaveWithdrawn = "ParentalLeaveWithdrawn";
    public const string ParentalLeaveCancellationRequested =
        "ParentalLeaveCancellationRequested";
    public const string ParentalLeaveCancelled = "ParentalLeaveCancelled";
    public const string ParentalLeaveCancellationRejected =
        "ParentalLeaveCancellationRejected";
    public const string ParentalLeaveEarlyReturnRequested =
        "ParentalLeaveEarlyReturnRequested";
    public const string ParentalLeaveEarlyReturnApproved =
        "ParentalLeaveEarlyReturnApproved";
    public const string AttendanceExceptionCreated = "AttendanceExceptionCreated";
    public const string AttendanceExceptionUpdated = "AttendanceExceptionUpdated";
    public const string AttendanceExceptionSubmitted = "AttendanceExceptionSubmitted";
    public const string AttendanceExceptionApproved = "AttendanceExceptionApproved";
    public const string AttendanceExceptionRejected = "AttendanceExceptionRejected";
    public const string AttendanceExceptionWithdrawn = "AttendanceExceptionWithdrawn";
    public const string AttendanceExceptionCancellationRequested = "AttendanceExceptionCancellationRequested";
    public const string AttendanceExceptionCancelled = "AttendanceExceptionCancelled";
    public const string AttendanceExceptionCancellationRejected = "AttendanceExceptionCancellationRejected";
    public const string StandardLeaveTypeInitialized = "StandardLeaveTypeInitialized";
    public const string CalendarDayLeaveTypeEnabled = "CalendarDayLeaveTypeEnabled";
    public const string AnnualLeaveEntitlementsInitialized = "AnnualLeaveEntitlementGranted";
    public const string AnnualLeaveReserved = "AnnualLeaveUsageReserved";
    public const string AnnualLeaveConsumed = "AnnualLeaveUsageConsumed";
    public const string AnnualLeaveReservationReleased = "AnnualLeaveUsageReleased";
    public const string AnnualLeaveRestored = "AnnualLeaveUsageRestored";
    public const string AnnualLeaveCarryForwardApplied = "AnnualLeaveCarryForwardApplied";
    public const string AnnualLeaveBackfillApplied = "AnnualLeaveBackfillApplied";
    public const string AnnualLeaveEntitlementSettled = "AnnualLeaveEntitlementSettled";
    public const string CompanyCalendarInitialized = "CompanyCalendarInitialized";
    public const string CompanyCalendarManifestImported = "CompanyCalendarManifestImported";
    public const string CompanyCalendarPublished = "CompanyCalendarPublished";
    public const string CompanyCalendarArchived = "CompanyCalendarArchived";
    public const string CompanyHolidayCreated = "CompanyHolidayCreated";
    public const string CompanyHolidayRemoved = "CompanyHolidayRemoved";
    public const string ExceptionalWorkingDayCreated = "ExceptionalWorkingDayCreated";
    public const string ExceptionalWorkingDayRemoved = "ExceptionalWorkingDayRemoved";
    public const string CompanyCalendarDayOverridden = "CompanyCalendarDayOverridden";
    public const string BioWebMappingCreated = "BioWebMappingCreated";
    public const string BioWebMappingUpdated = "BioWebMappingUpdated";
    public const string BioWebMappingDeactivated = "BioWebMappingDeactivated";
    public const string AttendanceSyncRequested = "AttendanceSyncRequested";
    public const string AttendanceSyncCompleted = "AttendanceSyncCompleted";
    public const string AttendanceSyncFailed = "AttendanceSyncFailed";
    public const string BioWebTaImportRunNowRequested =
        "BioWebTaImportRunNowRequested";
    public const string AttendanceUnmappedEventsRelinked = "AttendanceUnmappedEventsRelinked";
    public const string AttendanceShiftCreated = "AttendanceShiftCreated";
    public const string AttendanceShiftUpdated = "AttendanceShiftUpdated";
    public const string AttendanceShiftActivated = "AttendanceShiftActivated";
    public const string AttendanceShiftDeactivated = "AttendanceShiftDeactivated";
    public const string EmployeeShiftAssigned = "EmployeeShiftAssigned";
    public const string EmployeeShiftAssignmentUpdated = "EmployeeShiftAssignmentUpdated";
    public const string EmployeeShiftAssignmentActivated = "EmployeeShiftAssignmentActivated";
    public const string EmployeeShiftAssignmentDeactivated = "EmployeeShiftAssignmentDeactivated";
    public const string AttendanceRecalculationRequested = "AttendanceRecalculationRequested";
    public const string AttendanceRecalculationCompleted = "AttendanceRecalculationCompleted";
    public const string AttendanceRecalculationFailed = "AttendanceRecalculationFailed";
    public const string AttendanceAdjusted = "AttendanceAdjusted";
    public const string AttendanceAdjustmentRevised = "AttendanceAdjustmentRevised";
    public const string AttendanceAdjustmentReverted = "AttendanceAdjustmentReverted";
    public const string AttendanceCorrectionRequestCreated =
        "AttendanceCorrectionRequestCreated";
    public const string AttendanceCorrectionRequestSubmitted =
        "AttendanceCorrectionRequestSubmitted";
    public const string AttendanceCorrectionRequestWithdrawn =
        "AttendanceCorrectionRequestWithdrawn";
    public const string AttendanceCorrectionRequestApproved =
        "AttendanceCorrectionRequestApproved";
    public const string AttendanceCorrectionRequestRejected =
        "AttendanceCorrectionRequestRejected";
    public const string AttendanceCorrectionAdjustmentApplied =
        "AttendanceCorrectionAdjustmentApplied";
    public const string AttendanceReviewResolved = "AttendanceReviewResolved";
    public const string NonWorkingDayPunchMarkedNonOvertime = "NonWorkingDayPunchMarkedNonOvertime";
    public const string OvertimeDraftCreatedFromNonWorkingDayPunch = "OvertimeDraftCreatedFromNonWorkingDayPunch";
    public const string AttendanceReviewReopened = "AttendanceReviewReopened";
    public const string OvertimeRequestCreated = "OvertimeRequestCreated";
    public const string OvertimeRequestSubmitted = "OvertimeRequestSubmitted";
    public const string OvertimeRequestWithdrawn = "OvertimeRequestWithdrawn";
    public const string OvertimeRequestApproved = "OvertimeRequestApproved";
    public const string OvertimeRequestRejected = "OvertimeRequestRejected";
    public const string OvertimeRecognitionConfirmed = "OvertimeRecognitionConfirmed";
    public const string OvertimeRecognitionReopened = "OvertimeRecognitionReopened";
    public const string OvertimeRecognitionAdjusted = "OvertimeRecognitionAdjusted";
    public const string PayrollPlanAssigned = "PayrollPlanAssigned";
    public const string PayrollComponentOverrideCreated = "PayrollComponentOverrideCreated";
    public const string PayrollComponentOverrideChanged = "PayrollComponentOverrideChanged";
    public const string PayrollPayCycleCreated = "PayrollPayCycleCreated";
    public const string PayrollAdjustmentCreated = "PayrollAdjustmentCreated";
    public const string PayrollAdjustmentUpdated = "PayrollAdjustmentUpdated";
    public const string PayrollAdjustmentRemoved = "PayrollAdjustmentRemoved";
    public const string PayrollPeriodCreated = "PayrollPeriodCreated";
    public const string PayrollRunDraftCreated = "PayrollRunDraftCreated";
    public const string PayrollSnapshotCreated = "PayrollSnapshotCreated";
    public const string PayrollFinalized = "PayrollFinalized";
    public const string EmployeeLaborInsuranceEnrollmentCreated =
        "EmployeeLaborInsuranceEnrollmentCreated";
    public const string EmployeeLaborInsuranceEnrollmentSuperseded =
        "EmployeeLaborInsuranceEnrollmentSuperseded";
    public const string EmployeeLaborInsuranceEnrollmentEnded =
        "EmployeeLaborInsuranceEnrollmentEnded";
    public const string EmployeeLaborInsuranceEnrollmentDeactivated =
        "EmployeeLaborInsuranceEnrollmentDeactivated";
    public const string OccupationalInsuranceEnrollmentCreated =
        "OccupationalInsuranceEnrollmentCreated";
    public const string OccupationalInsuranceEnrollmentSuperseded =
        "OccupationalInsuranceEnrollmentSuperseded";
    public const string OccupationalInsuranceEnrollmentEnded =
        "OccupationalInsuranceEnrollmentEnded";
    public const string EmployeeHealthInsuranceEnrollmentCreated =
        "EmployeeHealthInsuranceEnrollmentCreated";
    public const string EmployeeHealthInsuranceEnrollmentSuperseded =
        "EmployeeHealthInsuranceEnrollmentSuperseded";
    public const string EmployeeHealthInsuranceEnrollmentEnded =
        "EmployeeHealthInsuranceEnrollmentEnded";
    public const string EmployeeHealthInsuranceDependentsChanged =
        "EmployeeHealthInsuranceDependentsChanged";
    public const string ApprovalSubmitted = "ApprovalSubmitted";
    public const string ApprovalViewed = "ApprovalViewed";
    public const string ApprovalApproved = "ApprovalApproved";
    public const string ApprovalReturned = "ApprovalReturned";
    public const string ApprovalCancelled = "ApprovalCancelled";
    public const string ApprovalSuperseded = "ApprovalSuperseded";
    public const string ApprovalNotificationSent = "ApprovalNotificationSent";
    public const string ApprovalNotificationFailed = "ApprovalNotificationFailed";
    public const string LineUserBindingVerified = "LineUserBindingVerified";
    public const string LinePairingRequested = "LinePairingRequested";
    public const string LinePairingVerified = "LinePairingVerified";
    public const string LineUserBindingRevoked = "LineUserBindingRevoked";
    public const string LineUserBindingReplaced = "LineUserBindingReplaced";
    public const string LineTestNotificationRequested = "LineTestNotificationRequested";
    public const string LineTestNotificationSucceeded = "LineTestNotificationSucceeded";
    public const string LineTestNotificationFailed = "LineTestNotificationFailed";
}
