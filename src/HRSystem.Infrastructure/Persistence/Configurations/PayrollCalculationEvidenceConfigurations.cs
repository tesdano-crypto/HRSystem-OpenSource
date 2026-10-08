using HRSystem.Domain.Payroll;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRSystem.Infrastructure.Persistence.Configurations;

public sealed class PayrollLeaveDeductionPolicyConfiguration :
    IEntityTypeConfiguration<PayrollLeaveDeductionPolicy>
{
    public void Configure(EntityTypeBuilder<PayrollLeaveDeductionPolicy> builder)
    {
        builder.ToTable("PayrollLeaveDeductionPolicies", table =>
        {
            table.HasCheckConstraint("CK_PayrollLeaveDeductionPolicies_Rate",
                "[DeductionRate] BETWEEN 0 AND 1");
            table.HasCheckConstraint("CK_PayrollLeaveDeductionPolicies_Range",
                "[EffectiveTo] IS NULL OR [EffectiveTo] >= [EffectiveFrom]");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.LeaveTypeCode).HasMaxLength(50)
            .UseCollation("Latin1_General_100_CI_AS").IsRequired();
        builder.Property(x => x.DeductionRate).HasPrecision(9, 6);
        builder.Property(x => x.CalculationBasis).HasConversion<short>().HasColumnType("smallint");
        builder.Property(x => x.EffectiveFrom).HasColumnType("date");
        builder.Property(x => x.EffectiveTo).HasColumnType("date");
        builder.Property(x => x.RowVersion).IsRowVersion();
        builder.HasIndex(x => new { x.LeaveTypeCode, x.EffectiveFrom }).IsUnique()
            .HasDatabaseName("UX_PayrollLeaveDeductionPolicies_Code_From");
        builder.HasIndex(x => new { x.IsActive, x.EffectiveFrom, x.EffectiveTo })
            .HasDatabaseName("IX_PayrollLeaveDeductionPolicies_EffectiveLookup");
        builder.HasData(
            new { Id = Guid.Parse("54000000-0000-0000-0000-000000000001"),
                LeaveTypeCode = "PERSONAL", DeductionRate = 1m,
                CalculationBasis = PayrollLeaveDeductionBasis.BaseSalaryOnly,
                EffectiveFrom = PayrollSeedIds.EffectiveFrom, EffectiveTo = (DateOnly?)null,
                IsActive = true },
            new { Id = Guid.Parse("54000000-0000-0000-0000-000000000002"),
                LeaveTypeCode = "SICK", DeductionRate = .5m,
                CalculationBasis = PayrollLeaveDeductionBasis.BaseSalaryOnly,
                EffectiveFrom = PayrollSeedIds.EffectiveFrom, EffectiveTo = (DateOnly?)null,
                IsActive = true });
    }
}

public sealed class PayrollAttendanceAllowanceSnapshotConfiguration :
    IEntityTypeConfiguration<PayrollAttendanceAllowanceSnapshot>
{
    public void Configure(EntityTypeBuilder<PayrollAttendanceAllowanceSnapshot> builder)
    {
        builder.ToTable("PayrollAttendanceAllowanceSnapshots", table =>
        {
            table.HasCheckConstraint("CK_PayrollAttendanceAllowanceSnapshots_Days",
                "[EmploymentPayableDays] BETWEEN 0 AND 31 AND [EligibleDays] BETWEEN 0 AND 30 AND [IneligibleDays] BETWEEN 0 AND 30");
            table.HasCheckConstraint("CK_PayrollAttendanceAllowanceSnapshots_FingerprintVersion",
                "[SourceFingerprintVersion] = 1");
        });
        builder.HasKey(x => x.Id); builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.FullMonthlyAmount).HasPrecision(18, 2);
        builder.Property(x => x.EmploymentProratedMaximum).HasPrecision(18, 6);
        builder.Property(x => x.RawCalculatedAmount).HasPrecision(18, 6);
        builder.Property(x => x.SourceFingerprintVersion).HasColumnType("smallint");
        builder.Property(x => x.SourceFingerprint).HasColumnType("binary(32)").IsRequired();
        builder.HasIndex(x => x.PayrollEmployeeSnapshotComponentId).IsUnique()
            .HasDatabaseName("UX_PayrollAttendanceAllowanceSnapshots_Component");
        builder.HasOne(x => x.SnapshotComponent).WithOne(x => x.AttendanceAllowanceSnapshot)
            .HasForeignKey<PayrollAttendanceAllowanceSnapshot>(x => x.PayrollEmployeeSnapshotComponentId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class PayrollAttendanceAllowanceEvidenceConfiguration :
    IEntityTypeConfiguration<PayrollAttendanceAllowanceEvidence>
{
    public void Configure(EntityTypeBuilder<PayrollAttendanceAllowanceEvidence> builder)
    {
        builder.ToTable("PayrollAttendanceAllowanceEvidence", table =>
            table.HasCheckConstraint("CK_PayrollAttendanceAllowanceEvidence_Reasons", "[Reasons] > 0"));
        builder.HasKey(x => x.Id); builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.WorkDate).HasColumnType("date");
        builder.Property(x => x.Reasons).HasConversion<int>();
        builder.HasIndex(x => new { x.PayrollAttendanceAllowanceSnapshotId, x.WorkDate }).IsUnique()
            .HasDatabaseName("UX_PayrollAttendanceAllowanceEvidence_Snapshot_Date");
        builder.HasOne(x => x.Snapshot).WithMany(x => x.Evidence)
            .HasForeignKey(x => x.PayrollAttendanceAllowanceSnapshotId).OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class PayrollLeaveDeductionSnapshotConfiguration :
    IEntityTypeConfiguration<PayrollLeaveDeductionSnapshot>
{
    public void Configure(EntityTypeBuilder<PayrollLeaveDeductionSnapshot> builder)
    {
        builder.ToTable("PayrollLeaveDeductionSnapshots", table =>
            table.HasCheckConstraint("CK_PayrollLeaveDeductionSnapshots_FingerprintVersion",
                "[SourceFingerprintVersion] = 1"));
        builder.HasKey(x => x.Id); builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.FullMonthlyBaseAmount).HasPrecision(18, 2);
        builder.Property(x => x.PersonalLeaveRawAmount).HasPrecision(18, 6);
        builder.Property(x => x.SickLeaveRawAmount).HasPrecision(18, 6);
        builder.Property(x => x.TotalRawAmount).HasPrecision(18, 6);
        builder.Property(x => x.SourceFingerprintVersion).HasColumnType("smallint");
        builder.Property(x => x.SourceFingerprint).HasColumnType("binary(32)").IsRequired();
        builder.HasIndex(x => x.PayrollEmployeeSnapshotComponentId).IsUnique()
            .HasDatabaseName("UX_PayrollLeaveDeductionSnapshots_Component");
        builder.HasOne(x => x.SnapshotComponent).WithOne(x => x.LeaveDeductionSnapshot)
            .HasForeignKey<PayrollLeaveDeductionSnapshot>(x => x.PayrollEmployeeSnapshotComponentId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class PayrollLeaveDeductionEvidenceConfiguration :
    IEntityTypeConfiguration<PayrollLeaveDeductionEvidence>
{
    public void Configure(EntityTypeBuilder<PayrollLeaveDeductionEvidence> builder)
    {
        builder.ToTable("PayrollLeaveDeductionEvidence", table =>
        {
            table.HasCheckConstraint("CK_PayrollLeaveDeductionEvidence_Minutes",
                "[LeaveMinutes] > 0 AND [ScheduledMinutes] >= 0");
            table.HasCheckConstraint("CK_PayrollLeaveDeductionEvidence_Rate",
                "[DeductionRate] IS NULL OR [DeductionRate] BETWEEN 0 AND 1");
        });
        builder.HasKey(x => x.Id); builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.WorkDate).HasColumnType("date");
        builder.Property(x => x.LeaveTypeCode).HasMaxLength(50)
            .UseCollation("Latin1_General_100_CI_AS").IsRequired();
        builder.Property(x => x.DeductionRate).HasPrecision(9, 6);
        builder.Property(x => x.RawAmount).HasPrecision(18, 6);
        builder.HasIndex(x => new { x.PayrollLeaveDeductionSnapshotId, x.WorkDate, x.LeaveTypeCode })
            .HasDatabaseName("IX_PayrollLeaveDeductionEvidence_Snapshot_Date_Code");
        builder.HasOne(x => x.Snapshot).WithMany(x => x.Evidence)
            .HasForeignKey(x => x.PayrollLeaveDeductionSnapshotId).OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class PayrollOvertimePaySnapshotConfiguration :
    IEntityTypeConfiguration<PayrollOvertimePaySnapshot>
{
    public void Configure(EntityTypeBuilder<PayrollOvertimePaySnapshot> builder)
    {
        builder.ToTable("PayrollOvertimePaySnapshots", table =>
        {
            table.HasCheckConstraint("CK_PayrollOvertimePaySnapshots_Minutes", "[TotalRecognizedMinutes] >= 0");
            table.HasCheckConstraint("CK_PayrollOvertimePaySnapshots_FingerprintVersion", "[SourceFingerprintVersion] = 1");
        });
        builder.HasKey(x => x.Id); builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.MonthlyOvertimeBase).HasPrecision(18, 2);
        builder.Property(x => x.HourlyBase).HasPrecision(18, 6);
        builder.Property(x => x.TotalOvertimePay).HasPrecision(18, 2);
        builder.Property(x => x.CalculationStatus).HasConversion<byte>();
        builder.Property(x => x.SourceFingerprintVersion).HasColumnType("smallint");
        builder.Property(x => x.SourceFingerprint).HasColumnType("binary(32)").IsRequired();
        builder.HasIndex(x => x.PayrollEmployeeSnapshotId).IsUnique()
            .HasDatabaseName("UX_PayrollOvertimePaySnapshots_EmployeeSnapshot");
        builder.HasOne(x => x.EmployeeSnapshot).WithOne(x => x.OvertimePaySnapshot)
            .HasForeignKey<PayrollOvertimePaySnapshot>(x => x.PayrollEmployeeSnapshotId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class PayrollOvertimeBaseComponentEvidenceConfiguration :
    IEntityTypeConfiguration<PayrollOvertimeBaseComponentEvidence>
{
    public void Configure(EntityTypeBuilder<PayrollOvertimeBaseComponentEvidence> builder)
    {
        builder.ToTable("PayrollOvertimeBaseComponentEvidence");
        builder.HasKey(x => x.Id); builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.ComponentCode).HasMaxLength(50).IsRequired();
        builder.Property(x => x.FullMonthlyAmount).HasPrecision(18, 2);
        builder.Property(x => x.EffectiveSourceDate).HasColumnType("date");
        builder.HasIndex(x => new { x.PayrollOvertimePaySnapshotId, x.PayrollComponentDefinitionId })
            .IsUnique().HasDatabaseName("UX_PayrollOvertimeBaseEvidence_Snapshot_Component");
        builder.HasOne(x => x.Snapshot).WithMany(x => x.IncludedComponents)
            .HasForeignKey(x => x.PayrollOvertimePaySnapshotId).OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class PayrollOvertimeBucketEvidenceConfiguration :
    IEntityTypeConfiguration<PayrollOvertimeBucketEvidence>
{
    public void Configure(EntityTypeBuilder<PayrollOvertimeBucketEvidence> builder)
    {
        builder.ToTable("PayrollOvertimeBucketEvidence", table =>
            table.HasCheckConstraint("CK_PayrollOvertimeBucketEvidence_Minutes", "[Minutes] >= 0"));
        builder.HasKey(x => x.Id); builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Bucket).HasConversion<byte>();
        builder.Property(x => x.Multiplier).HasPrecision(9, 4);
        builder.Property(x => x.RawPay).HasPrecision(18, 6);
        builder.Property(x => x.FinalPay).HasPrecision(18, 2);
        builder.Property(x => x.RatePolicyVersion).HasMaxLength(50).IsRequired();
        builder.HasIndex(x => new { x.PayrollOvertimePaySnapshotId, x.Bucket }).IsUnique()
            .HasDatabaseName("UX_PayrollOvertimeBucketEvidence_Snapshot_Bucket");
        builder.HasOne(x => x.Snapshot).WithMany(x => x.Buckets)
            .HasForeignKey(x => x.PayrollOvertimePaySnapshotId).OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class PayrollOvertimeDayEvidenceConfiguration :
    IEntityTypeConfiguration<PayrollOvertimeDayEvidence>
{
    public void Configure(EntityTypeBuilder<PayrollOvertimeDayEvidence> builder)
    {
        builder.ToTable("PayrollOvertimeDayEvidence", table =>
            table.HasCheckConstraint("CK_PayrollOvertimeDayEvidence_Minutes",
                "[RecognizedMinutes] >= 0 AND [FirstTwoHoursMinutes] >= 0 AND [AfterTwoHoursMinutes] >= 0 AND [AfterEightHoursMinutes] >= 0"));
        builder.HasKey(x => x.Id); builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.WorkDate).HasColumnType("date");
        builder.HasIndex(x => new { x.PayrollOvertimePaySnapshotId, x.WorkDate }).IsUnique()
            .HasDatabaseName("UX_PayrollOvertimeDayEvidence_Snapshot_Date");
        builder.HasOne(x => x.Snapshot).WithMany(x => x.Days)
            .HasForeignKey(x => x.PayrollOvertimePaySnapshotId).OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class PayrollOvertimeRecognitionEvidenceConfiguration :
    IEntityTypeConfiguration<PayrollOvertimeRecognitionEvidence>
{
    public void Configure(EntityTypeBuilder<PayrollOvertimeRecognitionEvidence> builder)
    {
        builder.ToTable("PayrollOvertimeRecognitionEvidence");
        builder.HasKey(x => x.Id); builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.WorkDate).HasColumnType("date");
        builder.Property(x => x.RecognizedStartAt).HasColumnType("datetime2");
        builder.Property(x => x.RecognizedEndAt).HasColumnType("datetime2");
        builder.Property(x => x.RecognitionStatus).HasConversion<byte>();
        builder.Property(x => x.RecognitionSourceFingerprint).HasColumnType("binary(32)").IsRequired();
        builder.HasIndex(x => new { x.PayrollOvertimePaySnapshotId, x.RecognitionId }).IsUnique()
            .HasDatabaseName("UX_PayrollOvertimeRecognitionEvidence_Snapshot_Recognition");
        builder.HasOne(x => x.Snapshot).WithMany(x => x.Recognitions)
            .HasForeignKey(x => x.PayrollOvertimePaySnapshotId).OnDelete(DeleteBehavior.NoAction);
    }
}
