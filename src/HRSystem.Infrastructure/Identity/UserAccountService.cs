using System.Data;
using HRSystem.Application.Abstractions.Security;
using HRSystem.Application.Common.Auditing;
using HRSystem.Application.Common.Exceptions;
using HRSystem.Application.Common.Models;
using HRSystem.Application.Common.Validation;
using HRSystem.Application.Security;
using HRSystem.Application.UserAccounts;
using HRSystem.Domain.Auditing;
using HRSystem.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace HRSystem.Infrastructure.Identity;

public sealed class UserAccountService(
    HRSystemDbContext dbContext,
    UserManager<ApplicationUser> userManager,
    RoleManager<IdentityRole> roleManager,
    ICurrentUser currentUser,
    TimeProvider timeProvider) : IUserAccountService
{
    public async Task<PagedResult<UserAccountDto>> GetListAsync(
        UserAccountQuery query,
        CancellationToken cancellationToken = default)
    {
        EnsureManagePermission();
        var pageNumber = Math.Max(1, query.PageNumber);
        var pageSize = Math.Clamp(query.PageSize, 1, 100);
        var users = dbContext.Users.AsNoTracking().Include(x => x.Employee).AsQueryable();

        if (!string.IsNullOrWhiteSpace(query.Keyword))
        {
            var keyword = query.Keyword.Trim().ToUpperInvariant();
            users = users.Where(x =>
                (x.NormalizedUserName != null && x.NormalizedUserName.Contains(keyword)) ||
                (x.NormalizedEmail != null && x.NormalizedEmail.Contains(keyword)) ||
                x.DisplayName.ToUpper().Contains(keyword));
        }

        if (query.IsActive.HasValue)
        {
            users = users.Where(x => x.IsActive == query.IsActive.Value);
        }

        if (!string.IsNullOrWhiteSpace(query.Role))
        {
            var role = await roleManager.FindByNameAsync(query.Role.Trim());
            users = role is null
                ? users.Where(_ => false)
                : users.Where(user => dbContext.UserRoles.Any(x => x.UserId == user.Id && x.RoleId == role.Id));
        }

        var totalCount = await users.CountAsync(cancellationToken);
        var entities = await users.OrderBy(x => x.UserName)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        var items = new List<UserAccountDto>(entities.Count);
        foreach (var user in entities)
        {
            items.Add(await MapAsync(user));
        }

        return new PagedResult<UserAccountDto>(items, totalCount, pageNumber, pageSize);
    }

    public async Task<UserAccountDto> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        EnsureManagePermission();
        var user = await dbContext.Users.AsNoTracking().Include(x => x.Employee)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new EntityNotFoundException("找不到指定的使用者帳號。");
        return await MapAsync(user);
    }

    public async Task<CurrentUserProfileDto> GetCurrentProfileAsync(CancellationToken cancellationToken = default)
    {
        if (!currentUser.IsAuthenticated || string.IsNullOrWhiteSpace(currentUser.UserId))
        {
            throw new ForbiddenAccessException("請先登入。");
        }

        var user = await dbContext.Users.AsNoTracking().Include(x => x.Employee)
            .SingleOrDefaultAsync(x => x.Id == currentUser.UserId, cancellationToken)
            ?? throw new EntityNotFoundException("找不到目前登入帳號。");
        return new CurrentUserProfileDto(
            user.UserName ?? string.Empty,
            user.Email ?? string.Empty,
            user.DisplayName,
            user.EmployeeId,
            user.Employee?.EmployeeNumber,
            user.Employee?.ChineseName,
            (await userManager.GetRolesAsync(user)).OrderBy(x => x).ToList(),
            user.LastLoginAtUtc);
    }

    public async Task<UserAccountDto> CreateAsync(
        CreateUserAccountRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureManagePermission();
        RequestValidator.Validate(request);
        UserAccountRules.EnsureRoles(request.Roles);
        await EnsureRolesExistAsync(request.Roles);
        await EnsureEmailUniqueAsync(request.Email, null, cancellationToken);
        await EnsureEmployeeAvailableAsync(request.EmployeeId, null, cancellationToken);

        var user = new ApplicationUser
        {
            UserName = request.UserName.Trim(),
            Email = NormalizeOptionalEmail(request.Email),
            DisplayName = request.DisplayName.Trim(),
            EmployeeId = request.EmployeeId,
            IsActive = true,
            CreatedAtUtc = timeProvider.GetUtcNow()
        };
        var createResult = await userManager.CreateAsync(user, request.TemporaryPassword);
        EnsureSucceeded(createResult, "建立帳號失敗");
        var roleResult = await userManager.AddToRolesAsync(user, request.Roles.Distinct(StringComparer.Ordinal));
        if (!roleResult.Succeeded)
        {
            await userManager.DeleteAsync(user);
            EnsureSucceeded(roleResult, "指派角色失敗");
        }

        dbContext.AuditLogs.Add(CreateAudit(AuditActions.Created, user, null, Snapshot(user, request.Roles)));
        await dbContext.SaveChangesAsync(cancellationToken);
        return await GetForAdminAsync(user.Id, cancellationToken);
    }

    public async Task<UserAccountDto> UpdateAsync(
        UpdateUserAccountRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureManagePermission();
        RequestValidator.Validate(request);
        UserAccountRules.EnsureRoles(request.Roles);
        return await dbContext.ExecuteTransactionAsync(
            IsolationLevel.ReadCommitted,
            async transactionCancellationToken =>
            {
                await EnsureRolesExistAsync(request.Roles);
                var user = await dbContext.Users.Include(x => x.Employee)
                    .SingleOrDefaultAsync(x => x.Id == request.Id, transactionCancellationToken)
                    ?? throw new EntityNotFoundException("找不到指定的使用者帳號。");
                EnsureRowVersion(user.RowVersion, request.RowVersion);
                await EnsureEmailUniqueAsync(request.Email, user.Id, transactionCancellationToken);
                await EnsureEmployeeAvailableAsync(request.EmployeeId, user.Id, transactionCancellationToken);

                var currentRoles = await userManager.GetRolesAsync(user);
                var isAdmin = currentRoles.Contains(RoleNames.Admin, StringComparer.Ordinal);
                var remainsAdmin = request.Roles.Contains(RoleNames.Admin, StringComparer.Ordinal) && user.IsActive;
                UserAccountRules.EnsureAdminRemains(
                    isAdmin,
                    await ActiveAdminCountAsync(transactionCancellationToken),
                    remainsAdmin);
                var oldValues = Snapshot(user, currentRoles);
                user.DisplayName = request.DisplayName.Trim();
                user.Email = NormalizeOptionalEmail(request.Email);
                user.EmployeeId = request.EmployeeId;
                EnsureSucceeded(await userManager.UpdateAsync(user), "更新帳號失敗");

                var removed = currentRoles.Except(request.Roles, StringComparer.Ordinal).ToList();
                var added = request.Roles.Except(currentRoles, StringComparer.Ordinal).ToList();
                if (removed.Count > 0)
                {
                    EnsureSucceeded(await userManager.RemoveFromRolesAsync(user, removed), "移除角色失敗");
                }

                if (added.Count > 0)
                {
                    EnsureSucceeded(await userManager.AddToRolesAsync(user, added), "指派角色失敗");
                }

                dbContext.AuditLogs.Add(CreateAudit(
                    AuditActions.Updated,
                    user,
                    oldValues,
                    Snapshot(user, request.Roles)));
                await dbContext.SaveChangesAsync(transactionCancellationToken);
                return await GetForAdminAsync(user.Id, transactionCancellationToken);
            },
            cancellationToken);
    }

    public async Task SetActiveAsync(
        string id,
        bool isActive,
        string rowVersion,
        CancellationToken cancellationToken = default)
    {
        EnsureManagePermission();
        var user = await dbContext.Users.SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new EntityNotFoundException("找不到指定的使用者帳號。");
        EnsureRowVersion(user.RowVersion, rowVersion);
        var roles = await userManager.GetRolesAsync(user);
        if (!isActive)
        {
            UserAccountRules.EnsureCanDeactivate(currentUser.UserId, user.Id);
            UserAccountRules.EnsureAdminRemains(
                roles.Contains(RoleNames.Admin, StringComparer.Ordinal),
                await ActiveAdminCountAsync(cancellationToken),
                false);
        }

        var oldValues = Snapshot(user, roles);
        user.IsActive = isActive;
        EnsureSucceeded(await userManager.UpdateAsync(user), "更新帳號狀態失敗");
        EnsureSucceeded(await userManager.UpdateSecurityStampAsync(user), "更新登入安全狀態失敗");
        dbContext.AuditLogs.Add(CreateAudit(
            isActive ? AuditActions.Activated : AuditActions.Deactivated,
            user, oldValues, Snapshot(user, roles)));
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task ResetPasswordAsync(
        ResetUserPasswordRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureManagePermission();
        RequestValidator.Validate(request);
        var user = await userManager.FindByIdAsync(request.UserId)
            ?? throw new EntityNotFoundException("找不到指定的使用者帳號。");
        var token = await userManager.GeneratePasswordResetTokenAsync(user);
        EnsureSucceeded(await userManager.ResetPasswordAsync(user, token, request.NewPassword), "重設密碼失敗");
        dbContext.AuditLogs.Add(CreateAudit(AuditActions.PasswordReset, user, null, new { user.Id, user.UserName }));
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private void EnsureManagePermission()
    {
        if (!currentUser.IsAuthenticated || !currentUser.HasPermission(PolicyNames.UserAdmin))
        {
            throw new ForbiddenAccessException("您沒有管理使用者帳號的權限。");
        }
    }

    private async Task EnsureRolesExistAsync(IEnumerable<string> roles)
    {
        foreach (var role in roles.Distinct(StringComparer.Ordinal))
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                throw new ApplicationValidationException($"角色 {role} 尚未建立。");
            }
        }
    }

    private async Task EnsureEmailUniqueAsync(
        string? email,
        string? excludedId,
        CancellationToken cancellationToken)
    {
        var suppliedEmail = NormalizeOptionalEmail(email);
        if (suppliedEmail is null)
        {
            return;
        }

        var normalized = userManager.NormalizeEmail(suppliedEmail);
        if (await dbContext.Users.AnyAsync(
                x => x.NormalizedEmail == normalized && (excludedId == null || x.Id != excludedId), cancellationToken))
        {
            throw new ApplicationValidationException("Email 已被其他帳號使用。");
        }
    }

    private static string? NormalizeOptionalEmail(string? email) =>
        string.IsNullOrWhiteSpace(email) ? null : email.Trim();

    private async Task EnsureEmployeeAvailableAsync(Guid? employeeId, string? excludedUserId, CancellationToken cancellationToken)
    {
        if (!employeeId.HasValue) return;
        if (!await dbContext.Employees.AnyAsync(x => x.Id == employeeId.Value, cancellationToken))
        {
            throw new ApplicationValidationException("指定的員工不存在。");
        }

        if (await dbContext.Users.AnyAsync(
                x => x.EmployeeId == employeeId && (excludedUserId == null || x.Id != excludedUserId), cancellationToken))
        {
            throw new ApplicationValidationException("此員工已綁定其他帳號。");
        }
    }

    private async Task<int> ActiveAdminCountAsync(CancellationToken cancellationToken)
    {
        var adminRoleId = await dbContext.Roles.Where(x => x.NormalizedName == RoleNames.Admin.ToUpperInvariant())
            .Select(x => x.Id).SingleAsync(cancellationToken);
        return await dbContext.Users.Where(x => x.IsActive)
            .Join(dbContext.UserRoles.Where(x => x.RoleId == adminRoleId), x => x.Id, x => x.UserId, (user, _) => user)
            .CountAsync(cancellationToken);
    }

    private async Task<UserAccountDto> GetForAdminAsync(string id, CancellationToken cancellationToken)
    {
        var user = await dbContext.Users.AsNoTracking().Include(x => x.Employee)
            .SingleAsync(x => x.Id == id, cancellationToken);
        return await MapAsync(user);
    }

    private async Task<UserAccountDto> MapAsync(ApplicationUser user) => new(
        user.Id,
        user.UserName ?? string.Empty,
        user.Email ?? string.Empty,
        user.DisplayName,
        user.EmployeeId,
        user.Employee?.EmployeeNumber,
        user.Employee?.ChineseName,
        user.IsActive,
        user.CreatedAtUtc,
        user.LastLoginAtUtc,
        (await userManager.GetRolesAsync(user)).OrderBy(x => x).ToList(),
        Convert.ToBase64String(user.RowVersion));

    private AuditLog CreateAudit(string action, ApplicationUser user, object? oldValues, object? newValues) =>
        AuditLogFactory.Create(currentUser, timeProvider, action, nameof(ApplicationUser), user.Id, oldValues, newValues);

    private static object Snapshot(ApplicationUser user, IEnumerable<string> roles) => new
    {
        user.Id,
        user.UserName,
        user.Email,
        user.DisplayName,
        user.EmployeeId,
        user.IsActive,
        Roles = roles.OrderBy(x => x).ToArray()
    };

    private static void EnsureRowVersion(byte[] current, string supplied)
    {
        var expected = string.IsNullOrWhiteSpace(supplied) ? [] : Convert.FromBase64String(supplied);
        if (!current.SequenceEqual(expected)) throw new ConcurrencyConflictException();
    }

    private static void EnsureSucceeded(IdentityResult result, string prefix)
    {
        if (!result.Succeeded)
        {
            throw new ApplicationValidationException($"{prefix}：{string.Join("；", result.Errors.Select(x => x.Description))}");
        }
    }
}
