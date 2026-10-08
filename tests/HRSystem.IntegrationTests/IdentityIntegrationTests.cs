using System.Net;
using System.Text.RegularExpressions;
using HRSystem.Application.Common.Exceptions;
using HRSystem.Application.Departments;
using HRSystem.Application.Employees;
using HRSystem.Application.Security;
using HRSystem.Application.UserAccounts;
using HRSystem.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace HRSystem.IntegrationTests;

public sealed class IdentityIntegrationTests : IClassFixture<IdentityWebApplicationFactory>
{
    private const string InitialPassword = "T3st!InitialPassword";
    private readonly IdentityWebApplicationFactory _factory;

    public IdentityIntegrationTests(IdentityWebApplicationFactory factory) => _factory = factory;

    [Theory]
    [InlineData(RoleNames.HR, "/employees")]
    [InlineData(RoleNames.HR, "/")]
    [InlineData(RoleNames.HR, "/attendance-review")]
    [InlineData(RoleNames.HR, "/admin/attendance-requests")]
    [InlineData(RoleNames.HR, "/admin/leave-requests")]
    [InlineData(RoleNames.HR, "/leave-requests/00000000-0000-0000-0000-000000000001")]
    [InlineData(RoleNames.HR, "/parental-leave/00000000-0000-0000-0000-000000000001")]
    [InlineData(RoleNames.HR, "/attendance-exceptions/00000000-0000-0000-0000-000000000001")]
    [InlineData(RoleNames.HR, "/admin/overtime")]
    [InlineData(RoleNames.HR, "/admin/payroll")]
    [InlineData(RoleNames.HR, "/admin/payroll/employees")]
    [InlineData(RoleNames.Accounting, "/employees")]
    [InlineData(RoleNames.Accounting, "/")]
    [InlineData(RoleNames.Accounting, "/attendance-review")]
    [InlineData(RoleNames.Accounting, "/admin/attendance-requests")]
    [InlineData(RoleNames.Accounting, "/admin/leave-requests")]
    [InlineData(RoleNames.Accounting, "/leave-requests/00000000-0000-0000-0000-000000000001")]
    [InlineData(RoleNames.Accounting, "/parental-leave/00000000-0000-0000-0000-000000000001")]
    [InlineData(RoleNames.Accounting, "/attendance-exceptions/00000000-0000-0000-0000-000000000001")]
    [InlineData(RoleNames.Accounting, "/admin/overtime")]
    [InlineData(RoleNames.Accounting, "/admin/payroll")]
    [InlineData(RoleNames.Accounting, "/admin/payroll/employees")]
    [InlineData(RoleNames.Accounting, "/admin/payroll/settings")]
    [InlineData(RoleNames.Owner, "/")]
    [InlineData(RoleNames.Owner, "/admin/payroll")]
    [InlineData(RoleNames.Owner, "/admin/payroll/employees")]
    public async Task Permission_Roles_Can_Open_Authorized_Routes(string role, string route)
    {
        await using var factory = new MasterDataWebApplicationFactory();
        using var client = factory.CreateClient(new()
        {
            AllowAutoRedirect = false
        });
        client.DefaultRequestHeaders.Add(TestAuthenticationHandler.RoleHeader, role);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(route)).StatusCode);
    }

    [Theory]
    [InlineData(RoleNames.HR)]
    [InlineData(RoleNames.Accounting)]
    [InlineData(RoleNames.Owner)]
    public async Task Payroll_View_Roles_Can_Open_Existing_Employee_Detail(string role)
    {
        var employee = await _factory.RunAsAdminAsync(async services =>
        {
            var suffix = Guid.NewGuid().ToString("N");
            var department = await services.GetRequiredService<IDepartmentService>()
                .CreateAsync(new CreateDepartmentRequest
                {
                    Code = $"S{suffix[..9]}",
                    Name = "Security 路由測試部門"
                });
            return await services.GetRequiredService<IEmployeeService>().CreateAsync(
                new CreateEmployeeRequest
                {
                    ChineseName = "Security 路由測試員工",
                    DepartmentId = department.Id,
                    HireDate = new DateOnly(2026, 8, 1)
                });
        });
        var user = await CreateUserAsync(role);
        using var client = _factory.CreateCookieClient();
        await LoginAsync(client, user.UserName, InitialPassword);

        var response = await client.GetAsync($"/admin/payroll/employees/{employee.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData(RoleNames.HR, "/admin/users")]
    [InlineData(RoleNames.Accounting, "/admin/users")]
    [InlineData(RoleNames.Owner, "/employees")]
    [InlineData(RoleNames.Owner, "/attendance-review")]
    [InlineData(RoleNames.Owner, "/admin/overtime")]
    [InlineData(RoleNames.Owner, "/admin/users")]
    [InlineData(RoleNames.Manager, "/attendance-review")]
    [InlineData(RoleNames.Manager, "/admin/payroll")]
    [InlineData(RoleNames.Manager, "/admin/attendance-requests")]
    [InlineData(RoleNames.Employee, "/employees")]
    [InlineData(RoleNames.Employee, "/admin/attendance-requests")]
    [InlineData(RoleNames.Employee, "/admin/leave-requests")]
    [InlineData(RoleNames.Employee, "/admin/overtime")]
    [InlineData(RoleNames.Employee, "/admin/payroll")]
    public async Task Permission_Roles_Cannot_Open_Forbidden_Routes(
        string role, string route)
    {
        await using var factory = new MasterDataWebApplicationFactory();
        using var client = factory.CreateClient(new()
        {
            AllowAutoRedirect = false
        });
        client.DefaultRequestHeaders.Add(TestAuthenticationHandler.RoleHeader, role);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(route)).StatusCode);
    }

    [Fact]
    public async Task User_Admin_Page_Route_Is_Authorized_And_Roles_Have_Chinese_Display_Names()
    {
        await using var factory = new MasterDataWebApplicationFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/admin/users");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("系統管理員", SecurityDisplayCatalog.RoleName(RoleNames.Admin));
        Assert.Equal("人資", SecurityDisplayCatalog.RoleName(RoleNames.HR));
        Assert.Equal("會計", SecurityDisplayCatalog.RoleName(RoleNames.Accounting));
        Assert.Equal("老闆", SecurityDisplayCatalog.RoleName(RoleNames.Owner));
    }

    [Fact]
    public async Task Anonymous_Department_Request_Redirects_To_Login()
    {
        using var client = _factory.CreateCookieClient();
        var response = await client.GetAsync("/departments");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/login", response.Headers.Location?.PathAndQuery, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Admin_Can_Login()
    {
        using var client = _factory.CreateCookieClient();
        var response = await LoginAsync(client, IdentityWebApplicationFactory.AdminUserName, _factory.AdminPassword);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/", response.Headers.Location?.OriginalString);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/account/profile")).StatusCode);
    }

    [Fact]
    public async Task Employee_Cannot_Enter_Department_Page()
    {
        var user = await CreateUserAsync(RoleNames.Employee);
        using var client = _factory.CreateCookieClient();
        await LoginAsync(client, user.UserName, InitialPassword);
        var response = await client.GetAsync("/departments");
        Assert.True(response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Redirect);
        if (response.StatusCode == HttpStatusCode.Redirect)
        {
            Assert.StartsWith("/access-denied", response.Headers.Location?.PathAndQuery, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Admin_Can_Create_User()
    {
        var created = await CreateUserAsync(RoleNames.Employee);
        Assert.True(created.IsActive);
        Assert.Contains(RoleNames.Employee, created.Roles);
    }

    [Theory]
    [InlineData("1234")]
    [InlineData("0000")]
    [InlineData("abcd")]
    public async Task Admin_Can_Create_And_Reset_Account_With_Simple_Password(
        string simplePassword)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var created = await _factory.RunAsAdminAsync(async services =>
            await services.GetRequiredService<IUserAccountService>().CreateAsync(
                new CreateUserAccountRequest
                {
                    UserName = $"simple-{suffix}",
                    DisplayName = "Simple Password User",
                    TemporaryPassword = simplePassword,
                    Roles = [RoleNames.Employee]
                }));
        var replacement = simplePassword == "0000" ? "1234" : "0000";

        await _factory.RunAsAdminAsync(async services =>
        {
            await services.GetRequiredService<IUserAccountService>()
                .ResetPasswordAsync(new ResetUserPasswordRequest
                {
                    UserId = created.Id,
                    NewPassword = replacement,
                    ConfirmPassword = replacement
                });
            var user = await services
                .GetRequiredService<UserManager<ApplicationUser>>()
                .FindByIdAsync(created.Id);
            Assert.NotNull(user);
            Assert.True(await services
                .GetRequiredService<UserManager<ApplicationUser>>()
                .CheckPasswordAsync(user, replacement));
            return true;
        });
    }

    [Fact]
    public async Task Admin_Can_Assign_Manager_Role()
    {
        var created = await CreateUserAsync(RoleNames.Employee);
        var updated = await _factory.RunAsAdminAsync(async services =>
            await services.GetRequiredService<IUserAccountService>().UpdateAsync(new UpdateUserAccountRequest
            {
                Id = created.Id,
                Email = created.Email,
                DisplayName = created.DisplayName,
                EmployeeId = created.EmployeeId,
                Roles = [RoleNames.Manager],
                RowVersion = created.RowVersion
            }));
        Assert.Equal([RoleNames.Manager], updated.Roles);
    }

    [Fact]
    public async Task Disabled_User_Cannot_Login()
    {
        var created = await CreateUserAsync(RoleNames.Employee);
        await _factory.RunAsAdminAsync(async services =>
        {
            await services.GetRequiredService<IUserAccountService>().SetActiveAsync(created.Id, false, created.RowVersion);
            return true;
        });
        using var client = _factory.CreateCookieClient();
        var response = await LoginAsync(client, created.UserName, InitialPassword);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/login?error=", response.Headers.Location?.OriginalString, StringComparison.Ordinal);
    }

    [Fact]
    public async Task User_Can_Change_Own_Password()
    {
        var created = await CreateUserAsync(RoleNames.Employee);
        using var client = _factory.CreateCookieClient();
        await LoginAsync(client, created.UserName, InitialPassword);
        var page = await client.GetStringAsync("/account/change-password");
        var token = AntiforgeryToken(page);
        const string replacement = "1234";
        var response = await client.PostAsync("/account/change-password/submit", Form(
            ("__RequestVerificationToken", token),
            ("currentPassword", InitialPassword),
            ("newPassword", replacement),
            ("confirmPassword", replacement)));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/account/change-password?success=true", response.Headers.Location?.OriginalString, StringComparison.Ordinal);

        using var verificationClient = _factory.CreateCookieClient();
        Assert.Equal(HttpStatusCode.Redirect, (await LoginAsync(verificationClient, created.UserName, replacement)).StatusCode);
    }

    [Fact]
    public async Task User_Cannot_Change_Own_Password_To_Whitespace()
    {
        var created = await CreateUserAsync(RoleNames.Employee);
        using var client = _factory.CreateCookieClient();
        await LoginAsync(client, created.UserName, InitialPassword);
        var page = await client.GetStringAsync("/account/change-password");
        var token = AntiforgeryToken(page);

        var response = await client.PostAsync(
            "/account/change-password/submit",
            Form(
                ("__RequestVerificationToken", token),
                ("currentPassword", InitialPassword),
                ("newPassword", "    "),
                ("confirmPassword", "    ")));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains(
            Uri.EscapeDataString("密碼不可只包含空白字元。"),
            response.Headers.Location?.OriginalString,
            StringComparison.OrdinalIgnoreCase);
        using var verificationClient = _factory.CreateCookieClient();
        Assert.Equal(
            HttpStatusCode.Redirect,
            (await LoginAsync(
                verificationClient,
                created.UserName,
                InitialPassword)).StatusCode);
    }

    [Theory]
    [InlineData(RoleNames.Manager)]
    [InlineData(RoleNames.Employee)]
    public async Task Non_Admin_Cannot_Enter_User_Management(string role)
    {
        var user = await CreateUserAsync(role);
        using var client = _factory.CreateCookieClient();
        await LoginAsync(client, user.UserName, InitialPassword);
        var response = await client.GetAsync("/admin/users");
        Assert.True(response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Redirect);
    }

    [Fact]
    public async Task Linked_Employee_Is_Returned_In_Profile()
    {
        var employee = await _factory.RunAsAdminAsync(async services =>
        {
            var departments = services.GetRequiredService<IDepartmentService>();
            var department = await departments.CreateAsync(new CreateDepartmentRequest { Code = $"D{Guid.NewGuid():N}"[..10], Name = "Identity 測試部門" });
            return await services.GetRequiredService<IEmployeeService>().CreateAsync(new CreateEmployeeRequest
            {
                ChineseName = "Identity 測試員工", DepartmentId = department.Id, HireDate = new DateOnly(2026, 7, 18)
            });
        });
        var user = await CreateUserAsync(RoleNames.Employee, employee.Id);
        var profile = await _factory.RunAsAsync(user.Id, async services =>
            await services.GetRequiredService<IUserAccountService>().GetCurrentProfileAsync());
        Assert.Equal(employee.Id, profile.EmployeeId);
        Assert.Equal(employee.EmployeeNumber, profile.EmployeeNumber);
    }

    [Fact]
    public async Task Account_Without_Email_Can_Login_By_UserName()
    {
        var userName = $"no-email-{Guid.NewGuid():N}";
        var created = await CreateUserWithIdentifiersAsync(
            RoleNames.Employee,
            userName,
            null);
        var identityState = await _factory.RunAsAdminAsync(async services =>
        {
            var user = await services.GetRequiredService<UserManager<ApplicationUser>>()
                .FindByIdAsync(created.Id);
            Assert.NotNull(user);
            return (user.Email, user.NormalizedEmail, user.NormalizedUserName);
        });
        using var client = _factory.CreateCookieClient();

        var response = await LoginAsync(client, userName, InitialPassword);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Null(identityState.Email);
        Assert.Null(identityState.NormalizedEmail);
        Assert.Equal(userName.ToUpperInvariant(), identityState.NormalizedUserName);
        Assert.Equal(
            HttpStatusCode.OK,
            (await client.GetAsync("/account/profile")).StatusCode);
    }

    [Fact]
    public async Task Email_Shaped_UserName_Remains_A_Valid_Login()
    {
        var userName = $"legacy-{Guid.NewGuid():N}@example.test";
        await CreateUserWithIdentifiersAsync(RoleNames.Employee, userName, null);
        using var client = _factory.CreateCookieClient();

        var response = await LoginAsync(client, userName, InitialPassword);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(
            HttpStatusCode.OK,
            (await client.GetAsync("/account/profile")).StatusCode);
    }

    [Fact]
    public async Task Supplied_Email_Remains_A_Valid_Login_Identifier()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var email = $"email-login-{suffix}@example.test";
        await CreateUserWithIdentifiersAsync(
            RoleNames.Employee,
            $"email-login-{suffix}",
            email);
        using var client = _factory.CreateCookieClient();

        var response = await LoginAsync(client, email, InitialPassword);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(
            HttpStatusCode.OK,
            (await client.GetAsync("/account/profile")).StatusCode);
    }

    [Fact]
    public async Task Cross_Field_Login_Ambiguity_Is_Rejected_Generically()
    {
        var ambiguousIdentifier = $"ambiguous-{Guid.NewGuid():N}@example.test";
        await CreateUserWithIdentifiersAsync(
            RoleNames.Employee,
            $"email-owner-{Guid.NewGuid():N}",
            ambiguousIdentifier);
        await CreateUserWithIdentifiersAsync(
            RoleNames.Employee,
            ambiguousIdentifier,
            null);
        using var client = _factory.CreateCookieClient();

        var response = await LoginAsync(
            client,
            ambiguousIdentifier,
            InitialPassword);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = Uri.UnescapeDataString(
            response.Headers.Location?.OriginalString ?? string.Empty);
        Assert.Contains("帳號或密碼不正確。", location, StringComparison.Ordinal);
        Assert.Equal(
            HttpStatusCode.Redirect,
            (await client.GetAsync("/account/profile")).StatusCode);
    }

    [Theory]
    [InlineData(RoleNames.Manager)]
    [InlineData(RoleNames.Employee)]
    public async Task Non_Admin_Cannot_Create_Account_Through_Service(string role)
    {
        var actor = await CreateUserAsync(role);
        var suffix = Guid.NewGuid().ToString("N");

        await Assert.ThrowsAsync<ForbiddenAccessException>(() =>
            _factory.RunAsAsync(actor.Id, async services =>
                await services.GetRequiredService<IUserAccountService>()
                    .CreateAsync(new CreateUserAccountRequest
                    {
                        UserName = $"forbidden-{suffix}",
                        DisplayName = "Forbidden Account",
                        TemporaryPassword = InitialPassword,
                        Roles = [RoleNames.Employee]
                    })));
    }

    [Fact]
    public async Task Login_Page_Does_Not_Offer_Email_Self_Service_Reset()
    {
        using var client = _factory.CreateCookieClient();

        var html = await client.GetStringAsync("/login");

        Assert.DoesNotContain("忘記密碼", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Email 重設", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Change_Password_Page_Shows_Approved_Password_Guidance()
    {
        using var client = _factory.CreateCookieClient();
        await LoginAsync(
            client,
            IdentityWebApplicationFactory.AdminUserName,
            _factory.AdminPassword);

        var html = await client.GetStringAsync("/account/change-password");

        Assert.Contains("至少 4 個字元", html, StringComparison.Ordinal);
        Assert.Contains("不限制大小寫、數字或特殊字元", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Login_And_Logout_Update_Cookie_Access()
    {
        using var client = _factory.CreateCookieClient();
        await LoginAsync(client, IdentityWebApplicationFactory.AdminUserName, _factory.AdminPassword);
        var profileHtml = await client.GetStringAsync("/account/profile");
        var response = await client.PostAsync("/logout", Form(("__RequestVerificationToken", AntiforgeryToken(profileHtml))));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/login", response.Headers.Location?.OriginalString);
        var protectedResponse = await client.GetAsync("/account/profile");
        Assert.Equal(HttpStatusCode.Redirect, protectedResponse.StatusCode);
    }

    private async Task<UserAccountDto> CreateUserAsync(string role, Guid? employeeId = null)
    {
        var suffix = Guid.NewGuid().ToString("N");
        return await _factory.RunAsAdminAsync(async services =>
            await services.GetRequiredService<IUserAccountService>().CreateAsync(new CreateUserAccountRequest
            {
                UserName = $"user-{suffix}",
                Email = $"user-{suffix}@example.test",
                DisplayName = "Integration User",
                TemporaryPassword = InitialPassword,
                EmployeeId = employeeId,
                Roles = [role]
            }));
    }

    private Task<UserAccountDto> CreateUserWithIdentifiersAsync(
        string role,
        string userName,
        string? email) =>
        _factory.RunAsAdminAsync(async services =>
            await services.GetRequiredService<IUserAccountService>()
                .CreateAsync(new CreateUserAccountRequest
                {
                    UserName = userName,
                    Email = email,
                    DisplayName = "Integration User",
                    TemporaryPassword = InitialPassword,
                    Roles = [role]
                }));

    private static async Task<HttpResponseMessage> LoginAsync(HttpClient client, string account, string password)
    {
        var loginPage = await client.GetStringAsync("/login");
        return await client.PostAsync("/account/login", Form(
            ("__RequestVerificationToken", AntiforgeryToken(loginPage)),
            ("account", account),
            ("password", password),
            ("rememberMe", "false"),
            ("returnUrl", "/")));
    }

    private static FormUrlEncodedContent Form(params (string Key, string Value)[] values) =>
        new(values.Select(x => new KeyValuePair<string, string>(x.Key, x.Value)));

    private static string AntiforgeryToken(string html)
    {
        var match = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"", RegexOptions.CultureInvariant);
        Assert.True(match.Success, "Antiforgery token was not rendered.");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }
}
