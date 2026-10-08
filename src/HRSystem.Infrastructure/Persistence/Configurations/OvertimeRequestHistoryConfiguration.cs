using HRSystem.Domain.Overtime;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRSystem.Infrastructure.Persistence.Configurations;

public sealed class OvertimeRequestHistoryConfiguration :
    IEntityTypeConfiguration<OvertimeRequestHistory>
{
    public void Configure(EntityTypeBuilder<OvertimeRequestHistory> builder)
    {
        builder.ToTable("OvertimeRequestHistories", table =>
        {
            table.HasCheckConstraint(
                "CK_OvertimeRequestHistories_Action",
                "[Action] IN (1, 2, 3, 4, 5, 6)");
            table.HasCheckConstraint(
                "CK_OvertimeRequestHistories_ToStatus",
                "[ToStatus] IN (1, 2, 3, 4, 5)");
        });
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Id).ValueGeneratedNever();
        builder.Property(item => item.Action).HasConversion<byte>().IsRequired();
        builder.Property(item => item.FromStatus).HasConversion<byte?>();
        builder.Property(item => item.ToStatus).HasConversion<byte>().IsRequired();
        builder.Property(item => item.ActorUserId).HasMaxLength(450).IsRequired();
        builder.Property(item => item.Note).HasMaxLength(1000);
        builder.Property(item => item.OccurredAtUtc).HasColumnType("datetimeoffset").IsRequired();
        builder.HasIndex(item => new { item.OvertimeRequestId, item.OccurredAtUtc })
            .HasDatabaseName("IX_OvertimeRequestHistories_RequestId_OccurredAtUtc");
        builder.HasOne(item => item.OvertimeRequest).WithMany(item => item.Histories)
            .HasForeignKey(item => item.OvertimeRequestId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
