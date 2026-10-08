using HRSystem.Domain.Attendance;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRSystem.Infrastructure.Persistence.Configurations;

public sealed class AttendanceShiftConfiguration :
    IEntityTypeConfiguration<AttendanceShift>
{
    public void Configure(EntityTypeBuilder<AttendanceShift> builder)
    {
        builder.ToTable("AttendanceShifts", table =>
        {
            table.HasCheckConstraint(
                "CK_AttendanceShifts_ExpectedWorkMinutes",
                "[ExpectedWorkMinutes] >= 1 AND [ExpectedWorkMinutes] <= 1440");
        });
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Id).ValueGeneratedNever();
        builder.Property(item => item.Code)
            .HasMaxLength(30)
            .UseCollation("Latin1_General_100_CI_AS")
            .IsRequired();
        builder.Property(item => item.Name).HasMaxLength(100).IsRequired();
        builder.Property(item => item.ScheduledStartTime).HasColumnType("time(0)");
        builder.Property(item => item.LateThresholdTime).HasColumnType("time(0)");
        builder.Property(item => item.LunchBreakStartTime).HasColumnType("time(0)");
        builder.Property(item => item.LunchBreakEndTime).HasColumnType("time(0)");
        builder.Property(item => item.ScheduledEndTime).HasColumnType("time(0)");
        builder.Property(item => item.CreatedAtUtc).HasColumnType("datetimeoffset");
        builder.Property(item => item.UpdatedAtUtc).HasColumnType("datetimeoffset");
        builder.Property(item => item.RowVersion).IsRowVersion();
        builder.HasIndex(item => item.Code)
            .IsUnique()
            .HasDatabaseName("UX_AttendanceShifts_Code");
        builder.HasIndex(item => new { item.IsActive, item.Name })
            .HasDatabaseName("IX_AttendanceShifts_IsActive_Name");
    }
}
