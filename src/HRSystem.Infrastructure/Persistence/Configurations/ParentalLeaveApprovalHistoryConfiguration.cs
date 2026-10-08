using HRSystem.Domain.ParentalLeave;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRSystem.Infrastructure.Persistence.Configurations;

public sealed class ParentalLeaveApprovalHistoryConfiguration :
    IEntityTypeConfiguration<ParentalLeaveApprovalHistory>
{
    public void Configure(EntityTypeBuilder<ParentalLeaveApprovalHistory> builder)
    {
        builder.ToTable("ParentalLeaveApprovalHistories");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Id).ValueGeneratedNever();
        builder.Property(item => item.Action).HasConversion<byte>();
        builder.Property(item => item.ActionByUserId).HasMaxLength(450).IsRequired();
        builder.Property(item => item.ActionByDisplayName).HasMaxLength(100).IsRequired();
        builder.Property(item => item.Comment).HasMaxLength(1000);
        builder.Property(item => item.ActionAtUtc).HasColumnType("datetimeoffset");
        builder.Property(item => item.FromStatus).HasConversion<byte>();
        builder.Property(item => item.ToStatus).HasConversion<byte>();
        builder.HasIndex(item => new
            {
                item.ParentalLeaveRequestId,
                item.ActionAtUtc
            })
            .HasDatabaseName(
                "IX_ParentalLeaveApprovalHistories_Request_ActionAtUtc");
        builder.HasIndex(item => item.ActionByUserId)
            .HasDatabaseName("IX_ParentalLeaveApprovalHistories_ActionByUserId");
        builder.HasOne(item => item.ParentalLeaveRequest)
            .WithMany(item => item.ApprovalHistories)
            .HasForeignKey(item => item.ParentalLeaveRequestId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
