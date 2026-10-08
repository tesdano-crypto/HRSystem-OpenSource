using HRSystem.Domain.Common;

namespace HRSystem.Domain.CompTime;

public static class CompTimePolicy
{
    public const string LeaveTypeCode = "COMP_TIME";
    public const string LeaveTypeName = "補休";
    public const decimal MinimumIncrementHours = 0.5m;
    public const int ReasonMaxLength = 1000;
    public const int CreatedByMaxLength = 450;

    public static bool IsCompTime(string? leaveTypeCode) =>
        string.Equals(leaveTypeCode, LeaveTypeCode, StringComparison.Ordinal);

    public static decimal ValidateHours(decimal hours)
    {
        if (hours <= 0m)
        {
            throw new DomainValidationException("補休時數必須大於 0。");
        }

        if (decimal.Truncate(hours / MinimumIncrementHours) !=
            hours / MinimumIncrementHours)
        {
            throw new DomainValidationException("補休時數必須以 0.5 小時為單位。");
        }

        return hours;
    }
}
