using HRSystem.Domain.Attendance;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRSystem.Infrastructure.Persistence.Configurations;

public sealed class AttendanceSyncStateConfiguration
    : IEntityTypeConfiguration<AttendanceSyncState>
{
    public void Configure(EntityTypeBuilder<AttendanceSyncState> builder)
    {
        builder.ToTable("AttendanceSyncStates", table =>
        {
            table.HasCheckConstraint(
                "CK_AttendanceSyncStates_LastExternalEventId",
                "[LastExternalEventId] >= 0");
            table.HasCheckConstraint(
                "CK_AttendanceSyncStates_LastImportedCount",
                "[LastImportedCount] >= 0");
        });
        builder.HasKey(state => state.SourceSystem);
        builder.Property(state => state.SourceSystem)
            .HasMaxLength(50)
            .UseCollation("Latin1_General_100_BIN2")
            .ValueGeneratedNever();
        builder.Property(state => state.LastAttemptAtUtc)
            .HasColumnType("datetimeoffset")
            .IsRequired();
        builder.Property(state => state.LastSuccessfulSyncAtUtc)
            .HasColumnType("datetimeoffset");
        builder.Property(state => state.LastErrorSummary)
            .HasMaxLength(500);
        builder.Property(state => state.RowVersion).IsRowVersion();
    }
}
