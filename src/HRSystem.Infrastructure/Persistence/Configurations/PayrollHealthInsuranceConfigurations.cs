using HRSystem.Domain.Payroll;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRSystem.Infrastructure.Persistence.Configurations;

public sealed class EmployeeHealthInsuranceEnrollmentConfiguration :
    IEntityTypeConfiguration<EmployeeHealthInsuranceEnrollment>
{
    public void Configure(EntityTypeBuilder<EmployeeHealthInsuranceEnrollment> builder)
    {
        builder.ToTable("EmployeeHealthInsuranceEnrollments", table =>
        {
            table.HasCheckConstraint("CK_EmployeeHealthInsuranceEnrollments_Period",
                "[EffectiveTo] IS NULL OR [EffectiveTo] >= [EffectiveFrom]");
            table.HasCheckConstraint("CK_EmployeeHealthInsuranceEnrollments_Setting",
                "([Status] = 1 AND ([MonthlyInsuredAmount] IS NULL OR [MonthlyInsuredAmount] > 0) AND ([DependentCount] IS NULL OR [DependentCount] >= 0)) OR ([Status] = 2 AND [MonthlyInsuredAmount] IS NULL AND [DependentCount] IS NULL)");
        });
        builder.HasKey(x => x.Id); builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Status).HasConversion<byte>();
        builder.Property(x => x.MonthlyInsuredAmount).HasPrecision(18, 2);
        builder.Property(x => x.EffectiveFrom).HasColumnType("date");
        builder.Property(x => x.EffectiveTo).HasColumnType("date");
        builder.Property(x => x.RowVersion).IsRowVersion();
        builder.HasIndex(x => new { x.EmployeeId, x.EffectiveFrom }).IsUnique()
            .HasDatabaseName("UX_EmployeeHealthInsuranceEnrollments_Employee_From");
        builder.HasIndex(x => new { x.EmployeeId, x.IsActive, x.EffectiveFrom, x.EffectiveTo })
            .HasDatabaseName("IX_EmployeeHealthInsuranceEnrollments_EffectiveLookup");
        builder.HasOne(x => x.Employee).WithMany().HasForeignKey(x => x.EmployeeId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class HealthInsuranceRatePolicyConfiguration :
    IEntityTypeConfiguration<HealthInsuranceRatePolicy>
{
    public void Configure(EntityTypeBuilder<HealthInsuranceRatePolicy> builder)
    {
        builder.ToTable("HealthInsuranceRatePolicies", table =>
        {
            table.HasCheckConstraint("CK_HealthInsuranceRatePolicies_Period",
                "[EffectiveTo] IS NULL OR [EffectiveTo] >= [EffectiveFrom]");
            table.HasCheckConstraint("CK_HealthInsuranceRatePolicies_Values",
                "[GeneralPremiumRate] > 0 AND [GeneralPremiumRate] <= 1 AND [EmployeeShareRate] > 0 AND [EmployeeShareRate] <= 1 AND [DependentCap] >= 0");
        });
        builder.HasKey(x => x.Id); builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Version).HasMaxLength(50).IsRequired();
        builder.Property(x => x.GeneralPremiumRate).HasPrecision(9, 8);
        builder.Property(x => x.EmployeeShareRate).HasPrecision(9, 8);
        builder.Property(x => x.DependentBillingRule).HasConversion<byte>();
        builder.Property(x => x.ContributionPeriodPolicy).HasConversion<byte>();
        builder.Property(x => x.EffectiveFrom).HasColumnType("date");
        builder.Property(x => x.EffectiveTo).HasColumnType("date");
        builder.Property(x => x.RowVersion).IsRowVersion();
        builder.HasIndex(x => new { x.Version, x.EffectiveFrom }).IsUnique()
            .HasDatabaseName("UX_HealthInsuranceRatePolicies_Version_From");
        builder.HasIndex(x => new { x.IsActive, x.EffectiveFrom, x.EffectiveTo })
            .HasDatabaseName("IX_HealthInsuranceRatePolicies_EffectiveLookup");
    }
}

public sealed class PayrollHealthInsuranceSnapshotConfiguration :
    IEntityTypeConfiguration<PayrollHealthInsuranceSnapshot>
{
    public void Configure(EntityTypeBuilder<PayrollHealthInsuranceSnapshot> builder)
    {
        builder.ToTable("PayrollHealthInsuranceSnapshots", table =>
            table.HasCheckConstraint("CK_PayrollHealthInsuranceSnapshots_Fingerprint",
                "DATALENGTH([SourceFingerprint]) = 32"));
        builder.HasKey(x => x.Id); builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.EnrollmentStatus).HasConversion<byte?>();
        builder.Property(x => x.MonthlyInsuredAmount).HasPrecision(18, 2);
        builder.Property(x => x.EnrollmentFrom).HasColumnType("date");
        builder.Property(x => x.EnrollmentTo).HasColumnType("date");
        builder.Property(x => x.PolicyVersion).HasMaxLength(50);
        builder.Property(x => x.PolicyFrom).HasColumnType("date");
        builder.Property(x => x.PolicyTo).HasColumnType("date");
        builder.Property(x => x.GeneralPremiumRate).HasPrecision(9, 8);
        builder.Property(x => x.EmployeeShareRate).HasPrecision(9, 8);
        builder.Property(x => x.RawEmployeeAmount).HasPrecision(18, 6);
        builder.Property(x => x.FinalEmployeeDeduction).HasPrecision(18, 2);
        builder.Property(x => x.SourceFingerprint).HasColumnType("binary(32)").IsRequired();
        builder.HasIndex(x => x.PayrollEmployeeSnapshotComponentId).IsUnique()
            .HasDatabaseName("UX_PayrollHealthInsuranceSnapshots_Component");
        builder.HasOne(x => x.PayrollEmployeeSnapshotComponent)
            .WithOne(x => x.HealthInsuranceSnapshot)
            .HasForeignKey<PayrollHealthInsuranceSnapshot>(x => x.PayrollEmployeeSnapshotComponentId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class PayrollHealthInsuranceEvidenceConfiguration :
    IEntityTypeConfiguration<PayrollHealthInsuranceEvidence>
{
    public void Configure(EntityTypeBuilder<PayrollHealthInsuranceEvidence> builder)
    {
        builder.ToTable("PayrollHealthInsuranceEvidence");
        builder.HasKey(x => x.Id); builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.MonthlyInsuredAmount).HasPrecision(18, 2);
        builder.Property(x => x.GeneralPremiumRate).HasPrecision(9, 8);
        builder.Property(x => x.EmployeeShareRate).HasPrecision(9, 8);
        builder.Property(x => x.RawEmployeeAmount).HasPrecision(18, 6);
        builder.Property(x => x.FinalEmployeeDeduction).HasPrecision(18, 2);
        builder.HasIndex(x => x.PayrollHealthInsuranceSnapshotId).IsUnique()
            .HasDatabaseName("UX_PayrollHealthInsuranceEvidence_Snapshot");
        builder.HasOne(x => x.PayrollHealthInsuranceSnapshot).WithOne(x => x.Evidence)
            .HasForeignKey<PayrollHealthInsuranceEvidence>(x => x.PayrollHealthInsuranceSnapshotId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
