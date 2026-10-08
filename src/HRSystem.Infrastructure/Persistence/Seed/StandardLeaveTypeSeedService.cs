using System.Text.Json;
using HRSystem.Application.Common.Auditing;
using HRSystem.Domain.Auditing;
using HRSystem.Domain.MasterData;
using HRSystem.Domain.CompTime;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HRSystem.Infrastructure.Persistence.Seed;

public sealed record StandardLeaveTypeSeedResult(
    int CreatedCount,
    int ExistingCount,
    IReadOnlyList<string> ConflictingCodes);

public sealed class StandardLeaveTypeSeedService(
    HRSystemDbContext dbContext,
    TimeProvider timeProvider,
    ILogger<StandardLeaveTypeSeedService> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<StandardLeaveTypeSeedResult> SeedAsync(
        CancellationToken cancellationToken = default)
    {
        var created = 0;
        var existingCount = 0;
        var conflicts = new List<string>();

        await dbContext.ExecuteSerializableAsync(async transactionToken =>
        {
            var existing = await dbContext.LeaveTypes
                .ToListAsync(transactionToken);
            var now = timeProvider.GetUtcNow();

            foreach (var definition in Definitions)
            {
                if (existing.Any(item => string.Equals(
                        item.Code,
                        definition.Code,
                        StringComparison.OrdinalIgnoreCase)))
                {
                    existingCount++;
                    continue;
                }

                if (existing.Any(item => string.Equals(
                        item.Name.Trim(),
                        definition.Name,
                        StringComparison.OrdinalIgnoreCase)))
                {
                    conflicts.Add(definition.Code);
                    logger.LogWarning(
                        "Standard leave type {LeaveTypeCode} was not created because the same display name is already used by another code.",
                        definition.Code);
                    continue;
                }

                var entity = definition.Create(now);
                dbContext.LeaveTypes.Add(entity);
                dbContext.AuditLogs.Add(new AuditLog(
                    null,
                    AuditActions.StandardLeaveTypeInitialized,
                    nameof(LeaveType),
                    entity.Id.ToString(),
                    null,
                    JsonSerializer.Serialize(new
                    {
                        entity.Code,
                        entity.Category,
                        entity.CalculationMode,
                        entity.IsEmployeeRequestEnabled
                    }, JsonOptions),
                    null,
                    now));
                existing.Add(entity);
                created++;
            }

            if (created > 0)
            {
                await dbContext.SaveChangesAsync(transactionToken);
            }
        }, cancellationToken);

        return new StandardLeaveTypeSeedResult(created, existingCount, conflicts);
    }

    private static readonly StandardLeaveTypeDefinition[] Definitions =
    [
        General("30000000-0000-0000-0000-000000000002", "PERSONAL", "事假", false, 10),
        General("30000000-0000-0000-0000-000000000003", "SICK", "普通傷病假", false, 20),
        General("30000000-0000-0000-0000-000000000001", "ANNUAL", "特別休假", true, 30),
        General("30000000-0000-0000-0000-000000000004", "OFFICIAL", "公假", true, 40),
        General("31000000-0000-0000-0000-000000000005", "MARRIAGE", "婚假", true, 50),
        General("31000000-0000-0000-0000-000000000006", "BEREAVEMENT", "喪假", true, 60),
        General("31000000-0000-0000-0000-000000000007", "FAMILY_CARE", "家庭照顧假", false, 70),
        General("31000000-0000-0000-0000-000000000008", "MENSTRUAL", "生理假", false, 80),
        General("31000000-0000-0000-0000-000000000009", "PRENATAL_CHECKUP", "產檢假", true, 90),
        General("31000000-0000-0000-0000-000000000010", "PATERNITY_PRENATAL", "陪產檢及陪產假", true, 100),
        General("31000000-0000-0000-0000-000000000015", CompTimePolicy.LeaveTypeCode, CompTimePolicy.LeaveTypeName, true, 105),
        Calendar("31000000-0000-0000-0000-000000000011", "MATERNITY", "產假", true, 110),
        Calendar("31000000-0000-0000-0000-000000000012", "MISCARRIAGE", "流產假", true, 120),
        Calendar("31000000-0000-0000-0000-000000000013", "PREGNANCY_BED_REST", "安胎休養", true, 130),
        LeaveOfAbsence("31000000-0000-0000-0000-000000000014", "PARENTAL_LEAVE_WITHOUT_PAY", "育嬰留職停薪", 140)
    ];

    private static StandardLeaveTypeDefinition General(
        string id,
        string code,
        string name,
        bool isPaid,
        int sortOrder) =>
        new(
            Guid.Parse(id), code, name, LeaveUnit.Hour, 0.5m,
            RequiresReason: true, IsPaid: isPaid, SortOrder: sortOrder,
            Category: LeaveCategory.General,
            CalculationMode: LeaveCalculationMode.WorkingSchedule,
            AllowHourlyRequest: true, MinimumRequestMinutes: 30,
            RequiresAttachment: false, IsEmployeeRequestEnabled: true,
            Description: "依有效班表工作區間計算；本階段不處理額度、薪資或資格規則。");

    private static StandardLeaveTypeDefinition Calendar(
        string id,
        string code,
        string name,
        bool isPaid,
        int sortOrder) =>
        new(
            Guid.Parse(id), code, name, LeaveUnit.Day, 1m,
            RequiresReason: true, IsPaid: isPaid, SortOrder: sortOrder,
            Category: LeaveCategory.SpecialCalendarLeave,
            CalculationMode: LeaveCalculationMode.CalendarDays,
            AllowHourlyRequest: false, MinimumRequestMinutes: null,
            RequiresAttachment: false, IsEmployeeRequestEnabled: false,
            Description: "特殊曆日計算尚未開放。");

    private static StandardLeaveTypeDefinition LeaveOfAbsence(
        string id,
        string code,
        string name,
        int sortOrder) =>
        new(
            Guid.Parse(id), code, name, LeaveUnit.Day, 1m,
            RequiresReason: true, IsPaid: false, SortOrder: sortOrder,
            Category: LeaveCategory.LeaveOfAbsence,
            CalculationMode: LeaveCalculationMode.LeaveOfAbsence,
            AllowHourlyRequest: false, MinimumRequestMinutes: null,
            RequiresAttachment: false, IsEmployeeRequestEnabled: false,
            Description: "此類型需使用專用留職停薪流程。");

    private sealed record StandardLeaveTypeDefinition(
        Guid Id,
        string Code,
        string Name,
        LeaveUnit Unit,
        decimal MinimumUnit,
        bool RequiresReason,
        bool IsPaid,
        int SortOrder,
        LeaveCategory Category,
        LeaveCalculationMode CalculationMode,
        bool AllowHourlyRequest,
        int? MinimumRequestMinutes,
        bool RequiresAttachment,
        bool IsEmployeeRequestEnabled,
        string? Description)
    {
        public LeaveType Create(DateTimeOffset nowUtc) =>
            new(
                Id, Code, Name, Unit, MinimumUnit, RequiresReason, IsPaid,
                SortOrder, Category, CalculationMode, AllowHourlyRequest,
                MinimumRequestMinutes, RequiresAttachment,
                IsEmployeeRequestEnabled, Description, nowUtc);
    }
}
