using HRSystem.Domain.Common;

namespace HRSystem.Domain.Attendance;

internal static class AttendanceRules
{
    public static string Required(string? value, string field, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainValidationException($"{field}為必填欄位。");
        }

        var normalized = value.Trim();
        if (normalized.Length > maxLength)
        {
            throw new DomainValidationException($"{field}不可超過 {maxLength} 個字元。");
        }

        return normalized;
    }

    public static string? Optional(string? value, string field, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return Required(value, field, maxLength);
    }

    public static DateTime SourceLocalTime(DateTime value, string field)
    {
        if (value.Kind != DateTimeKind.Unspecified)
        {
            throw new DomainValidationException($"{field}必須是不含時區的來源本地時間。");
        }

        return value;
    }

    public static DateTime? OptionalSourceLocalTime(DateTime? value, string field) =>
        value.HasValue ? SourceLocalTime(value.Value, field) : null;

    public static void ValidateEffectivePeriod(DateTime effectiveFrom, DateTime? effectiveTo)
    {
        SourceLocalTime(effectiveFrom, "生效起始時間");
        OptionalSourceLocalTime(effectiveTo, "生效結束時間");
        if (effectiveTo.HasValue && effectiveTo.Value <= effectiveFrom)
        {
            throw new DomainValidationException("生效結束時間必須晚於生效起始時間。");
        }
    }
}
