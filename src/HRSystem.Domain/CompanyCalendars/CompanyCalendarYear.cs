using HRSystem.Domain.Common;

namespace HRSystem.Domain.CompanyCalendars;

public sealed class CompanyCalendarYear
{
    private CompanyCalendarYear()
    {
    }

    public CompanyCalendarYear(
        Guid id,
        int year,
        string sourceAuthority,
        string sourceTitle,
        DateOnly sourcePublishedDate,
        string sourceReference,
        string? sourceDocumentIdentifier,
        string? sourceContentHash,
        string manifestVersion,
        string manifestHash,
        string actor,
        DateTimeOffset nowUtc)
    {
        if (year is < 1 or > 9999)
        {
            throw new DomainValidationException("行事曆年度無效。");
        }

        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        Year = year;
        Status = CompanyCalendarStatus.Draft;
        SetSource(
            sourceAuthority,
            sourceTitle,
            sourcePublishedDate,
            sourceReference,
            sourceDocumentIdentifier,
            sourceContentHash,
            manifestVersion,
            manifestHash);
        CreatedBy = CompanyCalendarRules.Required(actor, "建立者", 450);
        CreatedAtUtc = nowUtc.ToUniversalTime();
        ModifiedBy = CreatedBy;
        ModifiedAtUtc = CreatedAtUtc;
    }

    public Guid Id { get; private set; }
    public int Year { get; private set; }
    public CompanyCalendarStatus Status { get; private set; }
    public string SourceAuthority { get; private set; } = string.Empty;
    public string SourceTitle { get; private set; } = string.Empty;
    public DateOnly SourcePublishedDate { get; private set; }
    public string SourceReference { get; private set; } = string.Empty;
    public string? SourceDocumentIdentifier { get; private set; }
    public string? SourceContentHash { get; private set; }
    public string ManifestVersion { get; private set; } = string.Empty;
    public string ManifestHash { get; private set; } = string.Empty;
    public DateTimeOffset? PublishedAtUtc { get; private set; }
    public string? PublishedBy { get; private set; }
    public DateTimeOffset? ArchivedAtUtc { get; private set; }
    public string? ArchivedBy { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public string CreatedBy { get; private set; } = string.Empty;
    public DateTimeOffset ModifiedAtUtc { get; private set; }
    public string ModifiedBy { get; private set; } = string.Empty;
    public byte[] RowVersion { get; private set; } = [];
    public ICollection<CompanyCalendarDay> Days { get; } = new List<CompanyCalendarDay>();

    public void UpdateSource(
        string sourceAuthority,
        string sourceTitle,
        DateOnly sourcePublishedDate,
        string sourceReference,
        string? sourceDocumentIdentifier,
        string? sourceContentHash,
        string manifestVersion,
        string manifestHash,
        string actor,
        DateTimeOffset nowUtc)
    {
        if (Status == CompanyCalendarStatus.Archived)
        {
            throw new DomainValidationException("已封存的行事曆不可變更。");
        }

        SetSource(
            sourceAuthority,
            sourceTitle,
            sourcePublishedDate,
            sourceReference,
            sourceDocumentIdentifier,
            sourceContentHash,
            manifestVersion,
            manifestHash);
        Touch(actor, nowUtc);
    }

    public void Publish(string actor, DateTimeOffset nowUtc)
    {
        if (Status != CompanyCalendarStatus.Draft)
        {
            throw new DomainValidationException("只有草稿行事曆可以發布。");
        }

        Status = CompanyCalendarStatus.Published;
        PublishedBy = CompanyCalendarRules.Required(actor, "發布者", 450);
        PublishedAtUtc = nowUtc.ToUniversalTime();
        Touch(actor, nowUtc);
    }

    public void Archive(string actor, DateTimeOffset nowUtc)
    {
        if (Status != CompanyCalendarStatus.Published)
        {
            throw new DomainValidationException("只有已發布的行事曆可以封存。");
        }

        Status = CompanyCalendarStatus.Archived;
        ArchivedBy = CompanyCalendarRules.Required(actor, "封存者", 450);
        ArchivedAtUtc = nowUtc.ToUniversalTime();
        Touch(actor, nowUtc);
    }

    public void Touch(string actor, DateTimeOffset nowUtc)
    {
        ModifiedBy = CompanyCalendarRules.Required(actor, "異動者", 450);
        ModifiedAtUtc = nowUtc.ToUniversalTime();
    }

    private void SetSource(
        string sourceAuthority,
        string sourceTitle,
        DateOnly sourcePublishedDate,
        string sourceReference,
        string? sourceDocumentIdentifier,
        string? sourceContentHash,
        string manifestVersion,
        string manifestHash)
    {
        SourceAuthority = CompanyCalendarRules.Required(sourceAuthority, "來源機關", 200);
        SourceTitle = CompanyCalendarRules.Required(sourceTitle, "來源標題", 300);
        SourcePublishedDate = sourcePublishedDate;
        SourceReference = CompanyCalendarRules.Required(sourceReference, "來源網址", 1000);
        SourceDocumentIdentifier = CompanyCalendarRules.Optional(
            sourceDocumentIdentifier, "來源文號", 200);
        SourceContentHash = ValidateHash(sourceContentHash, "來源附件雜湊", false);
        ManifestVersion = CompanyCalendarRules.Required(manifestVersion, "Manifest 版本", 50);
        ManifestHash = ValidateHash(manifestHash, "Manifest 雜湊", true)!;
    }

    private static string? ValidateHash(string? value, string field, bool required)
    {
        if (!required && string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var hash = CompanyCalendarRules.Required(value, field, 64);
        if (hash.Length != 64 || hash.Any(character =>
                character is not (>= '0' and <= '9')
                    and not (>= 'a' and <= 'f')))
        {
            throw new DomainValidationException($"{field}必須為 64 碼小寫 SHA-256。");
        }

        return hash;
    }
}
