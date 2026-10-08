using HRSystem.Domain.Payroll;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRSystem.Infrastructure.Persistence.Configurations;

public sealed class PayrollPeriodicAccrualSnapshotConfiguration :
    IEntityTypeConfiguration<PayrollPeriodicAccrualSnapshot>
{
    public void Configure(EntityTypeBuilder<PayrollPeriodicAccrualSnapshot> builder)
    {
        builder.ToTable("PayrollPeriodicAccrualSnapshots", table =>
        {
            table.HasCheckConstraint("CK_PayrollPeriodicAccrualSnapshots_Cycle",
                "[CycleMonths] BETWEEN 2 AND 120 AND [PaymentTiming] = 1");
            table.HasCheckConstraint("CK_PayrollPeriodicAccrualSnapshots_Fingerprint",
                "DATALENGTH([SourceFingerprint]) = 32");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.CoveredFrom).HasColumnType("date");
        builder.Property(x => x.CoveredTo).HasColumnType("date");
        builder.Property(x => x.PaymentTiming).HasConversion<byte>();
        builder.Property(x => x.TotalAmount).HasPrecision(18, 2);
        builder.Property(x => x.CalculationStatus).HasConversion<int>();
        builder.Property(x => x.SourceFingerprint).HasColumnType("binary(32)")
            .IsRequired();
        builder.HasIndex(x => x.PayrollEmployeeSnapshotComponentId).IsUnique()
            .HasDatabaseName("UX_PayrollPeriodicAccrualSnapshots_Component");
        builder.HasOne(x => x.PayrollEmployeeSnapshotComponent)
            .WithOne(x => x.PeriodicAccrualSnapshot)
            .HasForeignKey<PayrollPeriodicAccrualSnapshot>(x =>
                x.PayrollEmployeeSnapshotComponentId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class PayrollPeriodicAccrualMonthEvidenceConfiguration :
    IEntityTypeConfiguration<PayrollPeriodicAccrualMonthEvidence>
{
    public void Configure(EntityTypeBuilder<PayrollPeriodicAccrualMonthEvidence> builder)
    {
        builder.ToTable("PayrollPeriodicAccrualMonthEvidence");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.CoveredMonth).HasColumnType("date");
        builder.Property(x => x.MonthlyFixedAmount).HasPrecision(18, 2);
        builder.Property(x => x.CalculationStatus).HasConversion<int>();
        builder.HasIndex(x => new
            { x.PayrollPeriodicAccrualSnapshotId, x.CoveredMonth }).IsUnique()
            .HasDatabaseName("UX_PayrollPeriodicAccrualMonthEvidence_Snapshot_Month");
        builder.HasOne(x => x.PayrollPeriodicAccrualSnapshot)
            .WithMany(x => x.Months)
            .HasForeignKey(x => x.PayrollPeriodicAccrualSnapshotId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
