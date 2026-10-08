using HRSystem.Application.Common.Auditing;
using HRSystem.Domain.Auditing;
using HRSystem.Infrastructure.Identity;
using HRSystem.Infrastructure.Persistence;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;

namespace HRSystem.Web.Authorization;

public static class AccountEndpoints
{
    public static IEndpointRouteBuilder MapAccountEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/account/login", LoginAsync).AllowAnonymous();
        endpoints.MapPost("/logout", LogoutAsync).RequireAuthorization();
        endpoints.MapPost("/account/change-password/submit", ChangePasswordAsync).RequireAuthorization();
        return endpoints;
    }

    private static async Task<IResult> LoginAsync(
        HttpContext httpContext,
        IAntiforgery antiforgery,
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        HRSystemDbContext dbContext,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        await antiforgery.ValidateRequestAsync(httpContext);
        var form = await httpContext.Request.ReadFormAsync(cancellationToken);
        var account = form["account"].ToString().Trim();
        var password = form["password"].ToString();
        var rememberMe = string.Equals(form["rememberMe"], "true", StringComparison.OrdinalIgnoreCase);
        var returnUrl = SafeReturnUrl(form["returnUrl"], "/");

        var userByName = await userManager.FindByNameAsync(account);
        var userByEmail = await userManager.FindByEmailAsync(account);
        if (userByName is not null &&
            userByEmail is not null &&
            !string.Equals(userByName.Id, userByEmail.Id, StringComparison.Ordinal))
        {
            return LoginError("帳號或密碼不正確。", returnUrl);
        }

        var user = userByName ?? userByEmail;
        if (user is null)
        {
            return LoginError("帳號或密碼不正確。", returnUrl);
        }

        if (!user.CanSignIn)
        {
            return LoginError("此帳號已停用，請洽系統管理員。", returnUrl);
        }

        var result = await signInManager.PasswordSignInAsync(user, password, rememberMe, lockoutOnFailure: true);
        if (!result.Succeeded)
        {
            var message = result.IsLockedOut
                ? "登入失敗次數過多，帳號已暫時鎖定。"
                : "帳號或密碼不正確。";
            return LoginError(message, returnUrl);
        }

        user.MarkLogin(timeProvider.GetUtcNow());
        var updateResult = await userManager.UpdateAsync(user);
        if (!updateResult.Succeeded)
        {
            await signInManager.SignOutAsync();
            return LoginError("無法完成登入，請稍後再試。", returnUrl);
        }

        dbContext.AuditLogs.Add(new AuditLog(
            user.Id,
            AuditActions.LoginSucceeded,
            nameof(ApplicationUser),
            user.Id,
            null,
            null,
            httpContext.Connection.RemoteIpAddress?.ToString(),
            timeProvider.GetUtcNow()));
        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.LocalRedirect(returnUrl);
    }

    private static async Task<IResult> LogoutAsync(
        HttpContext httpContext,
        IAntiforgery antiforgery,
        SignInManager<ApplicationUser> signInManager)
    {
        await antiforgery.ValidateRequestAsync(httpContext);
        await signInManager.SignOutAsync();
        return Results.LocalRedirect("/login");
    }

    private static async Task<IResult> ChangePasswordAsync(
        HttpContext httpContext,
        IAntiforgery antiforgery,
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        HRSystemDbContext dbContext,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        await antiforgery.ValidateRequestAsync(httpContext);
        var form = await httpContext.Request.ReadFormAsync(cancellationToken);
        var currentPassword = form["currentPassword"].ToString();
        var newPassword = form["newPassword"].ToString();
        var confirmPassword = form["confirmPassword"].ToString();
        if (!string.Equals(newPassword, confirmPassword, StringComparison.Ordinal))
        {
            return PasswordError("確認密碼與新密碼不一致。");
        }

        var user = await userManager.GetUserAsync(httpContext.User);
        if (user is null || !user.IsActive)
        {
            return Results.LocalRedirect("/login");
        }

        var result = await userManager.ChangePasswordAsync(user, currentPassword, newPassword);
        if (!result.Succeeded)
        {
            return PasswordError(string.Join("；", result.Errors.Select(x => x.Description)));
        }

        dbContext.AuditLogs.Add(new AuditLog(
            user.Id,
            AuditActions.PasswordChanged,
            nameof(ApplicationUser),
            user.Id,
            null,
            null,
            httpContext.Connection.RemoteIpAddress?.ToString(),
            timeProvider.GetUtcNow()));
        await dbContext.SaveChangesAsync(cancellationToken);
        await signInManager.RefreshSignInAsync(user);
        return Results.LocalRedirect("/account/change-password?success=true");
    }

    private static IResult LoginError(string message, string returnUrl) => Results.LocalRedirect(
        $"/login?error={Uri.EscapeDataString(message)}&returnUrl={Uri.EscapeDataString(returnUrl)}");

    private static IResult PasswordError(string message) => Results.LocalRedirect(
        $"/account/change-password?error={Uri.EscapeDataString(message)}");

    private static string SafeReturnUrl(string? returnUrl, string fallback) =>
        !string.IsNullOrWhiteSpace(returnUrl) && Uri.IsWellFormedUriString(returnUrl, UriKind.Relative) && returnUrl.StartsWith('/') && !returnUrl.StartsWith("//")
            ? returnUrl
            : fallback;
}
