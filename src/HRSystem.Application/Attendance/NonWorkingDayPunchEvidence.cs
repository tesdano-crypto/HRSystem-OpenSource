using HRSystem.Application.Abstractions.Persistence;
using HRSystem.Domain.Attendance;
using HRSystem.Domain.CompanyCalendars;
using HRSystem.Domain.Overtime;
using Microsoft.EntityFrameworkCore;

namespace HRSystem.Application.Attendance;

public sealed record AttendancePunchEvidence(Guid Id, DateTime LocalTime, byte[] Fingerprint);

public sealed record NonWorkingDayPunchEvidence(
    bool IsRequiredWorkday, AttendanceCalendarClassification Classification,
    CompanyCalendarDayType? DayType, IReadOnlyList<AttendancePunchEvidence> Punches,
    string Fingerprint)
{
    public bool HasNonWorkingPunch => !IsRequiredWorkday && Punches.Count > 0;
    public DateTime? FirstPunch => Punches.FirstOrDefault()?.LocalTime;
    public DateTime? LastPunch => Punches.LastOrDefault()?.LocalTime;
    public int PunchCount => Punches.Count;
    public TimeSpan? PunchSpan => FirstPunch.HasValue && LastPunch.HasValue ? LastPunch - FirstPunch : null;
}

public sealed record AttendanceOvertimeLink(Guid Id, OvertimeRequestStatus Status,
    int RequestedMinutes, int? RecognizedMinutes);

public static class NonWorkingDayAttendanceDisplay
{
    public const string Presence = "非工作日有打卡";
    public const string Pending = "待確認";
    public const string Resolved = "已認定非加班";
    public const string Queue = "非工作日有打卡待確認";
    public const string ManualReview = "打卡跨度僅為證據，實際工作及休息時段需人工確認。";
    public static string RequestStatus(OvertimeRequestStatus status) => status switch
    {
        OvertimeRequestStatus.Draft => "已建立加班申請（草稿）",
        OvertimeRequestStatus.Submitted => "已關聯加班申請（待審核）",
        OvertimeRequestStatus.Approved => "已核准加班",
        OvertimeRequestStatus.Rejected => "加班申請已駁回",
        OvertimeRequestStatus.Withdrawn => "加班申請已撤回",
        _ => "加班申請"
    };
    public static string Calendar(NonWorkingDayPunchEvidence evidence) => evidence.DayType switch
    {
        CompanyCalendarDayType.Saturday => "星期六休息日",
        CompanyCalendarDayType.Sunday => "星期日休息日",
        CompanyCalendarDayType.CompanyHoliday => "公司非工作日",
        CompanyCalendarDayType.ExceptionalWorkingDay => "特別工作日",
        CompanyCalendarDayType.WorkingDay => "工作日",
        null => evidence.IsRequiredWorkday ? "工作日（預設行事曆）" : "休息日（預設行事曆）",
        _ => "假日"
    };
    public static string Span(NonWorkingDayPunchEvidence evidence) => evidence.PunchSpan is { } span
        ? $"打卡跨度 {(int)span.TotalHours} 小時 {span.Minutes} 分鐘；{evidence.PunchCount} 筆打卡" : "無打卡證據";
}

internal static class NonWorkingDayPunchReader
{
    public static async Task<Dictionary<(Guid EmployeeId, DateOnly Date), NonWorkingDayPunchEvidence>> ReadAsync(
        IApplicationDbContext db, Guid[] employees, DateOnly from, DateOnly to,
        IEnumerable<(Guid EmployeeId, DateOnly Date)> retainedKeys, CancellationToken ct)
    {
        var start = from.ToDateTime(TimeOnly.MinValue);
        var end = to.AddDays(1).ToDateTime(TimeOnly.MinValue);
        var raw = await db.AttendanceRawEvents.AsNoTracking()
            .Where(x => x.EmployeeId.HasValue && employees.Contains(x.EmployeeId.Value) &&
                x.SourceSystem == AttendanceSourceSystems.BioWebTa &&
                x.EventLocalDateTime >= start && x.EventLocalDateTime < end)
            .Select(x => new { EmployeeId = x.EmployeeId!.Value, x.Id, x.EventLocalDateTime, x.SourceFingerprint })
            .ToListAsync(ct);
        var calendars = await db.CompanyCalendarDays.AsNoTracking()
            .Where(x => x.Date >= from && x.Date <= to && x.Year.Status == CompanyCalendarStatus.Published)
            .ToDictionaryAsync(x => x.Date, ct);
        var groups = raw.GroupBy(x => (x.EmployeeId, DateOnly.FromDateTime(x.EventLocalDateTime)))
            .ToDictionary(x => x.Key, x => x.OrderBy(p => p.EventLocalDateTime).ThenBy(p => p.Id)
                .Select(p => new AttendancePunchEvidence(p.Id, p.EventLocalDateTime, p.SourceFingerprint)).ToArray());
        var result = new Dictionary<(Guid, DateOnly), NonWorkingDayPunchEvidence>();
        foreach (var key in groups.Keys.Concat(retainedKeys).Distinct())
        {
            calendars.TryGetValue(key.Item2, out var calendar);
            var (required, classification) = AttendanceCalendarResolver.Resolve(key.Item2, calendar);
            var punches = groups.GetValueOrDefault(key) ?? [];
            result[key] = new(required, classification, calendar?.DayType, punches,
                Convert.ToBase64String(AttendanceReviewFingerprint.ComputeNonWorkingDay(
                    key.Item1, key.Item2, required, classification, calendar?.Id, calendar?.DayType, punches)));
        }
        return result;
    }
}
