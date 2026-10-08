using HRSystem.Domain.Overtime;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRSystem.Infrastructure.Persistence.Configurations;

public sealed class OvertimeRecognitionHistoryConfiguration :
    IEntityTypeConfiguration<OvertimeRecognitionHistory>
{
    public void Configure(EntityTypeBuilder<OvertimeRecognitionHistory> builder)
    {
        builder.ToTable("OvertimeRecognitionHistories", table =>
        {
            table.HasCheckConstraint("CK_OvertimeRecognitionHistories_Action",
                "[Action] IN (1, 2, 3, 4)");
            table.HasCheckConstraint("CK_OvertimeRecognitionHistories_ToStatus",
                "[ToStatus] IN (1, 2, 3, 4)");
        });
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Id).ValueGeneratedNever();
        builder.Property(item => item.Action).HasConversion<byte>().IsRequired();
        builder.Property(item => item.FromStatus).HasConversion<byte?>();
        builder.Property(item => item.ToStatus).HasConversion<byte>().IsRequired();
        builder.Property(item => item.ActorUserId).HasMaxLength(450).IsRequired();
        builder.Property(item => item.Reason).HasConversion<byte?>();
        builder.Property(item => item.Note).HasMaxLength(1000);
        builder.Property(item => item.OccurredAtUtc).HasColumnType("datetimeoffset").IsRequired();
        builder.HasIndex(item => new { item.RecognitionId, item.OccurredAtUtc })
            .HasDatabaseName("IX_OvertimeRecognitionHistories_RecognitionId_OccurredAtUtc");
        builder.HasOne(item => item.Recognition).WithMany(item => item.Histories)
            .HasForeignKey(item => item.RecognitionId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
