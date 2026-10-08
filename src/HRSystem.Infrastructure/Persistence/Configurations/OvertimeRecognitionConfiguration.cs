using HRSystem.Domain.Overtime;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRSystem.Infrastructure.Persistence.Configurations;

public sealed class OvertimeRecognitionConfiguration :
    IEntityTypeConfiguration<OvertimeRecognition>
{
    public void Configure(EntityTypeBuilder<OvertimeRecognition> builder)
    {
        builder.ToTable("OvertimeRecognitions", table =>
        {
            table.HasCheckConstraint("CK_OvertimeRecognitions_Status",
                "[Status] IN (1, 2, 3, 4)");
            table.HasCheckConstraint("CK_OvertimeRecognitions_ApprovedRange",
                "[ApprovedEndAt] > [ApprovedStartAt]");
            table.HasCheckConstraint("CK_OvertimeRecognitions_SuggestedRange",
                "([SuggestedStartAt] IS NULL AND [SuggestedEndAt] IS NULL) OR " +
                "([SuggestedStartAt] IS NOT NULL AND [SuggestedEndAt] IS NOT NULL AND [SuggestedEndAt] >= [SuggestedStartAt])");
            table.HasCheckConstraint("CK_OvertimeRecognitions_RecognizedRange",
                "([RecognizedStartAt] IS NULL AND [RecognizedEndAt] IS NULL AND ([RecognizedMinutes] IS NULL OR [RecognizedMinutes] = 0)) OR " +
                "([RecognizedStartAt] IS NOT NULL AND [RecognizedEndAt] IS NOT NULL AND [RecognizedEndAt] > [RecognizedStartAt] AND " +
                "[RecognizedMinutes] = DATEDIFF(MINUTE, [RecognizedStartAt], [RecognizedEndAt]) AND [RecognizedMinutes] <= 1440)");
            table.HasCheckConstraint("CK_OvertimeRecognitions_Confirmed",
                "[Status] <> 2 OR ([RecognizedMinutes] IS NOT NULL AND [Reason] IS NOT NULL)");
        });
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Id).ValueGeneratedNever();
        builder.Property(item => item.WorkDate).HasColumnType("date").IsRequired();
        builder.Property(item => item.ApprovedStartAt).HasColumnType("datetime2(0)").IsRequired();
        builder.Property(item => item.ApprovedEndAt).HasColumnType("datetime2(0)").IsRequired();
        builder.Property(item => item.ObservedClockOutAt).HasColumnType("datetime2(0)");
        builder.Property(item => item.SuggestedStartAt).HasColumnType("datetime2(0)");
        builder.Property(item => item.SuggestedEndAt).HasColumnType("datetime2(0)");
        builder.Property(item => item.RecognizedStartAt).HasColumnType("datetime2(0)");
        builder.Property(item => item.RecognizedEndAt).HasColumnType("datetime2(0)");
        builder.Property(item => item.Status).HasConversion<byte>().IsRequired();
        builder.Property(item => item.Reason).HasConversion<byte?>();
        builder.Property(item => item.Note).HasMaxLength(1000);
        builder.Property(item => item.SourceFingerprint).HasColumnType("binary(32)").IsRequired();
        builder.Property(item => item.RecognizedByUserId).HasMaxLength(450);
        builder.Property(item => item.RecognizedAtUtc).HasColumnType("datetimeoffset");
        builder.Property(item => item.CreatedAtUtc).HasColumnType("datetimeoffset").IsRequired();
        builder.Property(item => item.UpdatedAtUtc).HasColumnType("datetimeoffset").IsRequired();
        builder.Property(item => item.RowVersion).IsRowVersion();
        builder.HasIndex(item => item.OvertimeRequestId).IsUnique()
            .HasDatabaseName("UX_OvertimeRecognitions_OvertimeRequestId");
        builder.HasIndex(item => new { item.Status, item.WorkDate })
            .HasDatabaseName("IX_OvertimeRecognitions_Status_WorkDate");
        builder.HasIndex(item => new { item.EmployeeId, item.WorkDate })
            .HasDatabaseName("IX_OvertimeRecognitions_EmployeeId_WorkDate");
        builder.HasOne(item => item.OvertimeRequest).WithOne(item => item.Recognition)
            .HasForeignKey<OvertimeRecognition>(item => item.OvertimeRequestId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(item => item.Employee).WithMany()
            .HasForeignKey(item => item.EmployeeId).OnDelete(DeleteBehavior.NoAction);
    }
}
