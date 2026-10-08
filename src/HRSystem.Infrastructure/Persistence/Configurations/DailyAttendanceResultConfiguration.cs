using HRSystem.Domain.Attendance;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRSystem.Infrastructure.Persistence.Configurations;

public sealed class DailyAttendanceResultConfiguration :
    IEntityTypeConfiguration<DailyAttendanceResult>
{
    public void Configure(EntityTypeBuilder<DailyAttendanceResult> builder)
    {
        builder.ToTable("DailyAttendanceResults", table =>
        {
            table.HasCheckConstraint(
                "CK_DailyAttendanceResults_Durations",
                "[LateSeconds] >= 0 AND [EarlyLeaveSeconds] >= 0 " +
                "AND [ApprovedLeaveMinutes] >= 0 " +
                "AND [RequiredAttendanceMinutes] >= 0 " +
                "AND [RecognizedWorkMinutes] >= 0 " +
                "AND [MissingMinutes] >= 0 " +
                "AND [WorkedDuringApprovedLeaveMinutes] >= 0");
            table.HasCheckConstraint(
                "CK_DailyAttendanceResults_AttendanceExceptionMinutes",
                "[AttendanceExceptionMinutes] >= 0");
        });
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Id).ValueGeneratedNever();
        builder.Property(item => item.WorkDate).HasColumnType("date");
        builder.Property(item => item.CalendarClassification).HasConversion<byte>();
        builder.Property(item => item.Status).HasConversion<byte>();
        builder.Property(item => item.LeaveCoverageStatus).HasConversion<byte>();
        builder.Property(item => item.AttendanceExceptionType).HasConversion<byte?>();
        builder.Property(item => item.AttendanceExceptionReasonType).HasConversion<byte?>();
        builder.Property(item => item.AttendanceExceptionImpactType).HasConversion<byte?>();
        builder.Property(item => item.AttendanceExceptionFromTime).HasColumnType("time(0)");
        builder.Property(item => item.AttendanceExceptionToTime).HasColumnType("time(0)");
        builder.Property(item => item.ShiftCodeSnapshot).HasMaxLength(30);
        builder.Property(item => item.ShiftNameSnapshot).HasMaxLength(100);
        builder.Property(item => item.ScheduledStartTimeSnapshot).HasColumnType("time(0)");
        builder.Property(item => item.LateThresholdTimeSnapshot).HasColumnType("time(0)");
        builder.Property(item => item.LunchBreakStartTimeSnapshot).HasColumnType("time(0)");
        builder.Property(item => item.LunchBreakEndTimeSnapshot).HasColumnType("time(0)");
        builder.Property(item => item.ScheduledEndTimeSnapshot).HasColumnType("time(0)");
        builder.Property(item => item.RawClockInLocalTime).HasColumnType("datetime2(0)");
        builder.Property(item => item.RawClockOutLocalTime).HasColumnType("datetime2(0)");
        builder.Property(item => item.EffectiveClockInLocalTime).HasColumnType("datetime2(0)");
        builder.Property(item => item.EffectiveClockOutLocalTime).HasColumnType("datetime2(0)");
        builder.Property(item => item.CalculationVersion).HasMaxLength(30).IsRequired();
        builder.Property(item => item.CalculatedAtUtc).HasColumnType("datetimeoffset");
        builder.Property(item => item.CreatedAtUtc).HasColumnType("datetimeoffset");
        builder.Property(item => item.UpdatedAtUtc).HasColumnType("datetimeoffset");
        builder.Property(item => item.RowVersion).IsRowVersion();
        builder.HasIndex(item => new { item.EmployeeId, item.WorkDate })
            .IsUnique()
            .HasDatabaseName("UX_DailyAttendanceResults_Employee_WorkDate");
        builder.HasIndex(item => new { item.WorkDate, item.Status })
            .HasDatabaseName("IX_DailyAttendanceResults_WorkDate_Status");
        builder.HasIndex(item => new
            {
                item.WorkDate,
                item.LeaveCoverageStatus
            })
            .HasDatabaseName(
                "IX_DailyAttendanceResults_WorkDate_LeaveCoverageStatus");
        builder.HasIndex(item => new
            {
                item.WorkDate,
                item.IsEmploymentSuspended
            })
            .HasDatabaseName(
                "IX_DailyAttendanceResults_WorkDate_EmploymentSuspended");
        builder.HasIndex(item => new { item.WorkDate, item.IsAttendanceExempted })
            .HasDatabaseName("IX_DailyAttendanceResults_WorkDate_AttendanceExempted");
        builder.HasOne(item => item.Employee)
            .WithMany(item => item.DailyAttendanceResults)
            .HasForeignKey(item => item.EmployeeId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(item => item.Shift)
            .WithMany(item => item.DailyAttendanceResults)
            .HasForeignKey(item => item.ShiftId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<AttendanceRawEvent>()
            .WithMany()
            .HasForeignKey(item => item.RawClockInEventId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<AttendanceRawEvent>()
            .WithMany()
            .HasForeignKey(item => item.RawClockOutEventId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(item => item.CurrentAdjustment)
            .WithMany()
            .HasForeignKey(item => item.CurrentAdjustmentId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(item => item.EmploymentSuspensionSource)
            .WithMany()
            .HasForeignKey(item => item.EmploymentSuspensionSourceId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(item => item.AttendanceExceptionSource)
            .WithMany()
            .HasForeignKey(item => item.AttendanceExceptionSourceId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
