using HRSystem.Domain.CompanyCalendars;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRSystem.Infrastructure.Persistence.Configurations;

public sealed class CompanyCalendarYearConfiguration
    : IEntityTypeConfiguration<CompanyCalendarYear>
{
    public void Configure(EntityTypeBuilder<CompanyCalendarYear> builder)
    {
        builder.ToTable("CompanyCalendarYears", table =>
        {
            table.HasCheckConstraint(
                "CK_CompanyCalendarYears_Year_Range",
                "[Year] >= 1 AND [Year] <= 9999");
            table.HasCheckConstraint(
                "CK_CompanyCalendarYears_Status",
                "[Status] IN (1, 2, 3)");
            table.HasCheckConstraint(
                "CK_CompanyCalendarYears_LifecycleMetadata",
                "([Status] = 1 AND [PublishedAtUtc] IS NULL AND [PublishedBy] IS NULL AND [ArchivedAtUtc] IS NULL AND [ArchivedBy] IS NULL) OR " +
                "([Status] = 2 AND [PublishedAtUtc] IS NOT NULL AND [PublishedBy] IS NOT NULL AND [ArchivedAtUtc] IS NULL AND [ArchivedBy] IS NULL) OR " +
                "([Status] = 3 AND [PublishedAtUtc] IS NOT NULL AND [PublishedBy] IS NOT NULL AND [ArchivedAtUtc] IS NOT NULL AND [ArchivedBy] IS NOT NULL)");
            table.HasCheckConstraint(
                "CK_CompanyCalendarYears_ManifestHash",
                "LEN([ManifestHash]) = 64 AND [ManifestHash] NOT LIKE '%[^0-9a-f]%' COLLATE Latin1_General_100_BIN2");
            table.HasCheckConstraint(
                "CK_CompanyCalendarYears_SourceContentHash",
                "[SourceContentHash] IS NULL OR (LEN([SourceContentHash]) = 64 AND [SourceContentHash] NOT LIKE '%[^0-9a-f]%' COLLATE Latin1_General_100_BIN2)");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.HasAlternateKey(x => new { x.Id, x.Year })
            .HasName("AK_CompanyCalendarYears_Id_Year");
        builder.Property(x => x.Status).HasConversion<byte>().IsRequired();
        builder.Property(x => x.SourceAuthority).HasMaxLength(200).IsRequired();
        builder.Property(x => x.SourceTitle).HasMaxLength(300).IsRequired();
        builder.Property(x => x.SourcePublishedDate).HasColumnType("date").IsRequired();
        builder.Property(x => x.SourceReference).HasMaxLength(1000).IsRequired();
        builder.Property(x => x.SourceDocumentIdentifier).HasMaxLength(200);
        builder.Property(x => x.SourceContentHash)
            .HasColumnType("char(64)")
            .UseCollation("Latin1_General_100_BIN2");
        builder.Property(x => x.ManifestVersion).HasMaxLength(50).IsRequired();
        builder.Property(x => x.ManifestHash)
            .HasColumnType("char(64)")
            .UseCollation("Latin1_General_100_BIN2")
            .IsRequired();
        builder.Property(x => x.PublishedAtUtc).HasColumnType("datetimeoffset");
        builder.Property(x => x.PublishedBy).HasMaxLength(450);
        builder.Property(x => x.ArchivedAtUtc).HasColumnType("datetimeoffset");
        builder.Property(x => x.ArchivedBy).HasMaxLength(450);
        builder.Property(x => x.CreatedAtUtc).HasColumnType("datetimeoffset").IsRequired();
        builder.Property(x => x.CreatedBy).HasMaxLength(450).IsRequired();
        builder.Property(x => x.ModifiedAtUtc).HasColumnType("datetimeoffset").IsRequired();
        builder.Property(x => x.ModifiedBy).HasMaxLength(450).IsRequired();
        builder.Property(x => x.RowVersion).IsRowVersion();
        builder.HasIndex(x => x.Year)
            .IsUnique()
            .HasDatabaseName("UX_CompanyCalendarYears_Year");
        builder.HasIndex(x => new { x.Status, x.Year })
            .HasDatabaseName("IX_CompanyCalendarYears_Status_Year");
    }
}
