using HRSystem.Domain.Payroll;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRSystem.Infrastructure.Persistence.Configurations;

public sealed class PayrollFinalizationConfiguration :
    IEntityTypeConfiguration<PayrollFinalization>
{
    public void Configure(EntityTypeBuilder<PayrollFinalization> builder)
    {
        builder.ToTable("PayrollFinalizations", table =>
        {
            table.HasCheckConstraint("CK_PayrollFinalizations_Fingerprint",
                "DATALENGTH([MonthFingerprint]) = 32 AND [MonthFingerprintVersion] > 0");
            table.HasCheckConstraint("CK_PayrollFinalizations_Totals",
                "[EmployeeCount] > 0 AND [GrossPay] >= 0 AND [TotalDeductions] >= 0 AND [NetPay] >= 0");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.ApprovedByUserId).HasMaxLength(450).IsRequired();
        builder.Property(x => x.ApprovedByDisplayName).HasMaxLength(100).IsRequired();
        builder.Property(x => x.FinalizedByUserId).HasMaxLength(450).IsRequired();
        builder.Property(x => x.FinalizedByDisplayName).HasMaxLength(100).IsRequired();
        builder.Property(x => x.ApprovedAtUtc).HasColumnType("datetimeoffset");
        builder.Property(x => x.FinalizedAtUtc).HasColumnType("datetimeoffset");
        builder.Property(x => x.MonthFingerprintVersion).HasColumnType("smallint");
        builder.Property(x => x.MonthFingerprint).HasColumnType("binary(32)").IsRequired();
        builder.Property(x => x.GrossPay).HasPrecision(18, 2);
        builder.Property(x => x.TotalDeductions).HasPrecision(18, 2);
        builder.Property(x => x.NetPay).HasPrecision(18, 2);
        builder.Property(x => x.RowVersion).IsRowVersion();
        builder.HasIndex(x => x.PayrollPeriodId).IsUnique()
            .HasDatabaseName("UX_PayrollFinalizations_Period");
        builder.HasIndex(x => x.ApprovalId).IsUnique()
            .HasDatabaseName("UX_PayrollFinalizations_Approval");
        builder.HasOne(x => x.PayrollPeriod).WithOne(x => x.Finalization)
            .HasForeignKey<PayrollFinalization>(x => x.PayrollPeriodId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(x => x.Approval).WithMany()
            .HasForeignKey(x => x.ApprovalId).OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class PayrollFinalEmployeeSnapshotConfiguration :
    IEntityTypeConfiguration<PayrollFinalEmployeeSnapshot>
{
    public void Configure(EntityTypeBuilder<PayrollFinalEmployeeSnapshot> builder)
    {
        builder.ToTable("PayrollFinalEmployeeSnapshots", table =>
        {
            table.HasCheckConstraint("CK_PayrollFinalEmployeeSnapshots_Fingerprint",
                "DATALENGTH([SnapshotFingerprint]) = 32 AND [SnapshotFingerprintVersion] > 0");
            table.HasCheckConstraint("CK_PayrollFinalEmployeeSnapshots_Totals",
                "[GrossPay] >= 0 AND [TotalDeductions] >= 0 AND [NetPay] >= 0");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.EmployeeNumber).HasMaxLength(20).IsRequired();
        builder.Property(x => x.EmployeeName).HasMaxLength(100).IsRequired();
        builder.Property(x => x.DepartmentName).HasMaxLength(100).IsRequired();
        builder.Property(x => x.GrossPay).HasPrecision(18, 2);
        builder.Property(x => x.TotalDeductions).HasPrecision(18, 2);
        builder.Property(x => x.NetPay).HasPrecision(18, 2);
        builder.Property(x => x.SnapshotFingerprintVersion).HasColumnType("smallint");
        builder.Property(x => x.SnapshotFingerprint).HasColumnType("binary(32)").IsRequired();
        builder.HasIndex(x => new { x.PayrollFinalizationId, x.EmployeeId }).IsUnique()
            .HasDatabaseName("UX_PayrollFinalEmployeeSnapshots_Finalization_Employee");
        builder.HasIndex(x => x.PayrollEmployeeSnapshotId).IsUnique()
            .HasDatabaseName("UX_PayrollFinalEmployeeSnapshots_Snapshot");
        builder.HasOne(x => x.PayrollFinalization).WithMany(x => x.EmployeeSnapshots)
            .HasForeignKey(x => x.PayrollFinalizationId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(x => x.PayrollEmployeeSnapshot).WithMany()
            .HasForeignKey(x => x.PayrollEmployeeSnapshotId).OnDelete(DeleteBehavior.NoAction);
    }
}
