using HRSystem.Domain.Attendance;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRSystem.Infrastructure.Persistence.Configurations;

public sealed class BioWebTaImportBatchIssueConfiguration :
    IEntityTypeConfiguration<BioWebTaImportBatchIssue>
{
    public void Configure(EntityTypeBuilder<BioWebTaImportBatchIssue> builder)
    {
        builder.ToTable("BioWebTaImportBatchIssues", table =>
        {
            table.HasCheckConstraint(
                "CK_BioWebTaImportBatchIssues_ExternalEventId",
                "[ExternalEventId] IS NULL OR [ExternalEventId] > 0");
            table.HasCheckConstraint(
                "CK_BioWebTaImportBatchIssues_FingerprintVersion",
                "[FingerprintVersion] = 1");
        });
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Id).ValueGeneratedNever();
        builder.Property(item => item.FingerprintVersion).HasColumnType("smallint");
        builder.Property(item => item.IncomingFingerprint)
            .HasColumnType("binary(32)")
            .IsFixedLength();
        builder.Property(item => item.ExistingFingerprint)
            .HasColumnType("binary(32)")
            .IsFixedLength();
        builder.Property(item => item.IssueCode).HasConversion<byte>();
        builder.Property(item => item.SafeSummary).HasMaxLength(500).IsRequired();
        builder.Property(item => item.CreatedAtUtc).HasColumnType("datetimeoffset");
        builder.Property(item => item.RowVersion).IsRowVersion();
        builder.HasOne(item => item.ImportBatch)
            .WithMany(batch => batch.Issues)
            .HasForeignKey(item => item.ImportBatchId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasIndex(item => new { item.ImportBatchId, item.IssueCode })
            .HasDatabaseName("IX_BioWebTaImportBatchIssues_Batch_IssueCode");
        builder.HasIndex(item => new { item.ImportBatchId, item.ExternalEventId })
            .HasDatabaseName("IX_BioWebTaImportBatchIssues_Batch_ExternalEventId");
    }
}
