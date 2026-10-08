using HRSystem.Application;
using HRSystem.Application.Abstractions.Security;
using HRSystem.Application.Security;
using HRSystem.Infrastructure;
using HRSystem.Infrastructure.Identity;
using HRSystem.Infrastructure.Persistence.Seed;
using HRSystem.Web.Authorization;
using HRSystem.Web.Reporting;
using HRSystem.Web.Components;
using HRSystem.Web.Approvals;
using Microsoft.FluentUI.AspNetCore.Components;
using System.Security.Claims;

var builder = WebApplication.CreateBuilder(args);

// Avoid the Windows Event Log provider so the intranet app can run under a
// least-privilege service account without requiring Event Log write access.
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();
builder.Services.AddApplication();
var allowInMemoryDatabase = builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Testing");
builder.Services.AddInfrastructure(builder.Configuration, allowInMemoryDatabase);
builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/login";
    options.AccessDeniedPath = "/access-denied";
    options.Cookie.Name = "HRSystem.Auth";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    options.SlidingExpiration = true;
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
});
builder.Services.AddAuthorization(options =>
{
    foreach (var permission in PolicyNames.All)
    {
        options.AddPolicy(permission, policy =>
        {
            policy.RequireAuthenticatedUser();
            policy.RequireAssertion(context => RolePermissions.HasPermission(
                context.User.FindAll(ClaimTypes.Role).Select(claim => claim.Value),
                permission));
        });
    }
});
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddHttpClient();
builder.Services.AddFluentUIComponents();
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

if (app.Environment.IsDevelopment() && app.Configuration.GetValue<bool>("SeedData:Enabled"))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<DevelopmentSeedService>().SeedAsync();
}

await using (var identityScope = app.Services.CreateAsyncScope())
{
    await identityScope.ServiceProvider.GetRequiredService<IdentitySeedService>()
        .SeedAsync(app.Environment.IsProduction());
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();
app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();
app.MapAccountEndpoints();
app.MapAttendanceExcelExportEndpoints();
app.MapLineApprovalPostbackEndpoints();
app.MapLinePairingEndpoints();

app.Run();

public partial class Program;
