using HRSystem.Domain.MasterData;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRSystem.Infrastructure.Persistence.Configurations;

public sealed class EmployeeConfiguration : IEntityTypeConfiguration<Employee>
{
    public void Configure(EntityTypeBuilder<Employee> builder)
    {
        builder.ToTable("Employees");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.EmployeeNumber).HasMaxLength(20).UseCollation("Latin1_General_100_CI_AS").IsRequired();
        builder.Property(x => x.ChineseName).HasMaxLength(100).IsRequired();
        builder.Property(x => x.EnglishName).HasMaxLength(100);
        builder.Property(x => x.JobTitle).HasMaxLength(100);
        builder.Property(x => x.HireDate).HasColumnType("date").IsRequired();
        builder.Property(x => x.TerminationDate).HasColumnType("date");
        builder.Property(x => x.Email).HasMaxLength(254);
        builder.Property(x => x.MobilePhone).HasMaxLength(30);
        builder.Property(x => x.CreatedAtUtc).HasColumnType("datetimeoffset").IsRequired();
        builder.Property(x => x.UpdatedAtUtc).HasColumnType("datetimeoffset").IsRequired();
        builder.Property(x => x.RowVersion).IsRowVersion();
        builder.HasIndex(x => x.EmployeeNumber).IsUnique().HasDatabaseName("UX_Employees_EmployeeNumber");
        builder.HasIndex(x => new { x.DepartmentId, x.IsActive }).HasDatabaseName("IX_Employees_DepartmentId_IsActive");
        builder.HasIndex(x => x.ChineseName).HasDatabaseName("IX_Employees_ChineseName");
        builder.HasOne(x => x.Department)
            .WithMany(x => x.Employees)
            .HasForeignKey(x => x.DepartmentId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
