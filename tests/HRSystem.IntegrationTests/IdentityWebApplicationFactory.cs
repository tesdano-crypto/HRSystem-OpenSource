using System.Security.Claims;
using HRSystem.Infrastructure.Identity;
using HRSystem.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace HRSystem.IntegrationTests;

public sealed class IdentityWebApplicationFactory : WebApplicationFactory<Program>
{
    public const string AdminUserName = "integration-admin";
    public const string AdminEmail = "integration-admin@example.test";
    public string AdminPassword { get; } = $"Adm!n-{Guid.NewGuid():N}Aa1";
    private readonly string _databaseName = $"HRSystemIdentityIntegration-{Guid.NewGuid()}";
    internal TestEmployeeNumberSequence EmployeeNumbers { get; } = new(1000);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["DatabaseProvider"] = "InMemory",
                ["DatabaseName"] = _databaseName,
                ["SeedData:Enabled"] = "false",
                ["HR_ADMIN_USERNAME"] = AdminUserName,
                ["HR_ADMIN_EMAIL"] = AdminEmail,
                ["HR_ADMIN_PASSWORD"] = AdminPassword,
                ["HR_ADMIN_DISPLAY_NAME"] = "Integration Admin"
            }));
        builder.ConfigureLogging(logging => logging.AddConsole());
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<HRSystemDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<HRSystemDbContext>>();
            services.AddDbContext<HRSystemDbContext>(options => options.UseInMemoryDatabase(_databaseName));
            services.ReplaceEmployeeNumberSequence(EmployeeNumbers);
        });
    }

    public HttpClient CreateCookieClient() => CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false,
        HandleCookies = true
    });

    public async Task<T> RunAsAsync<T>(string userId, Func<IServiceProvider, Task<T>> action)
    {
        await using var scope = Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByIdAsync(userId) ?? throw new InvalidOperationException("Test user was not found.");
        var principalFactory = scope.ServiceProvider.GetRequiredService<IUserClaimsPrincipalFactory<ApplicationUser>>();
        var context = new DefaultHttpContext
        {
            RequestServices = scope.ServiceProvider,
            User = await principalFactory.CreateAsync(user)
        };
        context.Connection.RemoteIpAddress = System.Net.IPAddress.Loopback;
        scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext = context;
        return await action(scope.ServiceProvider);
    }

    public async Task<T> RunAsAdminAsync<T>(Func<IServiceProvider, Task<T>> action)
    {
        await using var scope = Services.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var admin = await manager.FindByNameAsync(AdminUserName) ?? throw new InvalidOperationException("Seeded admin was not found.");
        return await RunAsAsync(admin.Id, action);
    }
}
