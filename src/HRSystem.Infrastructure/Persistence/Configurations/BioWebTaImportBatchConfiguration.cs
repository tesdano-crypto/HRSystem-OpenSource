using HRSystem.Domain.Attendance;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRSystem.Infrastructure.Persistence.Configurations;

public sealed class BioWebTaImportBatchConfiguration :
    IEntityTypeConfiguration<BioWebTaImportBatch>
{
    public void Configure(EntityTypeBuilder<BioWebTaImportBatch> builder)
    {
        builder.ToTable("BioWebTaImportBatches", table =>
        {
            table.HasCheckConstraint(
                "CK_BioWebTaImportBatches_QueryWindow",
                "[QueryToLocal] > [QueryFromLocal]");
            table.HasCheckConstraint(
                "CK_BioWebTaImportBatches_RowCounts",
                "[SourceRowCount] >= 0 AND [InsertedCount] >= 0 AND " +
                "[DuplicateCount] >= 0 AND [ConflictCount] >= 0 AND " +
                "[FailedCount] >= 0 AND " +
                "[InsertedCount] + [DuplicateCount] + [ConflictCount] + " +
                "[FailedCount] = [SourceRowCount]");
            table.HasCheckConstraint(
                "CK_BioWebTaImportBatches_ProcessId",
                "[ProcessId] > 0");
            table.HasCheckConstraint(
                "CK_BioWebTaImportBatches_TerminalState",
                "([Status] = 1 AND [CompletedAtUtc] IS NULL) OR " +
                "([Status] IN (2, 3, 4, 5) AND [CompletedAtUtc] IS NOT NULL)");
        });
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Id).ValueGeneratedNever();
        builder.Property(item => item.TriggerType).HasConversion<byte>();
        builder.Property(item => item.QueryFromLocal).HasColumnType("datetime2(7)");
        builder.Property(item => item.QueryToLocal).HasColumnType("datetime2(7)");
        builder.Property(item => item.TimeZoneId).HasMaxLength(64).IsRequired();
        builder.Property(item => item.StartedAtUtc).HasColumnType("datetimeoffset");
        builder.Property(item => item.CompletedAtUtc).HasColumnType("datetimeoffset");
        builder.Property(item => item.Status).HasConversion<byte>();
        builder.Property(item => item.ErrorSummary).HasMaxLength(1000);
        builder.Property(item => item.HostName).HasMaxLength(255).IsRequired();
        builder.Property(item => item.JobVersion).HasMaxLength(100).IsRequired();
        builder.Property(item => item.RowVersion).IsRowVersion();
        builder.HasIndex(item => new { item.Status, item.StartedAtUtc })
            .HasDatabaseName("IX_BioWebTaImportBatches_Status_StartedAtUtc");
        builder.HasIndex(item => new { item.QueryFromLocal, item.QueryToLocal })
            .HasDatabaseName("IX_BioWebTaImportBatches_QueryWindow");
    }
}
