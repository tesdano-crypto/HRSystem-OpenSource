using HRSystem.Domain.AnnualLeave;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRSystem.Infrastructure.Persistence.Configurations;

public sealed class AnnualLeaveEntitlementConfiguration : IEntityTypeConfiguration<AnnualLeaveEntitlement>
{
    public void Configure(EntityTypeBuilder<AnnualLeaveEntitlement> builder)
    {
        builder.ToTable("AnnualLeaveEntitlements", table =>
        {
            table.HasCheckConstraint("CK_AnnualLeaveEntitlements_Period", "[PeriodEnd] >= [PeriodStart]");
            table.HasCheckConstraint("CK_AnnualLeaveEntitlements_Minutes", "[GrantedMinutes] > 0 AND [CarriedInMinutes] >= 0 AND [ReservedMinutes] >= 0 AND [ConsumedMinutes] >= 0 AND [SettledMinutes] >= 0");
            table.HasCheckConstraint("CK_AnnualLeaveEntitlements_Balance", "[ReservedMinutes] + [ConsumedMinutes] + [SettledMinutes] <= [GrantedMinutes] + [CarriedInMinutes]");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Milestone).HasConversion<byte>();
        builder.Property(x => x.GrantedDate).HasColumnType("date");
        builder.Property(x => x.PeriodStart).HasColumnType("date");
        builder.Property(x => x.PeriodEnd).HasColumnType("date");
        builder.Property(x => x.GrantedDays).HasPrecision(6, 2);
        builder.Property(x => x.Status).HasConversion<byte>();
        builder.Property(x => x.CreatedAtUtc).HasColumnType("datetimeoffset");
        builder.Property(x => x.UpdatedAtUtc).HasColumnType("datetimeoffset");
        builder.Property(x => x.RowVersion).IsRowVersion();
        builder.Ignore(x => x.AvailableMinutes);
        builder.HasIndex(x => new { x.EmployeeId, x.Milestone, x.CompletedServiceYears })
            .IsUnique().HasDatabaseName("UX_AnnualLeaveEntitlements_Employee_Milestone");
        builder.HasIndex(x => new { x.EmployeeId, x.PeriodStart, x.PeriodEnd })
            .HasDatabaseName("IX_AnnualLeaveEntitlements_Employee_Period");
        builder.HasOne(x => x.Employee).WithMany().HasForeignKey(x => x.EmployeeId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
