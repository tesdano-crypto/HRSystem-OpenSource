using System.Text.Json;
using HRSystem.Application;
using HRSystem.Application.Abstractions.Security;
using HRSystem.Application.Attendance;
using HRSystem.Application.Security;
using HRSystem.Domain.Attendance;
using HRSystem.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var parsed = JobArguments.Parse(args);
var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = []
});
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<ICurrentUser, JobCurrentUser>();
builder.Services.AddApplication();
builder.Services.AddInfrastructure(
    builder.Configuration,
    allowInMemoryDatabase: false);

using var host = builder.Build();
await using var scope = host.Services.CreateAsyncScope();
var coordinator = scope.ServiceProvider
    .GetRequiredService<IBioWebTaImportCoordinator>();
var request = new BioWebTaImportExecutionRequest(
    parsed.TriggerType,
    parsed.QueryFromLocal,
    parsed.QueryToLocal,
    parsed.DryRun,
    parsed.OverlapDays);
var result = parsed.DryRun
    ? await coordinator.PreviewAsync(request)
    : await coordinator.RunAsync(request);
Console.WriteLine(JsonSerializer.Serialize(new
{
    result.BatchId,
    Result = result.Outcome.ToString(),
    result.QueryFromLocal,
    result.QueryToLocal,
    result.SourceRowCount,
    result.InsertedCount,
    result.DuplicateCount,
    result.ConflictCount,
    result.UnmappedCount,
    result.AffectedEmployeeDateCount,
    result.RecalculatedCount,
    result.SafeErrorSummary,
    DurationMs = (long)(result.CompletedAtUtc - result.StartedAtUtc).TotalMilliseconds
}));
return result.Outcome is BioWebTaImportExecutionOutcome.Completed or
    BioWebTaImportExecutionOutcome.NoChanges or
    BioWebTaImportExecutionOutcome.Preview or
    BioWebTaImportExecutionOutcome.SkippedAlreadyRunning or
    BioWebTaImportExecutionOutcome.Disabled
    ? 0
    : 1;

file sealed class JobCurrentUser : ICurrentUser
{
    public string? UserId => "biowebta-import-job";
    public Guid? EmployeeId => null;
    public string? DisplayName => "BioWebTA Attendance Import Job";
    public string? IpAddress => null;
    public bool IsAuthenticated => true;
    public bool IsInRole(string role) => role == RoleNames.Admin;
    public bool HasPermission(string policy) =>
        RolePermissions.HasPermission([RoleNames.Admin], policy);
}

file sealed record JobArguments(
    bool DryRun,
    BioWebTaImportTriggerType TriggerType,
    int? OverlapDays,
    DateTime? QueryFromLocal,
    DateTime? QueryToLocal)
{
    public static JobArguments Parse(string[] args)
    {
        if (args.Length == 0 || args[0] is not ("run" or "dry-run"))
        {
            throw new ArgumentException(
                "Usage: run|dry-run [--trigger scheduled|manual] [--overlap-days N] or --from yyyy-MM-dd --to yyyy-MM-dd");
        }

        var dryRun = args[0] == "dry-run";
        var trigger = BioWebTaImportTriggerType.Manual;
        int? overlapDays = null;
        DateOnly? from = null;
        DateOnly? to = null;
        for (var index = 1; index < args.Length; index += 2)
        {
            if (index + 1 >= args.Length)
            {
                throw new ArgumentException($"Missing value for {args[index]}.");
            }

            var value = args[index + 1];
            switch (args[index])
            {
                case "--trigger":
                    trigger = value switch
                    {
                        "scheduled" => BioWebTaImportTriggerType.Scheduled,
                        "manual" => BioWebTaImportTriggerType.Manual,
                        _ => throw new ArgumentException("Trigger must be scheduled or manual.")
                    };
                    break;
                case "--overlap-days":
                    overlapDays = int.Parse(value);
                    break;
                case "--from":
                    from = DateOnly.ParseExact(value, "yyyy-MM-dd");
                    break;
                case "--to":
                    to = DateOnly.ParseExact(value, "yyyy-MM-dd");
                    break;
                default:
                    throw new ArgumentException($"Unknown option {args[index]}.");
            }
        }

        if (from.HasValue != to.HasValue ||
            (from.HasValue && overlapDays.HasValue))
        {
            throw new ArgumentException(
                "Specify both inclusive dates or overlap days, not both.");
        }

        if (from.HasValue && to!.Value < from.Value)
        {
            throw new ArgumentException("The inclusive date range is invalid.");
        }

        return new JobArguments(
            dryRun,
            trigger,
            overlapDays,
            from?.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified),
            to?.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified));
    }
}
