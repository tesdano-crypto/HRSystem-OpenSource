namespace HRSystem.Application.Security;

public static class RoleNames
{
    public const string Admin = "Admin";
    public const string HR = "HR";
    public const string Accounting = "Accounting";
    public const string Owner = "Owner";
    public const string Manager = "Manager";
    public const string Employee = "Employee";
    public const string AdminOrManager = Admin + "," + Manager;

    public static readonly IReadOnlyList<string> Ordered =
        [Admin, HR, Accounting, Owner, Manager, Employee];

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        Admin, HR, Accounting, Owner, Manager, Employee
    };
}
