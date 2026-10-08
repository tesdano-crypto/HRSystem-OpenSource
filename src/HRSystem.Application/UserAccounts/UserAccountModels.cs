using System.ComponentModel.DataAnnotations;

namespace HRSystem.Application.UserAccounts;

public static class InternalPasswordPolicy
{
    public const int MinimumLength = 4;
}

public sealed record UserAccountDto(
    string Id,
    string UserName,
    string Email,
    string DisplayName,
    Guid? EmployeeId,
    string? EmployeeNumber,
    string? EmployeeName,
    bool IsActive,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? LastLoginAtUtc,
    IReadOnlyList<string> Roles,
    string RowVersion);

public sealed record CurrentUserProfileDto(
    string UserName,
    string Email,
    string DisplayName,
    Guid? EmployeeId,
    string? EmployeeNumber,
    string? EmployeeName,
    IReadOnlyList<string> Roles,
    DateTimeOffset? LastLoginAtUtc);

public sealed class UserAccountQuery
{
    public string? Keyword { get; set; }
    public string? Role { get; set; }
    public bool? IsActive { get; set; }
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 10;
}

public sealed class CreateUserAccountRequest : IValidatableObject
{
    [Required(ErrorMessage = "使用者名稱為必填欄位。")]
    [StringLength(100, MinimumLength = 3, ErrorMessage = "使用者名稱須為 3 至 100 個字元。")]
    public string UserName { get; set; } = string.Empty;

    public string? Email { get; set; }

    [Required(ErrorMessage = "顯示名稱為必填欄位。")]
    [StringLength(100)]
    public string DisplayName { get; set; } = string.Empty;

    [Required(ErrorMessage = "暫時密碼為必填欄位。")]
    [MinLength(
        InternalPasswordPolicy.MinimumLength,
        ErrorMessage = "暫時密碼至少需要 4 個字元。")]
    [DataType(DataType.Password)]
    public string TemporaryPassword { get; set; } = string.Empty;

    public Guid? EmployeeId { get; set; }
    public List<string> Roles { get; set; } = [];

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!string.IsNullOrWhiteSpace(UserName) &&
            UserName.Trim().Length is < 3 or > 100)
        {
            yield return new ValidationResult(
                "使用者名稱須為 3 至 100 個字元。",
                [nameof(UserName)]);
        }

        foreach (var result in ValidateOptionalEmail(Email))
        {
            yield return result;
        }

        if (Roles.Count == 0)
        {
            yield return new ValidationResult("帳號至少需要一個角色。", [nameof(Roles)]);
        }
    }

    private static IEnumerable<ValidationResult> ValidateOptionalEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            yield break;
        }

        var trimmed = email.Trim();
        if (trimmed.Length > 254 || !new EmailAddressAttribute().IsValid(trimmed))
        {
            yield return new ValidationResult("Email 格式不正確。", [nameof(Email)]);
        }
    }
}

public sealed class UpdateUserAccountRequest : IValidatableObject
{
    public string Id { get; set; } = string.Empty;

    public string? Email { get; set; }

    [Required(ErrorMessage = "顯示名稱為必填欄位。")]
    [StringLength(100)]
    public string DisplayName { get; set; } = string.Empty;

    public Guid? EmployeeId { get; set; }
    public List<string> Roles { get; set; } = [];
    public string RowVersion { get; set; } = string.Empty;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!string.IsNullOrWhiteSpace(Email))
        {
            var trimmed = Email.Trim();
            if (trimmed.Length > 254 || !new EmailAddressAttribute().IsValid(trimmed))
            {
                yield return new ValidationResult("Email 格式不正確。", [nameof(Email)]);
            }
        }

        if (Roles.Count == 0)
        {
            yield return new ValidationResult("帳號至少需要一個角色。", [nameof(Roles)]);
        }
    }
}

public sealed class ResetUserPasswordRequest
{
    public string UserId { get; set; } = string.Empty;

    [Required(ErrorMessage = "新密碼為必填欄位。")]
    [MinLength(
        InternalPasswordPolicy.MinimumLength,
        ErrorMessage = "新密碼至少需要 4 個字元。")]
    [DataType(DataType.Password)]
    public string NewPassword { get; set; } = string.Empty;

    [Compare(nameof(NewPassword), ErrorMessage = "確認密碼與新密碼不一致。")]
    [DataType(DataType.Password)]
    public string ConfirmPassword { get; set; } = string.Empty;
}
