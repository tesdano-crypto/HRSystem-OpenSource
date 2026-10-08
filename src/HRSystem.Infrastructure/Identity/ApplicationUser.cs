using HRSystem.Domain.MasterData;
using Microsoft.AspNetCore.Identity;

namespace HRSystem.Infrastructure.Identity;

public sealed class ApplicationUser : IdentityUser
{
    public Guid? EmployeeId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? LastLoginAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public Employee? Employee { get; set; }

    public bool CanSignIn => IsActive;

    public void MarkLogin(DateTimeOffset nowUtc) => LastLoginAtUtc = nowUtc;
}
