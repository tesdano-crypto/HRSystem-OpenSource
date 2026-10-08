using HRSystem.Domain.Attendance;
using HRSystem.Domain.CompanyCalendars;

namespace HRSystem.Application.Attendance;

internal static class AttendanceCalendarResolver
{
    public static (bool IsRequired, AttendanceCalendarClassification Classification) Resolve(
        DateOnly date, CompanyCalendarDay? day)
    {
        if (day is not null)
            return day.DayType switch
            {
                CompanyCalendarDayType.WorkingDay => (true, AttendanceCalendarClassification.WorkingDay),
                CompanyCalendarDayType.ExceptionalWorkingDay => (true, AttendanceCalendarClassification.ExceptionalWorkingDay),
                CompanyCalendarDayType.Saturday or CompanyCalendarDayType.Sunday => (false, AttendanceCalendarClassification.Weekend),
                _ => (false, AttendanceCalendarClassification.Holiday)
            };
        return date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday
            ? (false, AttendanceCalendarClassification.FallbackRestDay)
            : (true, AttendanceCalendarClassification.FallbackWorkingDay);
    }
}
