using HRSystem.Domain.Attendance;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRSystem.Infrastructure.Persistence.Configurations;

public sealed class BioWebPersonMappingConfiguration
    : IEntityTypeConfiguration<BioWebPersonMapping>
{
    public void Configure(EntityTypeBuilder<BioWebPersonMapping> builder)
    {
        builder.ToTable("BioWebPersonMappings", table =>
        {
            table.HasCheckConstraint(
                "CK_BioWebPersonMappings_EffectivePeriod",
                "[EffectiveTo] IS NULL OR [EffectiveTo] > [EffectiveFrom]");
        });
        builder.HasKey(mapping => mapping.Id);
        builder.Property(mapping => mapping.Id).ValueGeneratedNever();
        builder.Property(mapping => mapping.EmployeeId).IsRequired();
        builder.Property(mapping => mapping.BioWebPin)
            .HasMaxLength(20)
            .UseCollation("Latin1_General_100_BIN2")
            .IsRequired();
        builder.Property(mapping => mapping.EffectiveFrom)
            .HasColumnType("datetime2(7)")
            .IsRequired();
        builder.Property(mapping => mapping.EffectiveTo)
            .HasColumnType("datetime2(7)");
        builder.Property(mapping => mapping.CreatedAtUtc)
            .HasColumnType("datetimeoffset")
            .IsRequired();
        builder.Property(mapping => mapping.UpdatedAtUtc)
            .HasColumnType("datetimeoffset")
            .IsRequired();
        builder.Property(mapping => mapping.RowVersion).IsRowVersion();
        builder.HasOne(mapping => mapping.Employee)
            .WithMany(employee => employee.BioWebPersonMappings)
            .HasForeignKey(mapping => mapping.EmployeeId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasIndex(mapping => new
            {
                mapping.EmployeeId,
                mapping.EffectiveFrom
            })
            .IsUnique()
            .HasDatabaseName(
                "UX_BioWebPersonMappings_EmployeeId_EffectiveFrom");
        builder.HasIndex(mapping => new
            {
                mapping.BioWebPin,
                mapping.EffectiveFrom
            })
            .IsUnique()
            .HasDatabaseName(
                "UX_BioWebPersonMappings_BioWebPin_EffectiveFrom");
        builder.HasIndex(mapping => new
            {
                mapping.EmployeeId,
                mapping.IsActive,
                mapping.EffectiveFrom,
                mapping.EffectiveTo
            })
            .HasDatabaseName(
                "IX_BioWebPersonMappings_Employee_Active_Period");
        builder.HasIndex(mapping => new
            {
                mapping.BioWebPin,
                mapping.IsActive,
                mapping.EffectiveFrom,
                mapping.EffectiveTo
            })
            .HasDatabaseName(
                "IX_BioWebPersonMappings_Pin_Active_Period");
    }
}
