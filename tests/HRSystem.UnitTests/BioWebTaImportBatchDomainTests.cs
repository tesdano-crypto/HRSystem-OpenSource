using HRSystem.Domain.Attendance;
using HRSystem.Domain.Common;

namespace HRSystem.UnitTests;

public sealed class BioWebTaImportBatchDomainTests
{
    private static readonly DateTimeOffset StartedAtUtc =
        new(2026, 8, 3, 0, 15, 0, TimeSpan.Zero);

    [Fact]
    public void New_Batch_Is_Running_With_Safe_Execution_Metadata()
    {
        var batch = NewBatch();

        Assert.Equal(BioWebTaImportBatchStatus.Running, batch.Status);
        Assert.Equal(BioWebTaImportTriggerType.Scheduled, batch.TriggerType);
        Assert.Equal("Asia/Taipei", batch.TimeZoneId);
        Assert.Equal("EXAMPLE-HOST", batch.HostName);
        Assert.Equal(1234, batch.ProcessId);
        Assert.Equal("1.0.0+abc", batch.JobVersion);
        Assert.Null(batch.CompletedAtUtc);
        Assert.Null(batch.ErrorSummary);
    }

    [Theory]
    [InlineData(false, BioWebTaImportBatchStatus.Completed)]
    [InlineData(true, BioWebTaImportBatchStatus.CompletedWithWarnings)]
    public void Running_Batch_Can_Complete_Once(
        bool withWarnings,
        BioWebTaImportBatchStatus expectedStatus)
    {
        var batch = NewBatch();

        batch.Complete(10, 7, 3, withWarnings, StartedAtUtc.AddMinutes(1));

        Assert.Equal(expectedStatus, batch.Status);
        Assert.Equal(10, batch.SourceRowCount);
        Assert.Equal(7, batch.InsertedCount);
        Assert.Equal(3, batch.DuplicateCount);
        Assert.Equal(0, batch.ConflictCount);
        Assert.Equal(0, batch.FailedCount);
        Assert.Throws<DomainValidationException>(() =>
            batch.Complete(10, 7, 3, false, StartedAtUtc.AddMinutes(2)));
    }

    [Fact]
    public void Failed_Batch_Records_Safe_Counts_And_Cannot_Transition_Again()
    {
        var batch = NewBatch();

        batch.Fail(
            4,
            0,
            2,
            1,
            1,
            "Source event content mismatch.",
            StartedAtUtc.AddMinutes(1));

        Assert.Equal(BioWebTaImportBatchStatus.Failed, batch.Status);
        Assert.Equal(1, batch.ConflictCount);
        Assert.Equal(1, batch.FailedCount);
        Assert.Equal("Source event content mismatch.", batch.ErrorSummary);
        Assert.Throws<DomainValidationException>(() =>
            batch.Abandon("late transition", StartedAtUtc.AddMinutes(2)));
    }

    [Fact]
    public void FailBeforeSourceRead_Records_Safe_Zero_Row_Failure()
    {
        var batch = NewBatch();

        batch.FailBeforeSourceRead(
            "Read-only source unavailable.",
            StartedAtUtc.AddMinutes(1));

        Assert.Equal(BioWebTaImportBatchStatus.Failed, batch.Status);
        Assert.Equal(0, batch.SourceRowCount);
        Assert.Equal("Read-only source unavailable.", batch.ErrorSummary);
    }

    [Fact]
    public void Query_Window_Requires_Unspecified_Half_Open_Local_Times()
    {
        Assert.Throws<DomainValidationException>(() => NewBatch(
            DateTime.SpecifyKind(QueryFrom(), DateTimeKind.Local),
            QueryTo()));
        Assert.Throws<DomainValidationException>(() => NewBatch(QueryTo(), QueryFrom()));
        Assert.Throws<DomainValidationException>(() => NewBatch(QueryFrom(), QueryFrom()));
    }

    [Fact]
    public void Invalid_Counts_And_Completion_Time_Are_Rejected()
    {
        var first = NewBatch();
        Assert.Throws<DomainValidationException>(() =>
            first.Complete(1, 1, 1, false, StartedAtUtc.AddMinutes(1)));

        var second = NewBatch();
        Assert.Throws<DomainValidationException>(() =>
            second.Fail(1, 0, 0, 0, 0, "failed", StartedAtUtc.AddMinutes(1)));

        var third = NewBatch();
        Assert.Throws<DomainValidationException>(() =>
            third.Abandon("clock error", StartedAtUtc.AddTicks(-1)));

        var fourth = NewBatch();
        Assert.Throws<DomainValidationException>(() =>
            fourth.Fail(0, 0, 0, 0, 0, "failed", StartedAtUtc.AddMinutes(1)));
    }

    [Fact]
    public void Issue_Stores_Only_Safe_Identifiers_Hashes_And_Summary()
    {
        var incoming = Enumerable.Repeat((byte)0x11, 32).ToArray();
        var existing = Enumerable.Repeat((byte)0x22, 32).ToArray();
        var issue = new BioWebTaImportBatchIssue(
            Guid.NewGuid(),
            Guid.NewGuid(),
            42,
            AttendanceRawEventFingerprintV1.Version,
            incoming,
            existing,
            BioWebTaImportIssueCode.SourceEventContentMismatch,
            "Source event content mismatch.",
            StartedAtUtc);

        incoming[0] = 0xFF;
        existing[0] = 0xFF;

        Assert.Equal(0x11, issue.IncomingFingerprint![0]);
        Assert.Equal(0x22, issue.ExistingFingerprint![0]);
        Assert.Equal(42, issue.ExternalEventId);
        Assert.DoesNotContain(
            typeof(BioWebTaImportBatchIssue).GetProperties(),
            property => new[] { "Pin", "Device", "EventLocalDateTime", "Payload" }
                .Any(term => property.Name.Contains(term, StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void Issue_Rejects_Invalid_Fingerprint_Evidence()
    {
        Assert.Throws<DomainValidationException>(() =>
            new BioWebTaImportBatchIssue(
                Guid.NewGuid(),
                Guid.NewGuid(),
                42,
                AttendanceRawEventFingerprintV1.Version,
                new byte[31],
                null,
                BioWebTaImportIssueCode.InvalidSourceRecord,
                "invalid",
                StartedAtUtc));
    }

    private static BioWebTaImportBatch NewBatch(
        DateTime? queryFrom = null,
        DateTime? queryTo = null) =>
        new(
            Guid.NewGuid(),
            BioWebTaImportTriggerType.Scheduled,
            queryFrom ?? QueryFrom(),
            queryTo ?? QueryTo(),
            "Asia/Taipei",
            StartedAtUtc,
            "EXAMPLE-HOST",
            1234,
            "1.0.0+abc");

    private static DateTime QueryFrom() =>
        new(2026, 8, 1, 0, 0, 0, DateTimeKind.Unspecified);

    private static DateTime QueryTo() =>
        new(2026, 8, 3, 8, 15, 0, DateTimeKind.Unspecified);
}
