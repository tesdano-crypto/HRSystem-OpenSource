using HRSystem.Domain.Common;

namespace HRSystem.Domain.LeaveRequests;

public sealed record CalendarDayLeaveRange(
    DateOnly StartDate,
    DateOnly EndDate,
    int CalendarDayCount,
    PregnancyDurationCategory? PregnancyDurationCategory);

public static class CalendarDayLeavePolicy
{
    public const string MaternityCode = "MATERNITY";
    public const string MiscarriageCode = "MISCARRIAGE";
    public const string PregnancyBedRestCode = "PREGNANCY_BED_REST";

    public static bool IsSupported(string code) =>
        string.Equals(code, MaternityCode, StringComparison.Ordinal) ||
        string.Equals(code, MiscarriageCode, StringComparison.Ordinal) ||
        string.Equals(code, PregnancyBedRestCode, StringComparison.Ordinal);

    public static CalendarDayLeaveRange Resolve(
        string code,
        DateOnly startDate,
        DateOnly? requestedEndDate,
        PregnancyDurationCategory? pregnancyDurationCategory)
    {
        if (!IsSupported(code))
        {
            throw new DomainValidationException("此連續曆日假別尚未開放申請。");
        }

        if (string.Equals(code, MaternityCode, StringComparison.Ordinal))
        {
            EnsureNoPregnancyDurationCategory(pregnancyDurationCategory);
            return Fixed(startDate, 56, null);
        }

        if (string.Equals(code, MiscarriageCode, StringComparison.Ordinal))
        {
            if (!pregnancyDurationCategory.HasValue ||
                !Enum.IsDefined(pregnancyDurationCategory.Value))
            {
                throw new DomainValidationException("流產假必須選擇妊娠期間類別。");
            }

            return Fixed(
                startDate,
                ResolveMiscarriageDays(pregnancyDurationCategory.Value),
                pregnancyDurationCategory);
        }

        EnsureNoPregnancyDurationCategory(pregnancyDurationCategory);
        var endDate = requestedEndDate ??
            throw new DomainValidationException("安胎休養假必須填寫結束日期。");
        if (endDate < startDate)
        {
            throw new DomainValidationException("結束日期不得早於開始日期。");
        }

        return new CalendarDayLeaveRange(
            startDate,
            endDate,
            checked(endDate.DayNumber - startDate.DayNumber + 1),
            null);
    }

    public static int ResolveMiscarriageDays(
        PregnancyDurationCategory category) => category switch
    {
        PregnancyDurationCategory.ThreeMonthsOrMore => 28,
        PregnancyDurationCategory.TwoToUnderThreeMonths => 7,
        PregnancyDurationCategory.UnderTwoMonths => 5,
        _ => throw new DomainValidationException("流產假妊娠期間類別無效。")
    };

    private static CalendarDayLeaveRange Fixed(
        DateOnly startDate,
        int calendarDayCount,
        PregnancyDurationCategory? category) =>
        new(
            startDate,
            startDate.AddDays(calendarDayCount - 1),
            calendarDayCount,
            category);

    private static void EnsureNoPregnancyDurationCategory(
        PregnancyDurationCategory? category)
    {
        if (category.HasValue)
        {
            throw new DomainValidationException("此假別不得設定流產假妊娠期間類別。");
        }
    }
}
