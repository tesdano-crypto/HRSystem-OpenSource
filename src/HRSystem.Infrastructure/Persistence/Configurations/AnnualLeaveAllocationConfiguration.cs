using HRSystem.Domain.AnnualLeave;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRSystem.Infrastructure.Persistence.Configurations;

public sealed class AnnualLeaveAllocationConfiguration : IEntityTypeConfiguration<AnnualLeaveAllocation>
{
    public void Configure(EntityTypeBuilder<AnnualLeaveAllocation> builder)
    {
        builder.ToTable("AnnualLeaveAllocations", table =>
            table.HasCheckConstraint("CK_AnnualLeaveAllocations_Minutes", "[AllocatedMinutes] > 0"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Status).HasConversion<byte>();
        builder.Property(x => x.CreatedAtUtc).HasColumnType("datetimeoffset");
        builder.Property(x => x.UpdatedAtUtc).HasColumnType("datetimeoffset");
        builder.Property(x => x.RowVersion).IsRowVersion();
        builder.HasIndex(x => new { x.LeaveRequestId, x.AnnualLeaveEntitlementId })
            .IsUnique().HasDatabaseName("UX_AnnualLeaveAllocations_Request_Entitlement");
        builder.HasIndex(x => new { x.AnnualLeaveEntitlementId, x.Status })
            .HasDatabaseName("IX_AnnualLeaveAllocations_Entitlement_Status");
        builder.HasOne(x => x.LeaveRequest).WithMany().HasForeignKey(x => x.LeaveRequestId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(x => x.Entitlement).WithMany(x => x.Allocations)
            .HasForeignKey(x => x.AnnualLeaveEntitlementId).OnDelete(DeleteBehavior.NoAction);
    }
}
