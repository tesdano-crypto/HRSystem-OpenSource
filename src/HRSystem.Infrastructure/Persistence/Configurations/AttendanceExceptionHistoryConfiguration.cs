using HRSystem.Domain.AttendanceExceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRSystem.Infrastructure.Persistence.Configurations;

public sealed class AttendanceExceptionHistoryConfiguration : IEntityTypeConfiguration<AttendanceExceptionHistory>
{
    public void Configure(EntityTypeBuilder<AttendanceExceptionHistory> builder)
    {
        builder.ToTable("AttendanceExceptionHistories");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Action).HasConversion<byte>();
        builder.Property(x => x.ActionByUserId).HasMaxLength(450).IsRequired();
        builder.Property(x => x.ActionByDisplayName).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Comment).HasMaxLength(1000);
        builder.Property(x => x.ActionAtUtc).HasColumnType("datetimeoffset");
        builder.Property(x => x.FromStatus).HasConversion<byte>();
        builder.Property(x => x.ToStatus).HasConversion<byte>();
        builder.HasIndex(x => new { x.AttendanceExceptionId, x.ActionAtUtc }).HasDatabaseName("IX_AttendanceExceptionHistories_Request_ActionAtUtc");
        builder.HasIndex(x => x.ActionByUserId).HasDatabaseName("IX_AttendanceExceptionHistories_ActionByUserId");
        builder.HasOne(x => x.AttendanceException).WithMany(x => x.Histories).HasForeignKey(x => x.AttendanceExceptionId).OnDelete(DeleteBehavior.Restrict);
    }
}
