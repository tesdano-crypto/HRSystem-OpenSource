using HRSystem.Domain.MasterData;
using Microsoft.EntityFrameworkCore;

namespace HRSystem.Infrastructure.Persistence.Seed;

public sealed class DevelopmentSeedService(
    HRSystemDbContext dbContext,
    TimeProvider timeProvider,
    StandardLeaveTypeSeedService standardLeaveTypeSeedService)
{
    private static readonly Guid AdministrationDepartmentId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid OperationsDepartmentId = Guid.Parse("10000000-0000-0000-0000-000000000002");

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        if (!await dbContext.Departments.AnyAsync(cancellationToken))
        {
            var now = timeProvider.GetUtcNow();
            var administration = new Department(AdministrationDepartmentId, "ADM", "管理部", now);
            var operations = new Department(OperationsDepartmentId, "OPS", "業務部", now);
            dbContext.Departments.AddRange(administration, operations);

            dbContext.Employees.AddRange(
                new Employee(Guid.Parse("20000000-0000-0000-0000-000000000001"), "TEST001", "測試管理員",
                    administration.Id, new DateOnly(2026, 1, 1), now, jobTitle: "行政專員", email: "test001@example.local"),
                new Employee(Guid.Parse("20000000-0000-0000-0000-000000000002"), "TEST002", "測試業務員",
                    operations.Id, new DateOnly(2026, 2, 1), now, jobTitle: "業務專員", email: "test002@example.local"));

            await dbContext.SaveChangesAsync(cancellationToken);
        }

        await standardLeaveTypeSeedService.SeedAsync(cancellationToken);
    }
}
