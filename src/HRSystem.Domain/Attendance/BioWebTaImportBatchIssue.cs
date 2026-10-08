using HRSystem.Domain.Common;

namespace HRSystem.Domain.Attendance;

public sealed class BioWebTaImportBatchIssue
{
    private BioWebTaImportBatchIssue()
    {
    }

    public BioWebTaImportBatchIssue(
        Guid id,
        Guid importBatchId,
        long? externalEventId,
        short fingerprintVersion,
        byte[]? incomingFingerprint,
        byte[]? existingFingerprint,
        BioWebTaImportIssueCode issueCode,
        string safeSummary,
        DateTimeOffset createdAtUtc)
    {
        if (importBatchId == Guid.Empty)
        {
            throw new DomainValidationException("Import batch is required.");
        }

        if (externalEventId is <= 0)
        {
            throw new DomainValidationException(
                "External event id must be positive when supplied.");
        }

        if (fingerprintVersion != AttendanceRawEventFingerprintV1.Version)
        {
            throw new DomainValidationException("Fingerprint version is invalid.");
        }

        if (!Enum.IsDefined(issueCode))
        {
            throw new DomainValidationException("Import issue code is invalid.");
        }

        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        ImportBatchId = importBatchId;
        ExternalEventId = externalEventId;
        FingerprintVersion = fingerprintVersion;
        IncomingFingerprint = CopyFingerprint(
            incomingFingerprint,
            nameof(incomingFingerprint));
        ExistingFingerprint = CopyFingerprint(
            existingFingerprint,
            nameof(existingFingerprint));
        IssueCode = issueCode;
        SafeSummary = Required(safeSummary, "Issue summary", 500);
        CreatedAtUtc = createdAtUtc.ToUniversalTime();
    }

    public Guid Id { get; private set; }
    public Guid ImportBatchId { get; private set; }
    public long? ExternalEventId { get; private set; }
    public short FingerprintVersion { get; private set; }
    public byte[]? IncomingFingerprint { get; private set; }
    public byte[]? ExistingFingerprint { get; private set; }
    public BioWebTaImportIssueCode IssueCode { get; private set; }
    public string SafeSummary { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public byte[] RowVersion { get; private set; } = [];
    public BioWebTaImportBatch ImportBatch { get; private set; } = null!;

    private static byte[]? CopyFingerprint(byte[]? value, string field)
    {
        if (value is null)
        {
            return null;
        }

        if (value.Length != AttendanceRawEventFingerprintV1.HashLength)
        {
            throw new DomainValidationException(
                $"{field} must contain {AttendanceRawEventFingerprintV1.HashLength} bytes.");
        }

        return [.. value];
    }

    private static string Required(
        string? value,
        string field,
        int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainValidationException($"{field} is required.");
        }

        var normalized = value.Trim();
        if (normalized.Length > maximumLength)
        {
            throw new DomainValidationException(
                $"{field} cannot exceed {maximumLength} characters.");
        }

        return normalized;
    }
}
