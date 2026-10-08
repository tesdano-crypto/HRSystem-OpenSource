using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Logging;

namespace HRSystem.IntegrationTests;

public class ApplicationSmokeTests : IClassFixture<MasterDataWebApplicationFactory>
{
    private readonly MasterDataWebApplicationFactory _factory;

    public ApplicationSmokeTests(MasterDataWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task RootPage_ReturnsFluentUiShell()
    {
        using var factory = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureLogging(logging => logging.ClearProviders()));
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        var response = await client.GetAsync("/");
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains("Example Company 人資管理系統", html, StringComparison.Ordinal);
        Assert.Contains("Microsoft.FluentUI.AspNetCore.Components", html, StringComparison.Ordinal);
    }
}
