using HRSystem.Domain.Payroll;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRSystem.Infrastructure.Persistence.Configurations;

public sealed class EmployeeLaborInsuranceEnrollmentConfiguration :
    IEntityTypeConfiguration<EmployeeLaborInsuranceEnrollment>
{
    public void Configure(EntityTypeBuilder<EmployeeLaborInsuranceEnrollment> builder)
    {
        builder.ToTable("EmployeeLaborInsuranceEnrollments", table =>
        {
            table.HasCheckConstraint("CK_EmployeeLaborInsuranceEnrollments_Period",
                "[EffectiveTo] IS NULL OR [EffectiveTo] >= [EffectiveFrom]");
            table.HasCheckConstraint("CK_EmployeeLaborInsuranceEnrollments_Salary",
                "([Status] = 1 AND ([MonthlyLaborInsuredSalary] IS NULL OR [MonthlyLaborInsuredSalary] > 0)) OR ([Status] = 2 AND [MonthlyLaborInsuredSalary] IS NULL)");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Status).HasConversion<byte>();
        builder.Property(x => x.MonthlyLaborInsuredSalary).HasPrecision(18, 2);
        builder.Ignore(x => x.MonthlyInsuredSalary);
        builder.Property(x => x.EffectiveFrom).HasColumnType("date");
        builder.Property(x => x.EffectiveTo).HasColumnType("date");
        builder.Property(x => x.RowVersion).IsRowVersion();
        builder.HasIndex(x => new { x.EmployeeId, x.EffectiveFrom }).IsUnique()
            .HasDatabaseName("UX_EmployeeLaborInsuranceEnrollments_Employee_From");
        builder.HasIndex(x => new { x.EmployeeId, x.IsActive, x.EffectiveFrom, x.EffectiveTo })
            .HasDatabaseName("IX_EmployeeLaborInsuranceEnrollments_EffectiveLookup");
        builder.HasOne(x => x.Employee).WithMany().HasForeignKey(x => x.EmployeeId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class EmployeeOccupationalInsuranceEnrollmentConfiguration :
    IEntityTypeConfiguration<EmployeeOccupationalInsuranceEnrollment>
{
    public void Configure(
        EntityTypeBuilder<EmployeeOccupationalInsuranceEnrollment> builder)
    {
        builder.ToTable("EmployeeOccupationalInsuranceEnrollments", table =>
        {
            table.HasCheckConstraint(
                "CK_EmployeeOccupationalInsuranceEnrollments_Period",
                "[EffectiveTo] IS NULL OR [EffectiveTo] >= [EffectiveFrom]");
            table.HasCheckConstraint(
                "CK_EmployeeOccupationalInsuranceEnrollments_Salary",
                "([Status] = 1 AND [MonthlyInsuredSalary] IS NOT NULL AND [MonthlyInsuredSalary] > 0) OR " +
                "([Status] = 2 AND [MonthlyInsuredSalary] IS NULL)");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Status).HasConversion<byte>();
        builder.Property(x => x.MonthlyInsuredSalary).HasPrecision(18, 2);
        builder.Property(x => x.EffectiveFrom).HasColumnType("date");
        builder.Property(x => x.EffectiveTo).HasColumnType("date");
        builder.Property(x => x.RowVersion).IsRowVersion();
        builder.HasIndex(x => new { x.EmployeeId, x.EffectiveFrom }).IsUnique()
            .HasDatabaseName(
                "UX_EmployeeOccupationalInsuranceEnrollments_Employee_From");
        builder.HasIndex(x => new
            { x.EmployeeId, x.IsActive, x.EffectiveFrom, x.EffectiveTo })
            .HasDatabaseName(
                "IX_EmployeeOccupationalInsuranceEnrollments_EffectiveLookup");
        builder.HasOne(x => x.Employee).WithMany().HasForeignKey(x => x.EmployeeId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class LaborInsuranceRatePolicyConfiguration :
    IEntityTypeConfiguration<LaborInsuranceRatePolicy>
{
    public void Configure(EntityTypeBuilder<LaborInsuranceRatePolicy> builder)
    {
        builder.ToTable("LaborInsuranceRatePolicies", table =>
        {
            table.HasCheckConstraint("CK_LaborInsuranceRatePolicies_Period",
                "[EffectiveTo] IS NULL OR [EffectiveTo] >= [EffectiveFrom]");
            table.HasCheckConstraint("CK_LaborInsuranceRatePolicies_Rates",
                "[Coverage] BETWEEN 1 AND 3 AND [OrdinaryAccidentInsuranceRate] BETWEEN 0 AND 1 AND [EmploymentInsuranceRate] BETWEEN 0 AND 1 AND [EmployeeShareRate] > 0 AND [EmployeeShareRate] <= 1");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Version).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Coverage).HasConversion<byte>();
        builder.Property(x => x.OrdinaryAccidentInsuranceRate).HasPrecision(9, 8);
        builder.Property(x => x.EmploymentInsuranceRate).HasPrecision(9, 8);
        builder.Property(x => x.EmployeeShareRate).HasPrecision(9, 8);
        builder.Property(x => x.ContributionPeriodPolicy).HasConversion<byte>();
        builder.Property(x => x.EffectiveFrom).HasColumnType("date");
        builder.Property(x => x.EffectiveTo).HasColumnType("date");
        builder.Property(x => x.RowVersion).IsRowVersion();
        builder.HasIndex(x => new { x.Version, x.EffectiveFrom }).IsUnique()
            .HasDatabaseName("UX_LaborInsuranceRatePolicies_Version_From");
        builder.HasIndex(x => new { x.IsActive, x.EffectiveFrom, x.EffectiveTo })
            .HasDatabaseName("IX_LaborInsuranceRatePolicies_EffectiveLookup");
    }
}

public sealed class PayrollLaborInsuranceSnapshotConfiguration :
    IEntityTypeConfiguration<PayrollLaborInsuranceSnapshot>
{
    public void Configure(EntityTypeBuilder<PayrollLaborInsuranceSnapshot> builder)
    {
        builder.ToTable("PayrollLaborInsuranceSnapshots", table =>
            table.HasCheckConstraint("CK_PayrollLaborInsuranceSnapshots_Fingerprint",
                "DATALENGTH([SourceFingerprint]) = 32"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.EnrollmentStatus).HasConversion<byte?>();
        builder.Property(x => x.MonthlyLaborInsuredSalary).HasPrecision(18, 2);
        builder.Property(x => x.MonthlyOccupationalInsuredSalary).HasPrecision(18, 2);
        builder.Ignore(x => x.MonthlyInsuredSalary);
        builder.Property(x => x.EnrollmentFrom).HasColumnType("date");
        builder.Property(x => x.EnrollmentTo).HasColumnType("date");
        builder.Property(x => x.PolicyVersion).HasMaxLength(50);
        builder.Property(x => x.PolicyFrom).HasColumnType("date");
        builder.Property(x => x.PolicyTo).HasColumnType("date");
        builder.Property(x => x.Coverage).HasConversion<byte>();
        builder.Property(x => x.OrdinaryAccidentInsuranceRate).HasPrecision(9, 8);
        builder.Property(x => x.EmploymentInsuranceRate).HasPrecision(9, 8);
        builder.Property(x => x.EmployeeShareRate).HasPrecision(9, 8);
        builder.Property(x => x.FinalEmployeeDeduction).HasPrecision(18, 2);
        builder.Property(x => x.SourceFingerprint).HasColumnType("binary(32)").IsRequired();
        builder.HasIndex(x => x.PayrollEmployeeSnapshotComponentId).IsUnique()
            .HasDatabaseName("UX_PayrollLaborInsuranceSnapshots_Component");
        builder.HasOne(x => x.PayrollEmployeeSnapshotComponent)
            .WithOne(x => x.LaborInsuranceSnapshot)
            .HasForeignKey<PayrollLaborInsuranceSnapshot>(x => x.PayrollEmployeeSnapshotComponentId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class PayrollLaborInsuranceContributionEvidenceConfiguration :
    IEntityTypeConfiguration<PayrollLaborInsuranceContributionEvidence>
{
    public void Configure(EntityTypeBuilder<PayrollLaborInsuranceContributionEvidence> builder)
    {
        builder.ToTable("PayrollLaborInsuranceContributionEvidence");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Kind).HasConversion<byte>();
        builder.Property(x => x.Rate).HasPrecision(9, 8);
        builder.Property(x => x.EmployeeShareRate).HasPrecision(9, 8);
        builder.Property(x => x.RawEmployeeAmount).HasPrecision(18, 6);
        builder.Property(x => x.RoundedDisplayAmount).HasPrecision(18, 2);
        builder.HasIndex(x => new { x.PayrollLaborInsuranceSnapshotId, x.Kind }).IsUnique()
            .HasDatabaseName("UX_PayrollLaborInsuranceEvidence_Snapshot_Kind");
        builder.HasOne(x => x.PayrollLaborInsuranceSnapshot)
            .WithMany(x => x.Contributions)
            .HasForeignKey(x => x.PayrollLaborInsuranceSnapshotId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
