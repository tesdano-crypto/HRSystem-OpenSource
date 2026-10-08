using HRSystem.Domain.Overtime;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRSystem.Infrastructure.Persistence.Configurations;

public sealed class OvertimeRequestConfiguration : IEntityTypeConfiguration<OvertimeRequest>
{
    public void Configure(EntityTypeBuilder<OvertimeRequest> builder)
    {
        builder.ToTable("OvertimeRequests", table =>
        {
            table.HasCheckConstraint(
                "CK_OvertimeRequests_Status",
                "[Status] IN (1, 2, 3, 4, 5)");
            table.HasCheckConstraint(
                "CK_OvertimeRequests_EndAfterStart",
                "[PlannedEndAt] > [PlannedStartAt]");
            table.HasCheckConstraint(
                "CK_OvertimeRequests_MaximumDuration",
                "DATEDIFF(MINUTE, [PlannedStartAt], [PlannedEndAt]) BETWEEN 1 AND 720");
            table.HasCheckConstraint(
                "CK_OvertimeRequests_RequestedMinutes",
                "[RequestedMinutes] = DATEDIFF(MINUTE, [PlannedStartAt], [PlannedEndAt])");
            table.HasCheckConstraint(
                "CK_OvertimeRequests_ThirtyMinuteUnit",
                "[RequestedMinutes] % 30 = 0");
        });
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Id).ValueGeneratedNever();
        builder.Property(item => item.OvertimeDate).HasColumnType("date").IsRequired();
        builder.Property(item => item.PlannedStartAt).HasColumnType("datetime2(0)").IsRequired();
        builder.Property(item => item.PlannedEndAt).HasColumnType("datetime2(0)").IsRequired();
        builder.Property(item => item.RequestedMinutes).IsRequired();
        builder.Property(item => item.Reason).HasMaxLength(500).IsRequired();
        builder.Property(item => item.Status).HasConversion<byte>().IsRequired();
        builder.Property(item => item.ReviewReason).HasConversion<byte?>();
        builder.Property(item => item.ReviewNote).HasMaxLength(1000);
        builder.Property(item => item.ReviewedByUserId).HasMaxLength(450);
        builder.Property(item => item.SubmittedAtUtc).HasColumnType("datetimeoffset");
        builder.Property(item => item.ApprovedAtUtc).HasColumnType("datetimeoffset");
        builder.Property(item => item.RejectedAtUtc).HasColumnType("datetimeoffset");
        builder.Property(item => item.WithdrawnAtUtc).HasColumnType("datetimeoffset");
        builder.Property(item => item.CreatedAtUtc).HasColumnType("datetimeoffset").IsRequired();
        builder.Property(item => item.UpdatedAtUtc).HasColumnType("datetimeoffset");
        builder.Property(item => item.CreatedByUserId).HasMaxLength(450).IsRequired();
        builder.Property(item => item.UpdatedByUserId).HasMaxLength(450);
        builder.Property(item => item.RowVersion).IsRowVersion();
        builder.HasIndex(item => new { item.EmployeeId, item.OvertimeDate })
            .HasDatabaseName("IX_OvertimeRequests_EmployeeId_OvertimeDate");
        builder.HasIndex(item => new { item.Status, item.OvertimeDate })
            .HasDatabaseName("IX_OvertimeRequests_Status_OvertimeDate");
        builder.HasIndex(item => item.SubmittedAtUtc)
            .HasDatabaseName("IX_OvertimeRequests_SubmittedAtUtc");
        builder.HasOne(item => item.Employee).WithMany()
            .HasForeignKey(item => item.EmployeeId).OnDelete(DeleteBehavior.NoAction);
    }
}
