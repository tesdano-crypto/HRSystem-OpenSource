using System.Text.Json;
using HRSystem.Application.Common.Auditing;
using HRSystem.Application.Common.Exceptions;
using HRSystem.Domain.Auditing;
using HRSystem.Domain.LeaveRequests;
using HRSystem.Domain.MasterData;
using Microsoft.EntityFrameworkCore;

namespace HRSystem.Infrastructure.Persistence.Seed;

public sealed record CalendarDayLeaveTypeActivationResult(
    int ActivatedCount,
    int AlreadyActiveCount);

public sealed class CalendarDayLeaveTypeActivationService(
    HRSystemDbContext dbContext,
    TimeProvider timeProvider)
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    private static readonly ActivationTarget[] Targets =
    [
        new(
            Guid.Parse("31000000-0000-0000-0000-000000000011"),
            CalendarDayLeavePolicy.MaternityCode),
        new(
            Guid.Parse("31000000-0000-0000-0000-000000000012"),
            CalendarDayLeavePolicy.MiscarriageCode),
        new(
            Guid.Parse("31000000-0000-0000-0000-000000000013"),
            CalendarDayLeavePolicy.PregnancyBedRestCode)
    ];

    public async Task<CalendarDayLeaveTypeActivationResult> ActivateAsync(
        CancellationToken cancellationToken = default)
    {
        var activated = 0;
        var alreadyActive = 0;
        await dbContext.ExecuteSerializableAsync(async transactionToken =>
        {
            var targetIds = Targets.Select(item => item.Id).ToArray();
            var entities = await dbContext.LeaveTypes
                .Where(item => targetIds.Contains(item.Id))
                .ToListAsync(transactionToken);

            foreach (var target in Targets)
            {
                var entity = entities.SingleOrDefault(item => item.Id == target.Id);
                if (entity is null ||
                    !string.Equals(entity.Code, target.Code, StringComparison.Ordinal) ||
                    entity.Category != LeaveCategory.SpecialCalendarLeave ||
                    entity.CalculationMode != LeaveCalculationMode.CalendarDays ||
                    entity.AllowHourlyRequest ||
                    !entity.IsActive)
                {
                    throw new ApplicationValidationException(
                        $"Calendar-day leave activation preflight failed for {target.Code}.");
                }
            }

            var now = timeProvider.GetUtcNow();
            foreach (var target in Targets)
            {
                var entity = entities.Single(item => item.Id == target.Id);
                if (entity.IsEmployeeRequestEnabled)
                {
                    alreadyActive++;
                    continue;
                }

                entity.SetEmployeeRequestEnabled(true, now);
                dbContext.AuditLogs.Add(new AuditLog(
                    null,
                    AuditActions.CalendarDayLeaveTypeEnabled,
                    nameof(LeaveType),
                    entity.Id.ToString(),
                    JsonSerializer.Serialize(
                        new { IsEmployeeRequestEnabled = false },
                        JsonOptions),
                    JsonSerializer.Serialize(
                        new
                        {
                            entity.Code,
                            entity.CalculationMode,
                            entity.IsEmployeeRequestEnabled
                        },
                        JsonOptions),
                    null,
                    now));
                activated++;
            }

            if (activated > 0)
            {
                await dbContext.SaveChangesAsync(transactionToken);
            }
        }, cancellationToken);

        return new CalendarDayLeaveTypeActivationResult(
            activated,
            alreadyActive);
    }

    private sealed record ActivationTarget(Guid Id, string Code);
}
