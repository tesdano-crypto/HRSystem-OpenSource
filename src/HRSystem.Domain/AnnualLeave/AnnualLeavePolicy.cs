using HRSystem.Domain.Common;

namespace HRSystem.Domain.AnnualLeave;

public static class AnnualLeavePolicy
{
    public const string LeaveTypeCode = "ANNUAL";
    public const int StandardWorkdayMinutes = 480;

    public static IReadOnlyList<AnnualLeaveGrantDefinition> GetGrants(
        DateOnly hireDate,
        DateOnly throughDate)
    {
        if (throughDate < hireDate)
        {
            return [];
        }

        var grants = new List<AnnualLeaveGrantDefinition>();
        var halfYearDate = AddMonthsClamped(hireDate, 6);
        if (halfYearDate <= throughDate)
        {
            grants.Add(new AnnualLeaveGrantDefinition(
                AnnualLeaveMilestone.HalfYear,
                0,
                halfYearDate,
                halfYearDate,
                AddYearsClamped(hireDate, 1).AddDays(-1),
                3));
        }

        for (var completedYears = 1; ; completedYears++)
        {
            var grantedDate = AddYearsClamped(hireDate, completedYears);
            if (grantedDate > throughDate)
            {
                break;
            }

            grants.Add(new AnnualLeaveGrantDefinition(
                AnnualLeaveMilestone.Anniversary,
                completedYears,
                grantedDate,
                grantedDate,
                AddYearsClamped(hireDate, completedYears + 1).AddDays(-1),
                ResolveGrantedDays(completedYears)));
        }

        return grants;
    }

    public static decimal ResolveGrantedDays(int completedYears) => completedYears switch
    {
        < 1 => throw new DomainValidationException("週年額度必須至少完成一年年資。"),
        1 => 7,
        2 => 10,
        3 or 4 => 14,
        >= 5 and < 10 => 15,
        _ => Math.Min(30, completedYears + 6)
    };

    public static DateOnly AddYearsClamped(DateOnly date, int years) =>
        AddMonthsClamped(date, checked(years * 12));

    public static DateOnly AddMonthsClamped(DateOnly date, int months)
    {
        var first = new DateOnly(date.Year, date.Month, 1).AddMonths(months);
        return new DateOnly(
            first.Year,
            first.Month,
            Math.Min(date.Day, DateTime.DaysInMonth(first.Year, first.Month)));
    }

    public static int ToMinutes(decimal days) => checked((int)(days * StandardWorkdayMinutes));
}

public sealed record AnnualLeaveGrantDefinition(
    AnnualLeaveMilestone Milestone,
    int CompletedServiceYears,
    DateOnly GrantedDate,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    decimal GrantedDays)
{
    public int GrantedMinutes => AnnualLeavePolicy.ToMinutes(GrantedDays);
}
