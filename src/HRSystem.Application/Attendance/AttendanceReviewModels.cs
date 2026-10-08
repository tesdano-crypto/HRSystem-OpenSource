using HRSystem.Domain.Attendance;
using HRSystem.Domain.Overtime;

namespace HRSystem.Application.Attendance;

public enum AttendanceReviewSortField
{
    WorkDate = 1,
    Employee = 2,
    Status = 3
}

public enum AttendanceReviewSortDirection
{
    Ascending = 1,
    Descending = 2
}

public enum AttendanceReviewQuickFilter
{
    All = 0,
    Late = 1,
    EarlyLeave = 2,
    MissingPunch = 3,
    ExtendedStay = 4,
    PotentialUnreportedOvertime = 5,
    PendingReview = 6,
    ResolvedReview = 7,
    NeedsReview = 8,
    Normal = 9,
    HasOvertimeRequest = 10,
    RecognizedOvertime = 11,
    NonWorkingDayPunch = 12,
    NonWorkingWeekend = 13,
    NonWorkingHoliday = 14,
    NonWorkingWithoutRequest = 15,
    NonWorkingResolved = 16
}

public enum AttendanceReviewState
{
    Pending = 1,
    Resolved = 2,
    NeedsReview = 3,
    Reopened = 4
}

public enum AttendanceReviewOvertimeRequestState
{
    NoRequest = 0,
    PendingRequest = 1,
    ApprovedCovered = 2,
    ApprovedPartiallyCovered = 3,
    RejectedRequest = 4,
    ApprovedPendingRecognition = 5,
    RecognitionConfirmed = 6,
    RecognitionNeedsReview = 7,
    RecognitionReopened = 8,
    DraftRequest = 9,
    WithdrawnRequest = 10
}

public sealed record AttendanceReviewOvertimeRequestDto(
    AttendanceReviewOvertimeRequestState State,
    int ApprovedCoveredMinutes,
    int ActualOverstayMinutes,
    int RequestedMinutes,
    Guid? OvertimeRequestId)
{
    public int? RecognizedMinutes { get; init; }
    public OvertimeRecognitionReason? RecognitionReason { get; init; }
    public bool RecognitionIsStale { get; init; }
    public bool ChangesReviewMeaning =>
        State != AttendanceReviewOvertimeRequestState.NoRequest &&
        !(State == AttendanceReviewOvertimeRequestState.RecognitionConfirmed &&
          RecognizedMinutes == 0 && RecognitionReason is
              OvertimeRecognitionReason.NonWorkActivityExcluded or
              OvertimeRecognitionReason.NoActualOvertime);
}

public sealed class AttendanceReviewQuery
{
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public IReadOnlyCollection<Guid> EmployeeIds { get; set; } = [];
    public bool OnlyAnomalies { get; set; }
    public bool OnlyPending { get; set; }
    public AttendanceReviewQuickFilter QuickFilter { get; set; }
    public AttendanceReviewSortField SortBy { get; set; } =
        AttendanceReviewSortField.WorkDate;
    public AttendanceReviewSortDirection SortDirection { get; set; } =
        AttendanceReviewSortDirection.Descending;
    public int? Limit { get; set; }
}

public sealed record AttendanceReviewDepartmentOption(
    Guid DepartmentId,
    string DepartmentCode,
    string DepartmentName);

public sealed record AttendanceReviewEmployeeOption(
    Guid EmployeeId,
    string EmployeeNumber,
    string EmployeeName,
    Guid DepartmentId,
    string DepartmentName);

public sealed record AttendanceReviewFilterOptions(
    IReadOnlyList<AttendanceReviewDepartmentOption> Departments,
    IReadOnlyList<AttendanceReviewEmployeeOption> Employees);

public sealed record AttendanceReviewLeaveDto(
    Guid LeaveRequestId,
    string LeaveTypeCode,
    string LeaveTypeName,
    int CoveredMinutes);

public sealed record AttendanceCorrectionPublicStatusDto(
    Guid RequestId,
    AttendanceCorrectionRequestType RequestType,
    AttendanceCorrectionRequestStatus Status,
    string PublicStatus);

public sealed record AttendanceReviewRowDto(
    Guid DailyAttendanceResultId,
    Guid EmployeeId,
    string EmployeeNumber,
    string EmployeeName,
    string DepartmentName,
    DateOnly WorkDate,
    bool IsRequiredWorkday,
    string CalendarClassification,
    string? ShiftCode,
    string? ShiftName,
    TimeOnly? ScheduledEndTime,
    bool? IsOvernightShift,
    DateTime? EffectiveClockInLocalTime,
    DateTime? EffectiveClockOutLocalTime,
    string Status,
    bool IsLate,
    bool IsEarlyLeave,
    bool MissingClockIn,
    bool MissingClockOut,
    int LateMinutes,
    int EarlyLeaveMinutes,
    int ApprovedLeaveMinutes,
    int RequiredAttendanceMinutes,
    int RecognizedWorkMinutes,
    int MissingMinutes,
    int WorkedDuringApprovedLeaveMinutes,
    string LeaveCoverageStatus,
    bool IsEmploymentSuspended,
    bool IsAttendanceExempted,
    int AttendanceExceptionMinutes,
    IReadOnlyList<AttendanceReviewLeaveDto> LeaveItems)
{
    public NonWorkingDayPunchEvidence? PunchEvidence { get; init; }
    public IReadOnlyList<AttendanceOvertimeLink> OvertimeLinks { get; init; } = [];
    public bool HasNonWorkingPunch => PunchEvidence?.HasNonWorkingPunch == true;
    public bool NonWorkingPunchPending => HasNonWorkingPunch && ReviewItems.Any(x =>
        x.AnomalyType == AttendanceReviewAnomalyType.NonWorkingDayPunch && x.IsPending);
    public TimeOnly? ScheduledStartTime { get; init; }
    public byte[] AttendanceRowVersion { get; init; } = [];
    public IReadOnlyList<AttendanceReviewAnomalyDto> ReviewItems { get; init; } = [];
    public IReadOnlyList<AttendanceCorrectionPublicStatusDto>
        CorrectionRequests { get; init; } = [];
    public AttendanceReviewOvertimeRequestDto OvertimeRequest { get; init; } =
        new(AttendanceReviewOvertimeRequestState.NoRequest, 0, 0, 0, null);

    public AttendanceReviewOverstay Overstay =>
        AttendanceReviewOverstayPolicy.Evaluate(
            WorkDate,
            ScheduledEndTime,
            IsOvernightShift,
            EffectiveClockOutLocalTime,
            IsRequiredWorkday,
            MissingClockOut,
            IsEmploymentSuspended,
            LeaveCoverageStatus);

    public int OverstayMinutes => Overstay.Minutes;

    public AttendanceReviewOverstayLevel OverstayLevel => Overstay.Level;

    public bool IsAnomaly =>
        IsRequiredWorkday &&
        !IsEmploymentSuspended &&
        !IsAttendanceExempted &&
        !string.Equals(LeaveCoverageStatus, "Full", StringComparison.Ordinal) &&
        (IsLate ||
         IsEarlyLeave ||
         MissingClockIn ||
         MissingClockOut ||
         MissingMinutes > 0 ||
         WorkedDuringApprovedLeaveMinutes > 0 ||
         OverstayLevel != AttendanceReviewOverstayLevel.None ||
         string.Equals(Status, "NoShift", StringComparison.Ordinal) ||
         string.Equals(Status, "AmbiguousPunch", StringComparison.Ordinal));
}

public sealed record AttendanceReviewAnomalyDto(
    AttendanceReviewAnomalyType AnomalyType,
    int Minutes,
    string SourceFingerprint,
    AttendanceReviewState ReviewState,
    Guid? ResolutionId,
    AttendanceReviewResolutionReason? Reason,
    string? Note,
    string? RowVersion)
{
    public bool IsPending => ReviewState != AttendanceReviewState.Resolved;
    public bool HasOvertimeResolutionConflict { get; init; }
}

public sealed record AttendanceReviewSummary(
    int ResultCount,
    int EmployeeCount,
    int AnomalyCount,
    int LateCount,
    int EarlyLeaveCount,
    int MissingPunchCount,
    int ExtendedStayCount,
    int PotentialUnreportedOvertimeCount,
    int FullLeaveCount,
    int PartialLeaveCount,
    int ExemptedCount,
    int SuspendedCount,
    int NonWorkingDayCount)
{
    public int PendingReviewCount { get; init; }
    public int ResolvedReviewCount { get; init; }
    public int NeedsReviewCount { get; init; }
}

public sealed record AttendanceReviewEmployeeSummary(
    Guid EmployeeId,
    string EmployeeNumber,
    string EmployeeName,
    int ResultCount,
    int NormalCount,
    int AnomalyCount,
    int LateCount,
    int EarlyLeaveCount,
    int MissingPunchCount,
    int ExtendedStayCount,
    int PotentialUnreportedOvertimeCount)
{
    public int PendingReviewCount { get; init; }
    public int ResolvedReviewCount { get; init; }
}

public sealed record ResolveAttendanceReviewRequest(
    Guid DailyAttendanceResultId,
    AttendanceReviewAnomalyType AnomalyType,
    string SourceFingerprint,
    AttendanceReviewResolutionReason Reason,
    string? Note,
    string? RowVersion)
{
    public Guid? EmployeeId { get; init; }
    public DateOnly? WorkDate { get; init; }
}

public sealed record ReopenAttendanceReviewRequest(
    Guid ResolutionId,
    string RowVersion,
    string? Note);

public sealed record AttendanceReviewResolutionResult(
    Guid ResolutionId,
    AttendanceReviewResolutionStatus Status,
    string RowVersion);

public sealed record AttendanceReviewResolutionHistoryDto(
    AttendanceReviewResolutionHistoryAction Action,
    AttendanceReviewResolutionStatus? FromStatus,
    AttendanceReviewResolutionStatus ToStatus,
    AttendanceReviewResolutionReason? Reason,
    string? Note,
    string ActorUserId,
    DateTimeOffset ActionAtUtc);

public sealed record AttendanceReviewResult(
    IReadOnlyList<AttendanceReviewRowDto> Items,
    AttendanceReviewSummary Summary,
    IReadOnlyList<AttendanceReviewEmployeeSummary> EmployeeSummaries);
