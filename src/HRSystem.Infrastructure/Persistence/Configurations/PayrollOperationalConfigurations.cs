using HRSystem.Domain.Payroll;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRSystem.Infrastructure.Persistence.Configurations;

public sealed class EmployeePayrollAssignmentConfiguration : IEntityTypeConfiguration<EmployeePayrollAssignment>
{
    public void Configure(EntityTypeBuilder<EmployeePayrollAssignment> builder)
    {
        builder.ToTable("EmployeePayrollAssignments", table => table.HasCheckConstraint("CK_EmployeePayrollAssignments_EffectiveRange", "[EffectiveTo] IS NULL OR [EffectiveTo] >= [EffectiveFrom]"));
        builder.HasKey(x => x.Id); builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.EffectiveFrom).HasColumnType("date"); builder.Property(x => x.EffectiveTo).HasColumnType("date"); builder.Property(x => x.RowVersion).IsRowVersion();
        builder.HasIndex(x => new { x.EmployeeId, x.EffectiveFrom }).IsUnique().HasDatabaseName("UX_EmployeePayrollAssignments_Employee_From");
        builder.HasIndex(x => new { x.EmployeeId, x.IsActive, x.EffectiveFrom, x.EffectiveTo }).HasDatabaseName("IX_EmployeePayrollAssignments_EffectiveLookup");
        builder.HasOne(x => x.Employee).WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(x => x.PayrollPlan).WithMany(x => x.EmployeeAssignments).HasForeignKey(x => x.PayrollPlanId).OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class EmployeePayrollComponentOverrideConfiguration : IEntityTypeConfiguration<EmployeePayrollComponentOverride>
{
    public void Configure(EntityTypeBuilder<EmployeePayrollComponentOverride> builder)
    {
        builder.ToTable("EmployeePayrollComponentOverrides", table =>
        {
            table.HasCheckConstraint("CK_EmployeePayrollComponentOverrides_Amount", "[OverrideAmount] IS NULL OR [OverrideAmount] >= 0");
            table.HasCheckConstraint("CK_EmployeePayrollComponentOverrides_EffectiveRange", "[EffectiveTo] IS NULL OR [EffectiveTo] >= [EffectiveFrom]");
            table.HasCheckConstraint("CK_EmployeePayrollComponentOverrides_ModeAmount", "([OverrideMode] IN (1, 2) AND [OverrideAmount] IS NOT NULL) OR ([OverrideMode] = 3 AND [OverrideAmount] IS NULL)");
        });
        builder.HasKey(x => x.Id); builder.Property(x => x.Id).ValueGeneratedNever(); builder.Property(x => x.OverrideAmount).HasPrecision(18, 2);
        builder.Property(x => x.ReasonCode).HasMaxLength(100); builder.Property(x => x.EffectiveFrom).HasColumnType("date"); builder.Property(x => x.EffectiveTo).HasColumnType("date"); builder.Property(x => x.RowVersion).IsRowVersion();
        builder.HasIndex(x => new { x.EmployeeId, x.PayrollComponentDefinitionId, x.EffectiveFrom }).IsUnique().HasDatabaseName("UX_EmployeePayrollOverrides_Employee_Component_From");
        builder.HasIndex(x => new { x.EmployeeId, x.PayrollComponentDefinitionId, x.EffectiveFrom, x.EffectiveTo }).HasDatabaseName("IX_EmployeePayrollOverrides_EffectiveLookup");
        builder.HasOne(x => x.Employee).WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(x => x.ComponentDefinition).WithMany().HasForeignKey(x => x.PayrollComponentDefinitionId).OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class PayrollPeriodConfiguration : IEntityTypeConfiguration<PayrollPeriod>
{
    public void Configure(EntityTypeBuilder<PayrollPeriod> builder)
    {
        builder.ToTable("PayrollPeriods", table =>
        {
            table.HasCheckConstraint("CK_PayrollPeriods_Month", "[Month] BETWEEN 1 AND 12");
            table.HasCheckConstraint("CK_PayrollPeriods_Dates", "[PeriodEnd] >= [PeriodStart]");
        });
        builder.HasKey(x => x.Id); builder.Property(x => x.Id).ValueGeneratedNever(); builder.Property(x => x.PeriodStart).HasColumnType("date"); builder.Property(x => x.PeriodEnd).HasColumnType("date"); builder.Property(x => x.RowVersion).IsRowVersion();
        builder.HasIndex(x => new { x.Year, x.Month }).IsUnique().HasDatabaseName("UX_PayrollPeriods_Year_Month");
    }
}

public sealed class PayrollRunConfiguration : IEntityTypeConfiguration<PayrollRun>
{
    public void Configure(EntityTypeBuilder<PayrollRun> builder)
    {
        builder.ToTable("PayrollRuns"); builder.HasKey(x => x.Id); builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.CreatedAtUtc).HasColumnType("datetimeoffset"); builder.Property(x => x.FinalizedAtUtc).HasColumnType("datetimeoffset"); builder.Property(x => x.CreatedBy).HasMaxLength(450).IsRequired(); builder.Property(x => x.RowVersion).IsRowVersion();
        builder.Property(x => x.Trigger).HasConversion<byte>().HasColumnType("tinyint");
        builder.HasIndex(x => new { x.PayrollPeriodId, x.RevisionNumber }).IsUnique()
            .HasDatabaseName("UX_PayrollRuns_Period_Revision");
        builder.HasOne(x => x.PayrollPeriod).WithMany(x => x.Runs).HasForeignKey(x => x.PayrollPeriodId).OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class PayrollAdjustmentConfiguration : IEntityTypeConfiguration<PayrollAdjustment>
{
    public void Configure(EntityTypeBuilder<PayrollAdjustment> builder)
    {
        builder.ToTable("PayrollAdjustments", table => table.HasCheckConstraint("CK_PayrollAdjustments_Amount", "[Amount] > 0"));
        builder.HasKey(x => x.Id); builder.Property(x => x.Id).ValueGeneratedNever(); builder.Property(x => x.Amount).HasPrecision(18, 2);
        builder.Property(x => x.Reason).HasMaxLength(500).IsRequired(); builder.Property(x => x.CreatedBy).HasMaxLength(450).IsRequired(); builder.Property(x => x.CreatedAtUtc).HasColumnType("datetimeoffset"); builder.Property(x => x.RowVersion).IsRowVersion();
        builder.HasIndex(x => new { x.PayrollPeriodId, x.EmployeeId }).HasDatabaseName("IX_PayrollAdjustments_Period_Employee");
        builder.HasOne(x => x.PayrollPeriod).WithMany(x => x.Adjustments).HasForeignKey(x => x.PayrollPeriodId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(x => x.Employee).WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(x => x.ComponentDefinition).WithMany().HasForeignKey(x => x.PayrollComponentDefinitionId).OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class PayrollEmployeeSnapshotConfiguration : IEntityTypeConfiguration<PayrollEmployeeSnapshot>
{
    public void Configure(EntityTypeBuilder<PayrollEmployeeSnapshot> builder)
    {
        builder.ToTable("PayrollEmployeeSnapshots", table =>
        {
            table.HasCheckConstraint("CK_PayrollEmployeeSnapshots_Totals",
                "[GrossPay] >= 0 AND [TotalDeductions] >= 0 AND [BlockingComponentCount] >= 0");
            table.HasCheckConstraint("CK_PayrollEmployeeSnapshots_TotalFingerprint",
                "[TotalSourceFingerprint] IS NULL OR DATALENGTH([TotalSourceFingerprint]) = 32");
        }); builder.HasKey(x => x.Id); builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.EmployeeCode).HasMaxLength(20).IsRequired(); builder.Property(x => x.EmployeeName).HasMaxLength(100).IsRequired(); builder.Property(x => x.DepartmentName).HasMaxLength(100).IsRequired(); builder.Property(x => x.PayrollPlanCode).HasMaxLength(50);
        builder.Property(x => x.EmploymentStart).HasColumnType("date"); builder.Property(x => x.EmploymentEnd).HasColumnType("date"); builder.Property(x => x.SnapshotAtUtc).HasColumnType("datetimeoffset"); builder.Property(x => x.RowVersion).IsRowVersion();
        builder.Property(x => x.GrossPay).HasPrecision(18, 2);
        builder.Property(x => x.TotalDeductions).HasPrecision(18, 2);
        builder.Property(x => x.NetPay).HasPrecision(18, 2);
        builder.Property(x => x.TotalCalculationStatus)
            .HasDefaultValue(PayrollCalculationStatus.NotCalculated);
        builder.Property(x => x.TotalsCalculatedAtUtc).HasColumnType("datetimeoffset");
        builder.Property(x => x.TotalSourceFingerprintVersion).HasColumnType("smallint");
        builder.Property(x => x.TotalSourceFingerprint).HasColumnType("binary(32)");
        builder.HasIndex(x => new { x.PayrollRunId, x.EmployeeId }).IsUnique().HasDatabaseName("UX_PayrollEmployeeSnapshots_Run_Employee");
        builder.HasAlternateKey(x => new { x.PayrollPeriodId, x.EmployeeId, x.Id })
            .HasName("AK_PayrollEmployeeSnapshots_Period_Employee_Id");
        builder.HasOne(x => x.PayrollRun).WithMany(x => x.EmployeeSnapshots).HasForeignKey(x => x.PayrollRunId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(x => x.PayrollPeriod).WithMany().HasForeignKey(x => x.PayrollPeriodId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(x => x.Employee).WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.NoAction);
        builder.HasMany(x => x.TotalBlockingEvidence)
            .WithOne(x => x.PayrollEmployeeSnapshot)
            .HasForeignKey(x => x.PayrollEmployeeSnapshotId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class PayrollPeriodEmployeeCurrentSnapshotConfiguration :
    IEntityTypeConfiguration<PayrollPeriodEmployeeCurrentSnapshot>
{
    public void Configure(EntityTypeBuilder<PayrollPeriodEmployeeCurrentSnapshot> builder)
    {
        builder.ToTable("PayrollPeriodEmployeeCurrentSnapshots");
        builder.HasKey(x => new { x.PayrollPeriodId, x.EmployeeId });
        builder.Property(x => x.UpdatedAtUtc).HasColumnType("datetimeoffset");
        builder.Property(x => x.RowVersion).IsRowVersion();
        builder.HasOne(x => x.PayrollPeriod).WithMany(x => x.CurrentSnapshots)
            .HasForeignKey(x => x.PayrollPeriodId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(x => x.Employee).WithMany()
            .HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(x => x.PayrollEmployeeSnapshot).WithMany()
            .HasForeignKey(x => new
            {
                x.PayrollPeriodId,
                x.EmployeeId,
                x.PayrollEmployeeSnapshotId
            })
            .HasPrincipalKey(x => new { x.PayrollPeriodId, x.EmployeeId, x.Id })
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasIndex(x => x.PayrollEmployeeSnapshotId).IsUnique()
            .HasDatabaseName("UX_PayrollCurrentSnapshots_Snapshot");
    }
}

public sealed class PayrollTotalBlockingEvidenceConfiguration :
    IEntityTypeConfiguration<PayrollTotalBlockingEvidence>
{
    public void Configure(EntityTypeBuilder<PayrollTotalBlockingEvidence> builder)
    {
        builder.ToTable("PayrollTotalBlockingEvidence");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.ComponentCode).HasMaxLength(50).IsRequired();
        builder.HasIndex(x => new
        {
            x.PayrollEmployeeSnapshotId,
            x.ComponentCode,
            x.Reason
        }).IsUnique().HasDatabaseName("UX_PayrollTotalBlockingEvidence_Snapshot_Code_Reason");
        builder.HasOne(x => x.ComponentDefinition).WithMany()
            .HasForeignKey(x => x.ComponentDefinitionId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class PayrollEmployeeSnapshotComponentConfiguration : IEntityTypeConfiguration<PayrollEmployeeSnapshotComponent>
{
    public void Configure(EntityTypeBuilder<PayrollEmployeeSnapshotComponent> builder)
    {
        builder.ToTable("PayrollEmployeeSnapshotComponents", table =>
        {
            table.HasCheckConstraint("CK_PayrollEmployeeSnapshotComponents_Amounts", "([StandardAmount] IS NULL OR [StandardAmount] >= 0) AND ([OverrideAmount] IS NULL OR [OverrideAmount] >= 0) AND ([ResolvedAmount] IS NULL OR [ResolvedAmount] >= 0) AND ([FullMonthlyAmount] IS NULL OR [FullMonthlyAmount] >= 0) AND ([RawProratedAmount] IS NULL OR [RawProratedAmount] >= 0)");
            table.HasCheckConstraint("CK_PayrollEmployeeSnapshotComponents_PayableDays", "[PayableDays] IS NULL OR [PayableDays] BETWEEN 0 AND 31");
            table.HasCheckConstraint("CK_PayrollEmployeeSnapshotComponents_ProrationFactor", "[ProrationFactor] IS NULL OR [ProrationFactor] BETWEEN 0 AND 1");
            table.HasCheckConstraint("CK_PayrollEmployeeSnapshotComponents_ProrationKind", "[ProrationKind] IN (0, 1, 2)");
        });
        builder.HasKey(x => x.Id); builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.ComponentCode).HasMaxLength(50).IsRequired(); builder.Property(x => x.ComponentName).HasMaxLength(100).IsRequired();
        builder.Property(x => x.StandardAmount).HasPrecision(18, 2); builder.Property(x => x.OverrideAmount).HasPrecision(18, 2); builder.Property(x => x.ResolvedAmount).HasPrecision(18, 2); builder.Property(x => x.EffectiveSourceDate).HasColumnType("date");
        builder.Property(x => x.ProrationKind).HasConversion<short>().HasColumnType("smallint");
        builder.Property(x => x.FullMonthlyAmount).HasPrecision(18, 2);
        builder.Property(x => x.ProrationFactor).HasPrecision(18, 6);
        builder.Property(x => x.RawProratedAmount).HasPrecision(18, 6);
        builder.HasIndex(x => new { x.PayrollEmployeeSnapshotId, x.PayrollComponentDefinitionId, x.SourceType, x.SourceId }).IsUnique().HasDatabaseName("UX_PayrollSnapshotComponents_Source");
        builder.HasOne(x => x.PayrollEmployeeSnapshot).WithMany(x => x.Components).HasForeignKey(x => x.PayrollEmployeeSnapshotId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(x => x.ComponentDefinition).WithMany().HasForeignKey(x => x.PayrollComponentDefinitionId).OnDelete(DeleteBehavior.NoAction);
    }
}
