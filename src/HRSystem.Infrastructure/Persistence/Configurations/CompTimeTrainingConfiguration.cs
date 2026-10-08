using HRSystem.Domain.CompTime;
using HRSystem.Domain.MasterData;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRSystem.Infrastructure.Persistence.Configurations;

public sealed class CompTimeTrainingConfiguration : IEntityTypeConfiguration<TrainingCompTimeGrant>,
    IEntityTypeConfiguration<TrainingCompTimeHistory>, IEntityTypeConfiguration<CompTimeLegacyPool>,
    IEntityTypeConfiguration<CompTimeLegacyMember>, IEntityTypeConfiguration<CompTimeLegacyReturn>,
    IEntityTypeConfiguration<CompTimeAllocation>, IEntityTypeConfiguration<CompTimeAllocationReturn>
{
    public void Configure(EntityTypeBuilder<TrainingCompTimeGrant> b)
    {
        b.ToTable("TrainingCompTimeGrants", t => {
            t.HasCheckConstraint("CK_TrainingCompTime_Hours", "[ApprovedHours] > 0 AND [ApprovedHours] * 2 = FLOOR([ApprovedHours] * 2)");
            t.HasCheckConstraint("CK_TrainingCompTime_Expiration", "[ExpirationDate] IS NULL OR [ExpirationDate] >= [TrainingDate]");
            t.HasCheckConstraint("CK_TrainingCompTime_Status", "([Status] = 1 AND [ApprovedBy] IS NULL AND [ApprovedAtUtc] IS NULL) OR ([Status] = 2 AND [ApprovedBy] IS NOT NULL AND [ApprovedAtUtc] IS NOT NULL AND [CalendarClassification] IS NOT NULL AND [CalendarVersion] IS NOT NULL)");
        });
        b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever();
        b.HasOne<Employee>().WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.NoAction);
        b.Property(x => x.ApprovedHours).HasPrecision(8, 2);
        b.Property(x => x.CourseOrReason).HasMaxLength(200); b.Property(x => x.CourseKey).HasMaxLength(200);
        b.Property(x => x.Notes).HasMaxLength(1000);
        b.Property(x => x.CreatedBy).HasMaxLength(450); b.Property(x => x.ApprovedBy).HasMaxLength(450);
        b.Property(x => x.CalendarVersion).HasMaxLength(100);
        b.Property(x => x.RowVersion).IsRowVersion();
        b.HasIndex(x => new { x.EmployeeId, x.TrainingDate, x.CourseKey }).IsUnique().HasDatabaseName("UX_TrainingCompTime_Source");
    }
    public void Configure(EntityTypeBuilder<TrainingCompTimeHistory> b)
    {
        b.ToTable("TrainingCompTimeHistories"); b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever();
        b.HasOne<TrainingCompTimeGrant>().WithMany().HasForeignKey(x => x.GrantId).OnDelete(DeleteBehavior.NoAction);
        b.Property(x => x.Actor).HasMaxLength(450); b.Property(x => x.Evidence).HasMaxLength(2000); b.Property(x => x.ReviewNote).HasMaxLength(1000);
    }
    public void Configure(EntityTypeBuilder<CompTimeLegacyPool> b)
    {
        b.ToTable("CompTimeLegacyPools", t => t.HasCheckConstraint("CK_CompTimeLegacyPool_Balance", "[GrantedHours] >= 0 AND [ConsumedHours] >= 0 AND [RestoredHours] >= 0 AND [GrantedHours] + [RestoredHours] >= [ConsumedHours] AND [LedgerCount] > 0"));
        b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever(); b.HasIndex(x => x.EmployeeId).IsUnique();
        b.HasOne<Employee>().WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.NoAction);
        b.Property(x => x.GrantedHours).HasPrecision(18, 2); b.Property(x => x.ConsumedHours).HasPrecision(18, 2); b.Property(x => x.RestoredHours).HasPrecision(18, 2);
        b.Property(x => x.Watermark).HasMaxLength(200); b.Ignore(x => x.BaselineHours);
    }
    public void Configure(EntityTypeBuilder<CompTimeLegacyMember> b)
    {
        b.ToTable("CompTimeLegacyMembers"); b.HasKey(x => x.TransactionId); b.Property(x => x.TransactionId).ValueGeneratedNever();
        b.HasOne<CompTimeTransaction>().WithMany().HasForeignKey(x => x.TransactionId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<CompTimeLegacyPool>().WithMany().HasForeignKey(x => x.PoolId).OnDelete(DeleteBehavior.NoAction);
    }
    public void Configure(EntityTypeBuilder<CompTimeLegacyReturn> b)
    {
        b.ToTable("CompTimeLegacyReturns"); b.HasKey(x => x.RestoreTransactionId); b.Property(x => x.RestoreTransactionId).ValueGeneratedNever();
        b.HasIndex(x => x.ConsumeTransactionId).IsUnique();
        b.HasOne<CompTimeTransaction>().WithMany().HasForeignKey(x => x.RestoreTransactionId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<CompTimeLegacyMember>().WithMany().HasForeignKey(x => x.ConsumeTransactionId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<CompTimeLegacyPool>().WithMany().HasForeignKey(x => x.PoolId).OnDelete(DeleteBehavior.NoAction);
    }
    public void Configure(EntityTypeBuilder<CompTimeAllocation> b)
    {
        b.ToTable("CompTimeAllocations", t => {
            t.HasCheckConstraint("CK_CompTimeAllocation_Target", "([GrantTransactionId] IS NOT NULL AND [LegacyPoolId] IS NULL) OR ([GrantTransactionId] IS NULL AND [LegacyPoolId] IS NOT NULL)");
            t.HasCheckConstraint("CK_CompTimeAllocation_Hours", "[AllocatedHours] > 0 AND [AllocatedHours] * 2 = FLOOR([AllocatedHours] * 2)");
        });
        b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever(); b.Property(x => x.AllocatedHours).HasPrecision(8, 2);
        b.HasOne<CompTimeTransaction>().WithMany().HasForeignKey(x => x.ConsumeTransactionId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<CompTimeTransaction>().WithMany().HasForeignKey(x => x.GrantTransactionId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<CompTimeLegacyPool>().WithMany().HasForeignKey(x => x.LegacyPoolId).OnDelete(DeleteBehavior.NoAction);
        b.HasIndex(x => new { x.ConsumeTransactionId, x.GrantTransactionId }).IsUnique().HasFilter("[GrantTransactionId] IS NOT NULL");
        b.HasIndex(x => new { x.ConsumeTransactionId, x.LegacyPoolId }).IsUnique().HasFilter("[LegacyPoolId] IS NOT NULL");
    }
    public void Configure(EntityTypeBuilder<CompTimeAllocationReturn> b)
    {
        b.ToTable("CompTimeAllocationReturns"); b.HasKey(x => x.AllocationId); b.Property(x => x.AllocationId).ValueGeneratedNever();
        b.HasOne<CompTimeAllocation>().WithMany().HasForeignKey(x => x.AllocationId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<CompTimeTransaction>().WithMany().HasForeignKey(x => x.RestoreTransactionId).OnDelete(DeleteBehavior.NoAction);
    }
}
