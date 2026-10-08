using Microsoft.AspNetCore.Identity;

namespace HRSystem.Infrastructure.Identity;

public sealed class NonWhitespacePasswordValidator
    : IPasswordValidator<ApplicationUser>
{
    public Task<IdentityResult> ValidateAsync(
        UserManager<ApplicationUser> manager,
        ApplicationUser user,
        string? password) =>
        Task.FromResult(string.IsNullOrWhiteSpace(password)
            ? IdentityResult.Failed(new IdentityError
            {
                Code = "PasswordRequiresNonWhitespace",
                Description = "密碼不可只包含空白字元。"
            })
            : IdentityResult.Success);
}
