using HRSystem.Application.Abstractions.Security;
using HRSystem.Application.Attendance;
using HRSystem.Application.Security;
using HRSystem.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace HRSystem.IntegrationTests;

public sealed class MasterDataWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _databaseName = $"HRSystemIntegration-{Guid.NewGuid()}";
    private readonly Action<IServiceCollection>? _serviceOverrides;
    internal TestEmployeeNumberSequence EmployeeNumbers { get; }
    internal TestBioWebTaAttendanceSource BioWebTaSource { get; } = new();

    public MasterDataWebApplicationFactory()
        : this(new TestEmployeeNumberSequence(), null)
    {
    }

    internal MasterDataWebApplicationFactory(TestEmployeeNumberSequence employeeNumbers)
        : this(employeeNumbers, null)
    {
    }

    internal MasterDataWebApplicationFactory(
        Action<IServiceCollection> serviceOverrides)
        : this(new TestEmployeeNumberSequence(), serviceOverrides)
    {
    }

    private MasterDataWebApplicationFactory(
        TestEmployeeNumberSequence employeeNumbers,
        Action<IServiceCollection>? serviceOverrides)
    {
        EmployeeNumbers = employeeNumbers;
        _serviceOverrides = serviceOverrides;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["DatabaseProvider"] = "InMemory",
                ["DatabaseName"] = _databaseName,
                ["SeedData:Enabled"] = "false"
            }));
        builder.ConfigureLogging(logging => logging.ClearProviders());
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<HRSystemDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<HRSystemDbContext>>();
            services.AddDbContext<HRSystemDbContext>(options => options.UseInMemoryDatabase(_databaseName));
            services.ReplaceEmployeeNumberSequence(EmployeeNumbers);
            services.RemoveAll<IBioWebTaAttendanceSource>();
            services.AddSingleton<IBioWebTaAttendanceSource>(BioWebTaSource);
            services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = TestAuthenticationHandler.SchemeName;
                    options.DefaultChallengeScheme = TestAuthenticationHandler.SchemeName;
                    options.DefaultForbidScheme = TestAuthenticationHandler.SchemeName;
                })
                .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(TestAuthenticationHandler.SchemeName, _ => { });
            services.RemoveAll<ICurrentUser>();
            services.AddSingleton<ICurrentUser, IntegrationCurrentUser>();
            _serviceOverrides?.Invoke(services);
        });
    }

    private sealed class IntegrationCurrentUser : ICurrentUser
    {
        public string? UserId => "integration-admin";
        public Guid? EmployeeId => null;
        public string? DisplayName => "Integration Admin";
        public string? IpAddress => "127.0.0.1";
        public bool IsAuthenticated => true;
        public bool IsInRole(string role) => role == RoleNames.Admin;
        public bool HasPermission(string policy) => RolePermissions.HasPermission([RoleNames.Admin], policy);
    }
}
