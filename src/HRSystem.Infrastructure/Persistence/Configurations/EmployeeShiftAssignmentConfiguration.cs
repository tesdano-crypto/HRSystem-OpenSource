using HRSystem.Domain.Attendance;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRSystem.Infrastructure.Persistence.Configurations;

public sealed class EmployeeShiftAssignmentConfiguration :
    IEntityTypeConfiguration<EmployeeShiftAssignment>
{
    public void Configure(EntityTypeBuilder<EmployeeShiftAssignment> builder)
    {
        builder.ToTable("EmployeeShiftAssignments", table =>
        {
            table.HasCheckConstraint(
                "CK_EmployeeShiftAssignments_EffectivePeriod",
                "[EffectiveTo] IS NULL OR [EffectiveTo] >= [EffectiveFrom]");
        });
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Id).ValueGeneratedNever();
        builder.Property(item => item.EffectiveFrom).HasColumnType("date");
        builder.Property(item => item.EffectiveTo).HasColumnType("date");
        builder.Property(item => item.CreatedAtUtc).HasColumnType("datetimeoffset");
        builder.Property(item => item.UpdatedAtUtc).HasColumnType("datetimeoffset");
        builder.Property(item => item.RowVersion).IsRowVersion();
        builder.HasIndex(item => new
            {
                item.EmployeeId,
                item.EffectiveFrom,
                item.EffectiveTo,
                item.IsActive
            })
            .HasDatabaseName("IX_EmployeeShiftAssignments_Employee_Period");
        builder.HasOne(item => item.Employee)
            .WithMany(item => item.ShiftAssignments)
            .HasForeignKey(item => item.EmployeeId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(item => item.Shift)
            .WithMany(item => item.Assignments)
            .HasForeignKey(item => item.ShiftId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
