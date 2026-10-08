using HRSystem.Domain.LeaveRequests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRSystem.Infrastructure.Persistence.Configurations;

public sealed class LeaveRequestConfiguration : IEntityTypeConfiguration<LeaveRequest>
{
    public void Configure(EntityTypeBuilder<LeaveRequest> builder)
    {
        builder.ToTable("LeaveRequests", table =>
        {
            table.HasCheckConstraint("CK_LeaveRequests_DurationHours_NonNegative", "[DurationHours] >= 0");
            table.HasCheckConstraint("CK_LeaveRequests_EndAfterStart", "[EndAt] > [StartAt]");
            table.HasCheckConstraint(
                "CK_LeaveRequests_PregnancyDurationCategory",
                "[PregnancyDurationCategory] IS NULL OR [PregnancyDurationCategory] IN (1, 2, 3)");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.RequestNumber).HasMaxLength(40).UseCollation("Latin1_General_100_CI_AS").IsRequired();
        builder.Property(x => x.StartAt).HasColumnType("datetimeoffset").IsRequired();
        builder.Property(x => x.EndAt).HasColumnType("datetimeoffset").IsRequired();
        builder.Property(x => x.DurationHours).HasPrecision(8, 2).IsRequired();
        builder.Property(x => x.Reason).HasMaxLength(500).IsRequired();
        builder.Property(x => x.PregnancyDurationCategory).HasConversion<byte?>();
        builder.Property(x => x.Status).HasConversion<byte>().IsRequired();
        builder.Property(x => x.SubmittedAtUtc).HasColumnType("datetimeoffset");
        builder.Property(x => x.ApprovedAtUtc).HasColumnType("datetimeoffset");
        builder.Property(x => x.RejectedAtUtc).HasColumnType("datetimeoffset");
        builder.Property(x => x.WithdrawnAtUtc).HasColumnType("datetimeoffset");
        builder.Property(x => x.CancellationRequestedAtUtc).HasColumnType("datetimeoffset");
        builder.Property(x => x.CancellationReason).HasMaxLength(1000);
        builder.Property(x => x.CancellationRequestedByUserId).HasMaxLength(450);
        builder.Property(x => x.CreatedAtUtc).HasColumnType("datetimeoffset").IsRequired();
        builder.Property(x => x.UpdatedAtUtc).HasColumnType("datetimeoffset");
        builder.Property(x => x.CreatedByUserId).HasMaxLength(450).IsRequired();
        builder.Property(x => x.UpdatedByUserId).HasMaxLength(450);
        builder.Property(x => x.RowVersion).IsRowVersion();
        builder.HasIndex(x => x.RequestNumber).IsUnique().HasDatabaseName("UX_LeaveRequests_RequestNumber");
        builder.HasIndex(x => new { x.EmployeeId, x.StartAt, x.EndAt }).HasDatabaseName("IX_LeaveRequests_EmployeeId_StartAt_EndAt");
        builder.HasIndex(x => new { x.Status, x.SubmittedAtUtc }).HasDatabaseName("IX_LeaveRequests_Status_SubmittedAtUtc");
        builder.HasIndex(x => x.LeaveTypeId).HasDatabaseName("IX_LeaveRequests_LeaveTypeId");
        builder.HasIndex(x => x.CreatedAtUtc).HasDatabaseName("IX_LeaveRequests_CreatedAtUtc");
        builder.HasOne(x => x.Employee).WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(x => x.LeaveType).WithMany().HasForeignKey(x => x.LeaveTypeId).OnDelete(DeleteBehavior.NoAction);
    }
}
