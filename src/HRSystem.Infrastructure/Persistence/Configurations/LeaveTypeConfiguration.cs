using HRSystem.Domain.MasterData;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRSystem.Infrastructure.Persistence.Configurations;

public sealed class LeaveTypeConfiguration : IEntityTypeConfiguration<LeaveType>
{
    public void Configure(EntityTypeBuilder<LeaveType> builder)
    {
        builder.ToTable("LeaveTypes", table =>
        {
            table.HasCheckConstraint("CK_LeaveTypes_MinimumUnit_Positive", "[MinimumUnit] > 0");
            table.HasCheckConstraint("CK_LeaveTypes_SortOrder_NonNegative", "[SortOrder] >= 0");
            table.HasCheckConstraint("CK_LeaveTypes_Category", "[Category] IN (1, 2, 3)");
            table.HasCheckConstraint("CK_LeaveTypes_CalculationMode", "[CalculationMode] IN (1, 2, 3)");
            table.HasCheckConstraint("CK_LeaveTypes_MinimumRequestMinutes_Positive", "[MinimumRequestMinutes] IS NULL OR [MinimumRequestMinutes] > 0");
            table.HasCheckConstraint(
                "CK_LeaveTypes_EmployeeRequestMode",
                $"[IsEmployeeRequestEnabled] = 0 OR [CalculationMode] IN ({(byte)LeaveCalculationMode.WorkingSchedule}, {(byte)LeaveCalculationMode.CalendarDays})");
            table.HasCheckConstraint("CK_LeaveTypes_HourlyRequestMode", "[AllowHourlyRequest] = 0 OR [CalculationMode] = 1");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Code).HasMaxLength(LeaveType.CodeMaxLength).UseCollation("Latin1_General_100_CI_AS").IsRequired();
        builder.Property(x => x.Name).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Unit).HasConversion<byte>();
        builder.Property(x => x.MinimumUnit).HasPrecision(8, 2);
        builder.Property(x => x.Category).HasConversion<byte>().IsRequired();
        builder.Property(x => x.CalculationMode).HasConversion<byte>().IsRequired();
        builder.Property(x => x.Description).HasMaxLength(LeaveType.DescriptionMaxLength);
        builder.Property(x => x.CreatedAtUtc).HasColumnType("datetimeoffset").IsRequired();
        builder.Property(x => x.UpdatedAtUtc).HasColumnType("datetimeoffset").IsRequired();
        builder.Property(x => x.RowVersion).IsRowVersion();
        builder.HasIndex(x => x.Code).IsUnique().HasDatabaseName("UX_LeaveTypes_Code");
        builder.HasIndex(x => new { x.IsActive, x.SortOrder }).HasDatabaseName("IX_LeaveTypes_IsActive_SortOrder");
    }
}
