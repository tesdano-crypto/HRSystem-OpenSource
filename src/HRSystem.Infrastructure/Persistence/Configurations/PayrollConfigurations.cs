using HRSystem.Domain.Payroll;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRSystem.Infrastructure.Persistence.Configurations;

internal static class PayrollSeedIds
{
    public static readonly DateOnly EffectiveFrom = new(2026, 1, 1);
    public static Guid Component(int number) => Guid.Parse($"50000000-0000-0000-0000-{number:D12}");
    public static Guid Plan(int number) => Guid.Parse($"51000000-0000-0000-0000-{number:D12}");
    public static Guid PlanComponent(int number) => Guid.Parse($"52000000-0000-0000-0000-{number:D12}");
    public static Guid Tier(int number) => Guid.Parse($"53000000-0000-0000-0000-{number:D12}");
    public static Guid OvertimeRate(int number) => Guid.Parse($"55000000-0000-0000-0000-{number:D12}");
}

public sealed class PayrollComponentDefinitionConfiguration : IEntityTypeConfiguration<PayrollComponentDefinition>
{
    public void Configure(EntityTypeBuilder<PayrollComponentDefinition> builder)
    {
        builder.ToTable("PayrollComponentDefinitions", table =>
        {
            table.HasCheckConstraint("CK_PayrollComponentDefinitions_EffectiveRange", "[EffectiveTo] IS NULL OR [EffectiveTo] >= [EffectiveFrom]");
            table.HasCheckConstraint("CK_PayrollComponentDefinitions_SortOrder", "[SortOrder] >= 0");
        });
        builder.HasKey(x => x.Id); builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Code).HasMaxLength(50).UseCollation("Latin1_General_100_CI_AS").IsRequired();
        builder.Property(x => x.Name).HasMaxLength(100).IsRequired();
        builder.Property(x => x.EffectiveFrom).HasColumnType("date"); builder.Property(x => x.EffectiveTo).HasColumnType("date");
        builder.Property(x => x.RowVersion).IsRowVersion();
        builder.HasIndex(x => x.Code).IsUnique().HasDatabaseName("UX_PayrollComponentDefinitions_Code");
        builder.HasIndex(x => new { x.IsActive, x.Category, x.SortOrder }).HasDatabaseName("IX_PayrollComponentDefinitions_Active_Category_Sort");
        builder.HasData(Definitions());
    }

    private static IEnumerable<object> Definitions()
    {
        var from = PayrollSeedIds.EffectiveFrom;
        yield return Definition(1, "BASE_SALARY", "底薪", PayrollComponentCategory.Earning, PayrollCalculationKind.FixedAmount, true, 10, from, true);
        yield return Definition(2, "PERFORMANCE", "績效", PayrollComponentCategory.Earning, PayrollCalculationKind.RuleBased, true, 20, from, true);
        yield return Definition(3, "JOB_ALLOWANCE", "職務加給", PayrollComponentCategory.Earning, PayrollCalculationKind.FixedAmount, true, 30, from, true);
        yield return Definition(4, "CERTIFICATE_ALLOWANCE", "證書加給", PayrollComponentCategory.Earning, PayrollCalculationKind.FixedAmount, true, 40, from);
        yield return Definition(5, "MEAL_ALLOWANCE", "伙食津貼", PayrollComponentCategory.Earning, PayrollCalculationKind.FixedAmount, true, 50, from, true);
        yield return Definition(6, "ATTENDANCE_ALLOWANCE", "出席補貼", PayrollComponentCategory.Earning, PayrollCalculationKind.RuleBased, true, 60, from, true);
        yield return Definition(7, "OVERTIME_FIRST_2H", "加班前 2 小時", PayrollComponentCategory.Earning, PayrollCalculationKind.ExternalCalculated, false, 70, from);
        yield return Definition(8, "OVERTIME_AFTER_2H", "加班 2 小時後", PayrollComponentCategory.Earning, PayrollCalculationKind.ExternalCalculated, false, 80, from);
        yield return Definition(9, "OVERTIME_AFTER_8H", "加班 8 小時後", PayrollComponentCategory.Earning, PayrollCalculationKind.ExternalCalculated, false, 90, from);
        yield return Definition(10, "CASE_BONUS", "案件", PayrollComponentCategory.Earning, PayrollCalculationKind.ManualAdjustment, false, 100, from);
        yield return Definition(11, "OTHER_EARNING", "其他應發", PayrollComponentCategory.Earning, PayrollCalculationKind.ManualAdjustment, false, 110, from);
        yield return Definition(12, "LABOR_INSURANCE", "勞保", PayrollComponentCategory.Deduction, PayrollCalculationKind.ExternalCalculated, true, 120, from);
        yield return Definition(13, "HEALTH_INSURANCE", "健保", PayrollComponentCategory.Deduction, PayrollCalculationKind.ExternalCalculated, true, 130, from);
        yield return Definition(14, "LEAVE_DEDUCTION", "請假", PayrollComponentCategory.Deduction, PayrollCalculationKind.ExternalCalculated, false, 140, from);
        yield return Definition(15, "OTHER_DEDUCTION", "其他應扣", PayrollComponentCategory.Deduction, PayrollCalculationKind.ManualAdjustment, false, 150, from);
        yield return Definition(16, "PERIODIC_FIXED_PAY", "週期固定給付", PayrollComponentCategory.Earning, PayrollCalculationKind.FixedAmount, false, 55, from);
        yield return Definition(17, PayrollLegacyAdjustmentComponents.AttendanceAllowanceCode, "出席補貼（歷史過渡）", PayrollComponentCategory.Earning, PayrollCalculationKind.ManualAdjustment, false, 61, from);
        yield return Definition(18, PayrollLegacyAdjustmentComponents.OvertimePayCode, "加班費（歷史過渡）", PayrollComponentCategory.Earning, PayrollCalculationKind.ManualAdjustment, false, 91, from);
    }
    private static object Definition(int id, string code, string name, PayrollComponentCategory category, PayrollCalculationKind kind, bool recurring, int sort, DateOnly from, bool overtimeBase = false) =>
        new { Id = PayrollSeedIds.Component(id), Code = code, Name = name, Category = category, CalculationKind = kind, IsRecurring = recurring, IncludeInOvertimeHourlyBase = overtimeBase, IsActive = true, SortOrder = sort, EffectiveFrom = from, EffectiveTo = (DateOnly?)null };
}

public sealed class OvertimePayRatePolicyConfiguration : IEntityTypeConfiguration<OvertimePayRatePolicy>
{
    public void Configure(EntityTypeBuilder<OvertimePayRatePolicy> builder)
    {
        builder.ToTable("OvertimePayRatePolicies", table =>
        {
            table.HasCheckConstraint("CK_OvertimePayRatePolicies_Multiplier", "[Multiplier] > 0");
            table.HasCheckConstraint("CK_OvertimePayRatePolicies_Range", "[EffectiveTo] IS NULL OR [EffectiveTo] >= [EffectiveFrom]");
        });
        builder.HasKey(x => x.Id); builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Version).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Bucket).HasConversion<byte>();
        builder.Property(x => x.Multiplier).HasPrecision(9, 4);
        builder.Property(x => x.EffectiveFrom).HasColumnType("date");
        builder.Property(x => x.EffectiveTo).HasColumnType("date");
        builder.Property(x => x.RowVersion).IsRowVersion();
        builder.HasIndex(x => new { x.Bucket, x.EffectiveFrom }).IsUnique()
            .HasDatabaseName("UX_OvertimePayRatePolicies_Bucket_From");
        builder.HasData(
            Rate(1, OvertimePayBucket.FirstTwoHours, 1.34m),
            Rate(2, OvertimePayBucket.AfterTwoHours, 1.67m),
            Rate(3, OvertimePayBucket.AfterEightHours, 2.67m));
    }
    private static object Rate(int id, OvertimePayBucket bucket, decimal multiplier) => new
    {
        Id = PayrollSeedIds.OvertimeRate(id), Version = "2026-v1", Bucket = bucket,
        Multiplier = multiplier, EffectiveFrom = PayrollSeedIds.EffectiveFrom,
        EffectiveTo = (DateOnly?)null, IsActive = true
    };
}

public sealed class PayrollPlanConfiguration : IEntityTypeConfiguration<PayrollPlan>
{
    public void Configure(EntityTypeBuilder<PayrollPlan> builder)
    {
        builder.ToTable("PayrollPlans", table => table.HasCheckConstraint("CK_PayrollPlans_EffectiveRange", "[EffectiveTo] IS NULL OR [EffectiveTo] >= [EffectiveFrom]"));
        builder.HasKey(x => x.Id); builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Code).HasMaxLength(50).UseCollation("Latin1_General_100_CI_AS").IsRequired();
        builder.Property(x => x.Name).HasMaxLength(100).IsRequired(); builder.Property(x => x.Description).HasMaxLength(500);
        builder.Property(x => x.EffectiveFrom).HasColumnType("date"); builder.Property(x => x.EffectiveTo).HasColumnType("date");
        builder.Property(x => x.RowVersion).IsRowVersion();
        builder.HasIndex(x => x.Code).IsUnique().HasDatabaseName("UX_PayrollPlans_Code");
        builder.HasData(
            new { Id = PayrollSeedIds.Plan(1), Code = "STANDARD_MONTHLY", Name = "標準月薪制", Description = "公司標準 component-based 月薪方案；規則可依生效日版本化。", EffectiveFrom = PayrollSeedIds.EffectiveFrom, EffectiveTo = (DateOnly?)null, IsActive = true },
            new { Id = PayrollSeedIds.Plan(2), Code = "CUSTOM_FIXED", Name = "個別固定薪資", Description = "特殊固定方案；必須以 BASE_SALARY Replace override 明確設定，不得默認 0 元。", EffectiveFrom = PayrollSeedIds.EffectiveFrom, EffectiveTo = (DateOnly?)null, IsActive = true },
            new { Id = PayrollSeedIds.Plan(3), Code = "PERIODIC_FIXED", Name = "週期固定給付", Description = "由員工有效發薪方式設定解析的固定週期給付。", EffectiveFrom = PayrollSeedIds.EffectiveFrom, EffectiveTo = (DateOnly?)null, IsActive = true });
    }
}

public sealed class PayrollPlanComponentConfiguration : IEntityTypeConfiguration<PayrollPlanComponent>
{
    public void Configure(EntityTypeBuilder<PayrollPlanComponent> builder)
    {
        builder.ToTable("PayrollPlanComponents", table =>
        {
            table.HasCheckConstraint("CK_PayrollPlanComponents_Amount", "[DefaultAmount] IS NULL OR [DefaultAmount] >= 0");
            table.HasCheckConstraint("CK_PayrollPlanComponents_EffectiveRange", "[EffectiveTo] IS NULL OR [EffectiveTo] >= [EffectiveFrom]");
            table.HasCheckConstraint("CK_PayrollPlanComponents_ProrationKind", "[ProrationKind] IN (0, 1, 2)");
        });
        builder.HasKey(x => x.Id); builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.DefaultAmount).HasPrecision(18, 2); builder.Property(x => x.EffectiveFrom).HasColumnType("date"); builder.Property(x => x.EffectiveTo).HasColumnType("date");
        builder.Property(x => x.ProrationKind).HasConversion<short>().HasColumnType("smallint");
        builder.Property(x => x.RowVersion).IsRowVersion();
        builder.HasIndex(x => new { x.PayrollPlanId, x.PayrollComponentDefinitionId, x.EffectiveFrom }).IsUnique().HasDatabaseName("UX_PayrollPlanComponents_Plan_Component_From");
        builder.HasOne(x => x.PayrollPlan).WithMany(x => x.Components).HasForeignKey(x => x.PayrollPlanId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(x => x.ComponentDefinition).WithMany(x => x.PlanComponents).HasForeignKey(x => x.PayrollComponentDefinitionId).OnDelete(DeleteBehavior.NoAction);
        builder.HasData(StandardComponents().Concat(CustomComponents()).Concat(PeriodicComponents()));
    }
    private static IEnumerable<object> StandardComponents()
    {
        yield return Item(1, 1, 29500m, PayrollRuleKind.None,
            PayrollProrationKind.Monthly30Day);
        yield return Item(2, 2, null, PayrollRuleKind.SeniorityTier,
            PayrollProrationKind.PendingPolicy);
        yield return Item(3, 5, 2000m, PayrollRuleKind.None,
            PayrollProrationKind.Monthly30Day);
        yield return Item(4, 6, 2000m, PayrollRuleKind.AttendanceProrated);
        yield return Item(5, 7, null, PayrollRuleKind.ExternalPending);
        yield return Item(6, 8, null, PayrollRuleKind.ExternalPending);
        yield return Item(7, 9, null, PayrollRuleKind.ExternalPending);
        yield return Item(8, 12, null, PayrollRuleKind.ExternalPending);
        yield return Item(9, 13, null, PayrollRuleKind.ExternalPending);
        yield return Item(10, 14, null, PayrollRuleKind.ExternalPending);
    }
    private static IEnumerable<object> CustomComponents()
    {
        yield return Item(11, 1, null, PayrollRuleKind.None,
            PayrollProrationKind.Monthly30Day, 2);
    }
    private static IEnumerable<object> PeriodicComponents()
    {
        yield return Item(12, 16, null, PayrollRuleKind.None,
            PayrollProrationKind.None, 3);
        yield return Item(13, 12, null, PayrollRuleKind.ExternalPending,
            PayrollProrationKind.None, 3);
        yield return Item(14, 13, null, PayrollRuleKind.ExternalPending,
            PayrollProrationKind.None, 3);
    }
    private static object Item(int id, int component, decimal? amount,
        PayrollRuleKind rule,
        PayrollProrationKind proration = PayrollProrationKind.None,
        int plan = 1) => new
    {
        Id = PayrollSeedIds.PlanComponent(id),
        PayrollPlanId = PayrollSeedIds.Plan(plan),
        PayrollComponentDefinitionId = PayrollSeedIds.Component(component),
        DefaultAmount = amount,
        RuleKind = rule,
        ProrationKind = proration,
        EffectiveFrom = PayrollSeedIds.EffectiveFrom,
        EffectiveTo = (DateOnly?)null
    };
}

public sealed class PayrollSeniorityTierConfiguration : IEntityTypeConfiguration<PayrollSeniorityTier>
{
    public void Configure(EntityTypeBuilder<PayrollSeniorityTier> builder)
    {
        builder.ToTable("PayrollSeniorityTiers", table =>
        {
            table.HasCheckConstraint("CK_PayrollSeniorityTiers_Range", "[MinMonthsInclusive] >= 0 AND ([MaxMonthsExclusive] IS NULL OR [MaxMonthsExclusive] > [MinMonthsInclusive])");
            table.HasCheckConstraint("CK_PayrollSeniorityTiers_Amount", "[Amount] >= 0");
        });
        builder.HasKey(x => x.Id); builder.Property(x => x.Id).ValueGeneratedNever(); builder.Property(x => x.Amount).HasPrecision(18, 2);
        builder.HasIndex(x => new { x.PayrollPlanComponentId, x.MinMonthsInclusive }).IsUnique().HasDatabaseName("UX_PayrollSeniorityTiers_Component_MinMonths");
        builder.HasOne(x => x.PayrollPlanComponent).WithMany(x => x.SeniorityTiers).HasForeignKey(x => x.PayrollPlanComponentId).OnDelete(DeleteBehavior.NoAction);
        builder.HasData(
            Tier(1, 0, 3, 0m), Tier(2, 3, 12, 2100m), Tier(3, 12, 36, 4200m),
            Tier(4, 36, 72, 6000m), Tier(5, 72, null, 7500m));
    }
    private static object Tier(int id, int min, int? max, decimal amount) => new { Id = PayrollSeedIds.Tier(id), PayrollPlanComponentId = PayrollSeedIds.PlanComponent(2), MinMonthsInclusive = min, MaxMonthsExclusive = max, Amount = amount };
}
