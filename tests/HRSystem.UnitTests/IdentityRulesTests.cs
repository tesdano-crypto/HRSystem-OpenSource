using System.ComponentModel.DataAnnotations;
using HRSystem.Application.Abstractions.Security;
using HRSystem.Application.Common.Exceptions;
using HRSystem.Application.Security;
using HRSystem.Application.UserAccounts;
using HRSystem.Domain.MasterData;
using HRSystem.Infrastructure;
using HRSystem.Infrastructure.Identity;
using HRSystem.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace HRSystem.UnitTests;

public sealed class IdentityRulesTests
{
    [Fact]
    public void HR_Permission_Matrix_Is_Least_Privilege()
    {
        AssertAllowed(RoleNames.HR,
            PolicyNames.EmployeeManage, PolicyNames.AttendanceManage,
            PolicyNames.LeaveManage, PolicyNames.OvertimeManage,
            PolicyNames.InsuranceManage, PolicyNames.PayrollView);
        AssertDenied(RoleNames.HR,
            PolicyNames.PayrollManage, PolicyNames.PayrollApprove,
            PolicyNames.UserAdmin);
    }

    [Fact]
    public void Accounting_Permission_Matrix_Is_Least_Privilege()
    {
        AssertAllowed(RoleNames.Accounting,
            PolicyNames.AttendanceViewAll, PolicyNames.AttendanceDailyReport,
            PolicyNames.AttendanceManage, PolicyNames.LeaveManage,
            PolicyNames.OvertimeManage, PolicyNames.InsuranceManage,
            PolicyNames.PayrollView, PolicyNames.PayrollManage,
            PolicyNames.PayrollSubmitApproval);
        AssertDenied(RoleNames.Accounting,
            PolicyNames.EmployeeManage, PolicyNames.PayrollApprove,
            PolicyNames.PayrollFinalize, PolicyNames.UserAdmin);
    }

    [Fact]
    public void Owner_Permission_Matrix_Is_Least_Privilege()
    {
        AssertAllowed(RoleNames.Owner,
            PolicyNames.PayrollView, PolicyNames.ApprovalView,
            PolicyNames.ApprovalAct, PolicyNames.ApprovalHistoryView,
            PolicyNames.PayrollApprove);
        AssertDenied(RoleNames.Owner,
            PolicyNames.PayrollManage, PolicyNames.EmployeeManage,
            PolicyNames.AttendanceManage, PolicyNames.UserAdmin);
    }

    [Theory]
    [InlineData(RoleNames.Manager)]
    [InlineData(RoleNames.Employee)]
    public void Existing_Self_Service_Roles_Do_Not_Gain_Admin_Permissions(string role)
    {
        AssertDenied(role,
            PolicyNames.AttendanceViewAll, PolicyNames.AttendanceManage,
            PolicyNames.LeaveManage, PolicyNames.OvertimeManage,
            PolicyNames.InsuranceManage, PolicyNames.PayrollView,
            PolicyNames.PayrollManage, PolicyNames.UserAdmin);
    }

    [Fact]
    public void Multiple_Roles_Use_The_Union_Of_Known_Permissions()
    {
        var roles = new[] { RoleNames.Employee, RoleNames.Owner };

        Assert.True(RolePermissions.HasPermission(
            roles, PolicyNames.AttendanceSelfService));
        Assert.True(RolePermissions.HasPermission(
            roles, PolicyNames.PayrollApprove));
        Assert.False(RolePermissions.HasPermission(roles, PolicyNames.UserAdmin));
        Assert.False(RolePermissions.HasPermission(["UnknownRole"],
            PolicyNames.PayrollView));
    }

    [Fact]
    public void Security_Display_Uses_Chinese_Role_And_Permission_Names()
    {
        Assert.Equal("系統管理員", SecurityDisplayCatalog.RoleName(RoleNames.Admin));
        Assert.Equal("人資", SecurityDisplayCatalog.RoleName(RoleNames.HR));
        Assert.Equal("會計", SecurityDisplayCatalog.RoleName(RoleNames.Accounting));
        Assert.Equal("老闆", SecurityDisplayCatalog.RoleName(RoleNames.Owner));
        Assert.Equal("主管", SecurityDisplayCatalog.RoleName(RoleNames.Manager));
        Assert.Equal("一般員工", SecurityDisplayCatalog.RoleName(RoleNames.Employee));
        Assert.Equal("員工資料管理",
            SecurityDisplayCatalog.PermissionName(PolicyNames.EmployeeManage));
        Assert.Equal("薪資結算核准",
            SecurityDisplayCatalog.PermissionName(PolicyNames.PayrollApprove));
    }

    [Fact]
    public void Disabled_Account_Cannot_Sign_In()
    {
        var user = new ApplicationUser { IsActive = false };
        Assert.False(user.CanSignIn);
    }

    [Fact]
    public async Task Employee_Cannot_Be_Bound_To_Two_Accounts()
    {
        await using var environment = await IdentityUnitEnvironment.CreateAsync();
        var employeeId = await environment.AddEmployeeAsync();
        await environment.CreateUserAsync("first", "first@example.test", employeeId);

        var service = environment.Services.GetRequiredService<IUserAccountService>();
        var exception = await Assert.ThrowsAsync<ApplicationValidationException>(() => service.CreateAsync(
            environment.NewUser("second", "second@example.test", employeeId)));

        Assert.Contains("員工", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Account_Must_Have_At_Least_One_Role()
    {
        Assert.Throws<ApplicationValidationException>(() => UserAccountRules.EnsureRoles([]));
    }

    private static void AssertAllowed(string role, params string[] permissions) =>
        Assert.All(permissions, permission =>
            Assert.True(RolePermissions.HasPermission([role], permission),
                $"{role} should have {permission}."));

    private static void AssertDenied(string role, params string[] permissions) =>
        Assert.All(permissions, permission =>
            Assert.False(RolePermissions.HasPermission([role], permission),
                $"{role} should not have {permission}."));

    [Fact]
    public void Administrator_Cannot_Deactivate_Self()
    {
        Assert.Throws<ApplicationValidationException>(() => UserAccountRules.EnsureCanDeactivate("same", "same"));
    }

    [Fact]
    public void Last_Active_Admin_Cannot_Be_Removed()
    {
        Assert.Throws<ApplicationValidationException>(() => UserAccountRules.EnsureAdminRemains(true, 1, false));
    }

    [Fact]
    public async Task Email_Must_Be_Unique()
    {
        await using var environment = await IdentityUnitEnvironment.CreateAsync();
        await environment.CreateUserAsync("first", "unique@example.test");
        var service = environment.Services.GetRequiredService<IUserAccountService>();

        await Assert.ThrowsAsync<ApplicationValidationException>(() => service.CreateAsync(
            environment.NewUser("second", "UNIQUE@example.test")));
    }

    [Fact]
    public void Create_Request_Allows_Optional_Email_And_Rejects_Invalid_Email()
    {
        var withoutEmail = new CreateUserAccountRequest
        {
            UserName = "no-email-user",
            Email = "   ",
            DisplayName = "No Email User",
            TemporaryPassword = IdentityUnitEnvironment.InitialPassword,
            Roles = [RoleNames.Employee]
        };
        var invalidEmail = new CreateUserAccountRequest
        {
            UserName = "invalid-email-user",
            Email = "not-an-email",
            DisplayName = "Invalid Email User",
            TemporaryPassword = IdentityUnitEnvironment.InitialPassword,
            Roles = [RoleNames.Employee]
        };

        Assert.Empty(Validate(withoutEmail));
        Assert.Contains(
            Validate(invalidEmail),
            result => result.MemberNames.Contains(
                nameof(CreateUserAccountRequest.Email),
                StringComparer.Ordinal));
    }

    [Fact]
    public void Create_Request_Rejects_Blank_And_Validates_Trimmed_UserName()
    {
        var blank = new CreateUserAccountRequest
        {
            UserName = " ",
            DisplayName = "Blank Login",
            TemporaryPassword = IdentityUnitEnvironment.InitialPassword,
            Roles = [RoleNames.Employee]
        };
        var tooShortAfterTrimming = new CreateUserAccountRequest
        {
            UserName = "  a  ",
            DisplayName = "Short Login",
            TemporaryPassword = IdentityUnitEnvironment.InitialPassword,
            Roles = [RoleNames.Employee]
        };

        Assert.Contains(
            Validate(blank),
            result => result.MemberNames.Contains(
                nameof(CreateUserAccountRequest.UserName),
                StringComparer.Ordinal));
        Assert.Contains(
            Validate(tooShortAfterTrimming),
            result => result.MemberNames.Contains(
                nameof(CreateUserAccountRequest.UserName),
                StringComparer.Ordinal));
    }

    [Fact]
    public async Task Account_Can_Be_Created_Without_Email()
    {
        await using var environment = await IdentityUnitEnvironment.CreateAsync();

        var created = await environment.CreateUserAsync("no-email", "   ");

        var manager = environment.Services.GetRequiredService<UserManager<ApplicationUser>>();
        var stored = await manager.FindByIdAsync(created.Id);
        Assert.NotNull(stored);
        Assert.Equal("no-email", created.UserName);
        Assert.Equal(string.Empty, created.Email);
        Assert.Null(stored.Email);
        Assert.Null(stored.NormalizedEmail);
    }

    [Fact]
    public async Task Supplied_Email_Is_Trimmed_And_Normalized()
    {
        await using var environment = await IdentityUnitEnvironment.CreateAsync();

        var created = await environment.CreateUserAsync(
            "trimmed-email",
            "  Trimmed.Email@example.test  ");

        var manager = environment.Services.GetRequiredService<UserManager<ApplicationUser>>();
        var stored = await manager.FindByIdAsync(created.Id);
        Assert.NotNull(stored);
        Assert.Equal("Trimmed.Email@example.test", stored.Email);
        Assert.Equal(
            manager.NormalizeEmail("Trimmed.Email@example.test"),
            stored.NormalizedEmail);
    }

    [Fact]
    public async Task Duplicate_Normalized_UserName_Is_Rejected()
    {
        await using var environment = await IdentityUnitEnvironment.CreateAsync();
        await environment.CreateUserAsync("duplicate-name", null);
        var service = environment.Services.GetRequiredService<IUserAccountService>();

        await Assert.ThrowsAsync<ApplicationValidationException>(() =>
            service.CreateAsync(environment.NewUser("DUPLICATE-NAME", null)));
    }

    [Fact]
    public async Task Password_Reset_Produces_A_Valid_Identity_Hash()
    {
        await using var environment = await IdentityUnitEnvironment.CreateAsync();
        var created = await environment.CreateUserAsync("reset-user", "reset@example.test");
        var service = environment.Services.GetRequiredService<IUserAccountService>();
        const string replacement = "N3w!IdentityPassword";

        await service.ResetPasswordAsync(new ResetUserPasswordRequest
        {
            UserId = created.Id, NewPassword = replacement, ConfirmPassword = replacement
        });

        var manager = environment.Services.GetRequiredService<UserManager<ApplicationUser>>();
        var reloaded = await manager.FindByIdAsync(created.Id);
        Assert.NotNull(reloaded);
        Assert.True(await manager.CheckPasswordAsync(reloaded, replacement));
    }

    [Fact]
    public async Task Password_Reset_Audit_Does_Not_Contain_Password_Or_Token()
    {
        await using var environment = await IdentityUnitEnvironment.CreateAsync();
        var created = await environment.CreateUserAsync("audit-user", "audit@example.test");
        var service = environment.Services.GetRequiredService<IUserAccountService>();
        const string replacement = "S3cret!NeverAudit";

        await service.ResetPasswordAsync(new ResetUserPasswordRequest
        {
            UserId = created.Id, NewPassword = replacement, ConfirmPassword = replacement
        });

        var db = environment.Services.GetRequiredService<HRSystemDbContext>();
        var log = db.AuditLogs.OrderByDescending(x => x.Id).First(x => x.Action == "PasswordReset");
        var serialized = $"{log.OldValuesJson}{log.NewValuesJson}";
        Assert.DoesNotContain(replacement, serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("token", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("password", serialized, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Create_Request_Requires_Temporary_Password()
    {
        var request = new CreateUserAccountRequest
        {
            UserName = "create-user",
            Email = "create-user@example.test",
            DisplayName = "Create User",
            Roles = [RoleNames.Employee]
        };

        var results = Validate(request);

        Assert.Contains(results, result =>
            result.MemberNames.Contains(nameof(CreateUserAccountRequest.TemporaryPassword), StringComparer.Ordinal));
    }

    [Fact]
    public void Update_Request_Does_Not_Contain_Or_Require_Temporary_Password()
    {
        var request = new UpdateUserAccountRequest
        {
            Id = "update-user",
            Email = "update-user@example.test",
            DisplayName = "Update User",
            Roles = [RoleNames.Employee],
            RowVersion = Convert.ToBase64String([])
        };

        var results = Validate(request);

        Assert.Empty(results);
        Assert.Null(typeof(UpdateUserAccountRequest).GetProperty(
            nameof(CreateUserAccountRequest.TemporaryPassword)));
    }

    [Fact]
    public void Reset_Request_Requires_New_Temporary_Password()
    {
        var results = Validate(new ResetUserPasswordRequest
        {
            UserId = "reset-user"
        });

        Assert.Contains(results, result =>
            result.MemberNames.Contains(nameof(ResetUserPasswordRequest.NewPassword), StringComparer.Ordinal));
    }

    [Fact]
    public async Task Identity_Password_Options_Match_Internal_Minimum_Policy()
    {
        await using var environment = await IdentityUnitEnvironment.CreateAsync();

        var options = environment.Services
            .GetRequiredService<IOptions<IdentityOptions>>()
            .Value.Password;

        Assert.Equal(4, options.RequiredLength);
        Assert.False(options.RequireUppercase);
        Assert.False(options.RequireLowercase);
        Assert.False(options.RequireDigit);
        Assert.False(options.RequireNonAlphanumeric);
        Assert.Equal(1, options.RequiredUniqueChars);
    }

    [Theory]
    [InlineData("1234")]
    [InlineData("0000")]
    [InlineData("abcd")]
    public async Task Create_Accepts_Approved_Simple_Passwords(string password)
    {
        await using var environment = await IdentityUnitEnvironment.CreateAsync();
        var request = environment.NewUser(
            $"simple-{Guid.NewGuid():N}",
            null);
        request.TemporaryPassword = password;

        var created = await environment.Services
            .GetRequiredService<IUserAccountService>()
            .CreateAsync(request);
        var user = await environment.Services
            .GetRequiredService<UserManager<ApplicationUser>>()
            .FindByIdAsync(created.Id);

        Assert.NotNull(user);
        Assert.True(await environment.Services
            .GetRequiredService<UserManager<ApplicationUser>>()
            .CheckPasswordAsync(user, password));
    }

    [Theory]
    [InlineData("")]
    [InlineData("    ")]
    [InlineData("123")]
    public void Create_And_Reset_Requests_Reject_Invalid_Short_Passwords(
        string password)
    {
        var create = new CreateUserAccountRequest
        {
            UserName = "short-password",
            DisplayName = "Short Password",
            TemporaryPassword = password,
            Roles = [RoleNames.Employee]
        };
        var reset = new ResetUserPasswordRequest
        {
            UserId = "reset-user",
            NewPassword = password,
            ConfirmPassword = password
        };

        Assert.Contains(
            Validate(create),
            result => result.MemberNames.Contains(
                nameof(CreateUserAccountRequest.TemporaryPassword),
                StringComparer.Ordinal));
        Assert.Contains(
            Validate(reset),
            result => result.MemberNames.Contains(
                nameof(ResetUserPasswordRequest.NewPassword),
                StringComparer.Ordinal));
    }

    [Fact]
    public async Task Create_Rejects_Password_Shorter_Than_Four_Characters()
    {
        await using var environment = await IdentityUnitEnvironment.CreateAsync();
        var service = environment.Services.GetRequiredService<IUserAccountService>();
        var request = environment.NewUser("short-password", "short-password@example.test");
        request.TemporaryPassword = "123";
        var db = environment.Services.GetRequiredService<HRSystemDbContext>();
        var auditCount = db.AuditLogs.Count();

        await Assert.ThrowsAsync<ApplicationValidationException>(() => service.CreateAsync(request));

        var manager = environment.Services.GetRequiredService<UserManager<ApplicationUser>>();
        Assert.Null(await manager.FindByNameAsync(request.UserName));
        Assert.Equal(auditCount, db.AuditLogs.Count());
    }

    [Fact]
    public async Task Metadata_Edit_Updates_Approved_Fields_Without_Changing_Password_State()
    {
        await using var environment = await IdentityUnitEnvironment.CreateAsync();
        var employeeId = await environment.AddEmployeeAsync();
        var created = await environment.CreateUserAsync("metadata-user", "metadata-before@example.test");
        var manager = environment.Services.GetRequiredService<UserManager<ApplicationUser>>();
        var before = await manager.FindByIdAsync(created.Id);
        Assert.NotNull(before);
        var passwordHash = before.PasswordHash;
        var securityStamp = before.SecurityStamp;
        Assert.NotNull(passwordHash);
        Assert.NotNull(securityStamp);
        var db = environment.Services.GetRequiredService<HRSystemDbContext>();
        var auditCount = db.AuditLogs.Count(x => x.Action == "Updated" && x.EntityId == created.Id);

        var updated = await environment.Services.GetRequiredService<IUserAccountService>().UpdateAsync(
            new UpdateUserAccountRequest
            {
                Id = created.Id,
                Email = "metadata-after@example.test",
                DisplayName = "Updated Display Name",
                EmployeeId = employeeId,
                Roles = [RoleNames.Manager],
                RowVersion = created.RowVersion
            });

        var after = await manager.FindByIdAsync(created.Id);
        Assert.NotNull(after);
        Assert.Equal(created.UserName, after.UserName);
        Assert.Equal("metadata-after@example.test", updated.Email);
        Assert.Equal("Updated Display Name", updated.DisplayName);
        Assert.Equal(employeeId, updated.EmployeeId);
        Assert.Equal([RoleNames.Manager], updated.Roles);
        Assert.Equal(passwordHash, after.PasswordHash);
        Assert.Equal(securityStamp, after.SecurityStamp);
        Assert.True(await manager.CheckPasswordAsync(after, IdentityUnitEnvironment.InitialPassword));
        Assert.Equal(auditCount + 1, db.AuditLogs.Count(x => x.Action == "Updated" && x.EntityId == created.Id));

        var audit = db.AuditLogs.Single(x => x.Action == "Updated" && x.EntityId == created.Id);
        var serialized = $"{audit.OldValuesJson}{audit.NewValuesJson}";
        Assert.Contains(RoleNames.Employee, audit.OldValuesJson ?? string.Empty,
            StringComparison.Ordinal);
        Assert.Contains(RoleNames.Manager, audit.NewValuesJson ?? string.Empty,
            StringComparison.Ordinal);
        Assert.DoesNotContain("password", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(passwordHash, serialized, StringComparison.Ordinal);
        Assert.DoesNotContain(securityStamp, serialized, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Email_Can_Be_Removed_With_Safe_Audit_History()
    {
        await using var environment = await IdentityUnitEnvironment.CreateAsync();
        var created = await environment.CreateUserAsync(
            "remove-email",
            "remove-email@example.test");

        var updated = await environment.Services
            .GetRequiredService<IUserAccountService>()
            .UpdateAsync(new UpdateUserAccountRequest
            {
                Id = created.Id,
                Email = " ",
                DisplayName = created.DisplayName,
                Roles = created.Roles.ToList(),
                RowVersion = created.RowVersion
            });

        var manager = environment.Services.GetRequiredService<UserManager<ApplicationUser>>();
        var stored = await manager.FindByIdAsync(created.Id);
        var db = environment.Services.GetRequiredService<HRSystemDbContext>();
        var audit = db.AuditLogs.Single(
            item => item.Action == "Updated" && item.EntityId == created.Id);
        var serialized = $"{audit.OldValuesJson}{audit.NewValuesJson}";
        Assert.NotNull(stored);
        Assert.Equal(string.Empty, updated.Email);
        Assert.Null(stored.Email);
        Assert.Null(stored.NormalizedEmail);
        Assert.Contains("remove-email@example.test", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("password", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("securitystamp", serialized, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Invalid_Employee_Link_Does_Not_Partially_Update_Account_Roles_Or_Audit()
    {
        await using var environment = await IdentityUnitEnvironment.CreateAsync();
        var created = await environment.CreateUserAsync("invalid-link", "invalid-link@example.test");
        var db = environment.Services.GetRequiredService<HRSystemDbContext>();
        var manager = environment.Services.GetRequiredService<UserManager<ApplicationUser>>();
        var auditCount = db.AuditLogs.Count();

        await Assert.ThrowsAsync<ApplicationValidationException>(() =>
            environment.Services.GetRequiredService<IUserAccountService>().UpdateAsync(
                new UpdateUserAccountRequest
                {
                    Id = created.Id,
                    Email = "must-not-persist@example.test",
                    DisplayName = "Must Not Persist",
                    EmployeeId = Guid.NewGuid(),
                    Roles = [RoleNames.Manager],
                    RowVersion = created.RowVersion
                }));

        var reloaded = await manager.FindByIdAsync(created.Id);
        Assert.NotNull(reloaded);
        Assert.Equal(created.Email, reloaded.Email);
        Assert.Equal(created.DisplayName, reloaded.DisplayName);
        Assert.Null(reloaded.EmployeeId);
        Assert.Equal([RoleNames.Employee], await manager.GetRolesAsync(reloaded));
        Assert.Equal(auditCount, db.AuditLogs.Count());
    }

    [Fact]
    public async Task Concurrency_Conflict_Does_Not_Partially_Update_Account_Roles_Or_Audit()
    {
        await using var environment = await IdentityUnitEnvironment.CreateAsync();
        var created = await environment.CreateUserAsync("concurrency-user", "concurrency@example.test");
        var db = environment.Services.GetRequiredService<HRSystemDbContext>();
        var manager = environment.Services.GetRequiredService<UserManager<ApplicationUser>>();
        var auditCount = db.AuditLogs.Count();

        await Assert.ThrowsAsync<ConcurrencyConflictException>(() =>
            environment.Services.GetRequiredService<IUserAccountService>().UpdateAsync(
                new UpdateUserAccountRequest
                {
                    Id = created.Id,
                    Email = "must-not-persist@example.test",
                    DisplayName = "Must Not Persist",
                    Roles = [RoleNames.Manager],
                    RowVersion = Convert.ToBase64String([1])
                }));

        var reloaded = await manager.FindByIdAsync(created.Id);
        Assert.NotNull(reloaded);
        Assert.Equal(created.Email, reloaded.Email);
        Assert.Equal(created.DisplayName, reloaded.DisplayName);
        Assert.Equal([RoleNames.Employee], await manager.GetRolesAsync(reloaded));
        Assert.Equal(auditCount, db.AuditLogs.Count());
    }

    [Fact]
    public async Task Invalid_Reset_Password_Does_Not_Change_Password_State_Or_Audit()
    {
        await using var environment = await IdentityUnitEnvironment.CreateAsync();
        var created = await environment.CreateUserAsync("invalid-reset", "invalid-reset@example.test");
        var manager = environment.Services.GetRequiredService<UserManager<ApplicationUser>>();
        var before = await manager.FindByIdAsync(created.Id);
        Assert.NotNull(before);
        var passwordHash = before.PasswordHash;
        var securityStamp = before.SecurityStamp;
        Assert.NotNull(passwordHash);
        Assert.NotNull(securityStamp);
        var db = environment.Services.GetRequiredService<HRSystemDbContext>();
        var auditCount = db.AuditLogs.Count();

        await Assert.ThrowsAsync<ApplicationValidationException>(() =>
            environment.Services.GetRequiredService<IUserAccountService>().ResetPasswordAsync(
                new ResetUserPasswordRequest
                {
                    UserId = created.Id,
                    NewPassword = "123",
                    ConfirmPassword = "123"
                }));

        var after = await manager.FindByIdAsync(created.Id);
        Assert.NotNull(after);
        Assert.Equal(passwordHash, after.PasswordHash);
        Assert.Equal(securityStamp, after.SecurityStamp);
        Assert.True(await manager.CheckPasswordAsync(after, IdentityUnitEnvironment.InitialPassword));
        Assert.Equal(auditCount, db.AuditLogs.Count());
    }

    private static IReadOnlyList<ValidationResult> Validate(object request)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(request, new ValidationContext(request), results, true);
        return results;
    }

    private sealed class IdentityUnitEnvironment : IAsyncDisposable
    {
        public const string InitialPassword = "T3st!InitialPassword";
        private readonly ServiceProvider _provider;
        public IServiceProvider Services => _provider;

        private IdentityUnitEnvironment(ServiceProvider provider) => _provider = provider;

        public static async Task<IdentityUnitEnvironment> CreateAsync()
        {
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DatabaseProvider"] = "InMemory",
                ["DatabaseName"] = $"IdentityUnit-{Guid.NewGuid()}"
            }).Build();
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton(TimeProvider.System);
            services.AddSingleton<ICurrentUser>(new UnitCurrentUser());
            services.AddInfrastructure(configuration, allowInMemoryDatabase: true);
            var provider = services.BuildServiceProvider();
            var roles = provider.GetRequiredService<RoleManager<IdentityRole>>();
            foreach (var role in RoleNames.All)
            {
                Assert.True((await roles.CreateAsync(new IdentityRole(role))).Succeeded);
            }
            return new IdentityUnitEnvironment(provider);
        }

        public CreateUserAccountRequest NewUser(string userName, string? email, Guid? employeeId = null) => new()
        {
            UserName = userName,
            Email = email,
            DisplayName = userName,
            TemporaryPassword = InitialPassword,
            EmployeeId = employeeId,
            Roles = [RoleNames.Employee]
        };

        public Task<UserAccountDto> CreateUserAsync(string userName, string? email, Guid? employeeId = null) =>
            Services.GetRequiredService<IUserAccountService>().CreateAsync(NewUser(userName, email, employeeId));

        public async Task<Guid> AddEmployeeAsync()
        {
            var employee = new Employee(Guid.NewGuid(), $"E{Guid.NewGuid():N}"[..10], "測試員工", Guid.NewGuid(), new DateOnly(2026, 7, 18), DateTimeOffset.UtcNow);
            var db = Services.GetRequiredService<HRSystemDbContext>();
            db.Employees.Add(employee);
            await db.SaveChangesAsync();
            return employee.Id;
        }

        public ValueTask DisposeAsync() => _provider.DisposeAsync();
    }

    private sealed class UnitCurrentUser : ICurrentUser
    {
        public string? UserId => "unit-admin";
        public Guid? EmployeeId => null;
        public string? DisplayName => "Unit Admin";
        public string? IpAddress => "127.0.0.1";
        public bool IsAuthenticated => true;
        public bool IsInRole(string role) => role == RoleNames.Admin;
        public bool HasPermission(string policy) => RolePermissions.HasPermission([RoleNames.Admin], policy);
    }
}
