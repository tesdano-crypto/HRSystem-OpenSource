namespace HRSystem.Domain.Payroll;

public static class PayrollLegacyAdjustmentComponents
{
    public const string AttendanceAllowanceCode = "LEGACY_ATTENDANCE_ALLOWANCE";
    public const string OvertimePayCode = "LEGACY_OVERTIME_PAY";

    public static bool IsLegacy(string? code) =>
        string.Equals(code, AttendanceAllowanceCode, StringComparison.Ordinal) ||
        string.Equals(code, OvertimePayCode, StringComparison.Ordinal);
}
