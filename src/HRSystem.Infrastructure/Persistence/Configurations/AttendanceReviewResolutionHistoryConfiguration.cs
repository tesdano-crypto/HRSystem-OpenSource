using HRSystem.Domain.Attendance;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRSystem.Infrastructure.Persistence.Configurations;

public sealed class AttendanceReviewResolutionHistoryConfiguration :
    IEntityTypeConfiguration<AttendanceReviewResolutionHistory>
{
    public void Configure(EntityTypeBuilder<AttendanceReviewResolutionHistory> builder)
    {
        builder.ToTable("AttendanceReviewResolutionHistories", table =>
        {
            table.HasCheckConstraint(
                "CK_AttendanceReviewResolutionHistories_Action",
                "[Action] IN (1, 2, 3)");
            table.HasCheckConstraint(
                "CK_AttendanceReviewResolutionHistories_Status",
                "([FromStatus] IS NULL OR [FromStatus] IN (1, 2)) AND [ToStatus] IN (1, 2)");
            table.HasCheckConstraint(
                "CK_AttendanceReviewResolutionHistories_FingerprintLength",
                "DATALENGTH([SourceFingerprint]) = 32");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Action).HasConversion<byte>();
        builder.Property(x => x.FromStatus).HasConversion<byte?>();
        builder.Property(x => x.ToStatus).HasConversion<byte>();
        builder.Property(x => x.Reason).HasConversion<byte?>();
        builder.Property(x => x.Note).HasMaxLength(1000);
        builder.Property(x => x.ActorUserId).HasMaxLength(450).IsRequired();
        builder.Property(x => x.ActionAtUtc).HasColumnType("datetimeoffset");
        builder.Property(x => x.SourceFingerprint).HasColumnType("binary(32)").IsRequired();
        builder.HasIndex(x => new { x.AttendanceReviewResolutionId, x.ActionAtUtc })
            .HasDatabaseName("IX_AttendanceReviewResolutionHistories_Resolution_ActionAtUtc");
        builder.HasOne(x => x.AttendanceReviewResolution)
            .WithMany(x => x.Histories)
            .HasForeignKey(x => x.AttendanceReviewResolutionId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
