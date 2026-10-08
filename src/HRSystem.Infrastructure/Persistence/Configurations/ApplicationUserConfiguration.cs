using HRSystem.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRSystem.Infrastructure.Persistence.Configurations;

public sealed class ApplicationUserConfiguration : IEntityTypeConfiguration<ApplicationUser>
{
    public void Configure(EntityTypeBuilder<ApplicationUser> builder)
    {
        builder.Property(x => x.DisplayName).HasMaxLength(100).IsRequired();
        builder.Property(x => x.CreatedAtUtc).HasColumnType("datetimeoffset").IsRequired();
        builder.Property(x => x.LastLoginAtUtc).HasColumnType("datetimeoffset");
        builder.Property(x => x.RowVersion).IsRowVersion();
        builder.HasIndex(x => x.EmployeeId)
            .IsUnique()
            .HasFilter("[EmployeeId] IS NOT NULL")
            .HasDatabaseName("UX_AspNetUsers_EmployeeId");
        builder.HasIndex(x => x.NormalizedEmail)
            .IsUnique()
            .HasFilter("[NormalizedEmail] IS NOT NULL")
            .HasDatabaseName("EmailIndex");
        builder.HasOne(x => x.Employee)
            .WithOne()
            .HasForeignKey<ApplicationUser>(x => x.EmployeeId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
