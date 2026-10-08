namespace HRSystem.Application.Attendance;

public sealed class AttendanceExcelExportRequest
{
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public Guid? DepartmentId { get; set; }
    public IReadOnlyCollection<Guid> EmployeeIds { get; set; } = [];
    public bool OnlyAnomalies { get; set; }
    public bool OnlyPending { get; set; }
    public AttendanceReviewQuickFilter QuickFilter { get; set; }
}

public sealed record AttendanceExcelDetailRow(
    DateOnly WorkDate,
    string Weekday,
    string EmployeeNumber,
    string EmployeeName,
    string DepartmentName,
    string ShiftName,
    TimeOnly? ScheduledStartTime,
    TimeOnly? ScheduledEndTime,
    DateTime? EffectiveClockInLocalTime,
    DateTime? EffectiveClockOutLocalTime,
    int LateMinutes,
    int EarlyLeaveMinutes,
    string MissingPunchStatus,
    int OverstayMinutes,
    string AttendanceStatus,
    string LeaveStatus,
    string OvertimeRequestStatus,
    int ApprovedOvertimeMinutes,
    int ValidRecognizedMinutes,
    string RecognitionStatus,
    string CorrectionStatus);

public sealed record AttendanceExcelMonthlyRow(
    DateOnly Month,
    string EmployeeNumber,
    string EmployeeName,
    string DepartmentName,
    int WorkdayCount,
    int NormalCount,
    int AnomalyCount,
    int LateCount,
    int LateMinutes,
    int EarlyLeaveCount,
    int EarlyLeaveMinutes,
    int MissingPunchCount,
    int ExtendedStayCount,
    int PotentialUnreportedOvertimeCount,
    int ApprovedOvertimeMinutes,
    int ValidRecognizedMinutes,
    int LeaveRequestCount,
    string LeaveSummary);

public sealed record AttendanceExcelReport(
    AttendanceExcelExportRequest Request,
    DateTimeOffset GeneratedAtUtc,
    IReadOnlyList<AttendanceExcelDetailRow> Details,
    IReadOnlyList<AttendanceExcelMonthlyRow> MonthlySummaries);

public sealed record AttendanceExcelWorkbook(
    byte[] Content,
    string FileName,
    string ContentType);

public interface IAttendanceExcelReportService
{
    Task<AttendanceExcelReport> BuildAsync(
        AttendanceExcelExportRequest request,
        CancellationToken cancellationToken = default);
}

public interface IAttendanceExcelWorkbookWriter
{
    AttendanceExcelWorkbook Write(AttendanceExcelReport report);
}
