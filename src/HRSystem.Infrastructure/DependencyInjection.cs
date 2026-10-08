using HRSystem.Application.Abstractions.Persistence;
using HRSystem.Application.Attendance;
using HRSystem.Application.CompanyCalendars;
using HRSystem.Application.Employees;
using HRSystem.Application.UserAccounts;
using HRSystem.Infrastructure.Persistence;
using HRSystem.Infrastructure.Persistence.Seed;
using HRSystem.Infrastructure.Identity;
using HRSystem.Infrastructure.CompanyCalendars;
using HRSystem.Infrastructure.Attendance;
using HRSystem.Infrastructure.Approvals;
using HRSystem.Application.Approvals;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HRSystem.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        bool allowInMemoryDatabase)
    {
        services.AddSingleton(new HRSystem.Application.Payroll.OrganizationBranding(
            configuration["Organization:Name"] ?? "Example Company"));
        var provider = configuration["DatabaseProvider"];
        services.AddDbContext<HRSystemDbContext>(options =>
        {
            if (allowInMemoryDatabase && string.Equals(provider, "InMemory", StringComparison.OrdinalIgnoreCase))
            {
                var databaseName = configuration["DatabaseName"] ?? "HRSystemDevelopment";
                options.UseInMemoryDatabase(databaseName);
                return;
            }

            var connectionString = configuration.GetConnectionString("HRSystemDb");
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException(
                    "尚未設定 HRSystemDB。請以 User Secrets 或環境變數 ConnectionStrings__HRSystemDb 提供連線字串。");
            }

            options.UseSqlServer(connectionString, sql =>
                sql.MigrationsAssembly(typeof(HRSystemDbContext).Assembly.FullName));
        });

        services.AddScoped<IApplicationDbContext>(provider => provider.GetRequiredService<HRSystemDbContext>());
        services.AddScoped<IEmployeeNumberSequence, SqlServerEmployeeNumberSequence>();
        services.AddScoped<ICompanyCalendarManifestReader, JsonCompanyCalendarManifestReader>();
        var attendanceBatchSize = configuration.GetValue<int?>(
            "BioWebTA:BatchSize") ?? 500;
        services.AddSingleton(new AttendanceImportSettings(attendanceBatchSize));
        var scheduledImportOptions = new BioWebTaScheduledImportOptions();
        configuration.GetSection(BioWebTaScheduledImportOptions.SectionName)
            .Bind(scheduledImportOptions);
        scheduledImportOptions.Validate();
        services.AddSingleton(scheduledImportOptions);
        services.AddScoped<IBioWebTaAttendanceSource, BioWebTaAttendanceSource>();
        services.AddScoped<IBioWebTaImportExecutionLock,
            SqlBioWebTaImportExecutionLock>();
        services.AddSingleton<IAttendanceExcelWorkbookWriter,
            ClosedXmlAttendanceWorkbookWriter>();
        services.AddIdentity<ApplicationUser, IdentityRole>(options =>
            {
                // Email is optional for internal accounts. Supplied addresses remain
                // unique through the application check and filtered database index.
                options.User.RequireUniqueEmail = false;
                options.Password.RequiredLength = InternalPasswordPolicy.MinimumLength;
                options.Password.RequireDigit = false;
                options.Password.RequireLowercase = false;
                options.Password.RequireUppercase = false;
                options.Password.RequireNonAlphanumeric = false;
                options.Password.RequiredUniqueChars = 1;
                options.Lockout.AllowedForNewUsers = true;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            })
            .AddEntityFrameworkStores<HRSystemDbContext>()
            .AddPasswordValidator<NonWhitespacePasswordValidator>()
            .AddDefaultTokenProviders();
        services.Configure<SecurityStampValidatorOptions>(options =>
            options.ValidationInterval = TimeSpan.Zero);
        services.AddScoped<IUserClaimsPrincipalFactory<ApplicationUser>, ApplicationUserClaimsPrincipalFactory>();
        services.AddScoped<DevelopmentSeedService>();
        services.AddScoped<StandardLeaveTypeSeedService>();
        services.AddScoped<CalendarDayLeaveTypeActivationService>();
        services.AddScoped<IdentitySeedService>();
        services.AddScoped<IUserAccountService, UserAccountService>();
        services.AddScoped<IApprovalActorDirectory, ApprovalActorDirectory>();
        services.AddScoped<ILinePairingUserDirectory, LinePairingUserDirectory>();
        var approvalBridgeOptions = new LineApprovalBridgeOptions();
        configuration.GetSection(LineApprovalBridgeOptions.SectionName)
            .Bind(approvalBridgeOptions);
        services.AddSingleton(approvalBridgeOptions);
        services.AddScoped<IApprovalPrivateNotificationSender,
            LineApprovalPrivateNotificationSender>();
        services.AddScoped<ILinePrivateTestNotificationSender,
            LinePrivateTestNotificationSender>();
        services.AddSingleton<ILineApprovalBridgeRequestVerifier,
            LineApprovalBridgeRequestVerifier>();
        return services;
    }
}
