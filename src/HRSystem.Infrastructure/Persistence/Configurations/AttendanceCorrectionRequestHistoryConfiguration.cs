using HRSystem.Domain.Attendance;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRSystem.Infrastructure.Persistence.Configurations;

public sealed class AttendanceCorrectionRequestHistoryConfiguration :
    IEntityTypeConfiguration<AttendanceCorrectionRequestHistory>
{
    public void Configure(
        EntityTypeBuilder<AttendanceCorrectionRequestHistory> builder)
    {
        builder.ToTable("AttendanceCorrectionRequestHistories", table =>
        {
            table.HasCheckConstraint(
                "CK_AttendanceCorrectionRequestHistories_Action",
                "[Action] IN (1, 2, 3, 4, 5, 6, 7)");
            table.HasCheckConstraint(
                "CK_AttendanceCorrectionRequestHistories_ToStatus",
                "[ToStatus] IN (1, 2, 3, 4, 5)");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Action).HasConversion<byte>().IsRequired();
        builder.Property(x => x.FromStatus).HasConversion<byte?>();
        builder.Property(x => x.ToStatus).HasConversion<byte>().IsRequired();
        builder.Property(x => x.ActorUserId).HasMaxLength(450).IsRequired();
        builder.Property(x => x.Note).HasMaxLength(1000);
        builder.Property(x => x.OccurredAtUtc).HasColumnType("datetimeoffset").IsRequired();
        builder.HasIndex(x => new { x.RequestId, x.OccurredAtUtc })
            .HasDatabaseName("IX_AttendanceCorrectionRequestHistories_RequestId_OccurredAtUtc");
        builder.HasOne(x => x.Request).WithMany(x => x.Histories)
            .HasForeignKey(x => x.RequestId).OnDelete(DeleteBehavior.NoAction);
    }
}
