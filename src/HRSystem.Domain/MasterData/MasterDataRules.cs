using HRSystem.Domain.Common;

namespace HRSystem.Domain.MasterData;

internal static class MasterDataRules
{
    public static string RequiredCode(string? value, string fieldName, int maxLength = 20)
    {
        var normalized = value?.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new DomainValidationException($"{fieldName}為必填欄位。");
        }

        if (normalized.Length > maxLength)
        {
            throw new DomainValidationException($"{fieldName}不可超過 {maxLength} 個字元。");
        }

        return normalized;
    }

    public static string RequiredText(string? value, string fieldName, int maxLength)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new DomainValidationException($"{fieldName}為必填欄位。");
        }

        if (normalized.Length > maxLength)
        {
            throw new DomainValidationException($"{fieldName}不可超過 {maxLength} 個字元。");
        }

        return normalized;
    }

    public static string? OptionalText(string? value, string fieldName, int maxLength)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        if (normalized?.Length > maxLength)
        {
            throw new DomainValidationException($"{fieldName}不可超過 {maxLength} 個字元。");
        }

        return normalized;
    }
}
