using HRSystem.Domain.Attendance;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRSystem.Infrastructure.Persistence.Configurations;

public sealed class AttendanceReviewResolutionConfiguration :
    IEntityTypeConfiguration<AttendanceReviewResolution>
{
    public void Configure(EntityTypeBuilder<AttendanceReviewResolution> builder)
    {
        builder.ToTable("AttendanceReviewResolutions", table =>
        {
            table.HasCheckConstraint(
                "CK_AttendanceReviewResolutions_AnomalyType",
                "[AnomalyType] IN (1, 2, 3, 4, 5, 6, 7, 8)");
            table.HasCheckConstraint(
                "CK_AttendanceReviewResolutions_ResultSource",
                "[DailyAttendanceResultId] IS NOT NULL OR [AnomalyType] = 8");
            table.HasCheckConstraint(
                "CK_AttendanceReviewResolutions_NonOvertimeReason",
                "[AnomalyType] <> 8 OR [Reason] IN (11, 12, 16, 99)");
            table.HasCheckConstraint(
                "CK_AttendanceReviewResolutions_Status",
                "[Status] IN (1, 2)");
            table.HasCheckConstraint(
                "CK_AttendanceReviewResolutions_Reason",
                "[Reason] IN (1, 2, 3, 4, 5, 10, 11, 12, 13, 14, 15, 16, 99)");
            table.HasCheckConstraint(
                "CK_AttendanceReviewResolutions_FingerprintLength",
                "DATALENGTH([SourceFingerprint]) = 32");
            table.HasCheckConstraint(
                "CK_AttendanceReviewResolutions_OtherNote",
                "[Reason] <> 99 OR NULLIF(LTRIM(RTRIM([Note])), '') IS NOT NULL");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.WorkDate).HasColumnType("date");
        builder.Property(x => x.AnomalyType).HasConversion<byte>();
        builder.Property(x => x.SourceFingerprint).HasColumnType("binary(32)").IsRequired();
        builder.Property(x => x.Status).HasConversion<byte>();
        builder.Property(x => x.Reason).HasConversion<byte>();
        builder.Property(x => x.Note).HasMaxLength(1000);
        builder.Property(x => x.LastActionByUserId).HasMaxLength(450).IsRequired();
        builder.Property(x => x.LastActionAtUtc).HasColumnType("datetimeoffset");
        builder.Property(x => x.CreatedAtUtc).HasColumnType("datetimeoffset");
        builder.Property(x => x.RowVersion).IsRowVersion();
        builder.HasIndex(x => new { x.EmployeeId, x.WorkDate, x.AnomalyType })
            .IsUnique()
            .HasDatabaseName("UX_AttendanceReviewResolutions_Employee_WorkDate_Anomaly");
        builder.HasIndex(x => new { x.WorkDate, x.Status })
            .HasDatabaseName("IX_AttendanceReviewResolutions_WorkDate_Status");
        builder.HasOne(x => x.Employee).WithMany().HasForeignKey(x => x.EmployeeId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(x => x.DailyAttendanceResult).WithMany()
            .HasForeignKey(x => x.DailyAttendanceResultId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
