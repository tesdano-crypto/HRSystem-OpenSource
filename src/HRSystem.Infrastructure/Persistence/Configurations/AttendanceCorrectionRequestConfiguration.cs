using HRSystem.Domain.Attendance;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRSystem.Infrastructure.Persistence.Configurations;

public sealed class AttendanceCorrectionRequestConfiguration :
    IEntityTypeConfiguration<AttendanceCorrectionRequest>
{
    public void Configure(EntityTypeBuilder<AttendanceCorrectionRequest> builder)
    {
        builder.ToTable("AttendanceCorrectionRequests", table =>
        {
            table.HasCheckConstraint(
                "CK_AttendanceCorrectionRequests_RequestType",
                "[RequestType] IN (1, 2, 3, 4, 5, 6, 7)");
            table.HasCheckConstraint(
                "CK_AttendanceCorrectionRequests_Status",
                "[Status] IN (1, 2, 3, 4, 5)");
            table.HasCheckConstraint(
                "CK_AttendanceCorrectionRequests_Reason",
                "[Reason] IN (1, 2, 3, 4, 5, 6, 7, 99)");
            table.HasCheckConstraint(
                "CK_AttendanceCorrectionRequests_FingerprintLength",
                "DATALENGTH([SourceFingerprint]) = 32");
            table.HasCheckConstraint(
                "CK_AttendanceCorrectionRequests_ProposedTimes",
                "([RequestType] IN (1, 4) AND [ProposedClockInAt] IS NOT NULL AND [ProposedClockOutAt] IS NULL) OR " +
                "([RequestType] IN (2, 5) AND [ProposedClockInAt] IS NULL AND [ProposedClockOutAt] IS NOT NULL) OR " +
                "([RequestType] = 3 AND [ProposedClockInAt] IS NOT NULL AND [ProposedClockOutAt] IS NOT NULL) OR " +
                "([RequestType] IN (6, 7) AND [ProposedClockInAt] IS NULL AND [ProposedClockOutAt] IS NULL)");
            table.HasCheckConstraint(
                "CK_AttendanceCorrectionRequests_AppliedAdjustment",
                "([Status] = 3 AND [RequestType] IN (1, 2, 3, 4, 5) AND [AppliedAttendanceAdjustmentId] IS NOT NULL) OR " +
                "([Status] = 3 AND [RequestType] IN (6, 7) AND [AppliedAttendanceAdjustmentId] IS NULL) OR [Status] <> 3");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.WorkDate).HasColumnType("date").IsRequired();
        builder.Property(x => x.RequestType).HasConversion<byte>().IsRequired();
        builder.Property(x => x.Status).HasConversion<byte>().IsRequired();
        builder.Property(x => x.OriginalClockInAt).HasColumnType("datetime2(7)");
        builder.Property(x => x.OriginalClockOutAt).HasColumnType("datetime2(7)");
        builder.Property(x => x.ProposedClockInAt).HasColumnType("datetime2(7)");
        builder.Property(x => x.ProposedClockOutAt).HasColumnType("datetime2(7)");
        builder.Property(x => x.Reason).HasConversion<byte>().IsRequired();
        builder.Property(x => x.EmployeeReason).HasMaxLength(1000).IsRequired();
        builder.Property(x => x.SourceFingerprint).HasColumnType("binary(32)").IsRequired();
        builder.Property(x => x.ReviewerNote).HasMaxLength(1000);
        builder.Property(x => x.SubmittedAtUtc).HasColumnType("datetimeoffset");
        builder.Property(x => x.ApprovedAtUtc).HasColumnType("datetimeoffset");
        builder.Property(x => x.RejectedAtUtc).HasColumnType("datetimeoffset");
        builder.Property(x => x.WithdrawnAtUtc).HasColumnType("datetimeoffset");
        builder.Property(x => x.CreatedAtUtc).HasColumnType("datetimeoffset").IsRequired();
        builder.Property(x => x.UpdatedAtUtc).HasColumnType("datetimeoffset").IsRequired();
        builder.Property(x => x.RowVersion).IsRowVersion();
        builder.HasIndex(x => new { x.EmployeeId, x.WorkDate })
            .HasDatabaseName("IX_AttendanceCorrectionRequests_EmployeeId_WorkDate");
        builder.HasIndex(x => new { x.Status, x.SubmittedAtUtc })
            .HasDatabaseName("IX_AttendanceCorrectionRequests_Status_SubmittedAtUtc");
        builder.HasIndex(x => new { x.EmployeeId, x.WorkDate, x.RequestType })
            .IsUnique()
            .HasFilter("[Status] IN (2, 3)")
            .HasDatabaseName("UX_AttendanceCorrectionRequests_ActiveType");
        builder.HasIndex(x => x.AppliedAttendanceAdjustmentId)
            .IsUnique()
            .HasFilter("[AppliedAttendanceAdjustmentId] IS NOT NULL")
            .HasDatabaseName("UX_AttendanceCorrectionRequests_AppliedAdjustmentId");
        builder.HasOne(x => x.Employee).WithMany()
            .HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(x => x.AttendanceResult).WithMany()
            .HasForeignKey(x => x.AttendanceResultId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(x => x.AppliedAttendanceAdjustment).WithMany()
            .HasForeignKey(x => x.AppliedAttendanceAdjustmentId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
