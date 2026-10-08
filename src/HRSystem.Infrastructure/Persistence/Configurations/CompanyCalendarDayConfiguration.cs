using HRSystem.Domain.CompanyCalendars;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRSystem.Infrastructure.Persistence.Configurations;

public sealed class CompanyCalendarDayConfiguration
    : IEntityTypeConfiguration<CompanyCalendarDay>
{
    public void Configure(EntityTypeBuilder<CompanyCalendarDay> builder)
    {
        builder.ToTable("CompanyCalendarDays", table =>
        {
            table.HasCheckConstraint(
                "CK_CompanyCalendarDays_DateYear",
                "DATEPART(year, [Date]) = [CalendarYear]");
            table.HasCheckConstraint(
                "CK_CompanyCalendarDays_BaseDayType",
                "[BaseDayType] IN (1, 2, 3, 4, 5)");
            table.HasCheckConstraint(
                "CK_CompanyCalendarDays_DayType",
                "[DayType] IN (1, 2, 3, 4, 5, 6, 7)");
            table.HasCheckConstraint(
                "CK_CompanyCalendarDays_OverrideConsistency",
                "([OverrideReason] IS NULL AND [DayType] = [BaseDayType]) OR " +
                "([OverrideReason] IS NOT NULL AND LEN(LTRIM(RTRIM([OverrideReason]))) > 0)");
            table.HasCheckConstraint(
                "CK_CompanyCalendarDays_NameRequired",
                "[DayType] IN (1, 2, 3) OR ([Name] IS NOT NULL AND LEN(LTRIM(RTRIM([Name]))) > 0)");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.CompanyCalendarYearId).IsRequired();
        builder.Property(x => x.CalendarYear).IsRequired();
        builder.Property(x => x.Date).HasColumnType("date").IsRequired();
        builder.Property(x => x.BaseDayType).HasConversion<byte>().IsRequired();
        builder.Property(x => x.BaseName).HasMaxLength(100);
        builder.Property(x => x.SourceNote).HasMaxLength(500);
        builder.Property(x => x.SourceReference).HasMaxLength(1000);
        builder.Property(x => x.DayType).HasConversion<byte>().IsRequired();
        builder.Property(x => x.Name).HasMaxLength(100);
        builder.Property(x => x.Description).HasMaxLength(500);
        builder.Property(x => x.OverrideReason).HasMaxLength(500);
        builder.Property(x => x.CreatedAtUtc).HasColumnType("datetimeoffset").IsRequired();
        builder.Property(x => x.CreatedBy).HasMaxLength(450).IsRequired();
        builder.Property(x => x.ModifiedAtUtc).HasColumnType("datetimeoffset").IsRequired();
        builder.Property(x => x.ModifiedBy).HasMaxLength(450).IsRequired();
        builder.Property(x => x.RowVersion).IsRowVersion();
        builder.Ignore(x => x.IsWorkingDay);
        builder.Ignore(x => x.IsManualOverride);
        builder.HasOne(x => x.Year)
            .WithMany(x => x.Days)
            .HasForeignKey(x => new { x.CompanyCalendarYearId, x.CalendarYear })
            .HasPrincipalKey(x => new { x.Id, x.Year })
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasIndex(x => new { x.CompanyCalendarYearId, x.Date })
            .IsUnique()
            .HasDatabaseName("UX_CompanyCalendarDays_Year_Date");
        builder.HasIndex(x => new { x.CalendarYear, x.Date })
            .HasDatabaseName("IX_CompanyCalendarDays_CalendarYear_Date");
    }
}
