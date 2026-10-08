using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace HRSystem.Infrastructure.Persistence;

public sealed class HRSystemDbContextFactory : IDesignTimeDbContextFactory<HRSystemDbContext>
{
    public HRSystemDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .AddUserSecrets<HRSystemDbContextFactory>(optional: true)
            .AddEnvironmentVariables()
            .Build();
        var connectionString = configuration.GetConnectionString("HRSystemDb");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "尚未設定 HRSystemDB。請以 User Secrets 的 ConnectionStrings:HRSystemDb 或環境變數 ConnectionStrings__HRSystemDb 提供連線字串。");
        }

        var connection = new SqlConnectionStringBuilder(connectionString);
        if (string.Equals(connection.UserID, "sa", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Migration 不得使用 sa 帳號。");
        }

        var options = new DbContextOptionsBuilder<HRSystemDbContext>()
            .UseSqlServer(
                connectionString,
                sql => sql.MigrationsAssembly(typeof(HRSystemDbContext).Assembly.FullName))
            .Options;

        return new HRSystemDbContext(options);
    }
}
