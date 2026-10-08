using HRSystem.Application.Abstractions.Security;
using HRSystem.Application.Common.Auditing;
using HRSystem.Application.Overtime;
using HRSystem.Application.Security;
using HRSystem.Domain.MasterData;
using HRSystem.Domain.Overtime;
using Microsoft.EntityFrameworkCore;

namespace HRSystem.IntegrationTests;

[Collection(DisposableSqlServerCollection.Name)]
public sealed class OvertimeRecognitionSqlIntegrationTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 21, 13, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Confirm_Reopen_Reconfirm_Persists_Without_Changing_Attendance()
    {
        await using var database = await DisposableSqlServerDatabase.CreateAsync(
            "OvertimeRecognitionWorkflow");
        var requestId = await SeedApprovedAsync(database);
        await using var db = database.CreateDbContext();
        var service = Service(db, "admin-a");
        var beforeResults = await db.DailyAttendanceResults.CountAsync();
        var beforeRaw = await db.AttendanceRawEvents.CountAsync();
        var beforeAdjustments = await db.AttendanceAdjustments.CountAsync();
        var pending = Assert.Single(await service.SearchAsync(Query()));

        var confirmed = await service.ConfirmAsync(new()
        {
            OvertimeRequestId = requestId,
            SourceFingerprint = pending.SourceFingerprint,
            RecognizedStartAt = Local(17, 30),
            RecognizedEndAt = Local(20, 10),
            Reason = OvertimeRecognitionReason.MissingPunchVerified,
            Note = "SQL recognition detail"
        });
        var reopened = await service.ReopenAsync(new()
        {
            RecognitionId = confirmed.RecognitionId!.Value,
            RowVersion = confirmed.RowVersion!,
            SourceFingerprint = confirmed.SourceFingerprint,
            Note = "SQL reopen detail"
        });
        var final = await service.ConfirmAsync(new()
        {
            OvertimeRequestId = requestId,
            RecognitionId = reopened.RecognitionId,
            RowVersion = reopened.RowVersion,
            SourceFingerprint = reopened.SourceFingerprint,
            RecognizedStartAt = Local(17, 30),
            RecognizedEndAt = Local(19, 47),
            Reason = OvertimeRecognitionReason.MissingPunchVerified
        });

        Assert.Equal(137, final.RecognizedMinutes);
        Assert.Equal(4, await db.OvertimeRecognitionHistories.CountAsync());
        Assert.Equal(OvertimeRequestStatus.Approved,
            (await db.OvertimeRequests.SingleAsync()).Status);
        Assert.Equal(beforeResults, await db.DailyAttendanceResults.CountAsync());
        Assert.Equal(beforeRaw, await db.AttendanceRawEvents.CountAsync());
        Assert.Equal(beforeAdjustments, await db.AttendanceAdjustments.CountAsync());
        var auditJson = string.Join('\n', await db.AuditLogs
            .Where(item => item.Action.StartsWith("OvertimeRecognition"))
            .Select(item => item.NewValuesJson).ToListAsync());
        Assert.DoesNotContain("SQL recognition detail", auditJson,
            StringComparison.Ordinal);
        Assert.Contains(await db.AuditLogs.Select(item => item.Action).ToListAsync(),
            item => item == AuditActions.OvertimeRecognitionReopened);
    }

    [Fact]
    public async Task Concurrent_First_Confirmation_Allows_One_Recognition_Row()
    {
        await using var database = await DisposableSqlServerDatabase.CreateAsync(
            "OvertimeRecognitionConcurrency");
        var requestId = await SeedApprovedAsync(database);
        string fingerprint;
        await using (var read = database.CreateDbContext())
            fingerprint = Assert.Single(await Service(read, "reader")
                .SearchAsync(Query())).SourceFingerprint;

        await using var firstDb = database.CreateDbContext();
        await using var secondDb = database.CreateDbContext();
        var request = new ConfirmOvertimeRecognitionRequest
        {
            OvertimeRequestId = requestId,
            SourceFingerprint = fingerprint,
            RecognizedStartAt = Local(17, 30),
            RecognizedEndAt = Local(20, 0),
            Reason = OvertimeRecognitionReason.MissingPunchVerified
        };
        var outcomes = await Task.WhenAll(
            Attempt(Service(firstDb, "admin-a").ConfirmAsync(request)),
            Attempt(Service(secondDb, "admin-b").ConfirmAsync(request)));
        Assert.Single(outcomes, value => value);
        await using var verify = database.CreateDbContext();
        Assert.Equal(1, await verify.OvertimeRecognitions.CountAsync());
        Assert.Equal(2, await verify.OvertimeRecognitionHistories.CountAsync());
    }

    private static async Task<Guid> SeedApprovedAsync(
        DisposableSqlServerDatabase database)
    {
        await using var db = database.CreateDbContext();
        var department = new Department(Guid.NewGuid(), "G1SQL", "認列 SQL 部", Now);
        var employee = new Employee(Guid.NewGuid(), "EMP-G1-SQL", "認列 SQL 員工",
            department.Id, new DateOnly(2025, 1, 1), Now);
        var request = new OvertimeRequest(Guid.NewGuid(), employee.Id,
            Local(17, 30), Local(20, 30), "SQL recognition", "employee", Now);
        request.Submit("employee", Now);
        request.Approve(OvertimeReviewReason.ApprovedAsRequested,
            null, "admin", Now);
        db.AddRange(department, employee, request);
        await db.SaveChangesAsync();
        return request.Id;
    }

    private static OvertimeRecognitionService Service(
        HRSystem.Infrastructure.Persistence.HRSystemDbContext db,
        string userId) => new(db, new CurrentUser(userId), new FixedTime(Now));
    private static OvertimeRecognitionQuery Query() => new()
        { StartDate = new DateOnly(2026, 8, 21), EndDate = new DateOnly(2026, 8, 21) };
    private static DateTime Local(int hour, int minute) =>
        DateTime.SpecifyKind(new DateTime(2026, 8, 21, hour, minute, 0),
            DateTimeKind.Unspecified);
    private static async Task<bool> Attempt(Task<OvertimeRecognitionDto> task)
    { try { await task; return true; } catch { return false; } }

    private sealed class CurrentUser(string userId) : ICurrentUser
    {
        public string? UserId => userId;
        public Guid? EmployeeId => null;
        public string? DisplayName => userId;
        public string? IpAddress => "127.0.0.1";
        public bool IsAuthenticated => true;
        public bool IsInRole(string role) => role == RoleNames.Admin;
        public bool HasPermission(string policy) =>
            RolePermissions.HasPermission([RoleNames.Admin], policy);
    }
    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    { public override DateTimeOffset GetUtcNow() => now; }
}
