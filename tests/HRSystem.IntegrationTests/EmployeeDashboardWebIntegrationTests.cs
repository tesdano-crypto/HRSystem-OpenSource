using System.Net;
using System.Text.RegularExpressions;

namespace HRSystem.IntegrationTests;

public sealed class EmployeeDashboardWebIntegrationTests :
    IClassFixture<IdentityWebApplicationFactory>
{
    private readonly IdentityWebApplicationFactory _factory;

    public EmployeeDashboardWebIntegrationTests(IdentityWebApplicationFactory factory) =>
        _factory = factory;

    [Fact]
    public async Task Anonymous_User_Is_Redirected_From_My_Dashboard()
    {
        using var client = _factory.CreateCookieClient();
        var response = await client.GetAsync("/my");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/login", response.Headers.Location?.AbsolutePath);
    }

    [Fact]
    public async Task Unbound_Admin_Loads_Safe_My_Dashboard_Without_Global_Data()
    {
        using var client = _factory.CreateCookieClient();
        await LoginAsync(client, IdentityWebApplicationFactory.AdminUserName,
            _factory.AdminPassword);

        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/my"));

        Assert.Contains("目前帳號尚未綁定員工資料", html, StringComparison.Ordinal);
        Assert.DoesNotContain("data-employee-dashboard", html, StringComparison.Ordinal);
    }

    private static async Task LoginAsync(
        HttpClient client,
        string account,
        string password)
    {
        var page = await client.GetStringAsync("/login");
        var response = await client.PostAsync("/account/login",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = AntiforgeryToken(page),
                ["account"] = account,
                ["password"] = password,
                ["rememberMe"] = "false",
                ["returnUrl"] = "/my"
            }));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    private static string AntiforgeryToken(string html)
    {
        var match = Regex.Match(html,
            "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"",
            RegexOptions.CultureInvariant);
        Assert.True(match.Success, "Antiforgery token was not rendered.");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }
}
