using HRSystem.Domain.LeaveRequests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRSystem.Infrastructure.Persistence.Configurations;

public sealed class LeaveApprovalHistoryConfiguration : IEntityTypeConfiguration<LeaveApprovalHistory>
{
    public void Configure(EntityTypeBuilder<LeaveApprovalHistory> builder)
    {
        builder.ToTable("LeaveApprovalHistories");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Action).HasConversion<byte>().IsRequired();
        builder.Property(x => x.ActionByUserId).HasMaxLength(450).IsRequired();
        builder.Property(x => x.ActionByDisplayName).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Comment).HasMaxLength(1000);
        builder.Property(x => x.ActionAtUtc).HasColumnType("datetimeoffset").IsRequired();
        builder.Property(x => x.FromStatus).HasConversion<byte>().IsRequired();
        builder.Property(x => x.ToStatus).HasConversion<byte>().IsRequired();
        builder.HasIndex(x => new { x.LeaveRequestId, x.ActionAtUtc }).HasDatabaseName("IX_LeaveApprovalHistories_LeaveRequestId_ActionAtUtc");
        builder.HasIndex(x => x.ActionByUserId).HasDatabaseName("IX_LeaveApprovalHistories_ActionByUserId");
        builder.HasOne(x => x.LeaveRequest)
            .WithMany(x => x.ApprovalHistories)
            .HasForeignKey(x => x.LeaveRequestId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
