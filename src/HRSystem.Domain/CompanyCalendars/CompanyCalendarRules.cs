using HRSystem.Domain.Common;

namespace HRSystem.Domain.CompanyCalendars;

internal static class CompanyCalendarRules
{
    public static string Required(string? value, string field, int maximumLength)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new DomainValidationException($"{field}為必填。");
        }

        if (normalized.Length > maximumLength)
        {
            throw new DomainValidationException($"{field}不得超過 {maximumLength} 個字元。");
        }

        return normalized;
    }

    public static string? Optional(string? value, string field, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return Required(value, field, maximumLength);
    }

    public static void EnsureDayType(CompanyCalendarDayType dayType)
    {
        if (!Enum.IsDefined(dayType))
        {
            throw new DomainValidationException("行事曆日期類型無效。");
        }
    }

    public static bool IsWorkingDay(CompanyCalendarDayType dayType)
    {
        EnsureDayType(dayType);
        return dayType is CompanyCalendarDayType.WorkingDay
            or CompanyCalendarDayType.ExceptionalWorkingDay;
    }

    public static void EnsureName(CompanyCalendarDayType dayType, string? name)
    {
        if (dayType is CompanyCalendarDayType.NationalHoliday
            or CompanyCalendarDayType.SubstituteHoliday
            or CompanyCalendarDayType.CompanyHoliday
            or CompanyCalendarDayType.ExceptionalWorkingDay)
        {
            _ = Required(name, "日期名稱", 100);
        }
    }
}
