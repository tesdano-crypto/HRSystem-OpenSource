using HRSystem.Domain.Attendance;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRSystem.Infrastructure.Persistence.Configurations;

public sealed class DailyAttendanceLeaveSegmentConfiguration :
    IEntityTypeConfiguration<DailyAttendanceLeaveSegment>
{
    public void Configure(
        EntityTypeBuilder<DailyAttendanceLeaveSegment> builder)
    {
        builder.ToTable("DailyAttendanceLeaveSegments", table =>
        {
            table.HasCheckConstraint(
                "CK_DailyAttendanceLeaveSegments_Period",
                "[EndAtUtc] > [StartAtUtc]");
            table.HasCheckConstraint(
                "CK_DailyAttendanceLeaveSegments_CoveredMinutes",
                "[CoveredMinutes] > 0");
        });
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Id).ValueGeneratedNever();
        builder.Property(item => item.LeaveTypeCodeSnapshot)
            .HasMaxLength(20)
            .IsRequired();
        builder.Property(item => item.LeaveTypeNameSnapshot)
            .HasMaxLength(100)
            .IsRequired();
        builder.Property(item => item.StartAtUtc)
            .HasColumnType("datetimeoffset")
            .IsRequired();
        builder.Property(item => item.EndAtUtc)
            .HasColumnType("datetimeoffset")
            .IsRequired();
        builder.Property(item => item.CreatedAtUtc)
            .HasColumnType("datetimeoffset")
            .IsRequired();
        builder.HasIndex(item => new
            {
                item.DailyAttendanceResultId,
                item.StartAtUtc,
                item.EndAtUtc
            })
            .IsUnique()
            .HasDatabaseName(
                "UX_DailyAttendanceLeaveSegments_Result_Period");
        builder.HasIndex(item => item.LeaveRequestId)
            .HasDatabaseName(
                "IX_DailyAttendanceLeaveSegments_LeaveRequestId");
        builder.HasIndex(item => item.LeaveTypeId)
            .HasDatabaseName(
                "IX_DailyAttendanceLeaveSegments_LeaveTypeId");
        builder.HasOne(item => item.DailyAttendanceResult)
            .WithMany(item => item.LeaveSegments)
            .HasForeignKey(item => item.DailyAttendanceResultId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(item => item.LeaveRequest)
            .WithMany()
            .HasForeignKey(item => item.LeaveRequestId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(item => item.LeaveType)
            .WithMany()
            .HasForeignKey(item => item.LeaveTypeId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
