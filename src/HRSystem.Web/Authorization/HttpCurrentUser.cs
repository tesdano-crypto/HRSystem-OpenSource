using System.Security.Claims;
using HRSystem.Application.Abstractions.Security;
using HRSystem.Infrastructure.Identity;

namespace HRSystem.Web.Authorization;

public sealed class HttpCurrentUser : ICurrentUser
{
    private readonly ClaimsPrincipal _principal;

    public HttpCurrentUser(IHttpContextAccessor httpContextAccessor)
    {
        var context = httpContextAccessor.HttpContext;
        _principal = context?.User ?? new ClaimsPrincipal(new ClaimsIdentity());
        IpAddress = context?.Connection.RemoteIpAddress?.ToString();
    }

    public string? UserId => _principal.FindFirstValue(ClaimTypes.NameIdentifier);
    public Guid? EmployeeId => Guid.TryParse(_principal.FindFirstValue(ApplicationUserClaimsPrincipalFactory.EmployeeIdClaimType), out var id) ? id : null;
    public string? DisplayName => _principal.FindFirstValue(ApplicationUserClaimsPrincipalFactory.DisplayNameClaimType) ?? _principal.Identity?.Name;
    public string? IpAddress { get; }
    public bool IsAuthenticated => _principal.Identity?.IsAuthenticated == true;
    public bool IsInRole(string role) => _principal.IsInRole(role);
    public bool HasPermission(string policy)
    {
        var roles = _principal.FindAll(ClaimTypes.Role).Select(x => x.Value);
        return HRSystem.Application.Security.RolePermissions.HasPermission(roles, policy);
    }
}
