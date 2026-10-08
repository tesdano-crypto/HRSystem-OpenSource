using HRSystem.Domain.MasterData;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRSystem.Infrastructure.Persistence.Configurations;

public sealed class DepartmentConfiguration : IEntityTypeConfiguration<Department>
{
    public void Configure(EntityTypeBuilder<Department> builder)
    {
        builder.ToTable("Departments");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Code).HasMaxLength(20).UseCollation("Latin1_General_100_CI_AS").IsRequired();
        builder.Property(x => x.Name).HasMaxLength(100).IsRequired();
        builder.Property(x => x.CreatedAtUtc).HasColumnType("datetimeoffset").IsRequired();
        builder.Property(x => x.UpdatedAtUtc).HasColumnType("datetimeoffset").IsRequired();
        builder.Property(x => x.RowVersion).IsRowVersion();
        builder.HasIndex(x => x.Code).IsUnique().HasDatabaseName("UX_Departments_Code");
        builder.HasIndex(x => x.ManagerEmployeeId).HasDatabaseName("IX_Departments_ManagerEmployeeId");
        builder.HasOne(x => x.ManagerEmployee)
            .WithMany(x => x.ManagedDepartments)
            .HasForeignKey(x => x.ManagerEmployeeId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
