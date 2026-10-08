namespace HRSystem.Application.Abstractions.Security;

public interface ICurrentUser
{
    string? UserId { get; }
    Guid? EmployeeId { get; }
    string? DisplayName { get; }
    string? IpAddress { get; }
    bool IsAuthenticated { get; }
    bool IsInRole(string role);
    bool HasPermission(string policy);
}
