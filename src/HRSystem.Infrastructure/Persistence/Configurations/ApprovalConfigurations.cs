using HRSystem.Domain.Approvals;
using HRSystem.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRSystem.Infrastructure.Persistence.Configurations;

public sealed class ApprovalConfiguration : IEntityTypeConfiguration<Approval>
{
    public void Configure(EntityTypeBuilder<Approval> builder)
    {
        builder.ToTable("Approvals", table =>
        {
            table.HasCheckConstraint("CK_Approvals_Type", "[ApprovalType] IN (1)");
            table.HasCheckConstraint("CK_Approvals_Status", "[Status] IN (1, 2, 3, 4, 5)");
            table.HasCheckConstraint("CK_Approvals_NotificationStatus", "[NotificationStatus] IN (1, 2, 3)");
            table.HasCheckConstraint("CK_Approvals_SourceFingerprintVersion", "[SourceFingerprintVersion] > 0");
            table.HasCheckConstraint("CK_Approvals_SourceFingerprint", "DATALENGTH([SourceFingerprint]) = 32");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.ApprovalType).HasConversion<byte>();
        builder.Property(x => x.SourceEntityType).HasMaxLength(100).IsRequired();
        builder.Property(x => x.SourceEntityId).HasMaxLength(128).IsRequired();
        builder.Property(x => x.SourceFingerprintVersion).HasColumnType("smallint");
        builder.Property(x => x.SourceFingerprint).HasColumnType("binary(32)").IsRequired();
        builder.Property(x => x.Title).HasMaxLength(200).IsRequired();
        builder.Property(x => x.SummaryJson).HasMaxLength(4000).IsRequired();
        builder.Property(x => x.RequestedByUserId).HasMaxLength(450).IsRequired();
        builder.Property(x => x.ApproverUserId).HasMaxLength(450).IsRequired();
        builder.Property(x => x.Status).HasConversion<byte>();
        builder.Property(x => x.DecisionChannel).HasConversion<byte?>();
        builder.Property(x => x.DecisionByUserId).HasMaxLength(450);
        builder.Property(x => x.DecisionReason).HasMaxLength(1000);
        builder.Property(x => x.NotificationStatus).HasConversion<byte>();
        builder.Property(x => x.NotificationErrorSummary).HasMaxLength(500);
        builder.Property(x => x.RequestedAtUtc).HasColumnType("datetimeoffset");
        builder.Property(x => x.DecisionAtUtc).HasColumnType("datetimeoffset");
        builder.Property(x => x.NotificationAttemptedAtUtc).HasColumnType("datetimeoffset");
        builder.Property(x => x.CreatedAtUtc).HasColumnType("datetimeoffset");
        builder.Property(x => x.UpdatedAtUtc).HasColumnType("datetimeoffset");
        builder.Property(x => x.RowVersion).IsRowVersion();
        builder.HasIndex(x => new
            {
                x.ApprovalType,
                x.SourceEntityType,
                x.SourceEntityId,
                x.SourceFingerprintVersion
            })
            .IsUnique()
            .HasFilter("[Status] = 1")
            .HasDatabaseName("UX_Approvals_PendingSourceVersion");
        builder.HasIndex(x => new { x.ApproverUserId, x.Status, x.RequestedAtUtc })
            .HasDatabaseName("IX_Approvals_Approver_Status_RequestedAt");
        builder.HasIndex(x => new { x.RequestedByUserId, x.RequestedAtUtc })
            .HasDatabaseName("IX_Approvals_Requester_RequestedAt");
        builder.HasOne<ApplicationUser>().WithMany()
            .HasForeignKey(x => x.RequestedByUserId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<ApplicationUser>().WithMany()
            .HasForeignKey(x => x.ApproverUserId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<ApplicationUser>().WithMany()
            .HasForeignKey(x => x.DecisionByUserId).OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class ApprovalHistoryConfiguration : IEntityTypeConfiguration<ApprovalHistory>
{
    public void Configure(EntityTypeBuilder<ApprovalHistory> builder)
    {
        builder.ToTable("ApprovalHistories", table =>
        {
            table.HasCheckConstraint("CK_ApprovalHistories_Action", "[Action] IN (1, 2, 3, 4, 5, 6, 7, 8)");
            table.HasCheckConstraint("CK_ApprovalHistories_Channel", "[Channel] IN (1, 2, 3)");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Action).HasConversion<byte>();
        builder.Property(x => x.Channel).HasConversion<byte>();
        builder.Property(x => x.ActorUserId).HasMaxLength(450);
        builder.Property(x => x.ActionAtUtc).HasColumnType("datetimeoffset");
        builder.Property(x => x.Comment).HasMaxLength(1000);
        builder.Property(x => x.SafeMetadataJson).HasMaxLength(2000);
        builder.HasIndex(x => new { x.ApprovalId, x.ActionAtUtc })
            .HasDatabaseName("IX_ApprovalHistories_Approval_ActionAt");
        builder.HasOne(x => x.Approval).WithMany(x => x.Histories)
            .HasForeignKey(x => x.ApprovalId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<ApplicationUser>().WithMany()
            .HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class ApprovalLineActionTokenConfiguration :
    IEntityTypeConfiguration<ApprovalLineActionToken>
{
    public void Configure(EntityTypeBuilder<ApprovalLineActionToken> builder)
    {
        builder.ToTable("ApprovalLineActionTokens", table =>
        {
            table.HasCheckConstraint("CK_ApprovalLineActionTokens_Action", "[Action] IN (1, 2)");
            table.HasCheckConstraint("CK_ApprovalLineActionTokens_Hash", "DATALENGTH([TokenHash]) = 32");
            table.HasCheckConstraint("CK_ApprovalLineActionTokens_Expiry", "[ExpiresAtUtc] > [CreatedAtUtc]");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.TokenHash).HasColumnType("binary(32)").IsRequired();
        builder.Property(x => x.Action).HasConversion<byte>();
        builder.Property(x => x.IntendedApproverUserId).HasMaxLength(450).IsRequired();
        builder.Property(x => x.LineUserId).HasMaxLength(100).IsRequired();
        builder.Property(x => x.ExpiresAtUtc).HasColumnType("datetimeoffset");
        builder.Property(x => x.ConsumedAtUtc).HasColumnType("datetimeoffset");
        builder.Property(x => x.RevokedAtUtc).HasColumnType("datetimeoffset");
        builder.Property(x => x.CreatedAtUtc).HasColumnType("datetimeoffset");
        builder.HasIndex(x => x.TokenHash).IsUnique()
            .HasDatabaseName("UX_ApprovalLineActionTokens_TokenHash");
        builder.HasIndex(x => new { x.ApprovalId, x.ConsumedAtUtc, x.RevokedAtUtc })
            .HasDatabaseName("IX_ApprovalLineActionTokens_Approval_Active");
        builder.HasOne(x => x.Approval).WithMany(x => x.LineActionTokens)
            .HasForeignKey(x => x.ApprovalId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<ApplicationUser>().WithMany()
            .HasForeignKey(x => x.IntendedApproverUserId).OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class LineUserBindingConfiguration : IEntityTypeConfiguration<LineUserBinding>
{
    public void Configure(EntityTypeBuilder<LineUserBinding> builder)
    {
        builder.ToTable("LineUserBindings", table =>
        {
            table.HasCheckConstraint("CK_LineUserBindings_TargetType",
                "[TargetType] = 1");
            table.HasCheckConstraint("CK_LineUserBindings_RevocationState",
                "([IsActive] = 1 AND [RevokedAtUtc] IS NULL) OR " +
                "([IsActive] = 0 AND [RevokedAtUtc] IS NOT NULL)");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.HrSystemUserId).HasMaxLength(450).IsRequired();
        builder.Property(x => x.LineUserId).HasMaxLength(100).IsRequired();
        builder.Property(x => x.TargetType).HasConversion<byte>()
            .HasDefaultValue(LineBindingTargetType.User);
        builder.Property(x => x.VerifiedAtUtc).HasColumnType("datetimeoffset");
        builder.Property(x => x.RevokedAtUtc).HasColumnType("datetimeoffset");
        builder.Property(x => x.CreatedAtUtc).HasColumnType("datetimeoffset");
        builder.Property(x => x.UpdatedAtUtc).HasColumnType("datetimeoffset");
        builder.Property(x => x.RowVersion).IsRowVersion();
        builder.HasIndex(x => x.HrSystemUserId).IsUnique()
            .HasFilter("[IsActive] = 1")
            .HasDatabaseName("UX_LineUserBindings_HRSystemUser_Active");
        builder.HasIndex(x => x.LineUserId).IsUnique()
            .HasFilter("[IsActive] = 1")
            .HasDatabaseName("UX_LineUserBindings_LineUser_Active");
        builder.HasOne<ApplicationUser>().WithMany()
            .HasForeignKey(x => x.HrSystemUserId).OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class LinePairingRequestConfiguration :
    IEntityTypeConfiguration<LinePairingRequest>
{
    public void Configure(EntityTypeBuilder<LinePairingRequest> builder)
    {
        builder.ToTable("LinePairingRequests", table =>
        {
            table.HasCheckConstraint("CK_LinePairingRequests_Purpose", "[Purpose] = 1");
            table.HasCheckConstraint("CK_LinePairingRequests_Hash",
                "DATALENGTH([TokenHash]) = 32");
            table.HasCheckConstraint("CK_LinePairingRequests_Expiry",
                "[ExpiresAtUtc] > [CreatedAtUtc]");
            table.HasCheckConstraint("CK_LinePairingRequests_FinalState",
                "[ConsumedAtUtc] IS NULL OR [RevokedAtUtc] IS NULL");
            table.HasCheckConstraint("CK_LinePairingRequests_Replacement",
                "([ReplacesExistingBinding] = 0 AND [ReplacementReason] IS NULL) OR " +
                "([ReplacesExistingBinding] = 1 AND LEN(LTRIM(RTRIM([ReplacementReason]))) > 0)");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.HrSystemUserId).HasMaxLength(450).IsRequired();
        builder.Property(x => x.TokenHash).HasColumnType("binary(32)").IsRequired();
        builder.Property(x => x.Purpose).HasConversion<byte>();
        builder.Property(x => x.ExpiresAtUtc).HasColumnType("datetimeoffset");
        builder.Property(x => x.ConsumedAtUtc).HasColumnType("datetimeoffset");
        builder.Property(x => x.RevokedAtUtc).HasColumnType("datetimeoffset");
        builder.Property(x => x.ReplacementReason).HasMaxLength(1000);
        builder.Property(x => x.CreatedByUserId).HasMaxLength(450).IsRequired();
        builder.Property(x => x.CreatedAtUtc).HasColumnType("datetimeoffset");
        builder.Property(x => x.RowVersion).IsRowVersion();
        builder.HasIndex(x => x.TokenHash).IsUnique()
            .HasDatabaseName("UX_LinePairingRequests_TokenHash");
        builder.HasIndex(x => new
            { x.HrSystemUserId, x.ConsumedAtUtc, x.RevokedAtUtc, x.ExpiresAtUtc })
            .HasDatabaseName("IX_LinePairingRequests_User_Active");
        builder.HasOne<ApplicationUser>().WithMany()
            .HasForeignKey(x => x.HrSystemUserId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<ApplicationUser>().WithMany()
            .HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.NoAction);
    }
}
