using HRSystem.Application.Abstractions.Persistence;
using HRSystem.Domain.Attendance;
using HRSystem.Domain.CompanyCalendars;
using HRSystem.Domain.MasterData;
using Microsoft.EntityFrameworkCore;

namespace HRSystem.Application.LeaveRequests;

public sealed class LeaveDurationCalculator(
    IApplicationDbContext dbContext) : ILeaveDurationCalculator
{
    private static readonly TimeZoneInfo TaipeiZone = ResolveTaipeiZone();

    public Task<LeaveDurationEstimateDto> CalculateAsync(
        Guid employeeId,
        DateTimeOffset startAt,
        DateTimeOffset endAt,
        CancellationToken cancellationToken = default) =>
        CalculateAsync(
            employeeId,
            startAt,
            endAt,
            LeaveCalculationMode.WorkingSchedule,
            cancellationToken);

    public async Task<LeaveDurationEstimateDto> CalculateAsync(
        Guid employeeId,
        DateTimeOffset startAt,
        DateTimeOffset endAt,
        LeaveCalculationMode calculationMode,
        CancellationToken cancellationToken = default)
    {
        if (employeeId == Guid.Empty)
        {
            return Invalid("無法識別申請員工，請重新登入後再試。");
        }

        var startUtc = startAt.ToUniversalTime();
        var endUtc = endAt.ToUniversalTime();
        if (startUtc >= endUtc)
        {
            return Invalid("開始時間必須早於結束時間。");
        }

        if (calculationMode == LeaveCalculationMode.WorkingSchedule &&
            endUtc - startUtc > TimeSpan.FromDays(31))
        {
            return Invalid("單張請假申請不得超過 31 天。");
        }

        var startLocal = TimeZoneInfo.ConvertTime(startUtc, TaipeiZone);
        var endLocal = TimeZoneInfo.ConvertTime(endUtc, TaipeiZone);
        var firstDate = DateOnly.FromDateTime(startLocal.DateTime);
        var lastDate = DateOnly.FromDateTime(endLocal.DateTime);
        if (endLocal.TimeOfDay == TimeSpan.Zero)
        {
            lastDate = lastDate.AddDays(-1);
        }

        var assignments = await dbContext.EmployeeShiftAssignments
            .AsNoTracking()
            .Include(item => item.Shift)
            .Where(item =>
                item.EmployeeId == employeeId &&
                item.IsActive &&
                item.Shift.IsActive &&
                item.EffectiveFrom <= lastDate &&
                (!item.EffectiveTo.HasValue ||
                 item.EffectiveTo.Value >= firstDate))
            .ToListAsync(cancellationToken);
        var calendarDays = await dbContext.CompanyCalendarDays
            .AsNoTracking()
            .Include(item => item.Year)
            .Where(item =>
                item.Date >= firstDate &&
                item.Date <= lastDate &&
                item.Year.Status == CompanyCalendarStatus.Published)
            .ToDictionaryAsync(item => item.Date, cancellationToken);

        var messages = new List<string>();
        var hasBlockingIssue = false;
        var total = TimeSpan.Zero;
        for (var date = firstDate; date <= lastDate; date = date.AddDays(1))
        {
            if (!IsWorkingDay(date, calendarDays))
            {
                messages.Add($"{date:yyyy/MM/dd} 為非工作日，該日預估為 0 小時。");
                continue;
            }

            var applicable = assignments
                .Where(item => item.AppliesOn(date))
                .ToArray();
            if (applicable.Length == 0)
            {
                messages.Add(
                    $"{date:yyyy/MM/dd} 沒有唯一有效的班別指派，請先由管理員處理班別資料。");
                hasBlockingIssue = true;
                continue;
            }

            if (applicable.Length > 1)
            {
                messages.Add(
                    $"{date:yyyy/MM/dd} 有超過一筆有效班別指派，請先由管理員排除重疊資料。");
                hasBlockingIssue = true;
                continue;
            }

            if (!TryBuildWorkingIntervals(
                    date,
                    applicable[0].Shift,
                    out var intervals))
            {
                messages.Add(
                    $"{date:yyyy/MM/dd} 的班別工作區間無效，請先由管理員處理班別資料。");
                hasBlockingIssue = true;
                continue;
            }

            foreach (var interval in intervals)
            {
                var overlapStart = startUtc > interval.Start
                    ? startUtc
                    : interval.Start;
                var overlapEnd = endUtc < interval.End
                    ? endUtc
                    : interval.End;
                if (overlapEnd > overlapStart)
                {
                    total += overlapEnd - overlapStart;
                }
            }
        }

        var durationHours = Math.Round(
            (decimal)total.TotalHours,
            2,
            MidpointRounding.AwayFromZero);
        if (durationHours <= 0)
        {
            messages.Add("請假期間未涵蓋任何有效工作區間，預估時數為 0 小時。");
            hasBlockingIssue = calculationMode == LeaveCalculationMode.WorkingSchedule ||
                hasBlockingIssue;
        }

        return new LeaveDurationEstimateDto(
            durationHours,
            !hasBlockingIssue,
            messages);
    }

    private static bool IsWorkingDay(
        DateOnly date,
        IReadOnlyDictionary<DateOnly, CompanyCalendarDay> calendarDays)
    {
        if (calendarDays.TryGetValue(date, out var day))
        {
            return day.IsWorkingDay;
        }

        return date.DayOfWeek is not (
            DayOfWeek.Saturday or
            DayOfWeek.Sunday);
    }

    private static bool TryBuildWorkingIntervals(
        DateOnly date,
        AttendanceShift shift,
        out IReadOnlyList<WorkingInterval> intervals)
    {
        var shiftStartLocal = LocalDateTime(date, shift.ScheduledStartTime);
        var shiftEndLocal = LocalDateTime(
            shift.IsOvernightShift ||
            shift.ScheduledEndTime <= shift.ScheduledStartTime
                ? date.AddDays(1)
                : date,
            shift.ScheduledEndTime);
        var lunchStartLocal = LocalDateTime(date, shift.LunchBreakStartTime);
        if (lunchStartLocal < shiftStartLocal)
        {
            lunchStartLocal = lunchStartLocal.AddDays(1);
        }

        var lunchEndLocal = LocalDateTime(date, shift.LunchBreakEndTime);
        if (lunchEndLocal <= lunchStartLocal)
        {
            lunchEndLocal = lunchEndLocal.AddDays(1);
        }

        if (shiftStartLocal >= lunchStartLocal ||
            lunchStartLocal >= lunchEndLocal ||
            lunchEndLocal >= shiftEndLocal)
        {
            intervals = [];
            return false;
        }

        intervals =
        [
            new WorkingInterval(
                ToUtc(shiftStartLocal),
                ToUtc(lunchStartLocal)),
            new WorkingInterval(
                ToUtc(lunchEndLocal),
                ToUtc(shiftEndLocal))
        ];
        return true;
    }

    private static DateTime LocalDateTime(DateOnly date, TimeOnly time) =>
        DateTime.SpecifyKind(date.ToDateTime(time), DateTimeKind.Unspecified);

    private static DateTimeOffset ToUtc(DateTime local) =>
        new(TimeZoneInfo.ConvertTimeToUtc(local, TaipeiZone), TimeSpan.Zero);

    private static LeaveDurationEstimateDto Invalid(string message) =>
        new(0m, false, [message]);

    private static TimeZoneInfo ResolveTaipeiZone()
    {
        foreach (var id in new[] { "Taipei Standard Time", "Asia/Taipei" })
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (TimeZoneNotFoundException)
            {
            }
        }

        throw new InvalidOperationException("找不到台北時區設定。");
    }

    private sealed record WorkingInterval(
        DateTimeOffset Start,
        DateTimeOffset End);
}
