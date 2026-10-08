using HRSystem.Domain.CompTime;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRSystem.Infrastructure.Persistence.Configurations;

public sealed class CompTimeTransactionConfiguration :
    IEntityTypeConfiguration<CompTimeTransaction>
{
    public void Configure(EntityTypeBuilder<CompTimeTransaction> builder)
    {
        builder.ToTable("CompTimeTransactions", table =>
        {
            table.HasCheckConstraint(
                "CK_CompTimeTransactions_TransactionType",
                "[TransactionType] IN (1, 2, 3)");
            table.HasCheckConstraint(
                "CK_CompTimeTransactions_SourceType",
                "[SourceType] IN (1, 2, 3)");
            table.HasCheckConstraint(
                "CK_CompTimeTransactions_Hours",
                "[Hours] > 0 AND ([Hours] * 2) = FLOOR([Hours] * 2)");
            table.HasCheckConstraint(
                "CK_CompTimeTransactions_SourceShape",
                "([SourceType] = 1 AND [TransactionType] = 1 AND [SourceId] IS NULL) OR " +
                "([SourceType] = 2 AND [TransactionType] IN (2, 3) AND [SourceId] IS NOT NULL) OR " +
                "([SourceType] = 3 AND [TransactionType] = 1 AND [SourceId] IS NOT NULL)");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.TransactionType).HasConversion<byte>().IsRequired();
        builder.Property(x => x.Hours).HasPrecision(8, 2).IsRequired();
        builder.Property(x => x.EffectiveDate).HasColumnType("date").IsRequired();
        builder.Property(x => x.SourceType).HasConversion<byte>().IsRequired();
        builder.Property(x => x.Reason)
            .HasMaxLength(CompTimePolicy.ReasonMaxLength)
            .IsRequired();
        builder.Property(x => x.CreatedAtUtc)
            .HasColumnType("datetimeoffset")
            .IsRequired();
        builder.Property(x => x.CreatedBy)
            .HasMaxLength(CompTimePolicy.CreatedByMaxLength)
            .IsRequired();
        builder.Property(x => x.RowVersion).IsRowVersion();
        builder.Ignore(x => x.SignedHours);
        builder.HasOne(x => x.Employee)
            .WithMany()
            .HasForeignKey(x => x.EmployeeId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasIndex(x => new { x.EmployeeId, x.EffectiveDate })
            .HasDatabaseName("IX_CompTimeTransactions_EmployeeId_EffectiveDate");
        builder.HasIndex(x => new
            {
                x.EmployeeId,
                x.SourceType,
                x.EffectiveDate
            })
            .IsUnique()
            .HasFilter("[SourceType] = 1 AND [TransactionType] = 1")
            .HasDatabaseName("UX_CompTimeTransactions_LegacyOpeningBalance");
        builder.HasIndex(x => new
            {
                x.SourceType,
                x.SourceId,
                x.TransactionType
            })
            .IsUnique()
            .HasFilter("[SourceId] IS NOT NULL")
            .HasDatabaseName("UX_CompTimeTransactions_LeaveRequestAction");
    }
}
