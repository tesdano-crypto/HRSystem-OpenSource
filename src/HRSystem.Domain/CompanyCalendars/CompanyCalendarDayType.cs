namespace HRSystem.Domain.CompanyCalendars;

public enum CompanyCalendarDayType : byte
{
    WorkingDay = 1,
    Saturday = 2,
    Sunday = 3,
    NationalHoliday = 4,
    SubstituteHoliday = 5,
    CompanyHoliday = 6,
    ExceptionalWorkingDay = 7
}
