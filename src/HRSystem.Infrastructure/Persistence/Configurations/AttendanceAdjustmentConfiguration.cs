using HRSystem.Domain.Attendance;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRSystem.Infrastructure.Persistence.Configurations;

public sealed class AttendanceAdjustmentConfiguration :
    IEntityTypeConfiguration<AttendanceAdjustment>
{
    public void Configure(EntityTypeBuilder<AttendanceAdjustment> builder)
    {
        builder.ToTable("AttendanceAdjustments", table =>
        {
            table.HasCheckConstraint(
                "CK_AttendanceAdjustments_RevisionNumber",
                "[RevisionNumber] >= 1");
        });
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Id).ValueGeneratedNever();
        builder.Property(item => item.WorkDate).HasColumnType("date");
        builder.Property(item => item.Action).HasConversion<byte>();
        builder.Property(item => item.Reason).HasConversion<byte>();
        builder.Property(item => item.PreviousClockInLocalTime).HasColumnType("datetime2(0)");
        builder.Property(item => item.PreviousClockOutLocalTime).HasColumnType("datetime2(0)");
        builder.Property(item => item.NewClockInLocalTime).HasColumnType("datetime2(0)");
        builder.Property(item => item.NewClockOutLocalTime).HasColumnType("datetime2(0)");
        builder.Property(item => item.Note).HasMaxLength(500).IsRequired();
        builder.Property(item => item.AdjustedByUserId).HasMaxLength(450).IsRequired();
        builder.Property(item => item.AdjustedAtUtc).HasColumnType("datetimeoffset");
        builder.HasIndex(item => new
            {
                item.DailyAttendanceResultId,
                item.RevisionNumber
            })
            .IsUnique()
            .HasDatabaseName("UX_AttendanceAdjustments_Result_Revision");
        builder.HasIndex(item => new { item.EmployeeId, item.WorkDate })
            .HasDatabaseName("IX_AttendanceAdjustments_Employee_WorkDate");
        builder.HasOne(item => item.DailyAttendanceResult)
            .WithMany(item => item.Adjustments)
            .HasForeignKey(item => item.DailyAttendanceResultId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(item => item.Employee)
            .WithMany(item => item.AttendanceAdjustments)
            .HasForeignKey(item => item.EmployeeId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(item => item.SupersedesAdjustment)
            .WithMany()
            .HasForeignKey(item => item.SupersedesAdjustmentId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
