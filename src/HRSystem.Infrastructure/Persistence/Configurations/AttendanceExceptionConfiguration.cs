using HRSystem.Domain.AttendanceExceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRSystem.Infrastructure.Persistence.Configurations;

public sealed class AttendanceExceptionConfiguration : IEntityTypeConfiguration<AttendanceException>
{
    public void Configure(EntityTypeBuilder<AttendanceException> builder)
    {
        builder.ToTable("AttendanceExceptions", table =>
        {
            table.HasCheckConstraint("CK_AttendanceExceptions_Type", "[ExceptionType] = 1");
            table.HasCheckConstraint("CK_AttendanceExceptions_ReasonType", "[ReasonType] IN (1, 2, 3, 4)");
            table.HasCheckConstraint("CK_AttendanceExceptions_ImpactType", "[ImpactType] IN (1, 2, 3)");
            table.HasCheckConstraint("CK_AttendanceExceptions_Status", "[Status] IN (1, 2, 3, 4, 5, 6, 7)");
            table.HasCheckConstraint("CK_AttendanceExceptions_ImpactTimes",
                "([ImpactType] = 1 AND [ExemptFromTime] IS NULL AND [ExemptToTime] IS NULL) OR " +
                "([ImpactType] = 2 AND [ExemptFromTime] IS NULL AND [ExemptToTime] IS NOT NULL) OR " +
                "([ImpactType] = 3 AND [ExemptFromTime] IS NOT NULL AND [ExemptToTime] IS NULL)");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.RequestNumber).HasMaxLength(40).UseCollation("Latin1_General_100_CI_AS").IsRequired();
        builder.Property(x => x.ExceptionType).HasConversion<byte>();
        builder.Property(x => x.ReasonType).HasConversion<byte>();
        builder.Property(x => x.WorkDate).HasColumnType("date");
        builder.Property(x => x.ImpactType).HasConversion<byte>();
        builder.Property(x => x.ExemptFromTime).HasColumnType("time(0)");
        builder.Property(x => x.ExemptToTime).HasColumnType("time(0)");
        builder.Property(x => x.Reason).HasMaxLength(1000);
        builder.Property(x => x.Status).HasConversion<byte>();
        builder.Property(x => x.SubmittedAtUtc).HasColumnType("datetimeoffset");
        builder.Property(x => x.ApprovedAtUtc).HasColumnType("datetimeoffset");
        builder.Property(x => x.RejectedAtUtc).HasColumnType("datetimeoffset");
        builder.Property(x => x.WithdrawnAtUtc).HasColumnType("datetimeoffset");
        builder.Property(x => x.CancellationRequestedAtUtc).HasColumnType("datetimeoffset");
        builder.Property(x => x.CancellationReason).HasMaxLength(1000);
        builder.Property(x => x.CancellationRequestedByUserId).HasMaxLength(450);
        builder.Property(x => x.CancelledAtUtc).HasColumnType("datetimeoffset");
        builder.Property(x => x.CreatedAtUtc).HasColumnType("datetimeoffset");
        builder.Property(x => x.UpdatedAtUtc).HasColumnType("datetimeoffset");
        builder.Property(x => x.CreatedByUserId).HasMaxLength(450).IsRequired();
        builder.Property(x => x.UpdatedByUserId).HasMaxLength(450);
        builder.Property(x => x.RowVersion).IsRowVersion();
        builder.HasIndex(x => x.RequestNumber).IsUnique().HasDatabaseName("UX_AttendanceExceptions_RequestNumber");
        builder.HasIndex(x => new { x.EmployeeId, x.WorkDate, x.Status }).HasDatabaseName("IX_AttendanceExceptions_Employee_WorkDate_Status");
        builder.HasIndex(x => new { x.EmployeeId, x.WorkDate }).IsUnique().HasFilter("[Status] IN (1, 2, 3, 6)").HasDatabaseName("UX_AttendanceExceptions_Employee_WorkDate_Active");
        builder.HasIndex(x => new { x.Status, x.SubmittedAtUtc }).HasDatabaseName("IX_AttendanceExceptions_Status_SubmittedAtUtc");
        builder.HasOne(x => x.Employee).WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.NoAction);
    }
}
