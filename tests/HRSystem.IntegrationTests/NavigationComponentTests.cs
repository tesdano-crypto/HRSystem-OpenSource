using System.Security.Claims;
using Bunit;
using HRSystem.Application.Security;
using HRSystem.Infrastructure.Identity;
using HRSystem.Web.Components.Layout;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FluentUI.AspNetCore.Components;

namespace HRSystem.IntegrationTests;

public sealed class NavigationComponentTests : BunitContext
{
    public NavigationComponentTests()
    {
        Services.AddFluentUIComponents();
        Services.AddLogging();
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void Catalog_Preserves_All_Existing_Navigation_Routes()
    {
        var expected = new HashSet<string>(StringComparer.Ordinal)
        {
            "/",
            "/departments",
            "/employees",
            "/company-calendar",
            "/admin/company-calendar",
            "/admin/biowebta-mappings",
            "/admin/attendance-import",
            "/attendance/punch-records",
            "/attendance/daily",
            "/attendance-review",
            "/admin/attendance-requests",
            "/admin/attendance/shifts",
            "/admin/attendance/shift-assignments",
            "/leave-types",
            "/admin/users",
            "/audit-logs",
            "/my",
            "/leave-requests",
            "/annual-leave",
            "/my-comp-time",
            "/parental-leave",
            "/attendance-exceptions",
            "/my-attendance",
            "/my-attendance-requests",
            "/my-overtime",
            "/my-payslips",
            "/account/profile",
            "/account/change-password",
            "/admin/overtime",
            "/approvals/leave",
            "/parental-leave/approvals",
            "/attendance-exceptions/approvals",
            "/admin/leave-requests",
            "/admin/annual-leave-entitlements",
            "/admin/comp-time",
            "/admin/payroll",
            "/admin/payroll/history",
            "/admin/payroll/employees",
            "/admin/insurance",
            "/admin/payroll/settings",
            "/approvals",
            "/admin/line-bindings"
        };

        var actual = AppNavigationCatalog.Sections
            .SelectMany(section => section.Items)
            .Select(item => item.Route)
            .ToHashSet(StringComparer.Ordinal);

        Assert.True(expected.SetEquals(actual));
        Assert.Equal(expected.Count, actual.Count);
    }

    [Fact]
    public void Employee_Sees_One_Expanded_Self_Service_Section_Without_Admin_Items()
    {
        var cut = RenderNavigation([RoleNames.Employee], hasEmployeeBinding: true, "/my");

        Assert.Equal(["workspace"], SectionIds(cut));
        Assert.Equal("true", Toggle(cut, "workspace").GetAttribute("aria-expanded"));
        Assert.Contains("/my", Routes(cut));
        Assert.Contains("/my-attendance", Routes(cut));
        Assert.Contains("/my-overtime", Routes(cut));
        Assert.DoesNotContain("/attendance-review", Routes(cut));
        Assert.DoesNotContain("/admin/overtime", Routes(cut));
        Assert.DoesNotContain("/admin/users", Routes(cut));
        Assert.Equal("/my", ActiveRoute(cut));
    }

    [Fact]
    public void Employee_Item_Preserves_Existing_Route_And_Closes_Mobile_Menu()
    {
        var selectedCount = 0;
        Navigation.NavigateTo("/my");
        var user = CreateUser([RoleNames.Employee], hasEmployeeBinding: true);
        var cut = Render<AppNavigation>(parameters => parameters
            .Add(component => component.User, user)
            .Add(component => component.ItemSelected, () => selectedCount++));
        var item = cut.Find("[data-navigation-route='/my-attendance']");

        Assert.Equal("/my-attendance", item.GetAttribute("href"));

        item.Click();

        Assert.Equal(1, selectedCount);
    }

    [Fact]
    public void Detail_Route_Uses_Longest_Parent_Match_And_Highlights_My_Attendance()
    {
        var detailRoute = $"/my-attendance/{Guid.NewGuid()}";
        var cut = RenderNavigation([RoleNames.Employee], hasEmployeeBinding: true, detailRoute);

        Assert.Equal("true", Toggle(cut, "workspace").GetAttribute("aria-expanded"));
        Assert.Equal("/my-attendance", ActiveRoute(cut));

        Navigation.NavigateTo("/my-attendance-requests?requestType=LateExplanation");
        cut.WaitForAssertion(() =>
            Assert.Equal("/my-attendance-requests", ActiveRoute(cut)));
    }

    [Fact]
    public void Admin_Defaults_All_Allowed_Sections_To_Collapsed_Without_An_Active_Route()
    {
        var cut = RenderNavigation([RoleNames.Admin], hasEmployeeBinding: false, "/not-found");

        Assert.Equal(
            ["workspace", "attendance", "overtime", "leave", "master-data", "payroll", "system"],
            SectionIds(cut));
        Assert.All(
            cut.FindAll(".navigation-section-toggle"),
            toggle => Assert.Equal("false", toggle.GetAttribute("aria-expanded")));
    }

    [Fact]
    public void Payroll_Section_Uses_Payroll_View_Permission()
    {
        var admin = RenderNavigation([RoleNames.Admin], false, "/admin/payroll");
        Assert.Contains("/admin/payroll", Routes(admin));
        Assert.Equal("true", Toggle(admin, "payroll").GetAttribute("aria-expanded"));
        admin.Dispose();
        var manager = RenderNavigation([RoleNames.Manager], true, "/not-found");
        Assert.DoesNotContain("/admin/payroll", Routes(manager));
        manager.Dispose();
        var employee = RenderNavigation([RoleNames.Employee], true, "/not-found");
        Assert.DoesNotContain("/admin/payroll", Routes(employee));
    }

    [Fact]
    public void Accounting_Sees_Work_Areas_But_Not_System_Administration()
    {
        var cut = RenderNavigation([RoleNames.Accounting], false, "/admin/payroll");
        var routes = Routes(cut);

        Assert.Contains("/attendance-review", routes);
        Assert.Contains("/admin/leave-requests", routes);
        Assert.Contains("/admin/overtime", routes);
        Assert.Contains("/admin/payroll", routes);
        Assert.Contains("/admin/payroll/employees", routes);
        Assert.Contains("/admin/insurance", routes);
        Assert.DoesNotContain("/admin/users", routes);
        Assert.DoesNotContain("/audit-logs", routes);
    }

    [Fact]
    public void Owner_Sees_Payroll_But_No_Admin_Or_Operations_Menus()
    {
        var cut = RenderNavigation([RoleNames.Owner], false, "/admin/payroll");
        var routes = Routes(cut);

        Assert.Contains("/admin/payroll", routes);
        Assert.Contains("/approvals", routes);
        Assert.Contains("待簽核事項", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("/attendance-review", routes);
        Assert.DoesNotContain("/admin/overtime", routes);
        Assert.DoesNotContain("/admin/insurance", routes);
        Assert.DoesNotContain("/admin/users", routes);
        Assert.DoesNotContain("/audit-logs", routes);
    }

    [Fact]
    public void Accounting_And_Employee_See_Correct_Approval_Navigation()
    {
        var accounting = RenderNavigation([RoleNames.Accounting], false, "/approvals");
        Assert.Contains("/approvals", Routes(accounting));
        Assert.Contains("送簽紀錄", accounting.Markup, StringComparison.Ordinal);
        accounting.Dispose();

        var employee = RenderNavigation([RoleNames.Employee], true, "/not-found");
        Assert.DoesNotContain("/approvals", Routes(employee));
    }

    [Fact]
    public void Admin_Attendance_Route_Auto_Expands_And_Accordion_Closes_It()
    {
        var cut = RenderNavigation([RoleNames.Admin], hasEmployeeBinding: false, "/attendance-review");

        Assert.Equal("true", Toggle(cut, "attendance").GetAttribute("aria-expanded"));
        Assert.Equal("false", Toggle(cut, "overtime").GetAttribute("aria-expanded"));
        Assert.Equal("true", Toggle(cut, "attendance").GetAttribute("data-active"));
        Assert.Equal("/attendance-review", ActiveRoute(cut));

        Toggle(cut, "overtime").Click();

        Assert.Equal("false", Toggle(cut, "attendance").GetAttribute("aria-expanded"));
        Assert.Equal("true", Toggle(cut, "overtime").GetAttribute("aria-expanded"));
    }

    [Fact]
    public void Admin_Overtime_Route_And_Back_Forward_Style_Changes_Update_Active_Section()
    {
        var cut = RenderNavigation([RoleNames.Admin], hasEmployeeBinding: false, "/admin/overtime");

        Assert.Equal("true", Toggle(cut, "overtime").GetAttribute("aria-expanded"));
        Assert.Equal("/admin/overtime", ActiveRoute(cut));

        Navigation.NavigateTo("/attendance-review");
        cut.WaitForAssertion(() =>
        {
            Assert.Equal("true", Toggle(cut, "attendance").GetAttribute("aria-expanded"));
            Assert.Equal("false", Toggle(cut, "overtime").GetAttribute("aria-expanded"));
            Assert.Equal("/attendance-review", ActiveRoute(cut));
        });
    }

    [Fact]
    public void Admin_Employee_Binding_Controls_Only_Employee_Self_Service_Items()
    {
        var unbound = RenderNavigation([RoleNames.Admin], hasEmployeeBinding: false, "/not-found");
        Assert.DoesNotContain("/my", Routes(unbound));
        Assert.DoesNotContain("/my-attendance", Routes(unbound));
        Assert.Contains("/account/profile", Routes(unbound));
        Assert.Contains("/attendance-review", Routes(unbound));

        unbound.Dispose();
        var bound = RenderNavigation([RoleNames.Admin], hasEmployeeBinding: true, "/my");
        Assert.Contains("/my", Routes(bound));
        Assert.Contains("/my-attendance", Routes(bound));
        Assert.Contains("/attendance-review", Routes(bound));
    }

    [Fact]
    public void Manager_Uses_Existing_Permissions_Without_Admin_Privilege_Escalation()
    {
        var cut = RenderNavigation([RoleNames.Manager], hasEmployeeBinding: true, "/my");

        Assert.Contains("/my", Routes(cut));
        Assert.Contains("/approvals/leave", Routes(cut));
        Assert.Contains("/departments", Routes(cut));
        Assert.DoesNotContain("/attendance-review", Routes(cut));
        Assert.DoesNotContain("/admin/overtime", Routes(cut));
        Assert.DoesNotContain("/admin/users", Routes(cut));
        Assert.DoesNotContain("system", SectionIds(cut));
        Assert.DoesNotContain("overtime", SectionIds(cut));
    }

    [Fact]
    public void Empty_Sections_Are_Not_Rendered_After_Permission_Filtering()
    {
        var cut = RenderNavigation([RoleNames.Employee], hasEmployeeBinding: true, "/my");

        Assert.DoesNotContain("overtime", SectionIds(cut));
        Assert.DoesNotContain("leave", SectionIds(cut));
        Assert.DoesNotContain("system", SectionIds(cut));
    }

    [Fact]
    public void Section_Headers_Are_Keyboard_Accessible_Buttons_With_Aria_State()
    {
        var cut = RenderNavigation([RoleNames.Admin], hasEmployeeBinding: false, "/not-found");
        var toggle = Toggle(cut, "attendance");

        Assert.Equal("BUTTON", toggle.TagName);
        Assert.Equal("false", toggle.GetAttribute("aria-expanded"));
        Assert.Equal("navigation-section-attendance", toggle.GetAttribute("aria-controls"));

        toggle.Click();

        Assert.Equal("true", Toggle(cut, "attendance").GetAttribute("aria-expanded"));
        Assert.False(SectionItems(cut, "attendance").HasAttribute("hidden"));
    }

    private NavigationManager Navigation => Services.GetRequiredService<NavigationManager>();

    private IRenderedComponent<AppNavigation> RenderNavigation(
        IReadOnlyCollection<string> roles,
        bool hasEmployeeBinding,
        string route)
    {
        Navigation.NavigateTo(route);
        var user = CreateUser(roles, hasEmployeeBinding);
        return Render<AppNavigation>(parameters => parameters.Add(component => component.User, user));
    }

    private static ClaimsPrincipal CreateUser(
        IEnumerable<string> roles,
        bool hasEmployeeBinding)
    {
        var claims = roles.Select(role => new Claim(ClaimTypes.Role, role)).ToList();
        claims.Add(new Claim(ClaimTypes.Name, "navigation-test"));
        if (hasEmployeeBinding)
        {
            claims.Add(new Claim(
                ApplicationUserClaimsPrincipalFactory.EmployeeIdClaimType,
                Guid.NewGuid().ToString()));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
    }

    private static string[] Routes(IRenderedComponent<AppNavigation> cut) =>
        cut.FindAll("[data-navigation-route]")
            .Select(element => element.GetAttribute("data-navigation-route")!)
            .ToArray();

    private static string[] SectionIds(IRenderedComponent<AppNavigation> cut) =>
        cut.FindAll("[data-section-id]")
            .Select(element => element.GetAttribute("data-section-id")!)
            .ToArray();

    private static AngleSharp.Dom.IElement Section(
        IRenderedComponent<AppNavigation> cut,
        string sectionId) => cut.Find($"[data-section-id='{sectionId}']");

    private static AngleSharp.Dom.IElement Toggle(
        IRenderedComponent<AppNavigation> cut,
        string sectionId) => cut.Find(
            $"[data-section-id='{sectionId}'] .navigation-section-toggle");

    private static AngleSharp.Dom.IElement SectionItems(
        IRenderedComponent<AppNavigation> cut,
        string sectionId) => cut.Find(
            $"[data-section-id='{sectionId}'] .navigation-section-items");

    private static string? ActiveRoute(IRenderedComponent<AppNavigation> cut) =>
        cut.FindAll("[data-navigation-route]")
            .SingleOrDefault(element => element.GetAttribute("aria-current") == "page")
            ?.GetAttribute("data-navigation-route");
}
