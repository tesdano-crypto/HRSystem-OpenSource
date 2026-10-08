using HRSystem.Domain.ParentalLeave;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRSystem.Infrastructure.Persistence.Configurations;

public sealed class ParentalLeaveRequestConfiguration :
    IEntityTypeConfiguration<ParentalLeaveRequest>
{
    public void Configure(EntityTypeBuilder<ParentalLeaveRequest> builder)
    {
        builder.ToTable("ParentalLeaveRequests", table =>
        {
            table.HasCheckConstraint(
                "CK_ParentalLeaveRequests_DateRange",
                "[EndDate] >= [StartDate]");
            table.HasCheckConstraint(
                "CK_ParentalLeaveRequests_ChildAge",
                "[ChildBirthDate] <= [StartDate]");
            table.HasCheckConstraint(
                "CK_ParentalLeaveRequests_ApplicationType",
                "[ApplicationType] IN (1, 2, 3)");
            table.HasCheckConstraint(
                "CK_ParentalLeaveRequests_NoticeType",
                "[NoticeType] IN (1, 2)");
            table.HasCheckConstraint(
                "CK_ParentalLeaveRequests_Status",
                "[Status] IN (1, 2, 3, 4, 5, 6, 7, 8)");
            table.HasCheckConstraint(
                "CK_ParentalLeaveRequests_EarlyReturnDate",
                "[EarlyReturnDate] IS NULL OR " +
                "([EarlyReturnDate] > [StartDate] AND [EarlyReturnDate] <= [EndDate])");
        });
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Id).ValueGeneratedNever();
        builder.Property(item => item.RequestNumber)
            .HasMaxLength(40)
            .UseCollation("Latin1_General_100_CI_AS")
            .IsRequired();
        builder.Property(item => item.ChildBirthDate).HasColumnType("date");
        builder.Property(item => item.ChildDisplayName).HasMaxLength(100);
        builder.Property(item => item.StartDate).HasColumnType("date");
        builder.Property(item => item.EndDate).HasColumnType("date");
        builder.Property(item => item.ApplicationType).HasConversion<byte>();
        builder.Property(item => item.NoticeType).HasConversion<byte>();
        builder.Property(item => item.RequestedAtUtc).HasColumnType("datetimeoffset");
        builder.Property(item => item.Reason).HasMaxLength(1000);
        builder.Property(item => item.ContactAddress).HasMaxLength(500).IsRequired();
        builder.Property(item => item.ContactPhone).HasMaxLength(30).IsRequired();
        builder.Property(item => item.EmergencyCareReason).HasMaxLength(1000);
        builder.Property(item => item.Status).HasConversion<byte>();
        builder.Property(item => item.SubmittedAtUtc).HasColumnType("datetimeoffset");
        builder.Property(item => item.ApprovedAtUtc).HasColumnType("datetimeoffset");
        builder.Property(item => item.RejectedAtUtc).HasColumnType("datetimeoffset");
        builder.Property(item => item.WithdrawnAtUtc).HasColumnType("datetimeoffset");
        builder.Property(item => item.CancellationRequestedAtUtc).HasColumnType("datetimeoffset");
        builder.Property(item => item.CancellationReason).HasMaxLength(1000);
        builder.Property(item => item.CancellationRequestedByUserId).HasMaxLength(450);
        builder.Property(item => item.CancelledAtUtc).HasColumnType("datetimeoffset");
        builder.Property(item => item.EarlyReturnDate).HasColumnType("date");
        builder.Property(item => item.EarlyReturnRequestedAtUtc).HasColumnType("datetimeoffset");
        builder.Property(item => item.EarlyReturnReason).HasMaxLength(1000);
        builder.Property(item => item.EarlyReturnRequestedByUserId).HasMaxLength(450);
        builder.Property(item => item.EarlyReturnApprovedAtUtc).HasColumnType("datetimeoffset");
        builder.Property(item => item.CompletedAtUtc).HasColumnType("datetimeoffset");
        builder.Property(item => item.CreatedAtUtc).HasColumnType("datetimeoffset");
        builder.Property(item => item.UpdatedAtUtc).HasColumnType("datetimeoffset");
        builder.Property(item => item.CreatedByUserId).HasMaxLength(450).IsRequired();
        builder.Property(item => item.UpdatedByUserId).HasMaxLength(450);
        builder.Property(item => item.RowVersion).IsRowVersion();
        builder.Ignore(item => item.CalendarDayCount);
        builder.Ignore(item => item.EffectiveEndDate);
        builder.HasIndex(item => item.RequestNumber)
            .IsUnique()
            .HasDatabaseName("UX_ParentalLeaveRequests_RequestNumber");
        builder.HasIndex(item => new { item.EmployeeId, item.StartDate, item.EndDate })
            .HasDatabaseName("IX_ParentalLeaveRequests_Employee_DateRange");
        builder.HasIndex(item => new { item.EmployeeId, item.ChildReferenceId, item.Status })
            .HasDatabaseName("IX_ParentalLeaveRequests_Employee_Child_Status");
        builder.HasIndex(item => new { item.Status, item.SubmittedAtUtc })
            .HasDatabaseName("IX_ParentalLeaveRequests_Status_SubmittedAtUtc");
        builder.HasOne(item => item.Employee)
            .WithMany()
            .HasForeignKey(item => item.EmployeeId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
