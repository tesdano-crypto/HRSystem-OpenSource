using HRSystem.Domain.Attendance;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRSystem.Infrastructure.Persistence.Configurations;

public sealed class AttendanceRawEventConfiguration
    : IEntityTypeConfiguration<AttendanceRawEvent>
{
    public void Configure(EntityTypeBuilder<AttendanceRawEvent> builder)
    {
        builder.ToTable("AttendanceRawEvents", table =>
        {
            table.HasCheckConstraint(
                "CK_AttendanceRawEvents_ExternalEventId_Positive",
                "[ExternalEventId] > 0");
        });
        builder.HasKey(rawEvent => rawEvent.Id);
        builder.Property(rawEvent => rawEvent.Id).ValueGeneratedNever();
        builder.Property(rawEvent => rawEvent.SourceSystem)
            .HasMaxLength(50)
            .UseCollation("Latin1_General_100_BIN2")
            .IsRequired();
        builder.Property(rawEvent => rawEvent.SourcePersonPin)
            .HasMaxLength(20)
            .UseCollation("Latin1_General_100_BIN2")
            .IsRequired();
        builder.Property(rawEvent => rawEvent.DeviceSerialNumber)
            .HasMaxLength(20)
            .UseCollation("Latin1_General_100_BIN2");
        builder.Property(rawEvent => rawEvent.EventLocalDateTime)
            .HasColumnType("datetime2(7)")
            .IsRequired();
        builder.Property(rawEvent => rawEvent.SourceCreatedTime)
            .HasColumnType("datetime2(7)");
        builder.Property(rawEvent => rawEvent.SourceFingerprintVersion)
            .HasColumnType("smallint")
            .IsRequired();
        builder.Property(rawEvent => rawEvent.SourceFingerprint)
            .HasColumnType("binary(32)")
            .IsFixedLength()
            .IsRequired();
        builder.Property(rawEvent => rawEvent.ImportedAtUtc)
            .HasColumnType("datetimeoffset")
            .IsRequired();
        builder.Property(rawEvent => rawEvent.RowVersion).IsRowVersion();
        builder.HasOne(rawEvent => rawEvent.Employee)
            .WithMany(employee => employee.AttendanceRawEvents)
            .HasForeignKey(rawEvent => rawEvent.EmployeeId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasIndex(rawEvent => new
            {
                rawEvent.SourceSystem,
                rawEvent.ExternalEventId
            })
            .IsUnique()
            .HasDatabaseName(
                "UX_AttendanceRawEvents_SourceSystem_ExternalEventId");
        builder.HasIndex(rawEvent => new
            {
                rawEvent.SourceSystem,
                rawEvent.SourceFingerprintVersion,
                rawEvent.SourceFingerprint
            })
            .IsUnique()
            .HasDatabaseName(
                "UX_AttendanceRawEvents_SourceSystem_FingerprintVersion_Fingerprint");
        builder.HasIndex(rawEvent => new
            {
                rawEvent.EmployeeId,
                rawEvent.EventLocalDateTime
            })
            .HasDatabaseName(
                "IX_AttendanceRawEvents_EmployeeId_EventLocalDateTime");
        builder.HasIndex(rawEvent => new
            {
                rawEvent.SourceSystem,
                rawEvent.SourcePersonPin,
                rawEvent.EventLocalDateTime
            })
            .HasFilter("[EmployeeId] IS NULL")
            .HasDatabaseName(
                "IX_AttendanceRawEvents_UnmappedPin_EventLocalDateTime");
        builder.HasIndex(rawEvent => new
            {
                rawEvent.SourceSystem,
                rawEvent.IsSourceMissing,
                rawEvent.ExternalEventId
            })
            .HasDatabaseName(
                "IX_AttendanceRawEvents_SourceMissing_ExternalEventId");
    }
}
