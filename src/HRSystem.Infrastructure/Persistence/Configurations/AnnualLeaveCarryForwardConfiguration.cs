using HRSystem.Domain.AnnualLeave;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRSystem.Infrastructure.Persistence.Configurations;

public sealed class AnnualLeaveCarryForwardConfiguration : IEntityTypeConfiguration<AnnualLeaveCarryForward>
{
    public void Configure(EntityTypeBuilder<AnnualLeaveCarryForward> builder)
    {
        builder.ToTable("AnnualLeaveCarryForwards", table =>
            table.HasCheckConstraint("CK_AnnualLeaveCarryForwards_Minutes", "[Minutes] > 0"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.ExpiresOn).HasColumnType("date");
        builder.Property(x => x.Status).HasConversion<byte>();
        builder.Property(x => x.AuthorizedByUserId).HasMaxLength(450);
        builder.Property(x => x.CreatedAtUtc).HasColumnType("datetimeoffset");
        builder.Property(x => x.RowVersion).IsRowVersion();
        builder.HasIndex(x => new { x.SourceEntitlementId, x.TargetEntitlementId })
            .IsUnique().HasDatabaseName("UX_AnnualLeaveCarryForwards_Source_Target");
        builder.HasOne(x => x.SourceEntitlement).WithMany()
            .HasForeignKey(x => x.SourceEntitlementId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(x => x.TargetEntitlement).WithMany()
            .HasForeignKey(x => x.TargetEntitlementId).OnDelete(DeleteBehavior.NoAction);
    }
}
