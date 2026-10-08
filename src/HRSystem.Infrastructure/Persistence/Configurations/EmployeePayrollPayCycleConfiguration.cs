using HRSystem.Domain.Payroll;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRSystem.Infrastructure.Persistence.Configurations;

public sealed class EmployeePayrollPayCycleConfiguration :
    IEntityTypeConfiguration<EmployeePayrollPayCycle>
{
    public void Configure(EntityTypeBuilder<EmployeePayrollPayCycle> builder)
    {
        builder.ToTable("EmployeePayrollPayCycles", table =>
        {
            table.HasCheckConstraint("CK_EmployeePayrollPayCycles_Type",
                "[Type] IN (1, 2, 3)");
            table.HasCheckConstraint("CK_EmployeePayrollPayCycles_Range",
                "[EffectiveTo] IS NULL OR [EffectiveTo] >= [EffectiveFrom]");
            table.HasCheckConstraint("CK_EmployeePayrollPayCycles_MonthBoundaries",
                "DAY([EffectiveFrom]) = 1 AND ([EffectiveTo] IS NULL OR DAY(DATEADD(day, 1, [EffectiveTo])) = 1)");
            table.HasCheckConstraint("CK_EmployeePayrollPayCycles_Details",
                "([Type] = 1 AND [AnchorPayMonth] IS NULL AND [FixedPaymentAmount] IS NULL AND [MonthlyFixedAmount] IS NULL AND [CycleMonths] IS NULL AND [PaymentTiming] IS NULL) OR " +
                "([Type] = 2 AND [AnchorPayMonth] IS NOT NULL AND DAY([AnchorPayMonth]) = 1 AND [FixedPaymentAmount] > 0 AND [MonthlyFixedAmount] IS NULL AND [CycleMonths] IS NULL AND [PaymentTiming] IS NULL) OR " +
                "([Type] = 3 AND [AnchorPayMonth] IS NOT NULL AND DAY([AnchorPayMonth]) = 1 AND [FixedPaymentAmount] IS NULL AND [MonthlyFixedAmount] > 0 AND [CycleMonths] BETWEEN 2 AND 120 AND [PaymentTiming] = 1)");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Type).HasConversion<byte>().HasColumnType("tinyint");
        builder.Property(x => x.EffectiveFrom).HasColumnType("date");
        builder.Property(x => x.EffectiveTo).HasColumnType("date");
        builder.Property(x => x.AnchorPayMonth).HasColumnType("date");
        builder.Property(x => x.FixedPaymentAmount).HasPrecision(18, 2);
        builder.Property(x => x.MonthlyFixedAmount).HasPrecision(18, 2);
        builder.Property(x => x.PaymentTiming).HasConversion<byte?>()
            .HasColumnType("tinyint");
        builder.Property(x => x.Reason).HasMaxLength(500);
        builder.Property(x => x.RowVersion).IsRowVersion();
        builder.HasIndex(x => new { x.EmployeeId, x.EffectiveFrom })
            .IsUnique()
            .HasDatabaseName("UX_EmployeePayrollPayCycles_Employee_From");
        builder.HasIndex(x => new { x.EmployeeId, x.IsActive, x.EffectiveFrom,
                x.EffectiveTo })
            .HasDatabaseName("IX_EmployeePayrollPayCycles_EffectiveLookup");
        builder.HasOne(x => x.Employee).WithMany()
            .HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.NoAction);
    }
}
