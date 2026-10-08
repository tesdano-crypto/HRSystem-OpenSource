using HRSystem.Application.Attendance;
using HRSystem.Application.Common.Exceptions;
using HRSystem.Application.Security;
using HRSystem.Domain.Attendance;

namespace HRSystem.UnitTests;

public sealed class BioWebTaImportStatusServiceTests
{
    private static readonly DateTimeOffset Friday0840Taipei =
        new(2026, 8, 14, 0, 40, 0, TimeSpan.Zero);

    [Fact]
    public async Task Status_Separates_Last_Scheduled_From_Newer_Manual_Batch()
    {
        await using var db = TestDb.Create();
        var scheduled = CompletedBatch(
            BioWebTaImportTriggerType.Scheduled,
            new DateTimeOffset(2026, 8, 14, 0, 30, 0, TimeSpan.Zero),
            source: 37,
            inserted: 0,
            duplicate: 37);
        var manual = CompletedBatch(
            BioWebTaImportTriggerType.Manual,
            new DateTimeOffset(2026, 8, 14, 0, 35, 0, TimeSpan.Zero),
            source: 37,
            inserted: 2,
            duplicate: 35);
        db.BioWebTaImportBatches.AddRange(scheduled, manual);
        await db.SaveChangesAsync();

        var result = await Service(db).GetAsync();

        Assert.Equal(scheduled.Id, result.LastScheduled!.Id);
        Assert.Equal(manual.Id, result.LastManual!.Id);
        Assert.True(result.LastScheduled.IsNoChanges);
        Assert.False(result.LastManual.IsNoChanges);
        Assert.Equal(
            [BioWebTaImportTriggerType.Manual, BioWebTaImportTriggerType.Scheduled],
            result.RecentBatches.Select(item => item.TriggerType));
        Assert.Equal(
            new DateTimeOffset(2026, 8, 14, 9, 45, 0, TimeSpan.Zero),
            result.NextScheduledAtUtc);
    }

    [Fact]
    public async Task Status_Returns_Latest_Fifteen_In_Descending_Order()
    {
        await using var db = TestDb.Create();
        var startedAtUtc = new DateTimeOffset(
            2026, 8, 13, 0, 0, 0, TimeSpan.Zero);
        var batches = Enumerable.Range(0, 18)
            .Select(index => CompletedBatch(
                index % 2 == 0
                    ? BioWebTaImportTriggerType.Scheduled
                    : BioWebTaImportTriggerType.Manual,
                startedAtUtc.AddMinutes(index),
                source: 1,
                inserted: 0,
                duplicate: 1))
            .ToArray();
        db.BioWebTaImportBatches.AddRange(batches);
        await db.SaveChangesAsync();

        var result = await Service(db).GetAsync();

        Assert.Equal(15, result.RecentBatches.Count);
        Assert.Equal(
            batches.OrderByDescending(item => item.StartedAtUtc)
                .Take(15)
                .Select(item => item.Id),
            result.RecentBatches.Select(item => item.Id));
        Assert.Equal(batches[^2].Id, result.LastScheduled!.Id);
        Assert.Equal(batches[^1].Id, result.LastManual!.Id);
    }

    [Fact]
    public async Task Status_Handles_Empty_Scheduled_And_Manual_History()
    {
        await using var db = TestDb.Create();

        var result = await Service(db).GetAsync();

        Assert.Null(result.LastScheduled);
        Assert.Null(result.LastManual);
        Assert.Empty(result.RecentBatches);
        Assert.Equal(0, result.UnmappedEventCount);
    }

    [Fact]
    public async Task Admin_Without_Employee_Binding_Can_Read_Operational_Status()
    {
        await using var db = TestDb.Create();

        var result = await Service(
            db,
            new TestCurrentUser(RoleNames.Admin, employeeId: null)).GetAsync();

        Assert.True(result.IsScheduleEnabled);
    }

    [Fact]
    public async Task Employee_Cannot_Read_Operational_Status()
    {
        await using var db = TestDb.Create();

        await Assert.ThrowsAsync<ForbiddenAccessException>(() => Service(
            db,
            new TestCurrentUser(RoleNames.Employee, Guid.NewGuid())).GetAsync());
    }

    [Theory]
    [InlineData(8, 0, 8, 5, 0)]
    [InlineData(8, 5, 8, 15, 0)]
    [InlineData(8, 20, 8, 30, 0)]
    [InlineData(8, 40, 17, 45, 0)]
    [InlineData(17, 50, 18, 0, 0)]
    [InlineData(18, 10, 21, 0, 0)]
    [InlineData(21, 10, 8, 5, 1)]
    public void Next_Schedule_Uses_Asia_Taipei_Weekday_Windows(
        int nowHour,
        int nowMinute,
        int expectedHour,
        int expectedMinute,
        int expectedDayOffset)
    {
        var localNow = new DateTimeOffset(
            2026, 8, 12, nowHour, nowMinute, 0, TimeSpan.FromHours(8));

        var actualUtc = BioWebTaImportSchedule.GetNextRunAtUtc(
            localNow.ToUniversalTime(),
            "Asia/Taipei");
        var actualLocal = actualUtc.ToOffset(TimeSpan.FromHours(8));

        Assert.Equal(
            new DateTimeOffset(
                2026,
                8,
                12 + expectedDayOffset,
                expectedHour,
                expectedMinute,
                0,
                TimeSpan.FromHours(8)),
            actualLocal);
    }

    [Fact]
    public void Friday_After_Last_Window_Skips_Weekend()
    {
        var friday = new DateTimeOffset(
            2026, 8, 14, 21, 10, 0, TimeSpan.FromHours(8));

        var actual = BioWebTaImportSchedule.GetNextRunAtUtc(
            friday.ToUniversalTime(),
            "Asia/Taipei").ToOffset(TimeSpan.FromHours(8));

        Assert.Equal(
            new DateTimeOffset(2026, 8, 17, 8, 5, 0, TimeSpan.FromHours(8)),
            actual);
    }

    private static BioWebTaImportStatusService Service(
        HRSystem.Infrastructure.Persistence.HRSystemDbContext db,
        TestCurrentUser? currentUser = null) =>
        new(
            db,
            new BioWebTaScheduledImportOptions
            {
                Enabled = true,
                OverlapDays = 3,
                PageSize = 500,
                MaxExecutionMinutes = 30,
                TimeZoneId = "Asia/Taipei"
            },
            currentUser ?? new TestCurrentUser(),
            new FixedTimeProvider(Friday0840Taipei));

    private static BioWebTaImportBatch CompletedBatch(
        BioWebTaImportTriggerType trigger,
        DateTimeOffset started,
        int source,
        int inserted,
        int duplicate)
    {
        var batch = new BioWebTaImportBatch(
            Guid.NewGuid(),
            trigger,
            new DateTime(2026, 8, 12),
            new DateTime(2026, 8, 14, 8, 40, 0),
            "Asia/Taipei",
            started,
            "unit-test",
            1,
            "1.0.0-test");
        batch.Complete(
            source,
            inserted,
            duplicate,
            completedWithWarnings: false,
            started.AddSeconds(3));
        return batch;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
