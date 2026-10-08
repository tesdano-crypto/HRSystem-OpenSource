using HRSystem.Application.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace HRSystem.Infrastructure.Identity;

public sealed class IdentitySeedService(
    RoleManager<IdentityRole> roleManager,
    UserManager<ApplicationUser> userManager,
    IConfiguration configuration,
    TimeProvider timeProvider,
    ILogger<IdentitySeedService> logger)
{
    public async Task<bool> SeedAsync(bool isProduction, CancellationToken cancellationToken = default)
    {
        var userName = configuration["HR_ADMIN_USERNAME"]?.Trim();
        var email = configuration["HR_ADMIN_EMAIL"]?.Trim();
        var password = configuration["HR_ADMIN_PASSWORD"];
        var displayName = configuration["HR_ADMIN_DISPLAY_NAME"]?.Trim();

        if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrWhiteSpace(email) ||
            string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(displayName))
        {
            const string message = "尚未設定初始管理員。請以 User Secrets 或環境變數設定 HR_ADMIN_USERNAME、HR_ADMIN_EMAIL、HR_ADMIN_PASSWORD、HR_ADMIN_DISPLAY_NAME。";
            if (isProduction)
            {
                throw new InvalidOperationException(message);
            }

            logger.LogWarning("{Message}", message);
            return false;
        }

        foreach (var role in RoleNames.All)
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                var roleResult = await roleManager.CreateAsync(new IdentityRole(role));
                EnsureSucceeded(roleResult, $"建立角色 {role}");
            }
        }

        var user = await userManager.FindByNameAsync(userName);
        if (user is null)
        {
            user = new ApplicationUser
            {
                UserName = userName,
                Email = email,
                DisplayName = displayName,
                IsActive = true,
                CreatedAtUtc = timeProvider.GetUtcNow()
            };
            var createResult = await userManager.CreateAsync(user, password);
            EnsureSucceeded(createResult, "建立初始管理員");
        }

        if (!await userManager.IsInRoleAsync(user, RoleNames.Admin))
        {
            EnsureSucceeded(await userManager.AddToRoleAsync(user, RoleNames.Admin), "指派初始管理員角色");
        }

        return true;
    }

    private static void EnsureSucceeded(IdentityResult result, string action)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException($"{action}失敗：{string.Join("；", result.Errors.Select(x => x.Description))}");
        }
    }
}
