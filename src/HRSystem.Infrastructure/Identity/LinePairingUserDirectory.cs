using HRSystem.Application.Approvals;
using HRSystem.Application.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace HRSystem.Infrastructure.Identity;

public sealed class LinePairingUserDirectory(
    UserManager<ApplicationUser> userManager,
    RoleManager<IdentityRole> roleManager) : ILinePairingUserDirectory
{
    public async Task<IReadOnlyList<LinePairingUserDto>> GetOwnerUsersAsync(
        CancellationToken cancellationToken = default)
    {
        var role = await roleManager.FindByNameAsync(RoleNames.Owner);
        if (role is null) return [];
        var ownerIds = (await userManager.GetUsersInRoleAsync(RoleNames.Owner))
            .Select(x => x.Id).ToArray();
        var users = await userManager.Users.AsNoTracking()
            .Include(x => x.Employee)
            .Where(x => x.IsActive && ownerIds.Contains(x.Id))
            .OrderBy(x => x.UserName)
            .ToListAsync(cancellationToken);
        var result = new List<LinePairingUserDto>(users.Count);
        foreach (var user in users)
            result.Add(await MapAsync(user));
        return result;
    }

    public async Task<LinePairingUserDto?> FindActiveOwnerAsync(string userId,
        CancellationToken cancellationToken = default)
    {
        var user = await userManager.Users.AsNoTracking().Include(x => x.Employee)
            .SingleOrDefaultAsync(x => x.Id == userId && x.IsActive,
                cancellationToken);
        if (user is null) return null;
        var roles = await userManager.GetRolesAsync(user);
        return roles.Contains(RoleNames.Owner, StringComparer.Ordinal)
            ? Map(user, roles)
            : null;
    }

    private async Task<LinePairingUserDto> MapAsync(ApplicationUser user) =>
        Map(user, await userManager.GetRolesAsync(user));

    private static LinePairingUserDto Map(ApplicationUser user,
        IEnumerable<string> roles) => new(user.Id, user.UserName ?? string.Empty,
            user.DisplayName, user.Employee?.EmployeeNumber,
            user.Employee?.ChineseName, roles.OrderBy(x => x).ToArray());
}
