using HRSystem.Application.Abstractions.Security;
using HRSystem.Application.Common.Auditing;
using HRSystem.Application.Common.Exceptions;
using HRSystem.Application.Overtime;
using HRSystem.Application.Security;
using HRSystem.Domain.Common;
using HRSystem.Domain.MasterData;
using HRSystem.Domain.Overtime;
using HRSystem.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HRSystem.UnitTests;

public sealed class OvertimeRecognitionTests
{
    private static readonly DateOnly WorkDate = new(2026, 8, 21);
    private static readonly DateTimeOffset Now =
        new(2026, 8, 21, 13, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(19, 45, 135, 0)]
    [InlineData(20, 30, 180, 0)]
    [InlineData(20, 50, 180, 20)]
    [InlineData(17, 32, 2, 0)]
    public void Suggestion_Clips_To_Approved_Interval_But_Reports_Excess(
        int clockOutHour, int clockOutMinute, int suggested, int excess)
    {
        var source = Source(Local(clockOutHour, clockOutMinute));
        Assert.Equal(suggested, source.SuggestedMinutes);
        Assert.Equal(excess, source.ExcessBeyondApprovalMinutes);
    }

    [Fact]
    public void Missing_ClockOut_Needs_Review_And_Has_No_Suggestion()
    {
        var source = Source(null, missingClockOut: true);
        Assert.Equal(OvertimeRecognitionStatus.NeedsReview, source.InitialStatus);
        Assert.Null(source.SuggestedMinutes);
    }

    [Fact]
    public void Observed_ClockOut_Is_Normalized_To_Minute_But_Remains_In_Fingerprint()
    {
        var withSeconds = Source(Local(19, 47).AddSeconds(59));
        var wholeMinute = Source(Local(19, 47));
        Assert.Equal(Local(19, 47), withSeconds.ObservedClockOutAt);
        Assert.Equal(137, withSeconds.SuggestedMinutes);
        Assert.False(withSeconds.Fingerprint.SequenceEqual(wholeMinute.Fingerprint));
    }

    [Theory]
    [InlineData(19, 47, 137)]
    [InlineData(20, 10, 160)]
    [InlineData(20, 50, 200)]
    [InlineData(20, 30, 180)]
    public void Manual_Recognition_Preserves_Exact_Minutes(
        int endHour, int endMinute, int expected)
    {
        var recognition = NewRecognition(Source(Local(20, 50)));
        recognition.Confirm(Local(17, 30), Local(endHour, endMinute),
            OvertimeRecognitionReason.ActualAttendanceConfirmed, null,
            recognition.SourceFingerprint, "admin", Now);
        Assert.Equal(expected, recognition.RecognizedMinutes);
    }

    [Fact]
    public void No_Actual_Overtime_Is_Confirmed_As_Zero()
    {
        var recognition = NewRecognition(Source(Local(17, 32)));
        recognition.Confirm(null, null,
            OvertimeRecognitionReason.NoActualOvertime, null,
            recognition.SourceFingerprint, "admin", Now);
        Assert.Equal(0, recognition.RecognizedMinutes);
        Assert.Equal(OvertimeRecognitionStatus.Confirmed, recognition.Status);
    }

    [Fact]
    public void Recognition_Rejects_Invalid_Range_And_Other_Without_Note()
    {
        var recognition = NewRecognition(Source(Local(20, 30)));
        Assert.Throws<DomainValidationException>(() => recognition.Confirm(
            Local(20), Local(19), OvertimeRecognitionReason.ActualAttendanceConfirmed,
            null, recognition.SourceFingerprint, "admin", Now));
        Assert.Throws<DomainValidationException>(() => recognition.Confirm(
            Local(17, 30), Local(20, 30), OvertimeRecognitionReason.Other,
            " ", recognition.SourceFingerprint, "admin", Now));
        Assert.Throws<DomainValidationException>(() => recognition.Confirm(
            Local(17, 30), Local(17, 31).AddHours(24),
            OvertimeRecognitionReason.ActualAttendanceConfirmed,
            null, recognition.SourceFingerprint, "admin", Now));
    }

    [Fact]
    public void Changed_Attendance_Fingerprint_Makes_Confirmed_Recognition_Stale()
    {
        var first = Source(Local(19, 45));
        var recognition = NewRecognition(first);
        recognition.Confirm(Local(17, 30), Local(19, 45),
            OvertimeRecognitionReason.ActualAttendanceConfirmed, null,
            first.Fingerprint, "admin", Now);
        var changed = Source(Local(20, 10));
        Assert.Equal(OvertimeRecognitionStatus.NeedsReview,
            OvertimeRecognitionPolicy.EffectiveStatus(recognition, changed,
                out var stale));
        Assert.True(stale);
        Assert.Equal(OvertimeRecognitionStatus.Confirmed,
            OvertimeRecognitionPolicy.EffectiveStatus(recognition, first,
                out stale));
        Assert.False(stale);
    }

    [Theory]
    [InlineData(30)]
    [InlineData(60)]
    [InlineData(90)]
    [InlineData(180)]
    public void Request_Accepts_Thirty_Minute_Units(int minutes) =>
        _ = new OvertimeRequest(Guid.NewGuid(), Guid.NewGuid(), Local(22),
            Local(22).AddMinutes(minutes), "工作需求", "employee", Now);

    [Theory]
    [InlineData(75)]
    [InlineData(181)]
    public void Request_Rejects_Non_Thirty_Minute_Units(int minutes) =>
        Assert.Throws<DomainValidationException>(() => new OvertimeRequest(
            Guid.NewGuid(), Guid.NewGuid(), Local(18),
            Local(18).AddMinutes(minutes), "工作需求", "employee", Now));

    [Fact]
    public async Task Workflow_Confirm_Reopen_Reconfirm_Is_Append_Only_And_Audited()
    {
        await using var setup = await Setup.CreateAsync(RoleNames.Admin);
        var pending = Assert.Single(await setup.Service.SearchAsync(Query()));
        var confirmed = await setup.Service.ConfirmAsync(new()
        {
            OvertimeRequestId = pending.OvertimeRequestId,
            SourceFingerprint = pending.SourceFingerprint,
            Reason = OvertimeRecognitionReason.MissingPunchVerified,
            RecognizedStartAt = Local(17, 30),
            RecognizedEndAt = Local(20, 10)
        });
        var reopened = await setup.Service.ReopenAsync(new()
        {
            RecognitionId = confirmed.RecognitionId!.Value,
            RowVersion = confirmed.RowVersion!,
            SourceFingerprint = confirmed.SourceFingerprint,
            Note = "重新確認"
        });
        var reconfirmed = await setup.Service.ConfirmAsync(new()
        {
            OvertimeRequestId = reopened.OvertimeRequestId,
            RecognitionId = reopened.RecognitionId,
            RowVersion = reopened.RowVersion,
            SourceFingerprint = reopened.SourceFingerprint,
            Reason = OvertimeRecognitionReason.MissingPunchVerified,
            RecognizedStartAt = Local(17, 30),
            RecognizedEndAt = Local(19, 47)
        });

        Assert.Equal(137, reconfirmed.RecognizedMinutes);
        Assert.Equal(4, await setup.Db.OvertimeRecognitionHistories.CountAsync());
        Assert.Equal(OvertimeRequestStatus.Approved,
            (await setup.Db.OvertimeRequests.SingleAsync()).Status);
        Assert.Contains(await setup.Db.AuditLogs.Select(x => x.Action).ToListAsync(),
            x => x == AuditActions.OvertimeRecognitionAdjusted);
        Assert.DoesNotContain("重新確認", string.Join('\n',
            await setup.Db.AuditLogs.Select(x => x.NewValuesJson).ToListAsync()),
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(RoleNames.Employee)]
    [InlineData(RoleNames.Manager)]
    public async Task Non_Admin_Cannot_Recognize(string role)
    {
        await using var setup = await Setup.CreateAsync(role);
        await Assert.ThrowsAsync<ForbiddenAccessException>(() =>
            setup.Service.SearchAsync(Query()));
    }

    private static OvertimeRecognitionQuery Query() => new()
        { StartDate = WorkDate, EndDate = WorkDate };

    private static OvertimeRecognitionSource Source(
        DateTime? clockOut, bool missingClockOut = false) =>
        OvertimeRecognitionPolicy.Build(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            WorkDate, OvertimeRequestStatus.Approved, [1],
            Local(17, 30), Local(20, 30),
            Guid.Parse("33333333-3333-3333-3333-333333333333"), [2],
            new TimeOnly(17, 30), false, clockOut, false, missingClockOut);

    private static OvertimeRecognition NewRecognition(OvertimeRecognitionSource source) =>
        new(Guid.NewGuid(),
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            WorkDate, Local(17, 30), Local(20, 30), source.ObservedClockOutAt,
            source.SuggestedStartAt, source.SuggestedEndAt,
            source.SuggestedMinutes, source.InitialStatus,
            source.Fingerprint, Now);

    private static DateTime Local(int hour, int minute = 0) =>
        DateTime.SpecifyKind(new DateTime(2026, 8, 21, hour, minute, 0),
            DateTimeKind.Unspecified);

    private sealed class Setup : IAsyncDisposable
    {
        private Setup(HRSystemDbContext db, OvertimeRecognitionService service)
        { Db = db; Service = service; }
        public HRSystemDbContext Db { get; }
        public OvertimeRecognitionService Service { get; }

        public static async Task<Setup> CreateAsync(string role)
        {
            var db = new HRSystemDbContext(new DbContextOptionsBuilder<HRSystemDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            var department = new Department(Guid.NewGuid(), "OTR", "加班認列部", Now);
            var employee = new Employee(Guid.NewGuid(), "EMP-G101", "認列測試員工",
                department.Id, new DateOnly(2025, 1, 1), Now);
            var request = new OvertimeRequest(Guid.NewGuid(), employee.Id,
                Local(17, 30), Local(20, 30), "測試", "employee", Now);
            request.Submit("employee", Now);
            request.Approve(OvertimeReviewReason.ApprovedAsRequested,
                null, "admin", Now);
            db.AddRange(department, employee, request);
            await db.SaveChangesAsync();
            return new Setup(db, new(db,
                new User(role, employee.Id), new FixedTime(Now)));
        }
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private sealed class User(string role, Guid? employeeId) : ICurrentUser
    {
        public string? UserId => "actor";
        public Guid? EmployeeId => employeeId;
        public string? DisplayName => "actor";
        public string? IpAddress => "127.0.0.1";
        public bool IsAuthenticated => true;
        public bool IsInRole(string candidate) => candidate == role;
        public bool HasPermission(string policy) =>
            RolePermissions.HasPermission([role], policy);
    }
    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    { public override DateTimeOffset GetUtcNow() => now; }
}
